using System.Collections.Generic;
using System.Linq;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

// Impasto: the toolbox is split into sections (Paint.NET style) instead of one undivided
// grid, and related tools share a single button with a flyout (Photoshop style). Tools are
// assigned to a section and a stack by their Priority, so addin tools land in a sensible
// bucket without needing to know about this file.
[GObject.Subclass<Gtk.Box>]
public sealed partial class ToolBoxWidget
{
	/// <summary>Upper bound (inclusive) of the tool priorities belonging to each section.</summary>
	private static readonly int[] section_bounds = [
		8,   // Move
		12,  // View (zoom, pan)
		20,  // Select
		36,  // Paint
		46,  // Shapes
		int.MaxValue, // Retouch, plus any addin tool with an unexpected priority
	];

	/// <summary>
	/// Tool priorities that collapse into one button with a flyout. The stack shows the
	/// icon of whichever member is currently selected.
	/// </summary>
	private static readonly int[][] stack_definitions = [
		[39, 41, 43, 45], // Rectangle, Rounded Rectangle, Ellipse, Freeform
	];

	private sealed class ToolStack
	{
		public required int[] Definition { get; init; }
		public required Gtk.ToggleButton Button { get; init; }
		public required Gtk.Image Icon { get; init; }
		public List<BaseTool> Members { get; } = [];
		public BaseTool Current { get; set; } = null!;
	}

	private ToolManager tools = null!; // NRT - set in factory method
	// Stores the button corresponding to each tool. Tools in the same stack share a button.
	private readonly Dictionary<BaseTool, Gtk.ToggleButton> tool_buttons = new ();
	private readonly Dictionary<int[], ToolStack> tool_stacks = new ();
	// Dummy ToggleButton to use for grouping together the tools' buttons.
	private readonly Gtk.ToggleButton toggle_group = Gtk.ToggleButton.New ();

	private readonly Gtk.FlowBox[] sections = new Gtk.FlowBox[section_bounds.Length];
	private readonly Gtk.Separator[] separators = new Gtk.Separator[section_bounds.Length - 1];

	// Impasto: tools pinned out of a stack's flyout also get a slot at the top of the
	// toolbox. They stay in their stack as well - pinning copies, it doesn't move.
	private Gtk.FlowBox pinned_section = null!;
	private Gtk.Separator pinned_separator = null!;
	private readonly Dictionary<BaseTool, Gtk.ToggleButton> pinned_buttons = new ();

	private static Gtk.FlowBox CreateSectionBox ()
	{
		Gtk.FlowBox section = Gtk.FlowBox.New ();
		// Horizontal orientation: ChildrenPerLine counts children per *row*, so this pins
		// every section to 2 columns and the buttons line up across the separators.
		// (Upstream used Vertical, where the same property counts children per column and
		// each section would pick its own width.)
		section.SetOrientation (Gtk.Orientation.Horizontal);
		section.MinChildrenPerLine = 2;
		section.MaxChildrenPerLine = 2;
		section.Homogeneous = true;
		section.SelectionMode = Gtk.SelectionMode.None; // Don't allow the buttons to be selected.
		section.Visible = false; // Shown when it receives its first tool.
		return section;
	}

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Vertical);

		pinned_section = CreateSectionBox ();
		Append (pinned_section);

		pinned_separator = Gtk.Separator.New (Gtk.Orientation.Horizontal);
		pinned_separator.Visible = false;
		Append (pinned_separator);

		for (int i = 0; i < sections.Length; i++) {

			if (i > 0) {
				Gtk.Separator separator = Gtk.Separator.New (Gtk.Orientation.Horizontal);
				separator.Visible = false; // Shown once both neighbouring sections have tools.
				separators[i - 1] = separator;
				Append (separator);
			}

			Gtk.FlowBox section = CreateSectionBox ();
			sections[i] = section;
			Append (section);
		}
	}

	public static ToolBoxWidget New (ToolManager tools)
	{
		ToolBoxWidget widget = NewWithProperties ([]);
		widget.Configure (tools);
		return widget;
	}

	private void Configure (ToolManager tools)
	{
		tools.ToolAdded += (_, e) => HandleToolAdded (e.Tool);
		tools.ToolRemoved += (_, e) => HandleToolRemoved (e.Tool);
		tools.ToolActivated += (_, e) => HandleToolActivated (e.Tool);

		this.tools = tools;
	}

	internal static int SectionIndex (int priority)
	{
		for (int i = 0; i < section_bounds.Length; i++)
			if (priority <= section_bounds[i])
				return i;

		return section_bounds.Length - 1;
	}

	internal static int[]? StackDefinition (int priority)
		=> stack_definitions.FirstOrDefault (s => s.Contains (priority));

	private static string TooltipFor (BaseTool tool)
	{
		string shortcutText = "";
		if (tool.ShortcutKey != Gdk.Key.Invalid) {
			string shortcutLabel = Translations.GetString ("Shortcut key");
			shortcutText = $"{shortcutLabel}: {tool.ShortcutKey.ToUpper ().Name ()}\n";
		}

		return $"{tool.Name}\n{shortcutText}\n{tool.StatusBarText}";
	}

	private static Gtk.ToggleButton CreateToolButton (BaseTool tool)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.IconName = tool.Icon;
		button.Name = tool.Name;
		button.CanFocus = false;

		button.SetCssClasses ([Resources.Styles.ToolBoxButton, AdwaitaStyles.Flat]);

		button.TooltipText = TooltipFor (tool);

		return button;
	}

	private void HandleToolAdded (BaseTool tool)
	{
		int[]? stackDefinition = StackDefinition (tool.Priority);

		if (stackDefinition is null)
			AddStandaloneTool (tool);
		else
			AddStackedTool (tool, stackDefinition);

		UpdateSectionVisibility ();
	}

	private void AddStandaloneTool (BaseTool tool)
	{
		Gtk.ToggleButton toolButton = CreateToolButton (tool);
		toolButton.Group = toggle_group;
		toolButton.OnClicked += (_, _) => HandleToolButtonClicked (tool);
		tool_buttons[tool] = toolButton;

		InsertIntoSection (toolButton, tool);
	}

	/// <summary>
	/// The first member of a stack creates the shared button; later members only join the
	/// flyout. The button always shows the currently selected member's icon.
	/// </summary>
	private void AddStackedTool (BaseTool tool, int[] stackDefinition)
	{
		if (!tool_stacks.TryGetValue (stackDefinition, out ToolStack? stack)) {

			Gtk.Image icon = Gtk.Image.NewFromIconName (tool.Icon);

			// Small corner marker so the flyout is discoverable, like Photoshop's triangle.
			Gtk.Image marker = Gtk.Image.NewFromIconName ("pan-down-symbolic");
			marker.PixelSize = 8;
			marker.Valign = Gtk.Align.End;
			marker.Halign = Gtk.Align.End;

			Gtk.Overlay overlay = Gtk.Overlay.New ();
			overlay.SetChild (icon);
			overlay.AddOverlay (marker);

			Gtk.ToggleButton button = Gtk.ToggleButton.New ();
			button.CanFocus = false;
			button.SetCssClasses ([Resources.Styles.ToolBoxButton, AdwaitaStyles.Flat]);
			button.SetChild (overlay);

			stack = new ToolStack {
				Definition = stackDefinition,
				Button = button,
				Icon = icon,
			};
			tool_stacks[stackDefinition] = stack;

			button.Group = toggle_group;
			button.OnClicked += (_, _) => HandleToolButtonClicked (stack.Current);

			AttachFlyoutGestures (stack);
			InsertIntoSection (button, tool);
		}

		stack.Members.Add (tool);
		stack.Members.Sort ((a, b) => a.Priority - b.Priority);
		stack.Current ??= tool;
		tool_buttons[tool] = stack.Button;

		SetStackTooltip (stack);
	}

	/// <summary>
	/// Long press or right click opens the flyout, matching Photoshop. A plain click still
	/// selects the current member, so the common case stays one click.
	/// </summary>
	private void AttachFlyoutGestures (ToolStack stack)
	{
		Gtk.GestureLongPress longPress = Gtk.GestureLongPress.New ();
		longPress.OnPressed += (_, _) => ShowFlyout (stack);
		stack.Button.AddController (longPress);

		Gtk.GestureClick rightClick = Gtk.GestureClick.New ();
		rightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
		rightClick.OnPressed += (_, _) => ShowFlyout (stack);
		stack.Button.AddController (rightClick);
	}

	private void ShowFlyout (ToolStack stack)
	{
		Gtk.Box list = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (list);
		popover.SetParent (stack.Button);
		popover.Position = Gtk.PositionType.Right;

		foreach (BaseTool member in stack.Members) {

			Gtk.Button entry = Gtk.Button.New ();
			entry.SetCssClasses ([AdwaitaStyles.Flat]);
			entry.TooltipText = TooltipFor (member);

			Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
			row.Append (Gtk.Image.NewFromIconName (member.Icon));
			row.Append (Gtk.Label.New (member.Name));
			entry.SetChild (row);

			entry.OnClicked += (_, _) => {
				popover.Popdown ();
				HandleToolButtonClicked (member);
			};

			// Right click a flyout entry to pin/unpin it. The pin menu is shown after this
			// popover closes rather than nested inside it, which GTK handles far more reliably.
			Gtk.GestureClick entryRightClick = Gtk.GestureClick.New ();
			entryRightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
			entryRightClick.OnPressed += (_, _) => {
				popover.Popdown ();
				ShowPinMenu (stack.Button, member);
			};
			entry.AddController (entryRightClick);

			list.Append (entry);
		}

		// The popover is rebuilt per showing, so release it once it closes.
		popover.OnClosed += (_, _) => popover.Unparent ();

		popover.Popup ();
	}

	private void ShowPinMenu (Gtk.Widget anchor, BaseTool tool)
	{
		bool pinned = pinned_buttons.ContainsKey (tool);

		Gtk.Button action = Gtk.Button.New ();
		action.SetCssClasses ([AdwaitaStyles.Flat]);

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		row.Append (Gtk.Image.NewFromIconName ("view-pin-symbolic"));
		row.Append (Gtk.Label.New (pinned
			? Translations.GetString ("Unpin this item")
			: Translations.GetString ("Pin this item")));
		action.SetChild (row);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (action);
		popover.SetParent (anchor);
		popover.Position = Gtk.PositionType.Right;
		popover.OnClosed += (_, _) => popover.Unparent ();

		action.OnClicked += (_, _) => {
			popover.Popdown ();
			SetPinned (tool, !pinned);
		};

		popover.Popup ();
	}

	/// <summary>
	/// Pinning copies a tool into the pinned section at the top of the toolbox; it stays
	/// available in its stack's flyout either way.
	/// </summary>
	private void SetPinned (BaseTool tool, bool pinned)
	{
		if (pinned == pinned_buttons.ContainsKey (tool))
			return;

		if (pinned) {
			Gtk.ToggleButton button = CreateToolButton (tool);
			button.Group = toggle_group;
			button.OnClicked += (_, _) => HandleToolButtonClicked (tool);

			// Right click a pinned button to unpin it again.
			Gtk.GestureClick rightClick = Gtk.GestureClick.New ();
			rightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
			rightClick.OnPressed += (_, _) => ShowPinMenu (button, tool);
			button.AddController (rightClick);

			pinned_buttons[tool] = button;

			// Keep pinned buttons in the same order the tools themselves are in.
			int index = tools
				.Where (t => pinned_buttons.ContainsKey (t))
				.ToList ()
				.IndexOf (tool);

			pinned_section.Insert (button, index);
		} else {
			pinned_section.Remove (pinned_buttons[tool]);
			pinned_buttons.Remove (tool);
		}

		UpdateSectionVisibility ();
	}

	/// <summary>
	/// Pinned tools as a comma separated list of type names, for persisting to settings.
	/// Type names are used rather than <see cref="BaseTool.Name"/>, which is translated.
	/// </summary>
	public string PinnedTools {
		get => string.Join (",", tools.Where (pinned_buttons.ContainsKey).Select (t => t.GetType ().Name));
		set {
			HashSet<string> wanted = [.. value.Split (',', System.StringSplitOptions.RemoveEmptyEntries)];

			foreach (BaseTool tool in tools.ToList ())
				SetPinned (tool, wanted.Contains (tool.GetType ().Name));
		}
	}

	private void SetStackTooltip (ToolStack stack)
	{
		string members = string.Join (", ", stack.Members.Select (t => t.Name));
		string hint = Translations.GetString ("Long press or right click for more");

		stack.Button.TooltipText = $"{TooltipFor (stack.Current)}\n\n{members}\n{hint}";
		stack.Button.Name = stack.Current.Name;
	}

	private void InsertIntoSection (Gtk.Widget button, BaseTool tool)
	{
		int sectionIndex = SectionIndex (tool.Priority);

		// Position within the section, reusing the global ordering the ToolManager provides,
		// but counting each stack once (its position is that of its first member).
		int index = tools
			.Where (t => SectionIndex (t.Priority) == sectionIndex)
			.Where (t => IsFirstOfItsStack (t))
			.ToList ()
			.IndexOf (tool);

		sections[sectionIndex].Insert (button, index);
	}

	/// <summary>
	/// True for tools that occupy their own slot in the grid: unstacked tools, and the
	/// lowest-priority member of each stack.
	/// </summary>
	private bool IsFirstOfItsStack (BaseTool tool)
	{
		int[]? definition = StackDefinition (tool.Priority);

		if (definition is null)
			return true;

		return !tools.Any (t => StackDefinition (t.Priority) == definition && t.Priority < tool.Priority);
	}

	private void HandleToolButtonClicked (BaseTool tool)
	{
		tools.SetCurrentTool (tool);
	}

	/// <summary>
	/// If the tool was switched without clicking on the button (e.g. via shortcut key),
	/// ensure the tool's button is active. Note we don't need to deactivate the previous
	/// button since they're all in the same toggle button group. For a stacked tool this
	/// also promotes it to be the face of its stack.
	/// </summary>
	private void HandleToolActivated (BaseTool tool)
	{
		int[]? definition = StackDefinition (tool.Priority);

		if (definition is not null && tool_stacks.TryGetValue (definition, out ToolStack? stack)) {
			stack.Current = tool;
			stack.Icon.SetFromIconName (tool.Icon);
			SetStackTooltip (stack);
		}

		// ponytail: every button shares one toggle group, so exactly one can be lit. A pinned
		// tool has two buttons; light the pinned one, since that's the copy the user asked for.
		Gtk.ToggleButton toolButton = pinned_buttons.TryGetValue (tool, out Gtk.ToggleButton? pinnedButton)
			? pinnedButton
			: tool_buttons[tool];

		toolButton.Active = true;
	}

	private void HandleToolRemoved (BaseTool tool)
	{
		SetPinned (tool, false);

		Gtk.ToggleButton toolButton = tool_buttons[tool];
		tool_buttons.Remove (tool);

		int[]? definition = StackDefinition (tool.Priority);

		if (definition is not null && tool_stacks.TryGetValue (definition, out ToolStack? stack)) {

			stack.Members.Remove (tool);

			if (stack.Members.Count > 0) {
				// Other members remain, so the shared button stays.
				if (stack.Current == tool)
					stack.Current = stack.Members[0];

				stack.Icon.SetFromIconName (stack.Current.Icon);
				SetStackTooltip (stack);
				UpdateSectionVisibility ();
				return;
			}

			tool_stacks.Remove (definition);
		}

		sections[SectionIndex (tool.Priority)].Remove (toolButton);
		UpdateSectionVisibility ();
	}

	/// <summary>
	/// Hide empty sections, and only draw a separator where it actually divides two
	/// populated sections, so a missing addin doesn't leave a stray line.
	/// </summary>
	private void UpdateSectionVisibility ()
	{
		bool[] populated = new bool[sections.Length];

		foreach (BaseTool tool in tool_buttons.Keys)
			populated[SectionIndex (tool.Priority)] = true;

		for (int i = 0; i < sections.Length; i++)
			sections[i].Visible = populated[i];

		pinned_section.Visible = pinned_buttons.Count > 0;
		pinned_separator.Visible = pinned_buttons.Count > 0 && populated.Any (p => p);

		bool anyAbove = populated[0];
		for (int i = 1; i < sections.Length; i++) {
			separators[i - 1].Visible = anyAbove && populated[i];
			anyAbove |= populated[i];
		}
	}
}
