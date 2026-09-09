using System;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The bug this pins: a rectangle-select drag started at the canvas's top-left corner selected the
/// whole canvas instead of drawing a new rectangle.
///
/// <para>
/// The selection's handle rectangle keeps its bounds while the selection is hidden, and
/// <see cref="Document.ResetSelectionPaths"/> — which every Deselect and every freshly opened
/// document runs — leaves it covering the whole canvas. So eight resize grips sat at the canvas
/// corners and edge midpoints, undrawn but still hit-tested, with the UpperLeft one exactly on
/// canvas (0,0). A press there grabbed that invisible grip and dragged out (cursor)→(W,H).
/// </para>
/// </summary>
[TestFixture]
internal sealed class SelectToolHiddenHandleGrabTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private BaseTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		Invoke (tool, "DoDeactivated", Document, null);
		SetCurrentTool (null);
		tool = null;
	}

	// Same reflection-based activation the other tool fixtures use: builds and activates a real
	// tool the way ToolManager.SetCurrentTool would, without its toolbar-building side effects.
	// OnActivated is what loads the handle rectangle from the document, so it has to run.
	private T Activate<T> () where T : BaseTool
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		T t = (T?) Activator.CreateInstance (typeof (T), PintaCore.Services)
			?? throw new InvalidOperationException ($"could not construct {typeof (T).Name}");

		// Initializes the toolbar's selection-mode combo, which is what sets the combine mode to
		// Replace — the default a plain left click resolves to.
		Invoke (t, "DoBuildToolBar", Gtk.Box.New (Gtk.Orientation.Horizontal, 0));
		Invoke (t, "DoActivated", Document);
		SetCurrentTool (t);

		tool = t;
		return t;
	}

	private static void Invoke (BaseTool target, string method, params object?[] args)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (target, args);

	private static void SetCurrentTool (BaseTool? t)
		=> typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

	private static ToolMouseEventArgs MouseArgs (double x, double y) => new () {
		PointDouble = new PointD (x, y),
		MouseButton = MouseButton.Left,
	};

	private void Drag (BaseTool t, PointD from, PointD to)
	{
		t.DoMouseDown (Document, MouseArgs (from.X, from.Y));
		t.DoMouseMove (Document, MouseArgs (to.X, to.Y));
		t.DoMouseUp (Document, MouseArgs (to.X, to.Y));
	}

	// A drag out of the canvas origin has to produce the dragged rectangle. Before the fix it
	// produced the whole canvas: the press grabbed the hidden UpperLeft grip of the full-canvas
	// handle rect, so the "drag" was a resize anchored at the canvas's bottom-right corner.
	[Test]
	public void DraggingFromTheCanvasOriginSelectsOnlyTheDraggedRectangle ()
	{
		// What a Deselect (or a newly opened document) leaves behind: handle bounds covering the
		// whole canvas, with the selection itself hidden.
		Document.ResetSelectionPaths ();
		Assert.That (Document.Selection.Visible, Is.False, "setup: the selection has to start hidden");

		RectangleSelectTool t = Activate<RectangleSelectTool> ();

		Drag (t, new PointD (0, 0), new PointD (8, 8));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (0, 0, 8, 8)),
			"a drag from the canvas origin has to select the dragged rectangle, not the whole canvas");
	}

	// The same press one grip-radius away from the origin: still inside the invisible UpperLeft
	// grip's tolerance box, so it took the same path.
	[Test]
	public void DraggingFromJustInsideTheCornerGripSelectsOnlyTheDraggedRectangle ()
	{
		Document.ResetSelectionPaths ();

		RectangleSelectTool t = Activate<RectangleSelectTool> ();

		Drag (t, new PointD (3, 3), new PointD (12, 12));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (3, 3, 9, 9)),
			"a press within the hidden grip's tolerance must still start a new rectangle");
	}

	// The grips exist to resize a selection the user can see, so a visible selection must still be
	// resizable by them — the fix gates on visibility, it does not disable the grips.
	[Test]
	public void AVisibleSelectionIsStillResizableByItsCornerGrip ()
	{
		RectangleSelectTool t = Activate<RectangleSelectTool> ();

		// Drag out a selection, then drag its UpperLeft grip inwards.
		Drag (t, new PointD (4, 4), new PointD (20, 20));
		Assert.That (Document.Selection.Visible, Is.True, "setup: the first drag has to leave a visible selection");

		Drag (t, new PointD (4, 4), new PointD (10, 10));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (10, 10, 10, 10)),
			"grabbing a visible selection's corner grip has to resize it, and shrinking must actually shrink it");
	}
}
