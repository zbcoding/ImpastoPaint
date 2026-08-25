using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Pins the z-order of the tool layer and its sibling overlay layer. A brush stroke in progress
/// must paint beneath the current layer's own objects (no z-jump when it commits into the raster -
/// the bug 1033f367 fixed), but overlay-only users - the text tool's dashed re-edit rectangle and
/// corner dots, the zoom tool's rubber band - live on a separate OverlayLayer that must stay above
/// it, or they vanish under opaque pixels.
/// </summary>
[TestFixture]
internal sealed class ToolLayerZOrderTest : ToolsTestHarness
{
	// --- The stroke rule ----------------------------------------------------------------------------

	[Test]
	public void ToolLayerPaintsBeneathTheCurrentLayer ()
	{
		Document.Layers.ToolLayer.Hidden = false;

		IEnumerable<Layer> paint = Document.Layers.GetLayersToPaint ().ToList ();

		int toolIndex = paint.ToList ().FindIndex (l => l.Surface == Document.Layers.ToolLayer.Surface);
		int layerIndex = paint.ToList ().FindIndex (l => l.Surface == Layer (0).Surface);

		Assert.That (toolIndex, Is.GreaterThan (-1), "setup: an unhidden tool layer is painted");
		Assert.That (layerIndex, Is.GreaterThan (-1), "setup: the user layer is painted");
		Assert.That (toolIndex, Is.LessThan (layerIndex),
			"an in-progress stroke has to sit beneath its layer's content, where committing it will leave it");
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
