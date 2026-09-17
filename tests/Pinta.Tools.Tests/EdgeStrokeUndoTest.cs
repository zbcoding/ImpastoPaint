using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Every paint tool decided it had changed pixels from the pointer being inside the canvas, which
/// is not the same question: a stroke sampled outside the canvas on both ends still paints whatever
/// it crosses, and a wide brush along an edge paints with the pointer never on the canvas at all.
/// Those strokes committed pixels that could be neither undone nor redone.
/// </summary>
[TestFixture]
internal sealed class EdgeStrokeUndoTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private BaseTool? tool;

	private static readonly Color RedInk = new (1, 0, 0);
	private static readonly Color White = new (1, 1, 1);
	private static readonly ColorBgra WhitePixel = ColorBgra.FromBgra (255, 255, 255, 255);

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	private T Activate<T> () where T : BaseTool
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		// Toolbar widgets hand focus back to the canvas when their value changes.
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		T t = (T) System.Activator.CreateInstance (typeof (T), PintaCore.Services)!;
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private static void SetBrushWidth (BaseTool t, int width)
	{
		Gtk.SpinButton spin = (Gtk.SpinButton) typeof (BaseBrushTool)
			.GetProperty ("BrushWidthSpinButton", NonPublicInstance)!.GetValue (t)!;
		spin.Value = width;
	}

	private static ToolMouseEventArgs MouseArgs (double x, double y) => new () {
		PointDouble = new PointD (x, y),
		MouseButton = MouseButton.Left,
		// Tools that read the button off the modifier state rather than off MouseButton.
		State = Gdk.ModifierType.Button1Mask,
	};

	private void Send (string method, BaseTool t, ToolMouseEventArgs e)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (t, [Document, e]);

	/// <summary>Drags through the given points, pressing at the first and releasing at the last.</summary>
	private void Stroke (BaseTool t, params (double X, double Y)[] points)
	{
		Send ("DoMouseDown", t, MouseArgs (points[0].X, points[0].Y));
		foreach ((double x, double y) in points.Skip (1))
			Send ("DoMouseMove", t, MouseArgs (x, y));
		Send ("DoMouseUp", t, MouseArgs (points[^1].X, points[^1].Y));
	}

	private int HistoryCount () => Document.History.Items.Count ();

	[Test]
	public void WideEraserScrubbedAlongTheEdgeIsUndoable ()
	{
		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);
		EraserTool t = Activate<EraserTool> ();
		SetBrushWidth (t, 20);
		int historyBefore = HistoryCount ();
		int middleRow = CanvasSize / 2;

		// The pointer never enters the canvas; half the eraser does.
		Stroke (t, (-6, middleRow), (-4, middleRow));

		Assert.That (surface.GetColorBgra (new PointI (1, middleRow)).A,
			Is.LessThan (Red.A),
			"the on-canvas part of the eraser has to erase");
		Assert.That (HistoryCount (), Is.EqualTo (historyBefore + 1),
			"an erase that cleared pixels has to leave something to undo");
	}

	[Test]
	public void PencilDraggedClearAcrossTheCanvasIsUndoable ()
	{
		PencilTool t = Activate<PencilTool> ();
		int historyBefore = HistoryCount ();
		int middleRow = CanvasSize / 2;

		// A fast drag that is sampled off the left edge and again off the right: neither end is on
		// the canvas, but the line between them crosses the whole of it.
		Stroke (t, (-5, middleRow), (CanvasSize + 8, middleRow));

		Assert.That (Layer (0).Surface.GetColorBgra (new PointI (CanvasSize / 2, middleRow)).A,
			Is.GreaterThan (0),
			"the crossing line has to be drawn");
		Assert.That (HistoryCount (), Is.EqualTo (historyBefore + 1),
			"a line that drew pixels has to leave something to undo");
	}

	[Test]
	public void RecolorDraggedClearAcrossTheCanvasIsUndoable ()
	{
		ImageSurface surface = Layer (0).Surface;
		Fill (surface, WhitePixel);
		// Recolor repaints pixels matching the secondary color with the primary one.
		PintaCore.Palette.PrimaryColor = RedInk;
		PintaCore.Palette.SecondaryColor = White;

		RecolorTool t = Activate<RecolorTool> ();
		SetBrushWidth (t, 10);
		int historyBefore = HistoryCount ();
		int middleRow = CanvasSize / 2;

		Stroke (t, (-6, middleRow), (CanvasSize + 6, middleRow));

		Assert.That (surface.GetColorBgra (new PointI (CanvasSize / 2, middleRow)).B,
			Is.LessThan (WhitePixel.B),
			"the white the stroke crossed has to be recolored towards red");
		Assert.That (HistoryCount (), Is.EqualTo (historyBefore + 1),
			"a recolor that changed pixels has to leave something to undo");
	}

	[Test]
	public void FreeformShapeDrawnAroundTheCanvasIsUndoable ()
	{
		FreeformShapeTool t = Activate<FreeformShapeTool> ();
		SetBrushWidth (t, 4);
		int historyBefore = HistoryCount ();

		// Every point of the shape is off the canvas, but the closed path drawn between them runs
		// straight across it. The first move only anchors the drag and the second one opens the
		// path, so the points that become edges start from the third.
		int middleRow = CanvasSize / 2;
		Stroke (t,
			(-6, middleRow),
			(-6, middleRow),
			(-6, middleRow),
			(CanvasSize + 6, middleRow),
			(CanvasSize + 6, middleRow + 6));

		Assert.That (Layer (0).Surface.GetColorBgra (new PointI (CanvasSize / 2, CanvasSize / 2)).A,
			Is.GreaterThan (0),
			"the part of the shape that crosses the canvas has to be drawn");
		Assert.That (HistoryCount (), Is.EqualTo (historyBefore + 1),
			"a shape that drew pixels has to leave something to undo");
	}
}
