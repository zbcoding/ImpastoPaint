using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// A layer carrying modifier nodes is painted from <see cref="UserLayer.Composite"/> rather than from
/// its own raster (see UserLayer.GetLayersToPaint), which makes the composite derived state that goes
/// stale the moment a tool paints onto the raster. The gradient tool hit exactly that: the drag drew
/// pixels nobody was painting from, so it looked like it had done nothing until the layer was
/// rasterized. These pin the two halves of the fix.
/// </summary>
[TestFixture]
internal sealed class ModifierCompositeFreshnessTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

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

	private static UserLayer BlackLayerWithInvertNode ()
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		using (Context g = new (surface)) {
			g.SetSourceRgba (0, 0, 0, 1);
			g.Paint ();
		}
		surface.MarkDirty ();

		UserLayer layer = new (surface);
		layer.Objects.Add (new EffectModifierNode (new InvertTestEffect ()));
		return layer;
	}

	// Paints onto the layer's own raster, the way a gradient, brush or bucket stroke does.
	private static void PaintRasterWhite (UserLayer layer)
	{
		using (Context g = new (layer.Surface)) {
			g.SetSourceRgba (1, 1, 1, 1);
			g.Operator = Operator.Source;
			g.Paint ();
		}
		layer.Surface.MarkDirty ();
	}

	private static byte CompositeRed (UserLayer layer)
	{
		Assert.That (layer.Composite, Is.Not.Null, "a layer with a node must have an accumulated surface");
		return layer.Composite!.GetColorBgra (new PointI (1, 1)).R;
	}

	// The rebuild has to start from the layer's current raster. Accumulating from a copy taken when the
	// composite was first built would keep serving the pre-stroke pixels forever, which is the failure
	// the user saw as "the gradient never rendered".
	[Test]
	public void RebuildingTheCompositePicksUpARasterEditMadeAfterwards ()
	{
		UserLayer layer = BlackLayerWithInvertNode ();

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (CompositeRed (layer), Is.EqualTo (255), "black raster inverted to white");

		PaintRasterWhite (layer);
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (CompositeRed (layer), Is.EqualTo (0), "the stroke was not folded in: the composite still shows the old raster");
	}

	// The tools call FoldRasterIntoComposite rather than the node walk directly, so that is what has to
	// carry the edit through — and it has to stay a no-op for a layer with no nodes, whose raster the
	// canvas paints directly.
	[Test]
	public void FoldRasterIntoCompositeCarriesTheStrokeThroughTheEffectStack ()
	{
		UserLayer layer = BlackLayerWithInvertNode ();
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		PaintRasterWhite (layer);
		Assert.That (ObjectOpacity.FoldRasterIntoComposite (chrome: null!, layer), Is.True, "a layer with a node has to be folded");

		Assert.That (CompositeRed (layer), Is.EqualTo (0), "white raster inverted to black");
	}

	[Test]
	public void FoldingALayerWithoutNodesLeavesItOnTheDirectRasterPath ()
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		UserLayer layer = new (surface);

		Assert.That (ObjectOpacity.FoldRasterIntoComposite (chrome: null!, layer), Is.False, "nothing to fold, so the caller owes no invalidate");
		Assert.That (layer.Composite, Is.Null, "a composite here would make GetLayersToPaint skip the layer's own surface");
	}
}
