using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// MoveSelectedTool and MoveSelectionTool both fall back to "move the whole layer" when the drag
/// starts with no real selection, by transforming the canvas-sized placeholder DocumentSelection
/// every document carries. OnUpdateTransform/OnFinishTransform then unconditionally forced
/// Selection.Visible to true and kept the shifted geometry, turning that placeholder into a real,
/// clip-restricting selection that outlived the drag - every later paint tool (Selection.Clip has no
/// Visible check) got silently bounded to wherever the drag happened to end, with no visible marching
/// ants to explain why.
/// </summary>
[TestFixture]
internal sealed class MoveToolsPreserveSelectionStateTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private BaseTool? tool;

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

	// Same reflection-based activation TransformToolDragStateTest uses: builds and activates a real
	// tool the way ToolManager.SetCurrentTool would, without its toolbar-building side effects.
	private T Activate<T> (T t) where T : BaseTool
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private void Drag (BaseTool t, PointD from, PointD to)
	{
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (from)]);
		typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (to)]);
		typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (to)]);
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	[Test]
	public void MoveSelectionWithNoRealSelectionLeavesSelectionUntouched ()
	{
		Assert.That (Document.Selection.Visible, Is.False, "setup: a fresh document has no active selection");
		RectangleD before = Document.Selection.GetBounds ();
		int historyPointerBefore = Document.History.Pointer;

		MoveSelectionTool t = Activate (new MoveSelectionTool (PintaCore.Services));
		Drag (t, new PointD (4, 4), new PointD (12, 20));

		Assert.That (Document.Selection.Visible, Is.False,
			"dragging with nothing selected must not fabricate a real, clip-restricting selection");
		Assert.That (Document.Selection.GetBounds (), Is.EqualTo (before),
			"the canvas-sized placeholder must come out of the drag exactly as it went in");
		// CanUndo is Pointer > 0, so on an otherwise-empty history a single pushed item (Pointer 0)
		// would still read as CanUndo == false; compare against the captured pointer instead.
		Assert.That (Document.History.Pointer, Is.EqualTo (historyPointerBefore),
			"a drag that changed nothing must not push a no-op undo step");
	}

	[Test]
	public void MoveSelectionWithARealSelectionStillMovesIt ()
	{
		Document.Selection = SelectionOf (new RectangleI (0, 0, 10, 10));
		Document.Selection.Visible = true;

		MoveSelectionTool t = Activate (new MoveSelectionTool (PintaCore.Services));
		Drag (t, new PointD (4, 4), new PointD (12, 12));

		Assert.That (Document.Selection.Visible, Is.True, "a real selection must still show as active after moving");
		Assert.That (Document.Selection.GetBounds ().X, Is.EqualTo (8).Within (0.01),
			"a real selection must still actually move with the drag");
	}

	[Test]
	public void MoveSelectedPixelsWithNoRealSelectionLeavesSelectionUntouched ()
	{
		Assert.That (Document.Selection.Visible, Is.False, "setup: a fresh document has no active selection");
		RectangleD before = Document.Selection.GetBounds ();

		MoveSelectedTool t = Activate (new MoveSelectedTool (PintaCore.Services));
		Drag (t, new PointD (4, 4), new PointD (12, 20));

		Assert.That (Document.Selection.Visible, Is.False,
			"moving the whole layer must not fabricate a real, clip-restricting selection");
		Assert.That (Document.Selection.GetBounds (), Is.EqualTo (before),
			"the canvas-sized placeholder must come out of the drag exactly as it went in");
	}
}
