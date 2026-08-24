using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Z-order contract for live shape objects, as the user states it: the object drawn last is the
/// one seen on top on canvas, and the sub-layer list agrees with the canvas - the top row is the
/// top overlay. Tests drive the real persist path a shape draw goes through
/// (<see cref="UserLayer.ReplaceShapes"/>) and read stacking through the same flattening the
/// canvas paints (<see cref="UserLayer.GetLayersToPaint"/>).
/// </summary>
[TestFixture]
internal sealed class SublayerOrderTest : ToolsTestHarness
{
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	private static readonly ColorBgra RedInk = ColorBgra.FromBgra (0, 0, 255, 255);

	// Two boxes sharing the 16..20 band so whichever paints second owns those pixels.
	private static readonly RectangleI LeftBox = new (4, 4, 16, 16);
	private static readonly RectangleI RightBox = new (16, 8, 16, 16);
	private static readonly PointI Overlap = new (18, 12);

	private void DrawTwoOverlappingBoxes (UserLayer layer)
	{
		// First draw: only the blue box exists yet.
		layer.ReplaceShapes ([Box (new Color (0, 0, 1), LeftBox)]);
		Refresh (layer);

		// Second draw: the engine persists the full draw-order list, oldest first.
		layer.ReplaceShapes ([
			Box (new Color (0, 0, 1), LeftBox),
			Box (new Color (1, 0, 0), RightBox),
		]);
		Refresh (layer);
	}

	// --- The user's rule -------------------------------------------------------------------------

	[Test]
	public void LastDrawnShapeIsSeenOnTop ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Transparent);
		DrawTwoOverlappingBoxes (layer);

		Assert.Multiple (() => {
			Assert.That (Shown (layer, Overlap.X, Overlap.Y).R, Is.EqualTo (255),
				"the red box was drawn last, so it has to be the one visible where the two overlap");
			Assert.That (Shown (layer, Overlap.X, Overlap.Y).B, Is.Zero,
				"the older blue box has to sit underneath at the overlap");
		});
	}

	// --- The dock-must-agree-with-canvas invariant ------------------------------------------------

	[Test]
	public void SubLayerListTopRowMatchesCanvasTopAtOverlap ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Transparent);
		DrawTwoOverlappingBoxes (layer);

		// The dock lists objects bottom row first (LayersListView walks Count-1 -> 0), so the
		// LAST row is the top overlay. Whatever row that is, the canvas has to agree at the
		// overlap pixel: the top-listed object's own fill color wins there.
		Assert.That (layer.Objects[^1], Is.InstanceOf<ShapeObject> (),
			"this scenario is all shapes, so the last list slot has to be a shape");
		ColorBgra expected = ((ShapeObject) layer.Objects[^1]).FillColor.ToColorBgra ();

		Assert.That (Shown (layer, Overlap.X, Overlap.Y), Is.EqualTo (expected),
			"the top sub-layer row in the menu and the top overlay on canvas have to be the same object");
	}
}
