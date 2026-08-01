//
// KeyboardShortcutManager.cs
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pinta.Core;

/// <summary>
/// Impasto: a single-key binding used inside a tool's own key handling (e.g. Enter /
/// Backspace / Escape while drawing a Scissors Select outline), as opposed to the
/// app-wide Commands or the toolbox activation key.
/// </summary>
public sealed record ToolBindingDescriptor (
	string Id,
	string TabName,
	string Label,
	KeyGesture DefaultGesture);

/// <summary>
/// Stores user overrides for command shortcuts, toolbox activation keys, and
/// tool-specific key bindings, and keeps GTK's accelerators in sync with them.
///
/// Overrides are kept in a dedicated, human-editable JSON file rather than the
/// general settings.xml blob, so a user can hand-edit or share their keymap. If
/// that file is missing or malformed, defaults are used and nothing crashes.
/// </summary>
public sealed class KeyboardShortcutManager
{
	private const string SHORTCUTS_FILE = "keyboard-shortcuts.json";

	private readonly ISettingsService settings;
	private readonly ActionManager actions;
	private readonly ToolManager tools;
	private readonly ChromeManager chrome;

	private readonly Dictionary<string, string> command_overrides = [];
	private readonly Dictionary<string, string> tool_overrides = [];
	private readonly Dictionary<string, string> binding_overrides = [];

	/// <summary>
	/// Known tool-specific bindings, grouped by dialog tab. Add an entry here
	/// whenever a tool's own OnKeyDown should be user-configurable.
	/// </summary>
	public static readonly ToolBindingDescriptor LassoFinalize = new (
		"LassoSelect.Finalize",
		Translations.GetString ("Lasso / Scissors Select"),
		Translations.GetString ("Finish selection"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Return)));

	public static readonly ToolBindingDescriptor LassoBacktrack = new (
		"LassoSelect.Backtrack",
		Translations.GetString ("Lasso / Scissors Select"),
		Translations.GetString ("Undo last point"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_BackSpace)));

	public static readonly ToolBindingDescriptor LassoCancel = new (
		"LassoSelect.Cancel",
		Translations.GetString ("Lasso / Scissors Select"),
		Translations.GetString ("Cancel selection"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Escape)));

	// Text Tool
	public static readonly ToolBindingDescriptor TextStopEditing = new (
		"TextTool.StopEditing",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Stop editing / Finalize text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Escape)));

	public static readonly ToolBindingDescriptor TextNewLine = new (
		"TextTool.NewLine",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Insert new line"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Return)));

	public static readonly ToolBindingDescriptor TextBackspace = new (
		"TextTool.Backspace",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Delete character left of cursor"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_BackSpace)));

	public static readonly ToolBindingDescriptor TextDelete = new (
		"TextTool.Delete",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Delete character right of cursor"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Delete)));

	public static readonly ToolBindingDescriptor TextMoveLeft = new (
		"TextTool.MoveLeft",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor left"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left)));

	public static readonly ToolBindingDescriptor TextMoveRight = new (
		"TextTool.MoveRight",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor right"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right)));

	public static readonly ToolBindingDescriptor TextMoveUp = new (
		"TextTool.MoveUp",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor up"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up)));

	public static readonly ToolBindingDescriptor TextMoveDown = new (
		"TextTool.MoveDown",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor down"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down)));

	public static readonly ToolBindingDescriptor TextMoveHome = new (
		"TextTool.MoveHome",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor to line start"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Home)));

	public static readonly ToolBindingDescriptor TextMoveEnd = new (
		"TextTool.MoveEnd",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Move cursor to line end"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_End)));

	public static readonly ToolBindingDescriptor TextUndo = new (
		"TextTool.Undo",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Undo last text edit"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Z), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextItalic = new (
		"TextTool.Italic",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Toggle italic"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_I), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextBold = new (
		"TextTool.Bold",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Toggle bold"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_B), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextUnderline = new (
		"TextTool.Underline",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Toggle underline"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_U), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextSelectAll = new (
		"TextTool.SelectAll",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Select all text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_A), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextPaste = new (
		"TextTool.Paste",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Paste text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Insert), Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TextCopy = new (
		"TextTool.Copy",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Copy text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Insert), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextDecreaseFontSize = new (
		"TextTool.DecreaseFontSize",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Decrease font size"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_bracketleft)));

	public static readonly ToolBindingDescriptor TextIncreaseFontSize = new (
		"TextTool.IncreaseFontSize",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Increase font size"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_bracketright)));

	public static readonly ToolBindingDescriptor TextReEdit = new (
		"TextTool.ReEdit",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Re-edit existing text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Pointer_Button1), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TextOpenProperties = new (
		"TextTool.OpenTextProperties",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Open text properties"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Pointer_Button1), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TextResize = new (
		"TextTool.Resize",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Resize text (drag corner, changes font size)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Pointer_Button1)));

	public static readonly ToolBindingDescriptor TextRotate = new (
		"TextTool.Rotate",
		Translations.GetString ("Text Tool"),
		Translations.GetString ("Rotate text"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Pointer_Button1), Gdk.ModifierType.AltMask));

	// Shared brush and shape width controls
	public static readonly ToolBindingDescriptor BrushDecreaseWidth = new (
		"BrushTools.DecreaseWidth",
		Translations.GetString ("Brush Tools"),
		Translations.GetString ("Decrease brush width"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_bracketleft)));

	public static readonly ToolBindingDescriptor BrushIncreaseWidth = new (
		"BrushTools.IncreaseWidth",
		Translations.GetString ("Brush Tools"),
		Translations.GetString ("Increase brush width"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_bracketright)));

	// Global shortcuts that are not represented by an AppAction.
	public static readonly ToolBindingDescriptor SwapColors = new (
		"Palette.SwapColors",
		Translations.GetString ("General"),
		Translations.GetString ("Swap primary and secondary colors"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_X)));

	public static readonly ToolBindingDescriptor SwitchDocument1 = CreateDocumentBinding (1);
	public static readonly ToolBindingDescriptor SwitchDocument2 = CreateDocumentBinding (2);
	public static readonly ToolBindingDescriptor SwitchDocument3 = CreateDocumentBinding (3);
	public static readonly ToolBindingDescriptor SwitchDocument4 = CreateDocumentBinding (4);
	public static readonly ToolBindingDescriptor SwitchDocument5 = CreateDocumentBinding (5);
	public static readonly ToolBindingDescriptor SwitchDocument6 = CreateDocumentBinding (6);
	public static readonly ToolBindingDescriptor SwitchDocument7 = CreateDocumentBinding (7);
	public static readonly ToolBindingDescriptor SwitchDocument8 = CreateDocumentBinding (8);
	public static readonly ToolBindingDescriptor SwitchDocument9 = CreateDocumentBinding (9);

	private static ToolBindingDescriptor CreateDocumentBinding (int index)
		=> new (
			$"Window.SwitchDocument{index}",
			Translations.GetString ("Window"),
			Translations.GetString ("Switch to document {0}", index),
			new KeyGesture (new Gdk.Key ((uint) (Gdk.Constants.KEY_0 + index)), Gdk.ModifierType.AltMask));

	// Gradient Tool
	public static readonly ToolBindingDescriptor GradientFinalize = new (
		"GradientTool.Finalize",
		Translations.GetString ("Gradient Tool"),
		Translations.GetString ("Finalize gradient"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Return)));

	// Shape Tools (Line/Curve, Rectangle, Ellipse, etc.)
	public static readonly ToolBindingDescriptor ShapeFinalize = new (
		"ShapeTool.Finalize",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Finalize shape"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Return)));

	public static readonly ToolBindingDescriptor ShapeDeletePoint = new (
		"ShapeTool.DeletePoint",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Delete selected control point"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Delete)));

	public static readonly ToolBindingDescriptor ShapeAddPoint = new (
		"ShapeTool.AddPoint",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Add control point at mouse position"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_space)));

	public static readonly ToolBindingDescriptor ShapeAddPointExact = new (
		"ShapeTool.AddPointExact",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Add control point at exact same position"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_space), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor ShapeMovePointLeft = new (
		"ShapeTool.MovePointLeft",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Move selected control point left"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left)));

	public static readonly ToolBindingDescriptor ShapeMovePointRight = new (
		"ShapeTool.MovePointRight",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Move selected control point right"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right)));

	public static readonly ToolBindingDescriptor ShapeMovePointUp = new (
		"ShapeTool.MovePointUp",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Move selected control point up"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up)));

	public static readonly ToolBindingDescriptor ShapeMovePointDown = new (
		"ShapeTool.MovePointDown",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Move selected control point down"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down)));

	public static readonly ToolBindingDescriptor ShapeSelectPrevPoint = new (
		"ShapeTool.SelectPrevPoint",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Select previous control point"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor ShapeSelectNextPoint = new (
		"ShapeTool.SelectNextPoint",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Select next control point"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor ShapeCreateNewAtPoint = new (
		"ShapeTool.CreateNewAtPoint",
		Translations.GetString ("Shape Tools"),
		Translations.GetString ("Create new shape at selected control point"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left), Gdk.ModifierType.ControlMask)); // or Right

	public static readonly ToolBindingDescriptor TriangleTypeSwitch = new (
		"TriangleTool.TypeSwitch",
		Translations.GetString ("Triangle Tool"),
		Translations.GetString ("Switch between right and equilateral triangle while drawing"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Shift_L)));


	// Transform Tools (Move Selection, Move Layer, etc.)
	public static readonly ToolBindingDescriptor TransformNudgeLeft = new (
		"TransformTool.NudgeLeft",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection left (1px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left)));

	public static readonly ToolBindingDescriptor TransformNudgeRight = new (
		"TransformTool.NudgeRight",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection right (1px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right)));

	public static readonly ToolBindingDescriptor TransformNudgeUp = new (
		"TransformTool.NudgeUp",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection up (1px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up)));

	public static readonly ToolBindingDescriptor TransformNudgeDown = new (
		"TransformTool.NudgeDown",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection down (1px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down)));

	public static readonly ToolBindingDescriptor TransformNudgeLeftLarge = new (
		"TransformTool.NudgeLeftLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection left (10px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left), Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeRightLarge = new (
		"TransformTool.NudgeRightLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection right (10px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right), Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeUpLarge = new (
		"TransformTool.NudgeUpLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection up (10px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up), Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeDownLarge = new (
		"TransformTool.NudgeDownLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection down (10px)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down), Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeLeftPct = new (
		"TransformTool.NudgeLeftPct",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection left (5% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TransformNudgeRightPct = new (
		"TransformTool.NudgeRightPct",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection right (5% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TransformNudgeUpPct = new (
		"TransformTool.NudgeUpPct",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection up (5% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TransformNudgeDownPct = new (
		"TransformTool.NudgeDownPct",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection down (5% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down), Gdk.ModifierType.ControlMask));

	public static readonly ToolBindingDescriptor TransformNudgeLeftPctLarge = new (
		"TransformTool.NudgeLeftPctLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection left (20% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Left), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeRightPctLarge = new (
		"TransformTool.NudgeRightPctLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection right (20% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Right), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeUpPctLarge = new (
		"TransformTool.NudgeUpPctLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection up (20% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Up), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly ToolBindingDescriptor TransformNudgeDownPctLarge = new (
		"TransformTool.NudgeDownPctLarge",
		Translations.GetString ("Transform Tools"),
		Translations.GetString ("Nudge selection down (20% canvas)"),
		new KeyGesture (new Gdk.Key (Gdk.Constants.KEY_Down), Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask));

	public static readonly IReadOnlyList<ToolBindingDescriptor> ToolBindings = [
		LassoFinalize,
		LassoBacktrack,
		LassoCancel,
		TextStopEditing,
		TextNewLine,
		TextBackspace,
		TextDelete,
		TextMoveLeft,
		TextMoveRight,
		TextMoveUp,
		TextMoveDown,
		TextMoveHome,
		TextMoveEnd,
		TextUndo,
		TextItalic,
		TextBold,
		TextUnderline,
		TextSelectAll,
		TextPaste,
		TextCopy,
		TextDecreaseFontSize,
		TextIncreaseFontSize,
		TextReEdit,
		TextOpenProperties,
		TextResize,
		TextRotate,
		BrushDecreaseWidth,
		BrushIncreaseWidth,
		SwapColors,
		SwitchDocument1,
		SwitchDocument2,
		SwitchDocument3,
		SwitchDocument4,
		SwitchDocument5,
		SwitchDocument6,
		SwitchDocument7,
		SwitchDocument8,
		SwitchDocument9,
		GradientFinalize,
		ShapeFinalize,
		ShapeDeletePoint,
		ShapeAddPoint,
		ShapeAddPointExact,
		ShapeMovePointLeft,
		ShapeMovePointRight,
		ShapeMovePointUp,
		ShapeMovePointDown,
		ShapeSelectPrevPoint,
		ShapeSelectNextPoint,
		ShapeCreateNewAtPoint,
		TriangleTypeSwitch,
		TransformNudgeLeft,
		TransformNudgeRight,
		TransformNudgeUp,
		TransformNudgeDown,
		TransformNudgeLeftLarge,
		TransformNudgeRightLarge,
		TransformNudgeUpLarge,
		TransformNudgeDownLarge,
		TransformNudgeLeftPct,
		TransformNudgeRightPct,
		TransformNudgeUpPct,
		TransformNudgeDownPct,
		TransformNudgeLeftPctLarge,
		TransformNudgeRightPctLarge,
		TransformNudgeUpPctLarge,
		TransformNudgeDownPctLarge,
	];

	public event EventHandler? ShortcutsChanged;

	public KeyboardShortcutManager (ActionManager actions, ToolManager tools, ChromeManager chrome, ISettingsService settings)
	{
		this.actions = actions;
		this.tools = tools;
		this.chrome = chrome;
		this.settings = settings;
	}

	private string ShortcutsFilePath
		=> Path.Combine (settings.GetUserSettingsDirectory (), SHORTCUTS_FILE);

	/// <summary>
	/// Loads overrides from disk (if any) and applies them to the already-registered
	/// commands and tools. Call once at startup, after commands have been added to
	/// the Gtk.Application.
	/// </summary>
	public void LoadAndApply ()
	{
		Load ();

		foreach (var command in AllCommands ())
			if (command_overrides.TryGetValue (command.Name, out var accel))
				ApplyCommandShortcut (command, accel.Length == 0 ? [] : [accel]);

		// Tools are registered asynchronously via the add-in extension mechanism,
		// which can run after this method. Apply overrides by tool type name rather
		// than iterating the (possibly still-empty) registered tool set so the saved
		// toolbox activation-key overrides are never silently dropped at startup.
		foreach (var kv in tool_overrides)
			if (KeyGesture.TryParse (kv.Value) is KeyGesture gesture)
				this.tools.SetShortcutKeyOverride (kv.Key, gesture);
	}

	/// <summary>
	/// A snapshot of the user's shortcut overrides, matching the on-disk shape of
	/// keyboard-shortcuts.json. Used to bundle shortcuts into a settings export.
	/// </summary>
	public sealed class ShortcutFile
	{
		public Dictionary<string, string>? Commands { get; set; }
		public Dictionary<string, string>? Tools { get; set; }
		public Dictionary<string, string>? ToolBindings { get; set; }
	}

	public ShortcutFile ExportOverrides ()
		=> new () {
			Commands = new (command_overrides),
			Tools = new (tool_overrides),
			ToolBindings = new (binding_overrides),
		};

	/// <summary>
	/// Replaces all shortcut overrides with the given set, overwriting
	/// keyboard-shortcuts.json and re-applying to the live commands and tools.
	/// </summary>
	public void ImportOverrides (ShortcutFile overrides)
	{
		command_overrides.Clear ();
		tool_overrides.Clear ();
		binding_overrides.Clear ();

		if (overrides.Commands is not null)
			foreach (var kv in overrides.Commands)
				command_overrides[kv.Key] = kv.Value;

		if (overrides.Tools is not null)
			foreach (var kv in overrides.Tools)
				tool_overrides[kv.Key] = kv.Value;

		if (overrides.ToolBindings is not null)
			foreach (var kv in overrides.ToolBindings)
				binding_overrides[kv.Key] = kv.Value;

		Save ();
		LoadAndApply ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	private void Load ()
	{
		command_overrides.Clear ();
		tool_overrides.Clear ();
		binding_overrides.Clear ();

		if (!File.Exists (ShortcutsFilePath))
			return;

		try {
			using var stream = File.OpenRead (ShortcutsFilePath);
			var doc = JsonSerializer.Deserialize<ShortcutFile> (stream);

			if (doc?.Commands is not null)
				foreach (var kv in doc.Commands)
					command_overrides[kv.Key] = kv.Value;

			if (doc?.Tools is not null)
				foreach (var kv in doc.Tools)
					tool_overrides[kv.Key] = kv.Value;

			if (doc?.ToolBindings is not null)
				foreach (var kv in doc.ToolBindings)
					binding_overrides[kv.Key] = kv.Value;
		} catch (Exception ex) {
			// Malformed / hand-edited file: fall back to defaults rather than crash.
			Console.Error.WriteLine ($"Failed to load {SHORTCUTS_FILE}, using default shortcuts: {ex.Message}");
			command_overrides.Clear ();
			tool_overrides.Clear ();
			binding_overrides.Clear ();
		}
	}

	private void Save ()
	{
		try {
			ShortcutFile doc = new () {
				Commands = new (command_overrides),
				Tools = new (tool_overrides),
				ToolBindings = new (binding_overrides),
			};

			Directory.CreateDirectory (settings.GetUserSettingsDirectory ());

			using var stream = File.Create (ShortcutsFilePath);
			JsonSerializer.Serialize (stream, doc, new JsonSerializerOptions { WriteIndented = true });
		} catch (Exception ex) {
			Console.Error.WriteLine ($"Failed to save {SHORTCUTS_FILE}: {ex.Message}");
		}
	}

	// --- Commands (menu / app actions) ---

	public void SetCommandShortcut (Command command, string accel)
	{
		ClearConflicts (accel, except: command);

		command_overrides[command.Name] = accel;
		ApplyCommandShortcut (command, accel.Length == 0 ? [] : [accel]);
		SaveAndNotify ();
	}

	public void ResetCommandShortcut (Command command)
	{
		command_overrides.Remove (command.Name);
		ApplyCommandShortcut (command, command.DefaultShortcuts);
		SaveAndNotify ();
	}

	private void ApplyCommandShortcut (Command command, IReadOnlyList<string> shortcuts)
	{
		command.SetShortcuts (shortcuts);
		chrome.Application.ApplyAccels (command);
	}

	// --- Toolbox activation keys ---

	public void SetToolShortcut (BaseTool tool, KeyGesture gesture)
	{
		ClearToolConflicts (gesture, except: tool);

		tool_overrides[tool.GetType ().Name] = gesture.ToAcceleratorName ();
		tools.SetShortcutKeyOverride (tool, gesture);
		SaveAndNotify ();
	}

	public void ResetToolShortcut (BaseTool tool)
	{
		tool_overrides.Remove (tool.GetType ().Name);
		tools.ResetShortcutKeyOverride (tool);
		SaveAndNotify ();
	}

	// --- Tool-specific in-canvas bindings ---

	public KeyGesture GetToolBinding (ToolBindingDescriptor descriptor)
		=> binding_overrides.TryGetValue (descriptor.Id, out var keyName) && KeyGesture.TryParse (keyName) is KeyGesture gesture
			? gesture
			: descriptor.DefaultGesture;

	public void SetToolBinding (ToolBindingDescriptor descriptor, KeyGesture gesture)
	{
		ClearBindingConflicts (descriptor, gesture);

		binding_overrides[descriptor.Id] = gesture.ToAcceleratorName ();
		SaveAndNotify ();
	}

	public void ResetToolBinding (ToolBindingDescriptor descriptor)
	{
		binding_overrides.Remove (descriptor.Id);
		SaveAndNotify ();
	}

	// --- Batching ---

	private int batch_depth;

	public void BeginBatch ()
		=> batch_depth++;

	public void EndBatch ()
	{
		if (batch_depth == 0)
			return;

		if (--batch_depth == 0) {
			Save ();
			ShortcutsChanged?.Invoke (this, EventArgs.Empty);
		}
	}

	private void SaveAndNotify ()
	{
		if (batch_depth > 0)
			return;

		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	// --- Bulk reset ---

	public void ResetAllToDefaults ()
	{
		foreach (var command in AllCommands ())
			ApplyCommandShortcut (command, command.DefaultShortcuts);

		foreach (var tool in tools)
			tools.ResetShortcutKeyOverride (tool);

		command_overrides.Clear ();
		tool_overrides.Clear ();
		binding_overrides.Clear ();
		SaveAndNotify ();
	}

	// --- Helpers ---

	// Prevents two commands from sharing the same accelerator: when a shortcut
	// is bound to one command, release it from any other command currently
	// using it. A cleared (empty) accelerator is not a conflict we resolve.
	private void ClearConflicts (string accel, Command except)
	{
		if (accel.Length == 0)
			return;

		foreach (var command in AllCommands ())
			if (command != except && command.Shortcuts.Contains (accel))
				ApplyCommandShortcut (command, []);
	}

	// Prevents two tools from sharing the same toolbox activation key.
	private void ClearToolConflicts (KeyGesture gesture, BaseTool except)
	{
		foreach (var tool in tools)
			if (tool != except && tools.GetEffectiveShortcutKey (tool) == gesture)
				tools.ResetShortcutKeyOverride (tool);
	}

	// Prevents two tool-specific bindings in the same tab (i.e. usable while the
	// same tool is active) from sharing the same key. Bindings in different tabs
	// never conflict, since only one tool's bindings are live at a time.
	private void ClearBindingConflicts (ToolBindingDescriptor descriptor, KeyGesture gesture)
	{
		if (!gesture.IsValid)
			return;

		foreach (var other in ToolBindings)
			if (other != descriptor && other.TabName == descriptor.TabName && GetToolBinding (other) == gesture)
				binding_overrides.Remove (other.Id);
	}

	public IEnumerable<Command> AllCommands ()
		=> new object[] {
			actions.App, actions.File, actions.Edit, actions.View, actions.Image,
			actions.Layers, actions.Adjustments, actions.Effects, actions.Window, actions.Help,
		}.SelectMany (GetCommands);

	private static IEnumerable<Command> GetCommands (object actionCollection)
		=> actionCollection.GetType ()
			.GetProperties ()
			.Where (p => p.PropertyType == typeof (Command))
			.Select (p => (Command) p.GetValue (actionCollection)!)
			.Where (c => c != null);
}
