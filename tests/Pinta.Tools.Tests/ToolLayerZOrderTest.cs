using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Pins the z-order of the tool layer and its sibling overlay layer. A brush stroke in progress
/// must paint above the current layer's own base raster, so it stays visible over opaque pixels
/// exactly where committing it will leave it (no invisible-until-mouse-up gap on an opaque layer,
/// e.g. a new document's background). Overlay-only users - the text tool's dashed re-edit rectangle
/// and corner dots, the zoom tool's rubber band - live on a separate OverlayLayer that must stay
/// above the layer's content too, or they vanish under opaque pixels.
/// </summary>
[TestFixture]
internal sealed class ToolLayerZOrderTest : ToolsTestHarness
{
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);

	// --- The stroke rule ----------------------------------------------------------------------------

	// PaintBrushTool.OnMouseUp commits by drawing ToolLayer onto CurrentPaintSurface (the raster)
	// with normal SourceOver compositing, so the stroke ends up ON TOP of whatever was already in
	// the raster. The live tool layer has to paint in that same visual position - above the base
	// raster - or a stroke over any opaque pixels is invisible while drawn and only appears at
	// mouse-up (the "missing paint" / "teleporting drawing" bug on the opaque background layer).
	[Test]
	public void ToolLayerPaintsAboveTheBaseRaster ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red); // Opaque ground, as a new document's background layer is.

		Document.Layers.ToolLayer.Hidden = false;

		IEnumerable<Layer> paint = Document.Layers.GetLayersToPaint ().ToList ();

		int toolIndex = paint.ToList ().FindIndex (l => l.Surface == Document.Layers.ToolLayer.Surface);
		int layerIndex = paint.ToList ().FindIndex (l => l.Surface == layer.Surface);

		Assert.That (toolIndex, Is.GreaterThan (-1), "setup: an unhidden tool layer is painted");
		Assert.That (layerIndex, Is.GreaterThan (-1), "setup: the user layer is painted");
		Assert.That (toolIndex, Is.GreaterThan (layerIndex),
			"an in-progress stroke has to sit above the base raster, matching where committing it will leave it");
	}

	[Test]
	public void LiveStrokeIsVisibleOverAnOpaqueLayer ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red); // Opaque ground: a background layer, e.g. on a new document.

		Fill (Document.Layers.ToolLayer.Surface, Blue); // A brush stroke in progress.
		Document.Layers.ToolLayer.Hidden = false;

		Assert.That (Composited (0, 0), Is.EqualTo (Blue),
			"the in-progress stroke must show through, not be hidden under the layer's opaque pixels");
	}

	private ColorBgra Composited (int x, int y)
	{
		using ImageSurface flat = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);
		using (Context g = new (flat)) {
			foreach (Layer piece in Document.Layers.GetLayersToPaint ()) {
				if (piece.Hidden)
					continue;
				g.BlendSurface (piece.Surface, piece.BlendMode, piece.Opacity);
			}
		}
		flat.MarkDirty ();
		return flat.GetColorBgra (new PointI (x, y));
	}

	// --- The overlay rule ---------------------------------------------------------------------------

	// 1033f367 moved the tool layer beneath its layer's content, which is right for a live brush
	// stroke but would have buried the text tool's dashed re-edit rectangle, its corner dots, the
	// caret and the "Obj." badge. Those now draw on a separate OverlayLayer that keeps painting above.
	[Test]
	public void OverlayLayerStillPaintsAboveTheCurrentLayer ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red); // Opaque ground: anything painted beneath it disappears.

		TextObject obj = new (new TextEngine (["Impasto"]) { Origin = new PointI (-36, 4) });
		layer.AddText (obj);

		Document.Layers.OverlayLayer.Hidden = false; // As DrawTextRectangles leaves it.

		List<Layer> paint = Document.Layers.GetLayersToPaint ().ToList ();
		int overlayIndex = paint.FindIndex (l => l.Surface == Document.Layers.OverlayLayer.Surface);
		int layerIndex = paint.FindIndex (l => l.Surface == layer.Surface);

		Assert.That (overlayIndex, Is.GreaterThan (layerIndex),
			"the dashed re-edit rectangle lives on the overlay layer and must not be buried under the layer's own pixels");
	}
}
