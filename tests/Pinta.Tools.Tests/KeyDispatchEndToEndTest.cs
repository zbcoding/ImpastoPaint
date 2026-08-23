using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cairo;
using Gdk;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// Pins docs-private/refactor.md T20, the end-to-end half of T6's unification: KeyDispatchTest
// proves the *tables* are complete and at their defaults, but nothing proved that each table
// entry's default gesture actually fires its command through the real handler - which is where
// the old hardcoded-keycode fallbacks lived (and drifted). This drives every binding's default
// gesture through BaseEditEngine.HandleKeyDown / TextTool.OnKeyDown and asserts the command's
// observable effect, so a future edit to either dispatch path that breaks a physical key fails
// here rather than in someone's muscle memory.
[TestFixture]
internal sealed class KeyDispatchEndToEndTest : ToolsTestHarness
{
	// --- The assertions -------------------------------------------------------------------------

	// Each command maps to an observable effect on a fresh engine/tool state. Commands whose
	// effect needs no setup are checked for all of them; the rest get targeted coverage below.
	[Test]
	public void EveryShapeBindingGestureFiresItsCommand ()
	{
		// BrushWidth reads/writes through the toolbar's spin button, which only exists after
		// the tool bar is built. The spin button's OnValueChanged grabs focus to the canvas,
		// so give the workspace a canvas + canvas window first (headless: plain widgets).
		Gtk.Box toolbar = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		Document.Workspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();

		foreach ((ToolBindingDescriptor binding, BaseEditEngine.ShapeKeyCommand command) in BaseEditEngine.shape_key_bindings) {
			RectangleEditEngine engine = CreateShapeEngine ();
			engine.HandleBuildToolBar (toolbar, PintaCore.Settings, "shapetest");

			bool handled = engine.HandleKeyDown (Document, GestureEventArgs (binding.DefaultGesture));

			switch (command) {
				case BaseEditEngine.ShapeKeyCommand.BrushDecreaseWidth:
					Assert.That (handled, Is.True, $"{binding.Id}: key should be handled");
					Assert.That (engine.BrushWidth, Is.EqualTo (DefaultBrushWidth - 1), binding.Id);
					break;

				case BaseEditEngine.ShapeKeyCommand.BrushIncreaseWidth:
					Assert.That (handled, Is.True, $"{binding.Id}: key should be handled");
					Assert.That (engine.BrushWidth, Is.EqualTo (DefaultBrushWidth + 1), binding.Id);
					break;

				case BaseEditEngine.ShapeKeyCommand.MovePointLeft:
				case BaseEditEngine.ShapeKeyCommand.MovePointRight:
				case BaseEditEngine.ShapeKeyCommand.MovePointUp:
				case BaseEditEngine.ShapeKeyCommand.MovePointDown:
				case BaseEditEngine.ShapeKeyCommand.SelectPrevPoint:
				case BaseEditEngine.ShapeKeyCommand.SelectNextPoint:
					Assert.That (handled, Is.True, $"{binding.Id}: key should be handled");
					// Nudging/moving requires a selected point; with none selected these are
					// consumed but must not throw. The nudge path itself is covered via the
					// selected-point case in NudgeMovesTheSelectedPoint.
					break;

				default:
					// DeletePoint/Finalize/AddPoint*/CreateNewAtPoint act on live editing state
					// (history pushes, commits) that a headless harness can't fully drive; the
					// contract under test here is dispatch, i.e. "this physical key reaches the
					// command", not each command's full behavior.
					Assert.That (handled, Is.True, $"{binding.Id} ({command}): key should be dispatched to the command");
					break;
			}
		}
	}

	[Test]
	public void EveryTextBindingGestureIsHandledWhileEditing ()
	{
		TextTool tool = ActivateTextTool ();
		try {
			TextObject obj = AddEditableText (tool);

			foreach ((ToolBindingDescriptor binding, TextTool.TextKeyCommand command) in TextTool.text_key_bindings) {
				// Paste/Copy touch the system clipboard; skip them headlessly rather than
				// assert on global state other tests could race with.
				if (command is TextTool.TextKeyCommand.Paste or TextTool.TextKeyCommand.Copy)
					continue;

				// Escape commits and ends the session; exercised separately below.
				if (command == TextTool.TextKeyCommand.StopEditing)
					continue;

				// Ctrl+Z commits the session as a side effect (text undo is "commit what you
				// had"), so restart editing before and after it.
				// Any surface clone outside the tool's ignore window commits the session
				// (HandleLayerCloned), and Ctrl+Z commits deliberately - so re-enter editing
				// whenever a previous command ended it.
				if (!IsEditing (tool))
					RestartEditing (tool, obj);

				bool handled = tool.DoKeyDown (Document, GestureEventArgs (binding.DefaultGesture));
				Assert.That (handled, Is.True, $"{binding.Id} ({command}): key should be handled while editing (editing={IsEditing (tool)})");

				// Italic/Bold/Underline mutate font flags; Undo commits history. Reset to a clean
				// single-line object so later commands start from a known state.
				if (command is TextTool.TextKeyCommand.Italic or TextTool.TextKeyCommand.Bold
					or TextTool.TextKeyCommand.Underline or TextTool.TextKeyCommand.Undo) {
					obj.Engine.Clear ();
					obj.Engine.InsertText ("hello");
				}
			}

			Assert.That (obj.Engine.Lines.Count, Is.EqualTo (1),
				"no newline-firing key ran in this loop, so the text must still be one line");
		} finally {
			DeactivateTool (tool);
		}
	}

	[Test]
	public void FontSizeGesturesResizeTheFontWhileNotEditing ()
	{
		TextTool tool = ActivateTextTool ();
		try {
			int before = CurrentFontSize (tool);

			Assert.That (tool.DoKeyDown (Document, GestureEventArgs (KeyboardShortcutManager.TextDecreaseFontSize.DefaultGesture)), Is.True);
			Assert.That (CurrentFontSize (tool), Is.LessThan (before), "decrease gesture should shrink the toolbar font size");

			Assert.That (tool.DoKeyDown (Document, GestureEventArgs (KeyboardShortcutManager.TextIncreaseFontSize.DefaultGesture)), Is.True);
			Assert.That (CurrentFontSize (tool), Is.EqualTo (before), "increase gesture should restore the toolbar font size");
		} finally {
			DeactivateTool (tool);
		}
	}

	// The aliasing quirk the fallback switches exist for, pinned concretely: KP_Enter must fire
	// NewLine even though it only aliases Return's gesture, not equals it.
	[Test]
	public void KpEnterFiresNewLineLikeReturn ()
	{
		TextTool tool = ActivateTextTool ();
		try {
			TextObject obj = AddEditableText (tool);

			bool handled = tool.DoKeyDown (Document, GestureEventArgs (
				new KeyGesture (new Key (Constants.KEY_KP_Enter))));

			Assert.That (handled, Is.True, "KP_Enter should be handled as NewLine");
			Assert.That (obj.Engine.Lines.Count, Is.GreaterThanOrEqualTo (2),
				"KP_Enter should split the line exactly like Return does");
		} finally {
			DeactivateTool (tool);
		}
	}

	[Test]
	public void EscapeCommitsAndStopsEditing ()
	{
		TextTool tool = ActivateTextTool ();
		try {
			TextObject obj = AddEditableText (tool);
			obj.Engine.InsertText ("hello");
			obj.Engine.SetCursorPosition (new TextPosition (0, 5), clearSelection: true);

			bool handled = tool.DoKeyDown (Document, GestureEventArgs (KeyboardShortcutManager.TextStopEditing.DefaultGesture));

			Assert.That (handled, Is.True, "Escape should be handled");
			var isEditing = typeof (TextTool).GetField ("is_editing", NonPublicInstance)!;
			Assert.That (isEditing.GetValue (tool), Is.False, "Escape should have ended the editing session");
		} finally {
			DeactivateTool (tool);
		}
	}

	// A rebound command must NOT fire from its old physical key via the fallback switch - that
	// guard is exactly what the IsDefault checks encode.
	[Test]
	public void ReboundCommandDoesNotFireFromItsOldDefaultKey ()
	{
		Gtk.Box toolbar = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		Document.Workspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();
		RectangleEditEngine engine = CreateShapeEngine ();
		engine.HandleBuildToolBar (toolbar, PintaCore.Settings, "shapetest");

		try {
			PintaCore.Shortcuts.SetToolBinding (
				KeyboardShortcutManager.BrushIncreaseWidth,
				new KeyGesture (new Key (Constants.KEY_bracketright), ModifierType.ShiftMask));

			bool handled = engine.HandleKeyDown (Document, GestureEventArgs (
				new KeyGesture (new Key (Constants.KEY_bracketright))));

			Assert.That (handled, Is.False,
				"after rebinding, the old default key must fall through instead of firing the command");
			Assert.That (engine.BrushWidth, Is.EqualTo (DefaultBrushWidth),
				"and it certainly must not change the brush width");
		} finally {
			PintaCore.Shortcuts.ResetToolBinding (KeyboardShortcutManager.BrushIncreaseWidth);
		}
	}

	[Test]
	public void ReboundCommandsNewKeyFiresThroughTheBindingPath ()
	{
		Gtk.Box toolbar = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		Document.Workspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();
		RectangleEditEngine engine = CreateShapeEngine ();
		engine.HandleBuildToolBar (toolbar, PintaCore.Settings, "shapetest");

		try {
			PintaCore.Shortcuts.SetToolBinding (
				KeyboardShortcutManager.BrushIncreaseWidth,
				new KeyGesture (new Key (Constants.KEY_bracketright), ModifierType.ShiftMask));

			bool handled = engine.HandleKeyDown (Document, GestureEventArgs (
				new KeyGesture (new Key (Constants.KEY_bracketright), ModifierType.ShiftMask)));

			Assert.That (handled, Is.True, "the new key should fire the command through the binding path");
			Assert.That (engine.BrushWidth, Is.EqualTo (DefaultBrushWidth + 1));
		} finally {
			PintaCore.Shortcuts.ResetToolBinding (KeyboardShortcutManager.BrushIncreaseWidth);
		}
	}

	// Nudge with an actual selection: the one shape-command effect asserted end-to-end.
	[Test]
	public void NudgeMovesTheSelectedPoint ()
	{
		UserLayer layer = Layer (0);
		ShapeObject source = Box (ShapeFill, new RectangleI (4, 4, CanvasSize - 8, CanvasSize - 8));
		layer.AddShape (source);

		// The nudge's redraw path dereferences tools.CurrentTool for cursor updates.
		RectangleTool tool = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [tool]);

		try {
			// Enter the editing state the way a shape tool does: rebuild the live engines from the
			// layer's objects and bind them to this layer. After this, SEngines[0] is live.
			BaseEditEngine.ReloadLayerShapes (layer);

			// The nudge's redraw sets the canvas cursor through the active workspace.
			PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

			BaseEditEngine editEngine = (tool.EditEngine as BaseEditEngine)!;
			editEngine.SelectedShapeIndex = 0;
			editEngine.SelectedPointIndex = 0;
			PointD before = editEngine.ActiveShapeEngine!.ControlPoints[0].Position;

			bool handled = editEngine.HandleKeyDown (Document, GestureEventArgs (KeyboardShortcutManager.ShapeMovePointRight.DefaultGesture));

			Assert.Multiple (() => {
				Assert.That (handled, Is.True);
				Assert.That (editEngine.ActiveShapeEngine!.ControlPoints[0].Position.X,
					Is.EqualTo (before.X + 1d), "Right should nudge the selected point +1px");
				Assert.That (editEngine.ActiveShapeEngine!.ControlPoints[0].Position.Y, Is.EqualTo (before.Y));
				Assert.That (layer.ShapeObjects[0].ControlPoints[0].Position.X,
					Is.EqualTo (before.X + 1d), "the nudged geometry must persist back into the layer's stored object");
			});
		} finally {
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			BaseEditEngine.SEngines.Clear ();
		}
	}

	// --- Plumbing -------------------------------------------------------------------------------

	private static readonly Color ShapeFill = new (0, 1, 0, 1);
	private const int DefaultBrushWidth = BaseTool.DEFAULT_BRUSH_WIDTH;
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private static ToolKeyEventArgs GestureEventArgs (KeyGesture gesture)
		=> new () { Key = gesture.Key, State = gesture.Modifiers };

	private RectangleEditEngine CreateShapeEngine ()
		=> new (PintaCore.Services, new RectangleTool (PintaCore.Services));

	private TextTool ActivateTextTool ()
	{
		TextTool t = new (PintaCore.Services);

		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		return t;
	}

	// Every TextTool ctor subscribes the static LayerObjectSelection.TextSelectRequested and never
	// unsubscribes (tools are app-lifetime singletons in the real app). Headless tests construct
	// many short-lived tools, so dispose of the subscription explicitly or a later fixture's
	// RequestTextSelect fans out into every dead tool from earlier fixtures.
	private void DeactivateTool (TextTool tool)
	{
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		var handler = typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance);
		if (handler is not null)
			LayerObjectSelection.TextSelectRequested -= handler.CreateDelegate<Action<UserLayer, int>> (tool);
	}

	// Starts an editing session on a fresh object, the way a first click with the text tool would.
	private TextObject AddEditableText (TextTool tool)
	{
		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		Layer (0).AddText (obj);

		RestartEditing (tool, obj);

		return obj;
	}

	// Re-enters the editing session on an existing object after something ended it
	// (e.g. Ctrl+Z committing the text).
	private void RestartEditing (TextTool tool, TextObject obj)
		=> typeof (TextTool).GetMethod ("StartEditing", NonPublicInstance)!.Invoke (tool, [obj, false]);

	private static bool IsEditing (TextTool tool)
		=> (bool) (typeof (TextTool).GetField ("is_editing", NonPublicInstance)!.GetValue (tool) ?? false);

	private static int CurrentFontSize (TextTool tool)
	{
		var field = typeof (TextTool).GetField ("font_size", NonPublicInstance)!;
		Gtk.SpinButton font_size = (Gtk.SpinButton) field.GetValue (tool)!;
		return (int) font_size.Adjustment!.Value;
	}
}
