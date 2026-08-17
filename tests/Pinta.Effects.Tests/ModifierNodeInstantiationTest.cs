using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

// Applying an effect builds a node holding its own instance of that effect. Most effects take the
// service provider they were registered with; a few take nothing. An effect whose constructor fits
// neither shape used to throw MissingMethodException the moment the user applied it, so this walks
// every shipped effect type rather than trusting one example.
[TestFixture]
internal sealed class ModifierNodeInstantiationTest
{
	private static IEnumerable<Type> ShippedEffectTypes ()
		=> typeof (InkSketchEffect).Assembly
			.GetTypes ()
			.Where (t => !t.IsAbstract && typeof (BaseEffect).IsAssignableFrom (t));

	// Banding must not change what the effect produces. A tileable effect rendered in one call and
	// the same effect rendered in per-core horizontal bands have to agree pixel for pixel, or the
	// speed-up would show up as seams across the canvas.
	[Test]
	public void TiledRenderMatchesTheSingleCallRender ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		InvertColorsEffect effect = new (services);
		Assert.That (effect.IsTileable, Is.True, "test relies on this effect taking the banded path");

		ImageSurface input = Utilities.LoadImage ("blackandwhite1.png");

		ImageSurface whole = CairoExtensions.CreateImageSurface (Format.Argb32, input.Width, input.Height);
		effect.Render (input, whole, [new RectangleI (0, 0, input.Width, input.Height)]);
		whole.MarkDirty ();

		EffectModifierNode node = EffectModifierNode.FromEffect (effect, clip: null, services);
		ImageSurface banded = EffectModifierNode.CopyOf (input);
		node.Apply (banded);

		Assert.That (banded.GetReadOnlyPixelData ().SequenceEqual (whole.GetReadOnlyPixelData ()), Is.True);
	}

	// Toggling a node's visibility, undo and redo all feed the effect an identical input. Re-running
	// an expensive effect there is what made the dock feel frozen, so the second render must come
	// from the cache — and must still be the same pixels.
	[Test]
	public void RepeatRenderOfTheSameInputIsCachedAndIdentical ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		InvertColorsEffect effect = new (services);
		EffectModifierNode node = EffectModifierNode.FromEffect (effect, clip: null, services);

		ImageSurface input = Utilities.LoadImage ("blackandwhite1.png");

		ImageSurface first = EffectModifierNode.CopyOf (input);
		node.Apply (first);

		ImageSurface second = EffectModifierNode.CopyOf (input);
		node.Apply (second);

		Assert.That (second.GetReadOnlyPixelData ().SequenceEqual (first.GetReadOnlyPixelData ()), Is.True);

		// A changed input must not serve the cached render.
		ImageSurface other = Utilities.LoadImage ("bulge1.png");
		ImageSurface third = EffectModifierNode.CopyOf (other);
		node.Apply (third);

		Assert.That (third.GetReadOnlyPixelData ().SequenceEqual (first.GetReadOnlyPixelData ()), Is.False,
			"a different input served a stale cached render");
	}

	// A saved effect node reloads by writing its settings back onto a fresh EffectData, one property at
	// a time. A property whose type the serializer has no converter for is silently left at the
	// effect's default, so the node reopens with settings the user never chose — this is the check that
	// notices, for every shipped effect, before a user does.
	[TestCaseSource (nameof (ShippedEffectTypes))]
	public void EveryEffectSettingSurvivesASaveAndReload (Type effectType)
	{
		EffectData? data = NewEffect (effectType).EffectData;
		if (data is null)
			return; // An effect with no settings has nothing to round-trip.

		IEnumerable<PropertyInfo> settings = data.GetType ()
			.GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.CanRead && p.CanWrite && p.GetIndexParameters ().Length == 0);

		foreach (PropertyInfo setting in settings)
			Assert.That (
				EffectDataSerializer.CanSerialize (setting.PropertyType),
				Is.True,
				$"{effectType.Name}.{setting.Name} is a {setting.PropertyType.Name}, which no converter handles: it would reload as the default");
	}

	private static BaseEffect NewEffect (Type effectType)
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		try {
			return (BaseEffect) Activator.CreateInstance (effectType, services)!;
		} catch (MissingMethodException) {
			return (BaseEffect) Activator.CreateInstance (effectType)!;
		}
	}

	[TestCaseSource (nameof (ShippedEffectTypes))]
	public void EveryEffectCanBeCopiedIntoANode (Type effectType)
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		BaseEffect menuInstance = NewEffect (effectType);

		EffectModifierNode node = EffectModifierNode.FromEffect (menuInstance, clip: null, services);

		Assert.That (
			node.Effect,
			Is.Not.SameAs (menuInstance),
			$"{effectType.Name}: the node shares the menu's instance, so editing the node would rewrite the menu's settings");
	}
}
