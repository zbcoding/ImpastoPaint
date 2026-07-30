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

	private static string NormalizeAccelForPlatform (string shortcut)
	{
		bool isMac = SystemManager.GetOperatingSystem () == OS.Mac;
		return shortcut
			.Replace ("<Primary>", isMac ? "<Meta>" : "<Control>")
			.Replace ("<Ctrl>", "<Control>");
	}

	private static string FormatAccel (string shortcut)
	{
		if (string.IsNullOrEmpty (shortcut))
			return Translations.GetString ("None");

		string normalized = NormalizeAccelForPlatform (shortcut);

		return GtkExtensions.TryParseAccelerator (normalized, out uint key, out var mods)
			? Gtk.Functions.AcceleratorGetLabel (key, mods)
			: shortcut;
	}

	private static KeyGesture? ParseGesture (string shortcut)
		=> KeyGesture.TryParse (NormalizeAccelForPlatform (shortcut));

	private void Activated (object sender, EventArgs e)
	{
		List<ShortcutRowState> states = [];
		List<Action> refreshers = [];

		void RefreshAll ()
		{
			foreach (var refresh in refreshers)
				refresh ();
			RefreshDuplicates (states);
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

			foreach (var state in states)
				state.Value = state.DefaultValue;

			RefreshAll ();
		};

		Gtk.Button cancelButton = Gtk.Button.NewWithLabel (Translations.GetString ("Cancel"));
		cancelButton.OnClicked += (_, _) => window.Close ();

		Gtk.Button okButton = Gtk.Button.NewWithLabel (Translations.GetString ("OK"));
		okButton.AddCssClass (AdwaitaStyles.SuggestedAction);
		okButton.OnClicked += (_, _) => {
			PintaCore.Shortcuts.BeginBatch ();
			try {
				foreach (var state in states)
					state.Apply (state.Value, state.IsDefaultValue);
			} finally {
				PintaCore.Shortcuts.EndBatch ();
			}

			window.Close ();
		};

		Gtk.SearchEntry searchEntry = Gtk.SearchEntry.New ();
		searchEntry.PlaceholderText = Translations.GetString ("Search commands…");
		searchEntry.SetAllMargins (6);

		Gtk.HeaderBar headerBar = Gtk.HeaderBar.New ();
		headerBar.PackStart (resetAllButton);
		headerBar.PackEnd (okButton);
		headerBar.PackEnd (cancelButton);
		window.SetTitlebar (headerBar);

		List<Gtk.ListBox> searchableLists = [];
		string Query () => searchEntry.GetText ();
		searchEntry.OnSearchChanged += (_, _) => {
			foreach (var list in searchableLists)
				list.InvalidateFilter ();
		};

		Gtk.Notebook notebook = Gtk.Notebook.New ();

		notebook.AppendPage (
			BuildToolsPage (states, refreshers, searchableLists, Query),
			Gtk.Label.New (Translations.GetString ("Tools")));

		notebook.AppendPage (
			BuildToolBindingsPage (states, refreshers, searchableLists, Query),
			Gtk.Label.New (Translations.GetString ("Tool Specific")));

		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Layers), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Layers")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.File), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("File")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Edit), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Edit")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.View), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("View")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Image), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Image")));
		notebook.AppendPage (BuildCommandsPage (actions.Adjustments.Actions, states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Adjustments")));
		notebook.AppendPage (BuildCommandsPage (actions.Effects.Actions, states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Effects")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Window), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Window")));
		notebook.AppendPage (BuildCommandsPage (GetCommands (actions.Help), states, refreshers, searchableLists, Query), Gtk.Label.New (Translations.GetString ("Help")));

		notebook.Vexpand = true;
		notebook.Hexpand = true;

		Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		content.Append (searchEntry);
		content.Append (notebook);
		window.SetChild (content);

		RefreshDuplicates (states);
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

	private Gtk.Widget BuildCommandsPage (IEnumerable<Command> commands, List<ShortcutRowState> states, List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var command in commands.OrderBy (c => c.Label)) {
			string value = command.Shortcuts.Length > 0 ? command.Shortcuts[0] : string.Empty;
			IReadOnlyList<string> defaults = command.DefaultShortcuts.Length > 0 ? command.DefaultShortcuts : [string.Empty];
			ShortcutRowState state = new (
				command.Label.Replace ("_", ""),
				value,
				defaults,
				(value, isDefault) => {
					if (isDefault)
						PintaCore.Shortcuts.ResetCommandShortcut (command);
					else
						PintaCore.Shortcuts.SetCommandShortcut (command, value);
				},
				ShortcutCategory.Command);
			states.Add (state);
			list.Append (BuildRow (state, () => RefreshDuplicates (states), refreshers));
		}

		searchableLists.Add (list);
		return Wrap (list);
	}

	private Gtk.Widget BuildToolsPage (List<ShortcutRowState> states, List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var tool in tools.OrderBy (t => t.Name)) {
			KeyGesture defaultGesture = new (tool.ShortcutKey);
			ShortcutRowState state = new (
				tool.Name,
				tools.GetEffectiveShortcutKey (tool).ToAcceleratorName (),
				[defaultGesture.ToAcceleratorName ()],
				(value, isDefault) => {
					if (isDefault)
						PintaCore.Shortcuts.ResetToolShortcut (tool);
					else if (ParseGesture (value) is KeyGesture gesture)
						PintaCore.Shortcuts.SetToolShortcut (tool, gesture);
				},
				ShortcutCategory.Tool);
			states.Add (state);
			list.Append (BuildRow (state, () => RefreshDuplicates (states), refreshers));
		}

		searchableLists.Add (list);
		return Wrap (list);
	}

	private Gtk.Widget BuildToolBindingsPage (List<ShortcutRowState> states, List<Action> refreshers, List<Gtk.ListBox> searchableLists, Func<string> query)
	{
		Gtk.ListBox list = MakeSearchableList (query);

		foreach (var descriptor in KeyboardShortcutManager.ToolBindings) {
			ShortcutRowState state = new (
				$"{descriptor.TabName} — {descriptor.Label}",
				PintaCore.Shortcuts.GetToolBinding (descriptor).ToAcceleratorName (),
				[descriptor.DefaultGesture.ToAcceleratorName ()],
				(value, isDefault) => {
					if (isDefault)
						PintaCore.Shortcuts.ResetToolBinding (descriptor);
					else if (ParseGesture (value) is KeyGesture gesture)
						PintaCore.Shortcuts.SetToolBinding (descriptor, gesture);
				},
				ShortcutCategory.ToolBinding);
			states.Add (state);
			list.Append (BuildRow (state, () => RefreshDuplicates (states), refreshers));
		}

		searchableLists.Add (list);
		return Wrap (list);
	}

	private static Gtk.ListBox MakeSearchableList (Func<string> query)
	{
		Gtk.ListBox list = Gtk.ListBox.New ();
		list.SelectionMode = Gtk.SelectionMode.None;
		list.SetFilterFunc (row =>
			string.IsNullOrWhiteSpace (query ()) ||
			(row.Name?.Contains (query (), StringComparison.OrdinalIgnoreCase) == true));
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

	private static Gtk.Widget BuildRow (
		ShortcutRowState state,
		Action refreshDuplicates,
		List<Action> refreshers)
	{
		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 8);
		row.SetAllMargins (6);

		Gtk.Label nameLabel = Gtk.Label.New (state.Label);
		nameLabel.Halign = Gtk.Align.Start;
		nameLabel.Hexpand = true;
		row.Append (nameLabel);

		Gtk.Label duplicateMarker = Gtk.Label.New ("*");
		duplicateMarker.AddCssClass (AdwaitaStyles.Error);
		duplicateMarker.Visible = false;
		row.Append (duplicateMarker);

		Gtk.Popover duplicatePopover = Gtk.Popover.New ();
		duplicatePopover.Autohide = false;
		duplicatePopover.Position = Gtk.PositionType.Top;
		duplicatePopover.SetParent (duplicateMarker);
		Gtk.Label duplicateLabel = Gtk.Label.New (Translations.GetString ("Duplicated"));
		duplicateLabel.MarginTop = duplicateLabel.MarginBottom = 4;
		duplicateLabel.MarginStart = duplicateLabel.MarginEnd = 8;
		duplicatePopover.SetChild (duplicateLabel);

		Gtk.Button shortcutButton = Gtk.Button.New ();
		shortcutButton.WidthRequest = 180;

		state.ShortcutButton = shortcutButton;
		state.DuplicateMarker = duplicateMarker;
		state.DuplicatePopover = duplicatePopover;

		AttachDuplicatePopover (duplicateMarker, state);
		AttachDuplicatePopover (shortcutButton, state);

		bool listening = false;

		void Refresh ()
		{
			listening = false;
			shortcutButton.Label = FormatAccel (state.Value);
			state.RefreshDuplicateState ();
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

			Gdk.ModifierType mods = args.State & KeyGesture.AcceleratorMask;

			if (!Gtk.Functions.AcceleratorValid (args.Keyval, mods))
				return true;

			state.Value = Gtk.Functions.AcceleratorName (args.Keyval, mods);
			Refresh ();
			refreshDuplicates ();
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
			state.Value = state.DefaultValue;
			Refresh ();
			refreshDuplicates ();
		};

		row.Append (shortcutButton);
		row.Append (resetButton);

		Gtk.ListBoxRow listRow = Gtk.ListBoxRow.New ();
		listRow.Child = row;
		listRow.Name = state.Label;
		return listRow;
	}

	private static void AttachDuplicatePopover (Gtk.Widget widget, ShortcutRowState state)
	{
		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnEnter += (_, _) => {
			if (state.IsDuplicate)
				state.DuplicatePopover?.Popup ();
		};
		motion.OnLeave += (_, _) => state.DuplicatePopover?.Popdown ();
		widget.AddController (motion);
	}

	private static void RefreshDuplicates (IReadOnlyList<ShortcutRowState> states)
	{
		Dictionary<KeyGesture, int> counts = [];

		foreach (var state in states) {
			// Tool and ToolBinding shortcuts are allowed to have duplicates (e.g., pressing the same key cycles through tools or tool modes)
			if (state.Category != ShortcutCategory.Command)
				continue;

			if (ParseGesture (state.Value) is not KeyGesture gesture || !gesture.IsValid)
				continue;

			counts.TryGetValue (gesture, out int count);
			counts[gesture] = count + 1;
		}

		foreach (var state in states) {
			// Only mark Command shortcuts as duplicates
			if (state.Category != ShortcutCategory.Command) {
				state.IsDuplicate = false;
				state.RefreshDuplicateState ();
				continue;
			}

			state.IsDuplicate =
				ParseGesture (state.Value) is KeyGesture gesture &&
				gesture.IsValid &&
				counts.TryGetValue (gesture, out int count) &&
				count > 1;
			state.RefreshDuplicateState ();
		}
	}

	private enum ShortcutCategory { Command, Tool, ToolBinding }

	private sealed class ShortcutRowState
	{
		private readonly IReadOnlyList<string> defaultValues;
		private readonly Action<string, bool> apply;

		public ShortcutRowState (string label, string value, IReadOnlyList<string> defaultValues, Action<string, bool> apply, ShortcutCategory category)
		{
			Label = label;
			Value = value;
			this.defaultValues = defaultValues;
			this.apply = apply;
			Category = category;
		}

		public string Label { get; }
		public string Value { get; set; }
		public bool IsDuplicate { get; set; }
		public Gtk.Button? ShortcutButton { get; set; }
		public Gtk.Label? DuplicateMarker { get; set; }
		public Gtk.Popover? DuplicatePopover { get; set; }
		public ShortcutCategory Category { get; }

		public string DefaultValue => defaultValues.Count > 0 ? defaultValues[0] : string.Empty;

		public bool IsDefaultValue
			=> defaultValues.Any (defaultValue => AccelsEqual (Value, defaultValue));

		public void Apply (string value, bool isDefault)
			=> apply (value, isDefault);

		public void RefreshDuplicateState ()
		{
			if (DuplicateMarker is not null)
				DuplicateMarker.Visible = IsDuplicate;

			if (ShortcutButton is null)
				return;

			if (IsDuplicate)
				ShortcutButton.AddCssClass (AdwaitaStyles.Error);
			else
				ShortcutButton.RemoveCssClass (AdwaitaStyles.Error);
		}

		private static bool AccelsEqual (string first, string second)
		{
			if (string.IsNullOrEmpty (first) || string.IsNullOrEmpty (second))
				return string.IsNullOrEmpty (first) && string.IsNullOrEmpty (second);

			return ParseGesture (first) is KeyGesture firstGesture &&
				ParseGesture (second) is KeyGesture secondGesture &&
				firstGesture == secondGesture;
		}
	}
}
