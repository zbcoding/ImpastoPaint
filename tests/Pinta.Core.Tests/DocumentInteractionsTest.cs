using System.Linq;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Scenes built the way a user builds them — several layers, effects confined to part of the canvas,
/// raster paint and text sitting in different places, shapes with real outlines — driven forwards and
/// backwards through a real history stack.
///
/// <para>
/// The per-piece suites each pin one thing against a bare <see cref="UserLayer"/>. What none of them
/// can reach is the seam where the pieces meet: a history item's Undo re-renders its layer through
/// <see cref="PintaCore"/>, so a stale composite, a render cache that outlived its input, a clip that
/// quietly followed the live selection, or a node list restored to the wrong contents only becomes
/// visible once a whole document is driving. Everything here asserts on what the canvas would show,
/// because that is the thing that goes wrong.
/// </para>
/// </summary>
[TestFixture]
internal sealed class DocumentInteractionsTest : DocumentHarness
{
	// Regions the scenes divide the canvas into. Kept apart so an effect confined to one cannot touch
	// another by rounding, and so text has somewhere of its own to land.
	private static readonly RectangleI TopLeft = new (0, 0, 12, 12);
	private static readonly RectangleI BottomRight = new (20, 20, 12, 12);
	private static readonly PointI InTopLeft = new (4, 4);
	private static readonly PointI InBottomRight = new (24, 24);
	private static readonly PointI Between = new (16, 16);

	// --- Several layers at once -----------------------------------------------------------------

	// Nodes belong to their layer. A stack on one must leave the others' pixels alone, forwards and
	// back — the composite is cached per layer, so a refresh that reached too far would show here.
	[Test]
	public void ANodeOnOneLayerLeavesTheOthersAlone ()
	{
		Fill (Layer (0).Surface, Red);
		UserLayer middle = AddLayer (Green);
		UserLayer top = AddLayer (Blue);

		AddObject (middle, Invert (), "Invert middle");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft), Is.EqualTo (Red), "bottom layer untouched");
			Assert.That (Shown (middle, InTopLeft).G, Is.EqualTo (0), "green inverted is magenta");
			Assert.That (Shown (top, InTopLeft), Is.EqualTo (Blue), "top layer untouched");
		});

		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft), Is.EqualTo (Red));
			Assert.That (Shown (middle, InTopLeft), Is.EqualTo (Green), "the node is gone from its own layer");
			Assert.That (Shown (top, InTopLeft), Is.EqualTo (Blue));
		});
	}

	// Each layer keeps its own stack through interleaved edits. Pushed in one order, undone in the
	// reverse — a per-layer refresh that ran against the wrong layer's list shows up as the other
	// layer's pixels moving when it should not.
	[Test]
	public void InterleavedEditsAcrossLayersUnwindInOrder ()
	{
		Fill (Layer (0).Surface, Red);
		UserLayer top = AddLayer (Green);

		AddObject (Layer (0), Invert (), "Invert bottom");      // red   -> cyan
		AddObject (top, Halve (), "Halve top");                 // green -> dark green
		AddObject (Layer (0), Halve (), "Halve bottom");        // cyan  -> half cyan

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (127));
			Assert.That (Shown (top, InTopLeft).G, Is.EqualTo (127));
		});

		Document.History.Undo (); // drop the bottom layer's halve
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (255), "bottom back to full cyan");
			Assert.That (Shown (top, InTopLeft).G, Is.EqualTo (127), "top's own node is untouched");
		});

		Document.History.Undo (); // drop the top layer's halve
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (255), "bottom still inverted");
			Assert.That (Shown (top, InTopLeft), Is.EqualTo (Green), "top back to plain green");
		});
	}

	// --- Effects over part of the canvas ---------------------------------------------------------

	// A clipped node is confined to its clip, and everything outside it stays the raster. Asserting
	// on all three zones is the point: a node that rendered the whole canvas and then clipped its
	// output would pass a check that only looked inside.
	[Test]
	public void AClippedNodeChangesOnlyItsOwnRegion ()
	{
		Fill (Layer (0).Surface, Red);
		AddObject (Layer (0), Invert (SelectionOf (TopLeft)), "Invert corner");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).B, Is.EqualTo (255), "inside the clip: red inverted to cyan");
			Assert.That (Shown (Layer (0), Between), Is.EqualTo (Red), "outside the clip: untouched");
			Assert.That (Shown (Layer (0), InBottomRight), Is.EqualTo (Red), "far outside: untouched");
		});
	}

	// The effect is handed the clip's bounds, not the whole canvas. This is what makes a committed
	// node match the live preview for any effect whose output depends on the region it was given —
	// a twist's centre, a gradient's span. Clipping the output afterwards cannot fix that.
	[Test]
	public void AClippedNodeRendersOverItsClipBoundsNotTheCanvas ()
	{
		Fill (Layer (0).Surface, Red);
		RegionRecordingEffect recorder = new ();
		AddObject (Layer (0), new EffectModifierNode (recorder, SelectionOf (BottomRight)), "Record");

		Assert.That (recorder.Regions, Is.Not.Empty);
		Assert.That (recorder.Regions[0], Is.EqualTo (BottomRight));
	}

	// Two clips that overlap compose in list order, and only the overlap gets both. Four zones are
	// checked because that is what separates "applied in order" from "applied at all".
	[Test]
	public void OverlappingClipsComposeOnlyWhereTheyMeet ()
	{
		Fill (Layer (0).Surface, Red);
		RectangleI left = new (0, 0, 20, 32);
		RectangleI right = new (12, 0, 20, 32);

		AddObject (Layer (0), Invert (SelectionOf (left)), "Invert left");
		AddObject (Layer (0), Halve (SelectionOf (right)), "Halve right");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), 4, 4).B, Is.EqualTo (255), "left only: inverted to cyan");
			Assert.That (Shown (Layer (0), 16, 4).B, Is.EqualTo (127), "overlap: inverted then halved");
			Assert.That (Shown (Layer (0), 28, 4).B, Is.EqualTo (0), "right only: red halved keeps B at 0");
			Assert.That (Shown (Layer (0), 28, 4).R, Is.EqualTo (127), "right only: halved red");
		});
	}

	// The clip is frozen at creation. Changing the document's selection afterwards must not move
	// where an already-placed node applies, or every past edit would drift with the current one.
	[Test]
	public void AFrozenClipIgnoresALaterSelectionChange ()
	{
		Fill (Layer (0).Surface, Red);
		AddObject (Layer (0), Invert (SelectionOf (TopLeft)), "Invert corner");

		Document.Selection.CreateRectangleSelection (new RectangleD (20, 20, 12, 12));
		Refresh (Layer (0));

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).B, Is.EqualTo (255), "still applies where it was placed");
			Assert.That (Shown (Layer (0), InBottomRight), Is.EqualTo (Red), "and not where the selection moved to");
		});
	}

	// --- Raster and objects in different places ---------------------------------------------------

	// Raster paint in one corner, an editable text object in the other. They live in different places
	// (the base surface and the object list), and a refresh has to bring both into the composite
	// without either overwriting the other.
	[Test]
	public void RasterAndTextInDifferentPlacesBothSurvive ()
	{
		FillRect (Layer (0).Surface, TopLeft, Red);
		AddObject (Layer (0), Text ("Hello", InBottomRight), "Add text");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft), Is.EqualTo (Red), "the raster corner is still raster");
			Assert.That (HasInk (Layer (0), BottomRight), Is.True, "the text put ink in its own corner");
			Assert.That (Shown (Layer (0), Between).A, Is.EqualTo (0), "the gap between them is untouched");
		});
	}

	// Painting raster under a text object must not disturb the object, and undoing that paint must
	// not take the object with it. The two are restored by different mechanisms — a surface diff and
	// a list swap — so this is the case where one can silently clobber the other.
	[Test]
	public void UndoingRasterPaintLeavesATextObjectStanding ()
	{
		AddObject (Layer (0), Text ("Hello", InBottomRight), "Add text");
		Assert.That (HasInk (Layer (0), BottomRight), Is.True);

		PaintRaster (Layer (0), s => FillRect (s, TopLeft, Red), "Paint");
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft), Is.EqualTo (Red));
			Assert.That (HasInk (Layer (0), BottomRight), Is.True, "the text is still drawn over the new paint");
		});

		Document.History.Undo ();
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).A, Is.EqualTo (0), "the paint is gone");
			Assert.That (Layer (0).TextObjects.Count, Is.EqualTo (1), "the text object is not");
			Assert.That (HasInk (Layer (0), BottomRight), Is.True, "and it is still on screen");
		});
	}

	// A node clipped to one corner, with raster in that corner and text in the other. The node has to
	// modify the raster it covers and leave the text region alone — the case where "the node applies
	// to everything beneath it" and "the node applies only inside its clip" have to hold at once.
	[Test]
	public void AClippedNodeSkipsAnObjectOutsideIt ()
	{
		FillRect (Layer (0).Surface, TopLeft, Red);
		AddObject (Layer (0), Text ("Hello", InBottomRight), "Add text");
		AddObject (Layer (0), Invert (SelectionOf (TopLeft)), "Invert corner");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).B, Is.EqualTo (255), "the covered raster inverted");
			Assert.That (HasInk (Layer (0), BottomRight), Is.True, "the text outside the clip still draws");
		});

		Document.History.Undo ();
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft), Is.EqualTo (Red), "the corner is plain red again");
			Assert.That (HasInk (Layer (0), BottomRight), Is.True, "and the text never moved");
		});
	}

	// --- Shapes ------------------------------------------------------------------------------------

	// A self-crossing outline fills under the even-odd rule, so it has a hole. A node above it must
	// modify the filled parts and leave the hole as whatever was underneath — the shape's alpha, not
	// its bounding box, is what the node composites against.
	[Test]
	public void ANodeOverASelfCrossingShapeFollowsItsHole ()
	{
		ShapeObject bowtie = Polygon (
			new Color (0, 1, 0, 1),
			new PointD (4, 4), new PointD (28, 4), new PointD (4, 28), new PointD (28, 28));

		AddObject (Layer (0), bowtie, "Bowtie");
		Assert.That (Shown (Layer (0), 16, 16).A, Is.EqualTo (0), "the crossing leaves the middle empty");

		AddObject (Layer (0), Invert (), "Invert");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), 16, 8).G, Is.EqualTo (0), "green filled area inverted to magenta");
			Assert.That (Shown (Layer (0), 16, 16).A, Is.EqualTo (0), "the hole is still a hole");
		});
	}

	// Two shapes, one clipped, one not, with a node between them in the list. The node applies to
	// what is below it and not to what is above — the ordering rule that makes the object list a
	// composition order rather than just a collection.
	[Test]
	public void ANodeAppliesOnlyToTheObjectsBeneathIt ()
	{
		AddObject (Layer (0), Box (new Color (0, 1, 0, 1), TopLeft), "Green box");
		AddObject (Layer (0), Invert (), "Invert");
		AddObject (Layer (0), Box (new Color (0, 1, 0, 1), BottomRight), "Green box above");

		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (0), "the box below the node was inverted");
			Assert.That (Shown (Layer (0), InBottomRight).G, Is.EqualTo (255), "the box above it was not");
		});

		// Undo the top box, then the node: each step exposes what was underneath, unchanged.
		Document.History.Undo ();
		Assert.That (Shown (Layer (0), InBottomRight).A, Is.EqualTo (0), "the top box is gone");
		Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (0), "the bottom box is still inverted");

		Document.History.Undo ();
		Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (255), "and now it is plain green again");
	}

	// A shape clipped to a region only draws inside it, and its clip is frozen the same way a node's
	// is — so a shape drawn inside a selection keeps its clipped shape after the selection is gone.
	[Test]
	public void AClippedShapeKeepsItsClipAfterTheSelectionChanges ()
	{
		ShapeObject wide = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, 32, 32));
		wide.Clip = SelectionOf (TopLeft);

		AddObject (Layer (0), wide, "Clipped box");
		Assert.Multiple (() => {
			Assert.That (Shown (Layer (0), InTopLeft).G, Is.EqualTo (255), "drawn inside its clip");
			Assert.That (Shown (Layer (0), InBottomRight).A, Is.EqualTo (0), "and nowhere else");
		});

		Document.Selection.Clear ();
		Refresh (Layer (0));

		Assert.That (Shown (Layer (0), InBottomRight).A, Is.EqualTo (0), "clearing the selection does not unclip it");
	}

	// --- Everything at once -------------------------------------------------------------------------

	// The full scene: two layers, raster and a shape and text in three different places, a clipped
	// node and a whole-layer node, and a mask over all of it. Wound all the way back and all the way
	// forward. Any one piece restoring in the wrong order changes a pixel somewhere here.
	[Test]
	public void AWholeSceneSurvivesAFullUnwindAndReplay ()
	{
		UserLayer bottom = Layer (0);
		Fill (bottom.Surface, Blue);

		UserLayer top = AddLayer ();
		FillRect (top.Surface, TopLeft, Red);
		AddObject (top, Box (new Color (0, 1, 0, 1), BottomRight), "Green box");
		AddObject (top, Text ("Hi", Between), "Add text");
		AddObject (top, Invert (SelectionOf (TopLeft)), "Invert corner");
		AddObject (top, Halve (), "Halve everything");

		LayerMask mask = top.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (top);
		Document.History.PushNewItem (
			new LayerMaskHistoryItem (PintaCore.Workspace, string.Empty, "Add Mask", top, null, mask.Surface));

		// Snapshot the finished scene at the points the pieces disagree about.
		PointI[] probes = [InTopLeft, InBottomRight, Between, new PointI (31, 0)];
		ColorBgra[] expected = probes.Select (p => Shown (top, p)).ToArray ();
		ColorBgra expectedBottom = Shown (bottom, InTopLeft);

		int steps = Document.History.Items.Count ();
		for (int i = 0; i < steps; ++i)
			Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (top.Objects, Is.Empty, "every object is back off the layer");
			Assert.That (top.Mask, Is.Null, "and the mask with them");
			Assert.That (Shown (top, InTopLeft), Is.EqualTo (Red), "the raster corner is all that is left");
			Assert.That (Shown (top, InBottomRight).A, Is.EqualTo (0));
			Assert.That (Shown (bottom, InTopLeft), Is.EqualTo (expectedBottom), "the other layer never moved");
		});

		for (int i = 0; i < steps; ++i)
			Document.History.Redo ();

		Assert.Multiple (() => {
			for (int i = 0; i < probes.Length; ++i)
				Assert.That (Shown (top, probes[i]), Is.EqualTo (expected[i]), $"probe {probes[i]} should replay exactly");
			Assert.That (top.Objects.Count, Is.EqualTo (4));
			Assert.That (top.Mask, Is.Not.Null);
		});
	}

	// --- Helpers -------------------------------------------------------------------------------------

	/// <summary>
	/// Whether anything was drawn inside a region. Text is asserted on this way rather than per pixel:
	/// the exact glyph coverage is the font stack's business, and where the ink landed is ours.
	/// </summary>
	private static bool HasInk (UserLayer layer, RectangleI region)
	{
		for (int y = region.Top; y <= region.Bottom; ++y)
			for (int x = region.Left; x <= region.Right; ++x)
				if (Shown (layer, x, y).A != 0)
					return true;

		return false;
	}
}
