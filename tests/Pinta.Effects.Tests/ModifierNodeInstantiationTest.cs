using System;
using System.Collections.Generic;
using System.Linq;
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

	[TestCaseSource (nameof (ShippedEffectTypes))]
	public void EveryEffectCanBeCopiedIntoANode (Type effectType)
	{
		IServiceProvider services = Utilities.CreateMockServices ();

		BaseEffect menuInstance;
		try {
			menuInstance = (BaseEffect) Activator.CreateInstance (effectType, services)!;
		} catch (MissingMethodException) {
			menuInstance = (BaseEffect) Activator.CreateInstance (effectType)!;
		}

		EffectModifierNode node = EffectModifierNode.FromEffect (menuInstance, clip: null, services);

		Assert.That (
			node.Effect,
			Is.Not.SameAs (menuInstance),
			$"{effectType.Name}: the node shares the menu's instance, so editing the node would rewrite the menu's settings");
	}
}
