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
	Gdk.Key DefaultKey);

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
		new Gdk.Key (Gdk.Constants.KEY_Return));

	public static readonly ToolBindingDescriptor LassoBacktrack = new (
		"LassoSelect.Backtrack",
		Translations.GetString ("Lasso / Scissors Select"),
		Translations.GetString ("Undo last point"),
		new Gdk.Key (Gdk.Constants.KEY_BackSpace));

	public static readonly ToolBindingDescriptor LassoCancel = new (
		"LassoSelect.Cancel",
		Translations.GetString ("Lasso / Scissors Select"),
		Translations.GetString ("Cancel selection"),
		new Gdk.Key (Gdk.Constants.KEY_Escape));

	public static readonly IReadOnlyList<ToolBindingDescriptor> ToolBindings = [
		LassoFinalize,
		LassoBacktrack,
		LassoCancel,
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

		foreach (var tool in tools)
			if (tool_overrides.TryGetValue (tool.GetType ().Name, out var keyName) && ParseKey (keyName) is Gdk.Key key)
				this.tools.SetShortcutKeyOverride (tool, key);
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
		ApplyCommandShortcut (command, [accel]);
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	public void ResetCommandShortcut (Command command)
	{
		command_overrides.Remove (command.Name);
		ApplyCommandShortcut (command, command.DefaultShortcuts);
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	private void ApplyCommandShortcut (Command command, IReadOnlyList<string> shortcuts)
	{
		command.SetShortcuts (shortcuts);
		chrome.Application.ApplyAccels (command);
	}

	// --- Toolbox activation keys ---

	public void SetToolShortcut (BaseTool tool, Gdk.Key key)
	{
		ClearToolConflicts (key, except: tool);

		tool_overrides[tool.GetType ().Name] = NameKey (key);
		tools.SetShortcutKeyOverride (tool, key);
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	public void ResetToolShortcut (BaseTool tool)
	{
		tool_overrides.Remove (tool.GetType ().Name);
		tools.ResetShortcutKeyOverride (tool);
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	// --- Tool-specific in-canvas bindings ---

	public Gdk.Key GetToolBinding (ToolBindingDescriptor descriptor)
		=> binding_overrides.TryGetValue (descriptor.Id, out var keyName) && ParseKey (keyName) is Gdk.Key key
			? key
			: descriptor.DefaultKey;

	public void SetToolBinding (ToolBindingDescriptor descriptor, Gdk.Key key)
	{
		binding_overrides[descriptor.Id] = NameKey (key);
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	public void ResetToolBinding (ToolBindingDescriptor descriptor)
	{
		binding_overrides.Remove (descriptor.Id);
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
		Save ();
		ShortcutsChanged?.Invoke (this, EventArgs.Empty);
	}

	// --- Helpers ---

	private void ClearConflicts (string accel, Command except)
	{
		foreach (var command in AllCommands ())
			if (command != except && command.Shortcuts.Contains (accel))
				ApplyCommandShortcut (command, []);
	}

	private void ClearToolConflicts (Gdk.Key key, BaseTool except)
	{
		foreach (var tool in tools)
			if (tool != except && tools.GetEffectiveShortcutKey (tool).ToUpper () == key.ToUpper ())
				tools.ResetShortcutKeyOverride (tool);
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

	private static Gdk.Key? ParseKey (string accel)
	{
		if (!GtkExtensions.TryParseAccelerator (accel, out uint keyval, out _))
			return null;

		return new Gdk.Key (keyval);
	}

	private static string NameKey (Gdk.Key key)
		=> Gtk.Functions.AcceleratorName (key.Value, default);

	private sealed class ShortcutFile
	{
		public Dictionary<string, string>? Commands { get; set; }
		public Dictionary<string, string>? Tools { get; set; }
		public Dictionary<string, string>? ToolBindings { get; set; }
	}
}
