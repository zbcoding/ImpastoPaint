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

	// Through the saved setting, the way the mode last chosen comes back in a new session.
	private MoveSelectedTool Activate (TransformMode mode)
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
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
	{
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (from)]);
		typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (to)]);
		typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!.Invoke (t, [Document, MouseArgs (to)]);
	}

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
