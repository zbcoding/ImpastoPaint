using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A mid-drag tool switch (a keyboard shortcut firing while drawing a new shape, dragging the whole
/// shape, rotating it, or changing tension) used to deactivate the shape tool with one of
/// BaseEditEngine's own gesture flags still set. HandleMouseDown's own "if (is_drawing) return;"
/// guard made every later click a no-op - Ellipse, Rectangle, RoundedLine, Triangle and Line/Curve
/// all went dead for the rest of the session.
/// </summary>
[TestFixture]
internal sealed class ShapeEditEngineDragStateTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private EllipseTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		tool.EditEngine.HandleDeactivated (null);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	// Drives BaseEditEngine directly rather than through the full BaseTool.DoActivated - EllipseTool
	// otherwise builds its own toolbar too (antialiasing button etc.), which this headless harness
	// has no shell for. HandleBuildToolBar still has to run: CreateShape reads dash_pattern_box off
	// it. ToolManager.CurrentTool has to be set too: UpdateHoverHandle dereferences it.
	private EllipseTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		EllipseTool t = new (PintaCore.Services);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		t.EditEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "ellipse");
		t.EditEngine.HandleActivated ();

		tool = t;
		return t;
	}

	private static bool IsDrawing (BaseEditEngine engine)
		=> (bool) typeof (BaseEditEngine).GetField ("is_drawing", NonPublicInstance)!.GetValue (engine)!;

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	[Test]
	public void DeactivatingMidDrawReleasesIsDrawingAndAcceptsAFreshMouseDownAfterwards ()
	{
		EllipseTool t = Activate ();
		BaseEditEngine engine = t.EditEngine;

		engine.HandleMouseDown (Document, MouseArgs (new PointD (4, 4)));
		Assert.That (IsDrawing (engine), Is.True, "setup: the press has to start drawing a new shape");

		engine.HandleDeactivated (null);

		Assert.That (IsDrawing (engine), Is.False,
			"switching tools mid-draw has to end the gesture, or the tool ignores every later click");

		// Coming back to the tool, a fresh press must start a real shape instead of being swallowed
		// by HandleMouseDown's "if (is_drawing) return;" guard.
		engine.HandleActivated ();
		engine.HandleMouseDown (Document, MouseArgs (new PointD (12, 12)));
		Assert.That (IsDrawing (engine), Is.True,
			"after returning to the tool, clicking has to start drawing again");
	}
}
