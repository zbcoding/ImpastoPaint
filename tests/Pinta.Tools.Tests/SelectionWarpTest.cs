using System;
using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Move Selected Pixels' Skew and Perspective modes: the grips reshape the floating pixels and
/// their selection together, one drag is one undo step, and re-adjusting within a session
/// resamples from the untouched pixels instead of compounding blur.
/// </summary>
[TestFixture]
internal sealed class SelectionWarpTest : ToolsTestHarness
{
	private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// An 8x8 red square at (8,8), selected.
	private static readonly RectangleI square = new (8, 8, 8, 8);

	private MoveSelectedTool? tool;

	[SetUp]
	public void SelectRedSquare ()
	{
		ImageSurface surface = Layer (0).Surface;
		Span<ColorBgra> pixels = surface.GetPixelData ();
		for (int y = square.Top; y < square.Top + square.Height; y++)
			for (int x = square.Left; x < square.Left + square.Width; x++)
				pixels[(y * surface.Width) + x] = Red;
		surface.MarkDirty ();

		Document.Selection = SelectionOf (square);
		Document.Selection.Visible = true;
	}

	[TearDown]
	public void DeactivateTool ()
	{
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) TransformMode.Move);
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	[Test]
	public void SkewSlidesTheGrabbedEdgeAlongItselfAndKeepsTheOppositeEdge ()
	{
		PointD[] rectangle = [new (0, 0), new (10, 0), new (10, 10), new (0, 10)];

		// Edge 0 is the top. The downward part of the drag would stretch, not skew: ignored.
		PointD[] skewed = SelectionWarp.Skew (rectangle, 0, new PointD (4, 7));

		Assert.That (skewed, Is.EqualTo (new PointD[] { new (4, 0), new (14, 0), new (10, 10), new (0, 10) }));
	}

	[Test]
	public void AWarpThatChangesNothingReproducesThePixelsExactly ()
	{
		// Pins the pixel-centre convention: a half-pixel slip here shifts and blurs every warp.
		using ImageSurface source = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);
		source.GetPixelData ()[(9 * CanvasSize) + 9] = Red;
		source.MarkDirty ();
		using ImageSurface destination = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);

		PointD[] quad = [new (8, 8), new (16, 8), new (16, 16), new (8, 16)];
		Assert.That (Homography.TryCreate (quad, quad, out Homography identity), Is.True);
		SelectionWarp.Render (source, CairoExtensions.CreateIdentityMatrix (), identity, quad, destination);

		Assert.That (destination.GetReadOnlyPixelData ().ToArray (), Is.EqualTo (source.GetReadOnlyPixelData ().ToArray ()));
	}

	[Test]
	public void SkewDragSlantsThePixelsAndTheSelectionAsOneUndoStep ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		int historyBefore = Document.History.Pointer;

		// Top edge's grip sits at its midpoint; slide it right by 4.
		Drag (t, new PointD (12, 8), new PointD (16, 8));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (12, 8), new (20, 8), new (16, 16), new (8, 16) }),
			"the selection must outline the parallelogram the pixels now fill");
		Assert.That (FloatingPixel (18, 8), Is.EqualTo (Red), "the top row must have slid right, past the old right edge");
		Assert.That (FloatingPixel (8, 8).A, Is.Zero, "the old top-left corner is outside the parallelogram");
		Assert.That (FloatingPixel (10, 15), Is.EqualTo (Red), "the bottom edge stays put");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore + 1), "one drag is one undo step");

		Document.History.Undo ();
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 8), new (16, 8), new (16, 16), new (8, 16) }));
		Assert.That (Layer (0).Surface.GetReadOnlyPixelData ()[(8 * CanvasSize) + 8], Is.EqualTo (Red),
			"undo must put the pixels back on the layer, unlifted");

		Document.History.Redo ();
		Assert.That (FloatingPixel (18, 8), Is.EqualTo (Red), "redo must bring the skew back");
	}

	[Test]
	public void PerspectiveDragMovesOneCornerOnly ()
	{
		MoveSelectedTool t = Activate (TransformMode.Perspective);

		// Corner 2 is the bottom-right one.
		Drag (t, new PointD (16, 16), new PointD (24, 24));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 8), new (16, 8), new (24, 24), new (8, 16) }));
		Assert.That (FloatingPixel (19, 18).A, Is.GreaterThan (128), "pixels must stretch out past the old corner to fill the new one");
		Assert.That (FloatingPixel (9, 9), Is.EqualTo (Red), "the fixed corner keeps its pixels");
	}

	[Test]
	public void SkewingBackWithinASessionReturnsThePixelsUnblurred ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		ColorBgra[] lifted = LiftedPixels (t);

		Drag (t, new PointD (12, 8), new PointD (15, 8));
		Drag (t, new PointD (15, 8), new PointD (12, 8));

		Assert.That (FloatingPixels (), Is.EqualTo (lifted),
			"skewing back must resample the original pixels, not the already-resampled ones");
	}

	[Test]
	public void FinishingTheSelectionPaintsTheWarpedPixelsOntoTheLayer ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		Drag (t, new PointD (12, 8), new PointD (16, 8));

		Document.FinishSelection ();

		Assert.That (LayerPixel (18, 8), Is.EqualTo (Red), "the slanted top row must land on the layer");
		Assert.That (LayerPixel (8, 8).A, Is.Zero, "the lifted corner the skew moved away from must stay empty");
		Assert.That (LayerPixel (10, 15), Is.EqualTo (Red));
	}

	[Test]
	public void AWarpAfterAMoveStartsFromWhereTheMoveLeftThePixels ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		Drag (t, new PointD (12, 8), new PointD (16, 8));

		// A body drag only changes the floating layer's transform, not its pixels.
		Drag (t, new PointD (14, 12), new PointD (18, 12));
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (16, 8), new (24, 8), new (20, 16), new (12, 16) }));

		// Slide the top edge back over the bottom edge: a plain square at the moved position.
		Drag (t, new PointD (20, 8), new PointD (16, 8));

		Assert.That (FloatingPixel (13, 9), Is.EqualTo (Red));
		Assert.That (FloatingPixel (18, 14), Is.EqualTo (Red));
		Assert.That (FloatingPixel (9, 12).A, Is.Zero,
			"the warp must not resample from where the pixels sat before the move");
	}

	[Test]
	public void AWarpAfterUndoContinuesFromTheUndoneShape ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		Drag (t, new PointD (12, 8), new PointD (16, 8));
		Drag (t, new PointD (16, 8), new PointD (20, 8));

		Document.History.Undo ();
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (12, 8), new (20, 8), new (16, 16), new (8, 16) }));

		// The top grip is back at the first skew's midpoint; sliding it home restores the square.
		Drag (t, new PointD (16, 8), new PointD (12, 8));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 8), new (16, 8), new (16, 16), new (8, 16) }));
		Assert.That (FloatingPixel (9, 9), Is.EqualTo (Red));
		Assert.That (FloatingPixel (21, 8).A, Is.Zero, "nothing from the undone second skew may survive");
	}

	[Test]
	public void ADragThatWouldFoldTheShapeHoldsTheLastShapeThatWorked ()
	{
		MoveSelectedTool t = Activate (TransformMode.Perspective);

		// Bottom-right corner out to (20,20), then across the top-left corner, which folds the quad.
		DragThrough (t, new PointD (16, 16), new PointD (20, 20), new PointD (4, 4));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 8), new (16, 8), new (20, 20), new (8, 16) }));
		Assert.That (FloatingPixel (9, 9), Is.EqualTo (Red));
	}

	[Test]
	public void SwitchingToolsMidDragKeepsTheWarpAsItsOwnUndoStep ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		int historyBefore = Document.History.Pointer;

		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (new PointD (12, 8))]);
		typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (new PointD (16, 8))]);
		DeactivateTool ();

		Assert.That (LayerPixel (18, 8), Is.EqualTo (Red), "the switch must land the skew on the layer");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore + 2), "the skew, then finishing the selection");

		Document.History.Undo ();
		Document.History.Undo ();
		Assert.That (LayerPixel (8, 8), Is.EqualTo (Red), "undoing both must put the square back unskewed");
		Assert.That (LayerPixel (18, 8).A, Is.Zero);
	}

	[Test]
	public void MoveModeGripsStillResize ()
	{
		MoveSelectedTool t = Activate (TransformMode.Move);

		Drag (t, new PointD (12, 8), new PointD (12, 4));
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 4), new (16, 4), new (16, 16), new (8, 16) }),
			"the top grip stretches the selection rather than skewing it");

		Drag (t, new PointD (8, 4), new PointD (4, 4));
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (4, 4), new (16, 4), new (16, 16), new (4, 16) }),
			"a corner grip resizes about the opposite corner rather than moving that corner alone");
	}

	[Test]
	public void SkewModeHasNoHiddenResizeGrips ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);

		// No skew grip sits on a corner, and the resize grips are not drawn in this mode, so a press
		// there is a plain move.
		Drag (t, new PointD (8, 8), new PointD (6, 6));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (6, 6), new (14, 6), new (14, 14), new (6, 14) }));
	}

	// Through the saved setting, the way the mode last chosen comes back in a new session.
	private MoveSelectedTool Activate (TransformMode mode)
	{
		// Zoomed in, as a user working on an 8px square would be; at 1:1 the grips' hit areas
		// cover the whole square and every press lands on one of them.
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.Scale = 8;
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) mode);

		MoveSelectedTool t = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		tool = t;
		return t;
	}

	// The square as it floats before any warp: lifted by a grip click that never moves.
	private ColorBgra[] LiftedPixels (MoveSelectedTool t)
	{
		Drag (t, new PointD (12, 8), new PointD (12, 8));
		return FloatingPixels ();
	}

	private void Drag (BaseTool t, PointD from, PointD to)
		=> DragThrough (t, from, to);

	private void DragThrough (BaseTool t, PointD from, params PointD[] path)
	{
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (from)]);
		foreach (PointD point in path)
			typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (point)]);
		typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (path[^1])]);
	}

	private ColorBgra LayerPixel (int x, int y)
		=> Layer (0).Surface.GetReadOnlyPixelData ()[(y * CanvasSize) + x];

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		WindowPoint = PintaCore.Workspace.CanvasPointToView (canvasPos),
		MouseButton = MouseButton.Left,
	};

	private ColorBgra FloatingPixel (int x, int y)
		=> FloatingPixels ()[(y * CanvasSize) + x];

	// Only meaningful while the selection layer's transform is identity, which a warp leaves it at.
	private ColorBgra[] FloatingPixels ()
	{
		ImageSurface surface = Document.Layers.SelectionLayer.Surface;
		return [.. surface.GetReadOnlyPixelData ()[..(CanvasSize * CanvasSize)]];
	}

	private static PointI[] CornersOf (DocumentSelection selection)
		=> [.. selection.SelectionPolygons.Single ().Take (4).Select (p => new PointI ((int) p.X, (int) p.Y))];
}
