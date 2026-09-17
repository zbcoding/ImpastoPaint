using System.Collections.Generic;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The paintbrush strokes onto the tool layer and only commits to the layer on mouse up, which
/// leaves two things to the commit: the canvas repaint for the swap (nothing else asks for one when
/// the pointer is at rest), and the history entry. Both used to key off the pointer being inside
/// the canvas rather than off what the stroke painted.
/// </summary>
[TestFixture]
internal sealed class PaintBrushStrokeCommitTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private PaintBrushTool? tool;

	[SetUp]
	public void RegisterBrush ()
	{
		// The tool's brush list comes from the service, which only CoreToolsExtension fills, and it
		// draws nothing at all without an active brush.
		PintaCore.PaintBrushes.AddPaintBrush (new Brushes.PlainBrush (PintaCore.Workspace));
	}

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is not null) {
			typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			tool = null;
		}

		PintaCore.PaintBrushes.RemoveInstanceOfPaintBrush (typeof (Brushes.PlainBrush));
	}

	private PaintBrushTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		// The brush width spin button hands focus back to the canvas when its value changes.
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		PaintBrushTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	private static void Send (string method, BaseTool t, ToolMouseEventArgs e, Document doc)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (t, [doc, e]);

	[Test]
	public void StrokePaintedWithThePointerOutsideTheCanvasIsUndoable ()
	{
		PaintBrushTool t = Activate ();
		SetBrushWidth (t, 20);
		int historyBefore = CountHistoryItems ();
		int middleRow = CanvasSize / 2;

		// A wide brush dragged just off the left edge: the pointer never enters the canvas, but half
		// the brush does, so pixels change and there has to be something to undo.
		Send ("DoMouseDown", t, MouseArgs (new PointD (-6, middleRow)), Document);
		Send ("DoMouseMove", t, MouseArgs (new PointD (-4, middleRow)), Document);
		Send ("DoMouseUp", t, MouseArgs (new PointD (-4, middleRow)), Document);

		Assert.That (Layer (0).Surface.GetColorBgra (new PointI (1, middleRow)).A,
			Is.GreaterThan (0),
			"the on-canvas part of the stroke has to be painted");
		Assert.That (CountHistoryItems (),
			Is.EqualTo (historyBefore + 1),
			"a stroke that painted pixels has to leave something to undo");
	}

	private static void SetBrushWidth (PaintBrushTool t, int width)
	{
		Gtk.SpinButton spin = (Gtk.SpinButton) typeof (BaseBrushTool)
			.GetProperty ("BrushWidthSpinButton", NonPublicInstance)!.GetValue (t)!;
		spin.Value = width;
	}

	[Test]
	public void CommittingTheStrokeRepaintsWhatItPainted ()
	{
		PaintBrushTool t = Activate ();
		int middleRow = CanvasSize / 2;

		Send ("DoMouseDown", t, MouseArgs (new PointD (4, middleRow)), Document);
		Send ("DoMouseMove", t, MouseArgs (new PointD (CanvasSize - 4, middleRow)), Document);

		// The layer paints straight from its own raster here (no mask, no modifier nodes), so the
		// history push's composite refresh does not invalidate on this path - the commit must.
		List<CanvasInvalidatedEventArgs> repaints = [];
		void Record (object? _, CanvasInvalidatedEventArgs args) => repaints.Add (args);
		Document.Workspace.CanvasInvalidated += Record;
		try {
			Send ("DoMouseUp", t, MouseArgs (new PointD (CanvasSize - 4, middleRow)), Document);
		} finally {
			Document.Workspace.CanvasInvalidated -= Record;
		}

		Assert.That (repaints, Is.Not.Empty, "committing the stroke has to repaint the canvas");
		Assert.That (
			repaints.Exists (r => r.EntireSurface || r.Rectangle.Contains (new PointI (CanvasSize / 2, middleRow))),
			Is.True,
			"the repaint has to cover the pixels the stroke committed");
	}

	private int CountHistoryItems ()
	{
		int count = 0;
		foreach (BaseHistoryItem _ in Document.History.Items)
			count++;
		return count;
	}
}
