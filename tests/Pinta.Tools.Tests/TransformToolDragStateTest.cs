using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A mid-drag tool switch (a keyboard shortcut firing while dragging, rotating, scaling, or handle-
/// scaling) used to deactivate BaseTransformTool with one of those flags still set. IsActive stayed
/// true forever after, and OnMouseDown's own "if (IsActive) return;" guard made every later click a
/// no-op - Move Selected Pixels and Move Selection went dead for the rest of the session.
/// </summary>
[TestFixture]
internal sealed class TransformToolDragStateTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private MoveSelectionTool? tool;

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

	// Same reflection-based activation the other tool fixtures use: builds and activates a real
	// tool the way ToolManager.SetCurrentTool would, without its toolbar-building side effects.
	private MoveSelectionTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		MoveSelectionTool t = new (PintaCore.Services);

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private static bool IsDragging (BaseTransformTool t)
		=> (bool) typeof (BaseTransformTool).GetField ("is_dragging", NonPublicInstance)!.GetValue (t)!;

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	[Test]
	public void DeactivatingMidDragReleasesIsDraggingAndAcceptsAFreshMouseDownAfterwards ()
	{
		MoveSelectionTool t = Activate ();

		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (new PointD (4, 4))]);
		Assert.That (IsDragging (t), Is.True, "setup: the press has to start a drag");

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);

		Assert.That (IsDragging (t), Is.False,
			"switching tools mid-drag has to end the drag, or IsActive stays true and the tool ignores every later click");

		// Coming back to the tool, a fresh press must start a real drag instead of being swallowed
		// by OnMouseDown's "if (IsActive) return;" guard.
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!
			.Invoke (t, [Document, MouseArgs (new PointD (12, 12))]);
		Assert.That (IsDragging (t), Is.True,
			"after returning to the tool, clicking has to work again");
	}
}
