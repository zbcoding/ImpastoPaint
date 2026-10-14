using System;
using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Move Selected Pixels' Skew, Perspective and 3D modes: the grips reshape the floating pixels
/// and their selection together, one drag is one undo step, and re-adjusting within a session
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
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_FIELD_OF_VIEW, 60.0);
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

	[Test]
	public void AnotherToolsSelectionChangesDoNotRedrawTheWholeCanvas ()
	{
		// Built but never made current, as every registered tool is while another one draws a
		// selection; that tool changes the selection on every mouse move.
		_ = new MoveSelectedTool (PintaCore.Services);

		int redraws = 0;
		void Count (object? _, CanvasInvalidatedEventArgs args) => redraws++;
		Document.Workspace.CanvasInvalidated += Count;
		try {
			Document.Selection = SelectionOf (new RectangleI (2, 2, 4, 4));
		} finally {
			Document.Workspace.CanvasInvalidated -= Count;
		}

		Assert.That (redraws, Is.Zero, "a tool that is not in use must not repaint for a selection it does not show");
	}

	[Test]
	public void HoldingAltOverAWarpGripShowsTheRotateItWouldDo ()
	{
		MoveSelectedTool t = Activate (TransformMode.Skew);
		Gdk.Cursor rotate = (Gdk.Cursor) typeof (BaseTransformTool).GetProperty ("RotateCursor", NonPublicInstance)!.GetValue (t)!;
		PointD topGrip = new (12, 8);

		Hover (t, topGrip, alt: false);
		Gdk.Cursor? gripCursor = t.CurrentCursor;
		Assert.That (gripCursor, Is.Not.SameAs (rotate));

		Hover (t, topGrip, alt: true);
		Assert.That (t.CurrentCursor, Is.SameAs (rotate), "an Alt press on the grip rotates, so the cursor must say so");

		// Alt-drag from the grip rotates; finishing it resets the cursor, and the grip's must return.
		MouseDown (t, topGrip, Gdk.ModifierType.AltMask);
		MouseUp (t, topGrip, Gdk.ModifierType.AltMask);
		Hover (t, topGrip, alt: false);
		Assert.That (t.CurrentCursor, Is.SameAs (gripCursor), "hovering the grip after a rotate must show its cursor again");
	}

	[Test]
	public void HoldingTheDepthKeyPushesTheCornerBackAndLettingGoMovesItOnWithoutAJump ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);

		// Corner 0 is the top-left, (8,8); the 32px canvas's centre is (16,16). Up a sixteenth of
		// the canvas is half a doubling, so the corner's distance grows by √2 and it shows 1/√2 as
		// far from the centre: (10.34, 10.34).
		MouseDown (t, new PointD (8, 8), Gdk.ModifierType.ShiftMask);
		MouseMove (t, new PointD (8, 4), Gdk.ModifierType.ShiftMask);
		Assert.That (CornersOf (Document.Selection)[0], Is.EqualTo (new PointI (10, 10)),
			"pushed back, the corner slides toward the canvas centre instead of following the pointer");

		// Let go mid-drag: the next 2px down moves the corner 2px down from where it shows.
		MouseMove (t, new PointD (8, 6));
		MouseUp (t, new PointD (8, 6));

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (10, 12), new (16, 8), new (16, 16), new (8, 16) }));
	}

	[Test]
	public void ACornerGripsHintNamesTheDepthKeyTheUserBound ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		PintaCore.Shortcuts.SetToolBinding (KeyboardShortcutManager.Transform3DPushDepth, new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Alt_R)));
		try {
			Hover (t, new PointD (8, 8), alt: false);
			string? hint = t.Handles.Single ().TooltipText;
			Assert.That (hint, Does.Contain (PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.Transform3DPushDepth).ModifierKeyLabel (PintaCore.System)));

			Hover (t, new PointD (12, 12), alt: false);
			Assert.That (t.Handles.Single ().TooltipText, Is.Null, "off the grips, no grip hint");
		} finally {
			PintaCore.Shortcuts.ResetToolBinding (KeyboardShortcutManager.Transform3DPushDepth);
		}
	}

	[Test]
	public void APlainKeyDepthBindingLeavesItsCtrlShortcutToTheApp ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		Gdk.Key d = new (Gdk.Constants.KEY_d);
		PintaCore.Shortcuts.SetToolBinding (KeyboardShortcutManager.Transform3DPushDepth, new KeyGesture (d));
		try {
			Assert.That (Press (t, d, Gdk.ModifierType.ControlMask), Is.False, "Ctrl+D is Deselect All, not the depth key");
			Assert.That (Press (t, d), Is.True, "the bare key is the depth key, and 3D mode keeps it");
		} finally {
			PintaCore.Shortcuts.ResetToolBinding (KeyboardShortcutManager.Transform3DPushDepth);
		}
	}

	[Test]
	public void TheCursorFollowsTheDepthKeyDuringACornerDrag ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		Gdk.Key shift = new (Gdk.Constants.KEY_Shift_L);

		MouseDown (t, new PointD (8, 8));
		MouseMove (t, new PointD (8, 7));
		Gdk.Cursor? moving = t.CurrentCursor;

		Press (t, shift);
		Assert.That (t.CurrentCursor, Is.SameAs (QuadHandle.DepthCursor), "the corner now pushes in depth, so the cursor must say so");

		Release (t, shift);
		MouseMove (t, new PointD (8, 6));
		Assert.That (t.CurrentCursor, Is.SameAs (moving));
		MouseUp (t, new PointD (8, 6));
	}

	[Test]
	public void TurningThereAndBackLeavesNothingForANewViewToReshape ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		Gdk.Key left = new (Gdk.Constants.KEY_Left);
		Gdk.Key right = new (Gdk.Constants.KEY_Right);
		for (int i = 0; i < 9; i++)
			Press (t, left, Gdk.ModifierType.AltMask);
		Release (t, left, Gdk.ModifierType.AltMask);
		for (int i = 0; i < 9; i++)
			Press (t, right, Gdk.ModifierType.AltMask);
		Release (t, right, Gdk.ModifierType.AltMask);
		int historyBefore = Document.History.Pointer;

		SetFieldOfView (t, 120);

		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore), "back on the canvas plane, a new view changes nothing to undo");
	}

	[Test]
	public void AViewThatCannotShowTheCornersKeepsTheShapeWithoutAnUndoStep ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);

		// Down a quarter of the canvas halves the corner's distance, which a 150° view puts behind its camera.
		MouseDown (t, new PointD (8, 8), Gdk.ModifierType.ShiftMask);
		MouseMove (t, new PointD (8, 16), Gdk.ModifierType.ShiftMask);
		MouseUp (t, new PointD (8, 16), Gdk.ModifierType.ShiftMask);
		PointI[] pulled = CornersOf (Document.Selection);
		int historyBefore = Document.History.Pointer;

		SetFieldOfView (t, 150);

		Assert.That (CornersOf (Document.Selection), Is.EqualTo (pulled));
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore), "nothing changed on screen, so there is nothing to undo");
	}

	[Test]
	public void DraggingTheRightTiltGripInwardTurnsThatSideAwayAsOneUndoStep ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		int historyBefore = Document.History.Pointer;

		// The right edge's tilt grip sits 18 screen pixels (2.25 canvas pixels at 8x) outside its
		// midpoint. Dragging it in by half the square's width turns it about one radian.
		Drag (t, new PointD (18.25, 12), new PointD (14.25, 12));

		PointI[] corners = CornersOf (Document.Selection);
		Assert.That (EdgeLength (corners, 1), Is.LessThan (EdgeLength (corners, 3) - 1),
			"the side turned away must show shorter than the side turned toward the viewer");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore + 1));

		Document.History.Undo ();
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (new PointI[] { new (8, 8), new (16, 8), new (16, 16), new (8, 16) }));
	}

	[Test]
	public void AHeldTurnKeyIsOneUndoStepAndANarrowerViewFlattensTheTurn ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		int historyBefore = Document.History.Pointer;

		// Alt+Left held through nine key repeats turns the square 45° left, until it is let go.
		Gdk.Key left = new (Gdk.Constants.KEY_Left);
		for (int i = 0; i < 9; i++)
			Press (t, left, Gdk.ModifierType.AltMask);
		Release (t, left, Gdk.ModifierType.AltMask);

		PointI[] turned = CornersOf (Document.Selection);
		double turnedDifference = EdgeLength (turned, 1) - EdgeLength (turned, 3);
		Assert.That (turnedDifference, Is.GreaterThan (1), "turned left, the left side recedes and shows shorter");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore + 1), "a held key is one undo step");

		SetFieldOfView (t, 10);
		PointI[] narrowed = CornersOf (Document.Selection);
		Assert.That (EdgeLength (narrowed, 1) - EdgeLength (narrowed, 3), Is.LessThan (turnedDifference),
			"through a narrower view the same turn shows less foreshortening");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore + 2), "the new view is its own undo step");

		Document.History.Undo ();
		Assert.That (CornersOf (Document.Selection), Is.EqualTo (turned));
	}

	[Test]
	public void ChangingTheViewOfAFlatSelectionChangesNothing ()
	{
		MoveSelectedTool t = Activate (TransformMode.ThreeD);
		int historyBefore = Document.History.Pointer;

		SetFieldOfView (t, 120);

		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore), "nothing off the canvas plane, nothing to reshape or undo");
		Assert.That (Document.Layers.ShowSelectionLayer, Is.False, "the pixels must not be lifted for a no-op");
	}

	private static double EdgeLength (PointI[] corners, int edge)
	{
		PointI a = corners[edge];
		PointI b = corners[(edge + 1) % 4];
		return Math.Sqrt (((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
	}

	// What the toolbar's spin button calls; the button itself needs a canvas that can take focus.
	private static void SetFieldOfView (MoveSelectedTool t, double degrees)
		=> typeof (MoveSelectedTool).GetMethod ("ChangeFieldOfView", NonPublicInstance)!.Invoke (t, [degrees]);

	private bool Press (BaseTool t, Gdk.Key key, Gdk.ModifierType state = default)
		=> (bool) typeof (BaseTool).GetMethod ("DoKeyDown", NonPublicInstance)!.Invoke (t, [Document, new ToolKeyEventArgs { Key = key, State = state }])!;

	private void Release (BaseTool t, Gdk.Key key, Gdk.ModifierType state = default)
		=> typeof (BaseTool).GetMethod ("DoKeyUp", NonPublicInstance)!.Invoke (t, [Document, new ToolKeyEventArgs { Key = key, State = state }]);

	private void MouseDown (BaseTool t, PointD at, Gdk.ModifierType state = default)
		=> typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (at, state)]);

	private void MouseMove (BaseTool t, PointD at, Gdk.ModifierType state = default)
		=> typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (at, state)]);

	private void MouseUp (BaseTool t, PointD at, Gdk.ModifierType state = default)
		=> typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (at, state)]);

	private void Hover (BaseTool t, PointD at, bool alt)
		=> MouseMove (t, at, alt ? Gdk.ModifierType.AltMask : default);

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

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos, Gdk.ModifierType state = default) => new () {
		PointDouble = canvasPos,
		WindowPoint = PintaCore.Workspace.CanvasPointToView (canvasPos),
		MouseButton = MouseButton.Left,
		State = state,
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
