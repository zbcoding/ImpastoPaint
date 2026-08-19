using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Impasto: the tool name chip as a dropdown, for users who hide the tool box and pick
/// tools from the toolbar instead. It keeps the plain chip's contents and size - the
/// "Tool:" label and the current tool's icon - and adds a triangle and an outline that
/// lights up on hover, so it reads as a menu rather than a label.
/// </summary>
[GObject.Subclass<Gtk.MenuButton>]
public sealed partial class ToolSelectorButton
{
	// Draws the chip's border, radius and padding on the button, so the toolbar keeps its height.
	private const string SELECTOR_CLASS = "tool-selector-button";

	// Every tool in one list is taller than a short screen, so the list scrolls instead.
	private const int MAX_MENU_HEIGHT = 420;

	private ToolManager tools = null!; // NRT - set in the factory method

	private readonly Gtk.Image current_icon = Gtk.Image.New ();
	private readonly Gtk.Box entries = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

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

		Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New ();
		scroll.Child = entries;
		scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		scroll.PropagateNaturalHeight = true;
		scroll.PropagateNaturalWidth = true;
		scroll.MaxContentHeight = MAX_MENU_HEIGHT;

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (scroll);
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

	private void RebuildEntries ()
	{
		entries.RemoveAll ();
		selected_marks.Clear ();

		foreach (BaseTool tool in tools) {

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

			entries.Append (entry);
		}
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
