using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Where a clip's real area and its bounding box disagree, and which of the two the code uses.
///
/// <para>
/// An ellipse inscribed in a box leaves its four corners unselected; a notched or concave outline
/// leaves a hole in the middle of its own bounds. Any decision made on the box instead of the area
/// gets those regions wrong. <see cref="SelectionNodeInteractionsTest"/> pins the query side of this
/// — whether a selection is judged to reach a node or an object. These pin the render side, in
/// pixels: an effect confined to an ellipse renders over the ellipse's bounding box, so what the
/// canvas ends up showing in the corners is the only thing that says which region actually applied.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ClipGeometryTest : DocumentHarness
{
	// A box whose corners are far enough outside its inscribed ellipse that antialiasing at the
	// boundary cannot reach them.
	private static readonly RectangleI Box = new (4, 4, 24, 24);
	private static readonly PointI BoxCorner = new (5, 5);
	private static readonly PointI EllipseCentre = new (16, 16);

	private UserLayer Only => Layer (0);

	[SetUp]
	public void PaintTheLayer () => Fill (Only.Surface, Red);

	// The node is handed its clip's bounding box to render over — that is what the live preview used,
	// so a region-dependent effect lands where the dialog showed it. The clip itself is what decides
	// where the output is kept. Both halves have to hold at once, and only the corners can tell:
	// they are inside the rendered box and outside the kept area.
	[Test]
	public void AnEllipseClippedNodeLeavesTheCornersOfItsBoundingBox ()
	{
		AddObject (Only, Invert (EllipseIn (Box)), "Invert ellipse");

		Assert.Multiple (() => {
			Assert.That (Shown (Only, EllipseCentre).B, Is.EqualTo (255), "inside the ellipse: red inverted to cyan");
			Assert.That (Shown (Only, BoxCorner), Is.EqualTo (Red), "the corner is inside the bounding box but outside the ellipse");
			Assert.That (Shown (Only, 0, 0), Is.EqualTo (Red), "and outside the bounding box entirely");
		});
	}

	// The same question for a shape that doubles back on itself: the notch is deep inside the clip's
	// bounds and inside its convex hull, and still must not be touched. A clip reduced to its bounds,
	// or to a hull, passes the ellipse case above and fails this one.
	[Test]
	public void ANotchedClipLeavesItsNotchAlone ()
	{
		// A "C": full height at the left, arms reaching right, and a notch bitten out of the middle.
		DocumentSelection c = PolygonSelection (
			new PointI (4, 4),
			new PointI (28, 4),
			new PointI (28, 10),
			new PointI (12, 10),
			new PointI (12, 22),
			new PointI (28, 22),
			new PointI (28, 28),
			new PointI (4, 28));

		AddObject (Only, Invert (c), "Invert C");

		Assert.Multiple (() => {
			Assert.That (Shown (Only, 6, 16).B, Is.EqualTo (255), "the spine of the C is inverted");
			Assert.That (Shown (Only, 20, 6).B, Is.EqualTo (255), "the top arm is inverted");
			Assert.That (Shown (Only, 20, 25).B, Is.EqualTo (255), "the bottom arm is inverted");
			Assert.That (Shown (Only, 20, 16), Is.EqualTo (Red), "the notch is untouched, though it sits inside the bounds");
		});
	}

	// Two clips whose bounding boxes overlap heavily while their areas do not touch. Nothing on the
	// canvas may come out with both effects applied — the overlap is entirely in empty corners.
	[Test]
	public void TwoEllipseClipsThatShareOnlyBoundingBoxDoNotCompose ()
	{
		// Bounds (2,2)-(17,17) and (14,14)-(29,29): boxes overlap over a 4x4 square, ellipses miss.
		AddObject (Only, Invert (EllipseIn (new RectangleI (2, 2, 16, 16))), "Invert upper");
		AddObject (Only, Halve (EllipseIn (new RectangleI (14, 14, 16, 16))), "Halve lower");

		Assert.Multiple (() => {
			Assert.That (Shown (Only, 9, 9).B, Is.EqualTo (255), "upper ellipse only: inverted, not halved");
			Assert.That (Shown (Only, 21, 21).R, Is.EqualTo (127), "lower ellipse only: halved, not inverted");
			Assert.That (Shown (Only, 15, 15), Is.EqualTo (Red), "the shared corner of the two boxes is in neither ellipse");
		});
	}

	// Undo and redo restore the clip itself, not a rectangle standing in for it. A clip that came back
	// as its bounding box would repaint the corners, which the forward pass left alone.
	[Test]
	public void AnEllipseClipComesBackAsAnEllipse ()
	{
		AddObject (Only, Invert (EllipseIn (Box)), "Invert ellipse");

		Document.History.Undo ();
		Assert.That (Shown (Only, EllipseCentre), Is.EqualTo (Red), "the node is gone");

		Document.History.Redo ();
		Assert.Multiple (() => {
			Assert.That (Shown (Only, EllipseCentre).B, Is.EqualTo (255), "and back");
			Assert.That (Shown (Only, BoxCorner), Is.EqualTo (Red), "still an ellipse, not its bounding box");
		});
	}

	// An ellipse-clipped node under a mask: two region rules stacked, one per-pixel alpha and one
	// path clip. A corner is outside the clip, so it keeps the raster — but the mask still scales it,
	// because the mask applies to the whole layer rather than to the node's region.
	[Test]
	public void AMaskScalesTheCornersAnEllipseClipLeftAlone ()
	{
		AddObject (Only, Invert (EllipseIn (Box)), "Invert ellipse");

		LayerMask mask = Only.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (Only);

		Assert.Multiple (() => {
			Assert.That (Shown (Only, EllipseCentre).B, Is.EqualTo (128), "cyan, halved by the mask");
			Assert.That (Shown (Only, BoxCorner).R, Is.EqualTo (128), "still red, and halved by the mask too");
			Assert.That (Shown (Only, BoxCorner).B, Is.EqualTo (0), "the corner never got inverted");
		});
	}

	// A shape drawn under an elliptical selection freezes that ellipse as its clip. What the shape
	// contributes has to follow the ellipse, not its bounds — the same rule as a node's clip, applied
	// on the object side of the list.
	[Test]
	public void AnEllipseClippedShapeDrawsOnlyInsideTheEllipse ()
	{
		Fill (Only.Surface, Transparent);

		ShapeObject wide = Box (new Color (0, 1, 0, 1), Box);
		wide.Clip = EllipseIn (Box);
		AddObject (Only, wide, "Clipped box");

		Assert.Multiple (() => {
			Assert.That (Shown (Only, EllipseCentre).G, Is.EqualTo (255), "drawn inside the ellipse");
			Assert.That (Shown (Only, BoxCorner).A, Is.EqualTo (0), "and not in the corners its bounds cover");
		});
	}



	// The ellipse is stored as a sampled polygon, and the sampling stopped just short of each curve's
	// end point — so three of the four vertices were never in the polygon at all and its bounds came
	// out a pixel short wherever the coordinate truncation rounded inward. Cropping to an ellipse
	// selection dropped a row of pixels because of it.
	[TestCase (4, 4, 24, 24)]
	[TestCase (0, 0, 16, 16)]
	[TestCase (3, 7, 15, 9)]
	public void AnEllipseSpansTheBoxItWasInscribedIn (int x, int y, int width, int height)
	{
		RectangleI box = new (x, y, width, height);

		Assert.That (EllipseIn (box).GetBounds ().ToInt (), Is.EqualTo (box));
	}
}
