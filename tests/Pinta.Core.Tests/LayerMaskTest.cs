using System;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// A layer mask is a slot on the layer (UserLayer.Mask), not a z-ordered child: its alpha multiplies
/// the layer's accumulated result, last. These pin the slot's behaviour — rendering, the paint-target
/// seam, geometry and baking — against a bare UserLayer, with no managers involved. The mask's
/// behaviour across a history step is covered by NodeHistoryTest, which runs a whole document.
/// </summary>
[TestFixture]
internal sealed class LayerMaskTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	// LayerMaskHistoryItem's restore path re-renders through PintaCore, so it is exercised from
	// DocumentHarness instead (see NodeHistoryTest.UndoOfAMaskLeavesTheNodeStackIntact), which brings
	// the managers up headless. What stays here is the slot's own behaviour, which needs no document.

	private static UserLayer LayerWithPixel (int width, int height, PointI point)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		using (Context g = new (surface)) {
			g.SetSourceRgba (1, 0, 0, 1); // opaque red
			g.Operator = Operator.Source;
			g.Rectangle (point.X, point.Y, 1, 1);
			g.Fill ();
		}
		surface.MarkDirty ();

		return new UserLayer (surface);
	}

	// Fills the mask with an opaque white (fully revealing) or semi-transparent (half-hiding) alpha.
	private static void FillMask (UserLayer layer, byte alpha)
	{
		using (Context g = new (layer.Mask!.Surface)) {
			g.Operator = Operator.Source;
			g.SetSourceRgba (1, 1, 1, alpha / 255.0);
			g.Paint ();
		}
		layer.Mask.Surface.MarkDirty ();
	}

	private static ColorBgra CompositePixel (UserLayer layer, PointI point)
	{
		Assert.That (layer.Composite, Is.Not.Null, "a masked layer must render through the accumulator");
		return layer.Composite!.GetColorBgra (point);
	}

	// The mask is premultiplied alpha: an opaque red pixel under a mask alpha of 128 keeps its colour
	// channels scaled by 128/255 and its alpha at 128. White (255) leaves the layer untouched.
	[Test]
	public void MaskAlphaScalesTheCompositePremultiplied ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ();
		FillMask (layer, 128);

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		ColorBgra half = CompositePixel (layer, new PointI (1, 1));
		Assert.That (half.A, Is.EqualTo (128), "alpha is scaled by the mask");
		Assert.That (half.R, Is.EqualTo (128), "premultiplied red is scaled with alpha");
		Assert.That (half.G, Is.EqualTo (0));
		Assert.That (half.B, Is.EqualTo (0));

		// A pixel outside the red dot is transparent before the mask; it stays transparent.
		ColorBgra empty = CompositePixel (layer, new PointI (2, 2));
		Assert.That (empty.A, Is.EqualTo (0));
	}

	[Test]
	public void FullyTransparentMaskHidesTheLayerAndWhiteMaskLeavesItAlone ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));

		layer.CreateMask (); // starts fully transparent
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (CompositePixel (layer, new PointI (1, 1)).A, Is.EqualTo (0),
			"a fresh mask hides everything: paint on it to reveal");

		FillMask (layer, 255);
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		ColorBgra shown = CompositePixel (layer, new PointI (1, 1));
		Assert.That (shown.A, Is.EqualTo (255));
		Assert.That (shown.R, Is.EqualTo (255), "full alpha mask is a no-op on the layer content");
	}

	// A mask forces the accumulator path even on a layer with no modifier nodes: the canvas has to
	// paint one composited surface, or the mask would never be applied.
	[Test]
	public void MaskAloneForcesTheCompositePath ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		Assert.That (layer.NeedsComposite, Is.False, "no mask, no modifiers: direct two-surface path");

		layer.CreateMask ();
		Assert.That (layer.NeedsComposite, Is.True, "a mask alone must render through the accumulator");

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (layer.Composite, Is.Not.Null);
		Assert.That (layer.GetLayersToPaint ().Count (), Is.EqualTo (1),
			"the canvas paints the single composited surface, not the two-surface stack");
	}

	// A hidden mask stops applying and the layer falls back to the direct raster path (no composite).
	[Test]
	public void HiddenMaskLeavesTheLayerOnTheDirectRasterPath ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ().Hidden = true;

		Assert.That (layer.NeedsComposite, Is.False);
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (layer.Composite, Is.Null);
	}

	// Tools fold raster edits into the composite via FoldRasterIntoComposite; a stroke that paints the
	// mask (revealing it) has to show up through that same seam, not only after a history push.
	[Test]
	public void FoldingPicksUpAMaskEditMadeAfterwards ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask (); // transparent: layer hidden

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (CompositePixel (layer, new PointI (1, 1)).A, Is.EqualTo (0));

		FillMask (layer, 255); // "paint" the mask the way a brush stroke commits
		Assert.That (ObjectOpacity.FoldRasterIntoComposite (chrome: null!, layer), Is.True,
			"a mask edit must fold; the caller then owes the canvas an invalidate");

		Assert.That (CompositePixel (layer, new PointI (1, 1)).A, Is.EqualTo (255),
			"the reveal was not folded into the composite");
	}

	// The paint-target seam: when the mask row is selected, paint tools write to the mask surface;
	// anything else and they write to the layer raster.
	[Test]
	public void PaintSurfaceFollowsTheMaskSelectionState ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));

		try {
			Assert.That (layer.PaintSurface, Is.SameAs (layer.Surface), "no mask selection: paint the layer");

			layer.CreateMask ();
			Assert.That (layer.PaintSurface, Is.SameAs (layer.Surface), "mask exists but is not selected: paint the layer");

			LayerMaskSelection.SetActiveMaskLayer (layer);
			Assert.That (layer.PaintSurface, Is.SameAs (layer.Mask!.Surface), "mask selected: paint the mask");

			LayerMaskSelection.SetActiveMaskLayer (null);
			Assert.That (layer.PaintSurface, Is.SameAs (layer.Surface), "selection cleared: paint the layer again");
		} finally {
			LayerMaskSelection.SetActiveMaskLayer (null);
		}
	}

	// Geometry ops move the mask with the layer: a crop shifts the mask's pixels into the new origin.
	[Test]
	public void CropMovesTheMaskWithTheLayer ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (3, 3));
		layer.CreateMask ();
		FillMask (layer, 255);

		// An opaque mask pixel at (3,3); after cropping to the (2,2,2,2) region it must sit at (1,1).
		using (Context g = new (layer.Mask!.Surface)) {
			g.Operator = Operator.Clear;
			g.Rectangle (0, 0, 4, 4);
			g.Fill ();
			g.Operator = Operator.Source;
			g.SetSourceRgba (1, 1, 1, 1);
			g.Rectangle (3, 3, 1, 1);
			g.Fill ();
		}
		layer.Mask.Surface.MarkDirty ();

		layer.Crop (new RectangleI (2, 2, 2, 2), selection: null);

		Assert.That (layer.Mask.Surface.Width, Is.EqualTo (2));
		Assert.That (layer.Mask.Surface.Height, Is.EqualTo (2));
		Assert.That (layer.Mask.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (255),
			"the mask's revealed pixel moved with the layer's content");
		Assert.That (layer.Mask.Surface.GetColorBgra (new PointI (0, 0)).A, Is.EqualTo (0));
	}

	// A non-uniform resize scales the mask like the layer, keeping reveal and hide aligned.
	[Test]
	public void ResizeScalesTheMaskWithTheLayer ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ();

		// One opaque mask pixel at (1,1) on an otherwise transparent mask.
		using (Context g = new (layer.Mask!.Surface)) {
			g.Operator = Operator.Source;
			g.SetSourceRgba (1, 1, 1, 1);
			g.Rectangle (1, 1, 1, 1);
			g.Fill ();
		}
		layer.Mask.Surface.MarkDirty ();

		layer.Resize (new Size (8, 4), ResamplingMode.NearestNeighbor);

		Assert.That (layer.Mask.Surface.Width, Is.EqualTo (8));
		Assert.That (layer.Mask.Surface.Height, Is.EqualTo (4));
		// A 1x1 opaque dot at (1,1) of 4x4 scales to x=2..3 of 8 (x doubles), y stays 1.
		Assert.That (layer.Mask.Surface.GetColorBgra (new PointI (2, 1)).A, Is.EqualTo (255));
		Assert.That (layer.Mask.Surface.GetColorBgra (new PointI (0, 0)).A, Is.EqualTo (0));
	}

	// Baking a layer's stack turns the mask into pixels: the mask is dropped so the next render does
	// not apply it a second time to the already-masked raster.
	[Test]
	public void RasterizeModifierStackBakesAndDropsTheMask ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ();
		FillMask (layer, 128);
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (layer.RasterizeModifierStack (), Is.True);
		Assert.That (layer.HasMask, Is.False, "the mask was baked into the raster; leaving it would re-apply it");
		Assert.That (layer.Composite, Is.Null);
		Assert.That (layer.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (128),
			"the baked raster carries the masked result");
	}

	// A hidden mask contributes nothing to the composite, so baking the stack has nothing of it to
	// fold in — dropping it anyway would destroy pixels the user deliberately parked out of the way.
	[Test]
	public void RasterizeModifierStackKeepsAHiddenMask ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ();
		FillMask (layer, 128);
		layer.Mask!.Hidden = true;
		layer.Objects.Add (new LayerTransformNode (new LayerTransformData { FlipHorizontal = true }));
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (layer.RasterizeModifierStack (), Is.True);
		Assert.That (layer.HasMask, Is.True, "a hidden mask was never applied, so a bake must not consume it");
		Assert.That (layer.Surface.GetColorBgra (new PointI (2, 1)).A, Is.EqualTo (255),
			"the baked raster carries the transform, unmasked");
	}

	// A duplicated mask is an independent copy: painting the copy must not change the original.
	[Test]
	public void MaskCloneDoesNotShareItsSurface ()
	{
		UserLayer layer = LayerWithPixel (4, 4, new PointI (1, 1));
		layer.CreateMask ();
		FillMask (layer, 255);

		LayerMask copy = layer.Mask!.CloneSurface ();
		Assert.That (ReferenceEquals (copy.Surface, layer.Mask.Surface), Is.False);

		using (Context g = new (copy.Surface)) {
			g.Operator = Operator.Clear;
			g.Paint ();
		}
		copy.Surface.MarkDirty ();

		Assert.That (layer.Mask.Surface.GetColorBgra (new PointI (0, 0)).A, Is.EqualTo (255),
			"clearing the copy must not touch the original's mask");
	}
}
