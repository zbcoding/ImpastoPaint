//
// KeyboardShortcutsDialogAction.cs
//

using System;
using System.Collections.Generic;
using System.Linq;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class KeyboardShortcutsDialogAction : IActionHandler
{
	private readonly AppActions app;
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly ToolManager tools;

	internal KeyboardShortcutsDialogAction (
		AppActions app,
		ActionManager actions,
		ChromeManager chrome,
		ToolManager tools)
	{
		this.app = app;
		this.actions = actions;
		this.chrome = chrome;
		this.tools = tools;
	}

	void IActionHandler.Initialize ()
	{
		app.KeyboardShortcuts.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		app.KeyboardShortcuts.Activated -= Activated;
	}

	// Helper to format a GTK accel string ("<Primary>V") for the current OS.
	private static string FormatAccel (string shortcut)
	{
		bool isMac = SystemManager.GetOperatingSystem () == OS.Mac;
		string normalized = shortcut
			.Replace ("<Primary>", isMac ? "<Meta>" : "<Control>")
			.Replace ("<Ctrl>", "<Control>");

		return GtkExtensions.TryParseAccelerator (normalized, out uint key, out var mods)
			? Gtk.Functions.AcceleratorGetLabel (key, mods)
			: shortcut;
	}

	private static string FormatKey (Gdk.Key key)
		=> key.Value is 0 or Gdk.Constants.KEY_VoidSymbol
			? Translations.GetString ("None")
			: Gtk.Functions.AcceleratorGetLabel (key.Value, 0);

	private void Activated (object sender, EventArgs e)
	{
		List<Action> refreshers = [];
		void RefreshAll ()
		{
			foreach (var refresh in refreshers)
				refresh ();
		}

		Gtk.Window window = Gtk.Window.New ();
		window.SetTransientFor (chrome.MainWindow);
		window.Modal = true;
		window.Title = Translations.GetString ("Keyboard Shortcuts");
		window.SetDefaultSize (640, 480);

		Gtk.Button resetAllButton = Gtk.Button.NewWithLabel (Translations.GetString ("Reset All to Defaults"));
		resetAllButton.OnClicked += async (_, _) => {
			Gtk.AlertDialog confirmation = Gtk.AlertDialog.NewWithProperties ([]);
			confirmation.Message = Translations.GetString ("Reset All Keyboard Shortcuts?");
			confirmation.Detail = Translations.GetString ("This resets every command, tool, and tool binding shortcut to its default.");
			confirmation.Buttons = [Translations.GetString ("Reset"), Translations.GetString ("Cancel")];
			confirmation.DefaultButton = 1;
			confirmation.CancelButton = 1;

			if (await confirmation.ChooseAsync (window) != 0)
				return;

			PintaCore.Shortcuts.ResetAllToDefaults ();
			RefreshAll ();
		};

		Gtk.SearchEntry searchEntry = Gtk.SearchEntry.New ();
		searchEntry.PlaceholderText = Translations.GetString ("Search commands…");
		searchEntry.SetAllMargins (6);

		Gtk.HeaderBar headerBar = Gtk.HeaderBar.New ();
		headerBar.PackStart (resetAllButton);
		window.SetTitlebar (headerBar);

		List<Gtk.ListBox> searchableLists = [];
		string Query () => searchEntry.GetText ();
		searchEntry.OnSearchChanged += (_, _) => {
			foreach (var list in searchableLists)
				list.InvalidateFilter ();
		};

		Gtk.Notebook notebook = Gtk.Notebook.New ();

		notebook.AppendPage (
			BuildToolsPage (refreshers, searchableLists, Query),
			Gtk.Label.New (Translations.GetString ("Tools")));

		foreach (var tabName in KeyboardShortcutManager.ToolBindings.Select (b => b.TabName).Distinct ())
			notebook.AppendPage (
				BuildToolBindingsPage (tabName, refreshers, searchableLists, Query),
				Gtk.Label.New (tabName));

		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Layers), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Layers")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.File), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("File")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Edit), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Edit")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.View), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("View")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Image), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Image")));
		notebook.AppendPage (BuildCommandsPage (actions.Adjustments.Actions, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Adjustments")));
		notebook.AppendPage (BuildCommandsPage (actions.Effects.Actions, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Effects")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Window), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Window")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Help), refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Help")));

		notebook.Vexpand = true;
		notebook.Hexpand = true;

		Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		content.Append (searchEntry);
		content.Append (notebook);
		window.SetChild (content);

		// Impasto: other rows can change out from under this dialog (e.g. assigning a
		// shortcut that was in use elsewhere clears it from its previous owner).
		EventHandler onShortcutsChanged = (_, _) => RefreshAll ();
		PintaCore.Shortcuts.ShortcutsChanged += onShortcutsChanged;
		window.OnCloseRequest += (_, _) => {
			PintaCore.Shortcuts.ShortcutsChanged -= onShortcutsChanged;
			return false;
		};

		window.Present ();
	}

	private static IEnumerable<Command> GetCommands (object actionCollection)
	{
		return actionCollection.GetType ()
			.GetProperties ()
			.Where (p => p.PropertyType == typeof (Command))
			.Select (p => (Command) p.GetValue (actionCollection)!)
			.Where (c => c != null && !string.IsNullOrEmpty (c.Label));
	}

	private Gtk.Widget BuildCommandsPage (IEnumerable<Command> commands, List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var command in commands.OrderBy (c => c.Label))
			list.Append (
				BuildRow (
					command.Label.Replace ("_", ""),
					() => command.Shortcuts.Length > 0 ? FormatAccel (command.Shortcuts[0]) : Translations.GetString ("None"),
					(keyval, mods) => PintaCore.Shortcuts.SetCommandShortcut (command, Gtk.Functions.AcceleratorName (keyval, mods)),
					() => PintaCore.Shortcuts.ResetCommandShortcut (command),
					refreshers));

		searchableLists.Add (list);
		return Wrap (list);
	}

	private Gtk.Widget BuildToolsPage (List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var tool in tools.OrderBy (t => t.Name))
			list.Append (
				BuildRow (
					tool.Name,
					() => FormatKey (tools.GetEffectiveShortcutKey (tool)),
					(keyval, _) => PintaCore.Shortcuts.SetToolShortcut (tool, new Gdk.Key (keyval)),
					() => PintaCore.Shortcuts.ResetToolShortcut (tool),
					refreshers));

		searchableLists.Add (list);
		return Wrap (list);
	}

	private Gtk.Widget BuildToolBindingsPage (string tabName, List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var descriptor in KeyboardShortcutManager.ToolBindings.Where (b => b.TabName == tabName))
			list.Append (
				BuildRow (
					// Impasto: breadcrumb prefix, since more than one tool's binding can share
					// a generic verb like "Confirm" - the tab alone won't tell them apart once
					// search or a future shared tab mixes bindings from different tools.
					$"{descriptor.TabName} — {descriptor.Label}",
					() => FormatKey (PintaCore.Shortcuts.GetToolBinding (descriptor)),
					(keyval, _) => PintaCore.Shortcuts.SetToolBinding (descriptor, new Gdk.Key (keyval)),
					() => PintaCore.Shortcuts.ResetToolBinding (descriptor),
					refreshers));

		searchableLists.Add (list);
		return Wrap (list);
	}

	private static Gtk.ListBox MakeSearchableList (Func<string> query)
	{
		Gtk.ListBox list = Gtk.ListBox.New ();
		list.SelectionMode = Gtk.SelectionMode.None;
		list.SetFilterFunc (row =>
			string.IsNullOrWhiteSpace (query ()) ||
			row.Name.Contains (query (), StringComparison.OrdinalIgnoreCase));
		return list;
	}

	private static Gtk.Widget Wrap (Gtk.Widget child)
	{
		Gtk.ScrolledWindow scroller = Gtk.ScrolledWindow.New ();
		scroller.Vexpand = true;
		scroller.Hexpand = true;
		scroller.SetChild (child);
		return scroller;
	}

	/// <summary>
	/// A single editable shortcut row: a label, a button showing the current shortcut
	/// (click it, then press the new key combo; Escape cancels), and a reset button.
	/// </summary>
	private static Gtk.Widget BuildRow (
		string label,
		Func<string> getDisplay,
		Action<uint, Gdk.ModifierType> setNew,
		Action reset,
		List<Action> refreshers)
	{
		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 8);
		row.SetAllMargins (6);

		Gtk.Label nameLabel = Gtk.Label.New (label);
		nameLabel.Halign = Gtk.Align.Start;
		nameLabel.Hexpand = true;
		row.Append (nameLabel);

		Gtk.Button shortcutButton = Gtk.Button.New ();
		shortcutButton.WidthRequest = 180;

		bool listening = false;

		void Refresh ()
		{
			listening = false;
			shortcutButton.Label = getDisplay ();
		}
		Refresh ();
		refreshers.Add (Refresh);

		Gtk.EventControllerKey capture = Gtk.EventControllerKey.New ();
		capture.OnKeyPressed += (_, args) => {
			if (!listening)
				return false;

			if (args.GetKey ().Value == Gdk.Constants.KEY_Escape) {
				Refresh ();
				return true;
			}

			// Impasto: build the mask from named flags rather than
			// Gtk.Functions.AcceleratorGetDefaultModMask () - on some platforms that mask
			// dropped Shift/Alt, silently truncating captured chords down to a bare key.
			const Gdk.ModifierType ACCEL_MODS =
				Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask | Gdk.ModifierType.AltMask |
				Gdk.ModifierType.SuperMask | Gdk.ModifierType.MetaMask | Gdk.ModifierType.HyperMask;
			Gdk.ModifierType mods = args.State & ACCEL_MODS;

			if (!Gtk.Functions.AcceleratorValid (args.Keyval, mods))
				return true; // modifier-only press; keep listening

			setNew (args.Keyval, mods);
			Refresh ();
			return true;
		};
		shortcutButton.AddController (capture);

		shortcutButton.OnClicked += (_, _) => {
			listening = true;
			shortcutButton.Label = Translations.GetString ("Press keys…");
			shortcutButton.GrabFocus ();
		};

		Gtk.Button resetButton = Gtk.Button.New ();
		resetButton.IconName = "edit-undo-symbolic";
		resetButton.TooltipText = Translations.GetString ("Reset to default");
		resetButton.OnClicked += (_, _) => {
			reset ();
			Refresh ();
		};

		row.Append (shortcutButton);
		row.Append (resetButton);

		Gtk.ListBoxRow listRow = Gtk.ListBoxRow.New ();
		listRow.Child = row;
		listRow.Name = label; // used by the search filter
		return listRow;
	}
}
