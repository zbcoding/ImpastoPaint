using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinta.Core;

/// <summary>
/// Impasto: the tool name chip as a dropdown, for users who hide the tool box and pick
/// tools from the toolbar instead. It keeps the plain chip's contents and size - the
/// "Tool:" label and the current tool's icon - and adds a triangle and an outline that
/// lights up on hover, so it reads as a menu rather than a label.
///
/// <para>
/// The menu is the toolbox laid out sideways: the same sections in the same order, two
/// tools per row, tall enough to show them all without scrolling.
/// </para>
/// </summary>
[GObject.Subclass<Gtk.MenuButton>]
public sealed partial class ToolSelectorButton
{
	// Draws the chip's border, radius and padding on the button, so the toolbar keeps its height.
	private const string SELECTOR_CLASS = "tool-selector-button";

	// One per section, above its heading; smaller than the entries so the tools stay the focus.
	private const string SECTION_HEADING_CLASS = "tool-selector-section";

	// Spaces the rules dividing the sections inside the menu.
	private const string MENU_CLASS = "tool-selector-menu";

	private const int MENU_COLUMNS = 2;

	// Share of the window the menu may grow to before it starts scrolling. The built-in
	// sections fit inside this on an ordinary screen; a small screen, or enough add-in tools,
	// scrolls instead of running off the window.
	private const double MENU_HEIGHT_SHARE = 0.85;

	// Used until the main window has been given its size.
	private const int FALLBACK_MENU_HEIGHT = 600;

	private ToolManager tools = null!; // NRT - set in the factory method

	private readonly Gtk.Image current_icon = Gtk.Image.New ();
	private readonly Gtk.Box entries = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
	private readonly Gtk.ScrolledWindow menu_scroll = Gtk.ScrolledWindow.New ();

	// The checkmark on each entry, so activating a tool moves the mark without rebuilding
	// the list - which only happens when the set of tools or their shortcuts changes.
	private readonly Dictionary<BaseTool, Gtk.Image> selected_marks = new ();

	partial void Initialize ()
	{
		Gtk.Box chip = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		chip.Append (Gtk.Label.New (string.Format (" {0}:  ", Translations.GetString ("Tool"))));
		chip.Append (current_icon);

		SetChild (chip);
		AlwaysShowArrow = true; // The triangle; a custom child suppresses it otherwise.
		AddCssClass (SELECTOR_CLASS);
		Valign = Gtk.Align.Center;
		CanFocus = false;

		menu_scroll.Child = entries;
		menu_scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		menu_scroll.PropagateNaturalHeight = true;
		menu_scroll.PropagateNaturalWidth = true;
		menu_scroll.MaxContentHeight = FALLBACK_MENU_HEIGHT;

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (menu_scroll);
		popover.AddCssClass (MENU_CLASS);
		// The window can be resized between two openings, so the menu takes its share of
		// whatever height the window has now.
		popover.OnShow += (_, _) => UpdateMenuHeight ();
		Popover = popover;
	}

	public static ToolSelectorButton New (ToolManager tools)
	{
		ToolSelectorButton button = NewWithProperties ([]);
		button.Configure (tools);
		return button;
	}

	private void Configure (ToolManager tools)
	{
		this.tools = tools;

		tools.ToolAdded += (_, _) => RebuildEntries ();
		tools.ToolRemoved += (_, _) => RebuildEntries ();
		tools.ToolActivated += (_, e) => ShowTool (e.Tool);
		PintaCore.Shortcuts.ShortcutsChanged += (_, _) => RebuildEntries ();

		// The tools registered before the dropdown was built missed ToolAdded.
		RebuildEntries ();

		if (tools.CurrentTool is BaseTool current)
			ShowTool (current);

		UpdateMenuHeight (); // A sane height even before the window reports one.
	}

	/// <summary>
	/// Follows the selected tool, whichever way it was picked - this dropdown, the tool box,
	/// a shortcut key, or a tool that switches to another one.
	/// </summary>
	private void ShowTool (BaseTool tool)
	{
		current_icon.SetFromIconName (IconNameFor (tool));
		TooltipText = TooltipFor (tool);

		foreach (var (entryTool, mark) in selected_marks)
			mark.Visible = entryTool == tool;
	}

	/// <summary>
	/// The menu grows to its sections and no further: a ceiling, not a fixed height, so it
	/// neither leaves blank space below the last section nor runs off a short screen. Setting
	/// the ceiling alone keeps GTK's min &lt;= max invariant, which a minimum would break here.
	/// </summary>
	private void UpdateMenuHeight ()
	{
		int windowHeight = PintaCore.Chrome.MainWindow.GetHeight ();

		menu_scroll.MaxContentHeight = windowHeight > 0
			? (int) (windowHeight * MENU_HEIGHT_SHARE)
			: FALLBACK_MENU_HEIGHT;
	}

	private void RebuildEntries ()
	{
		entries.RemoveAll ();
		selected_marks.Clear ();

		for (int section = 0; section < ToolSections.Count; section++) {

			BaseTool[] members = tools.Where (t => ToolSections.IndexOf (t) == section).ToArray ();

			// An empty section draws no heading, so the add-in one only appears once an
			// add-in tool is installed.
			if (members.Length == 0)
				continue;

			// A rule between sections, as the toolbox column divides them. None above the
			// first: it would read as a line under the menu's own top edge.
			if (entries.GetFirstChild () is not null)
				entries.Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));

			Gtk.Label heading = Gtk.Label.New (ToolSections.NameOf (section));
			heading.Halign = Gtk.Align.Start;
			heading.SetCssClasses ([SECTION_HEADING_CLASS, AdwaitaStyles.DimLabel]);
			entries.Append (heading);

			Gtk.FlowBox grid = Gtk.FlowBox.New ();
			grid.SetOrientation (Gtk.Orientation.Horizontal);
			grid.MinChildrenPerLine = MENU_COLUMNS;
			grid.MaxChildrenPerLine = MENU_COLUMNS;
			grid.Homogeneous = true; // Both columns as wide as the longest tool name.
			grid.SelectionMode = Gtk.SelectionMode.None;
			entries.Append (grid);

			foreach (BaseTool tool in members)
				grid.Append (CreateEntry (tool));
		}
	}

	private Gtk.Button CreateEntry (BaseTool tool)
	{
		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		row.Append (Gtk.Image.NewFromIconName (IconNameFor (tool)));

		Gtk.Label name = Gtk.Label.New (tool.Name);
		name.Halign = Gtk.Align.Start;
		name.Hexpand = true;
		row.Append (name);

		Gtk.Image selectedMark = Gtk.Image.NewFromIconName (Resources.StandardIcons.ObjectSelect);
		selectedMark.Visible = tools.CurrentTool == tool;
		row.Append (selectedMark);
		selected_marks[tool] = selectedMark;

		Gtk.Button entry = Gtk.Button.New ();
		entry.SetCssClasses ([AdwaitaStyles.Flat]);
		entry.TooltipText = TooltipFor (tool);
		entry.SetChild (row);
		entry.OnClicked += (_, _) => {
			Popover?.Popdown ();
			tools.SetCurrentTool (tool);
		};

		return entry;
	}

	/// <summary>
	/// The chip shows an icon rather than the tool's name, so the toolbar doesn't shift as
	/// tools with longer names are selected. The name goes in the tooltip instead.
	/// </summary>
	private string TooltipFor (BaseTool tool)
	{
		KeyGesture shortcut = tools.GetEffectiveShortcutKey (tool);

		if (!shortcut.IsValid)
			return tool.Name;

		return $"{tool.Name}\n{Translations.GetString ("Shortcut key")}: {shortcut.ToLabel ()}";
	}

	/// <summary>
	/// A tool's <c>Icon</c> is a theme icon name, and GTK draws its broken-image glyph for
	/// any name the theme cannot resolve - which an add-in tool shipping no icons would hit.
	/// </summary>
	private static string IconNameFor (BaseTool tool)
		=> GtkExtensions.GetDefaultIconTheme ().HasIcon (tool.Icon)
			? tool.Icon
			: Resources.Icons.ToolDefault;
}
