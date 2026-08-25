using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The bugs these pin surfaced around changing tools while a rectangle-select drag was live:
/// a mouse-up routed to a select tool that never saw the mouse-down crashed on
/// RectangleHandle.HasDragged ("Drag operation has not been started!"), and a tool switched away
/// mid-drag kept its handle in the dragging state forever, so every later click was swallowed by
/// OnMouseDown's IsDragging guard - the tool looked dead until the app restarted.
/// </summary>
[TestFixture]
internal sealed class SelectToolDragStateTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private SelectTool? tool;

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

	// Same reflection-based activation the other fixtures use: builds and activates a real tool
	// the way ToolManager.SetCurrentTool would, without its toolbar-building side effects.
	private T Activate<T> () where T : BaseTool
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		T t = (T?) Activator.CreateInstance (typeof (T), PintaCore.Services)
			?? throw new InvalidOperationException ($"could not construct {nameof (T)}");

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t as SelectTool;
		return t;
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	private bool HandleIsDragging (SelectTool t)
	{
		var handle = typeof (SelectTool).GetField ("handle", NonPublicInstance)!.GetValue (t)!;
		return (bool) handle.GetType ().GetProperty (nameof (RectangleHandle.IsDragging))!.GetValue (handle)!;
	}

	// A stray mouse-up (e.g. the drag began under a different tool before a shortcut switched
	// tools) has to be ignored, not crash the drag-end signal handler.
	[Test]
	public void MouseUpWithoutAMouseDownDoesNotThrow ()
	{
		RectangleSelectTool t = Activate<RectangleSelectTool> ();

		Assert.DoesNotThrow (() =>
			typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!
				.Invoke (t, [Document, MouseArgs (new PointD (8, 8))]),
			"a mouse-up with no matching mouse-down must be a no-op, not an InvalidOperationException");
	}

	// Switching away mid-drag has to release the drag: the handle must not stay in the dragging
	// state, or OnMouseDown's IsDragging guard eats every click for the rest of the session.
	[Test]
	public void DeactivatingMidDragReleasesTheHandle ()
	{
		RectangleSelectTool t = Activate<RectangleSelectTool> ();

		// Any press off the (empty) handles starts a brand-new rectangle drag.
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (new PointD (4, 4))]);
		Assert.That (HandleIsDragging (t), Is.True, "setup: the press has to start a drag");

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);

		Assert.That (HandleIsDragging (t), Is.False,
			"switching tools mid-drag has to end the drag, or the tool ignores every later click");

		// Coming back to the tool, a fresh press must start a real drag instead of being swallowed.
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (new PointD (12, 12))]);
		Assert.That (HandleIsDragging (t), Is.True,
			"after returning to the tool, clicking has to work again");
	}
}
