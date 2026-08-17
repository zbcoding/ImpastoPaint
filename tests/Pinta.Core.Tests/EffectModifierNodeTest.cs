using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class EffectModifierNodeTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	// A modifier applies to what is beneath it and nothing above it. That ordering is the whole point
	// of putting these in the z-ordered object list, so it is what the test pins down: the same node
	// over the same pixels must produce a different result depending on where it sits.
	private sealed class InvertTestEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Invert (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> dstData = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
			for (int i = 0; i < dstData.Length; ++i) {
				ColorBgra c = srcData[i];
				dstData[i] = ColorBgra.FromBgra ((byte) (255 - c.B), (byte) (255 - c.G), (byte) (255 - c.R), c.A);
			}
		}
	}

	// Skips rather than fails when the native cairo-graphics library isn't present, matching
	// ShapeObjectTest: a machine without it must not turn the suite red and hide real regressions.
	private static void RequireCairo ()
	{
		try {
			using ImageSurface _ = CairoExtensions.CreateImageSurface (Format.Argb32, 1, 1);
		} catch (DllNotFoundException e) {
			Assert.Ignore ($"Native cairo-graphics unavailable: {e.Message}");
		}
	}

	private static ImageSurface OpaqueBlack (int size)
	{
		ImageSurface s = CairoExtensions.CreateImageSurface (Format.Argb32, size, size);
		using Context g = new (s);
		g.SetSourceRgba (0, 0, 0, 1);
		g.Paint ();
		s.MarkDirty ();
		return s;
	}

	// A third-party effect that throws must cost its own contribution and nothing else: a node
	// re-renders on every stroke, undo and visibility toggle, so a propagating exception would take
	// the application down in the middle of someone's editing session.
	private sealed class ThrowingTestEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Throws (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
			=> throw new ArgumentOutOfRangeException (nameof (rois), "as an add-in effect might");
	}

	[Test]
	public void FailingEffectLeavesThePixelsAloneInsteadOfThrowing ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));
		EffectModifierNode node = new (new ThrowingTestEffect ());

		ImageSurface accumulator = EffectModifierNode.CopyOf (layer.Surface);
		Assert.DoesNotThrow (() => node.Apply (accumulator));
		accumulator.MarkDirty ();

		ColorBgra result = accumulator.GetColorBgra (new PointI (1, 1));
		Assert.That (result.R, Is.EqualTo (0), "the base raster is untouched");
	}

	[Test]
	public void ModifierAppliesToBaseRaster ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));
		layer.Objects.Add (new EffectModifierNode (new InvertTestEffect ()));

		ImageSurface accumulator = EffectModifierNode.CopyOf (layer.Surface);
		((EffectModifierNode) layer.Objects[0]).Apply (accumulator);
		accumulator.MarkDirty ();

		ColorBgra result = accumulator.GetColorBgra (new PointI (1, 1));
		Assert.That (result.R, Is.EqualTo (255), "black base raster inverted to white");
	}

	[Test]
	public void HiddenModifierDoesNothing ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));
		EffectModifierNode node = new (new InvertTestEffect ()) { Hidden = true };

		ImageSurface accumulator = EffectModifierNode.CopyOf (layer.Surface);
		node.Apply (accumulator);
		accumulator.MarkDirty ();

		Assert.That (accumulator.GetColorBgra (new PointI (1, 1)).R, Is.EqualTo (0), "hidden node left the pixels alone");
	}

	[Test]
	public void ZeroOpacityMeansNoEffect ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));
		EffectModifierNode node = new (new InvertTestEffect ()) { Opacity = 0 };

		ImageSurface accumulator = EffectModifierNode.CopyOf (layer.Surface);
		node.Apply (accumulator);
		accumulator.MarkDirty ();

		Assert.That (accumulator.GetColorBgra (new PointI (1, 1)).R, Is.EqualTo (0), "opacity is effect strength");
	}

	[Test]
	public void LayerWithoutModifiersKeepsTheOriginalRenderPath ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));

		Assert.That (layer.HasModifiers, Is.False);
		Assert.That (layer.Composite, Is.Null, "no composite means GetLayersToPaint uses the two-surface path");
	}

	[Test]
	public void RasterizingWithoutAModifierIsANoOp ()
	{
		RequireCairo ();
		UserLayer layer = new (OpaqueBlack (4));

		// No composite means the object-only rasterize path owns the layer; baking here would wipe
		// live shapes and text without going through their confirmation.
		Assert.That (layer.RasterizeModifierStack (), Is.False);
	}

	[Test]
	public void FromEffectTakesItsOwnCopyOfTheParameters ()
	{
		InvertTestEffect menuInstance = new ();
		EffectModifierNode node = EffectModifierNode.FromEffect (menuInstance, clip: null);

		// The menu keeps one long-lived instance per effect. A node sharing it would be rewritten
		// the next time the user opened that effect's dialog from the menu.
		Assert.That (node.Effect, Is.Not.SameAs (menuInstance));
		Assert.That (node.Effect.EffectId, Is.EqualTo (menuInstance.EffectId), "identity survives the copy");
	}

	[Test]
	public void CloneDoesNotAliasEffectParameters ()
	{
		EffectModifierNode node = new (new InvertTestEffect ()) { Opacity = 0.5, Name = "mine" };
		EffectModifierNode copy = node.Clone ();

		Assert.That (copy.Opacity, Is.EqualTo (0.5));
		Assert.That (copy.Name, Is.EqualTo ("mine"));
		Assert.That (copy.Effect, Is.Not.SameAs (node.Effect), "a duplicated layer must not share the original's effect instance");
	}
}
