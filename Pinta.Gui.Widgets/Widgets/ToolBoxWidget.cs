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
		public Gtk.Popover? OpenFlyout { get; set; }
		public uint HoverTimeoutId { get; set; }
		public uint CloseTimeoutId { get; set; }
	}

	private ToolManager tools = null!; // NRT - set in factory method
					   // Stores the button corresponding to each tool. Tools in the same stack share a button.
	private readonly Dictionary<BaseTool, Gtk.ToggleButton> tool_buttons = new ();
	private readonly Dictionary<int[], ToolStack> tool_stacks = new ();
	// Dummy ToggleButton to use for grouping together the tools' buttons.
	private readonly Gtk.ToggleButton toggle_group = Gtk.ToggleButton.New ();
	// The pinned copies group separately, so a pinned tool can light both its buttons - one
	// group would force a choice. Activating this dummy leader turns the whole section off.
	private readonly Gtk.ToggleButton pinned_toggle_group = Gtk.ToggleButton.New ();

	private readonly Gtk.FlowBox[] sections = new Gtk.FlowBox[section_bounds.Length];
	private readonly Gtk.Separator[] separators = new Gtk.Separator[section_bounds.Length - 1];

	// Impasto: pinned tools get a copy in a highlighted section at the top of the toolbox.
	// They stay in their original spot as well - pinning copies, it doesn't move.
	private Gtk.Box pinned_container = null!;
	private Gtk.FlowBox pinned_section = null!;
	private Gtk.Separator pinned_separator = null!;
	private readonly Dictionary<BaseTool, Gtk.ToggleButton> pinned_buttons = new ();

	// While a pin menu is up, hovering its anchor must not pop the flyout back open -
	// the flyout's grab would dismiss the pin menu before it can be clicked.
	private Gtk.Popover? open_pin_menu;
	private Gtk.Popover? hint_popup;
	private uint hint_timeout_id;

	/// <param name="classic">
	/// The pre-fork layout: Vertical orientation, so ChildrenPerLine counts children per
	/// *column* and is left unbounded, letting the box overflow into another column only
	/// once it runs out of vertical space, instead of a fixed 2-column grid.
	/// </param>
	private static Gtk.FlowBox CreateSectionBox (bool classic)
	{
		Gtk.FlowBox section = Gtk.FlowBox.New ();
		if (classic) {
			section.SetOrientation (Gtk.Orientation.Vertical);
			// GtkFlowBox asks for ceil(children / min-children-per-line) *columns* of width,
			// so upstream's 8 reserves three columns for 22 tools even when one is in use -
			// that reserved width is the wide empty box around the column. A minimum above
			// the tool count asks for the one column that is actually drawn.
			section.MinChildrenPerLine = 1024;
			section.MaxChildrenPerLine = 1024;
		} else {
			// Horizontal orientation: ChildrenPerLine counts children per *row*, so this
			// pins every section to 2 columns and the buttons line up across the separators.
			section.SetOrientation (Gtk.Orientation.Horizontal);
			section.MinChildrenPerLine = 2;
			section.MaxChildrenPerLine = 2;
		}
		// Homogeneous stretches every cell to the widest one and inflates the box's minimum
		// width, which is what made the thin column layout several buttons wide. The
		// sectioned layout needs it so its two columns line up across the separators.
		section.Homogeneous = !classic;
		section.SelectionMode = Gtk.SelectionMode.None; // Don't allow the buttons to be selected.
		section.Visible = false; // Shown when it receives its first tool.
		return section;
	}

	// Impasto: whether to use the pre-fork single-column layout instead of the sectioned
	// 2-column grid. Set once from Configure(); changing it takes effect on next launch,
	// since the section boxes below are only built once.
	private bool classic_layout;

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Vertical);
	}

	private void BuildLayout ()
	{
		pinned_section = CreateSectionBox (classic_layout);
		pinned_section.Visible = true; // Container visibility is managed instead.

		Gtk.Image pinIcon = Gtk.Image.NewFromIconName (Resources.StandardIcons.Pin);
		pinIcon.PixelSize = 15;
		pinIcon.Halign = Gtk.Align.Start;
		pinIcon.AddCssClass (AdwaitaStyles.DimLabel);
		pinIcon.TooltipText = Translations.GetString ("Pinned items");

		// Right click the pin icon to clear all pinned tools at once.
		Gtk.GestureClick pinIconRightClick = Gtk.GestureClick.New ();
		pinIconRightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
		pinIconRightClick.OnPressed += (_, _) => ShowClearPinnedMenu (pinIcon);
		pinIcon.AddController (pinIconRightClick);

		pinned_container = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		pinned_container.AddCssClass (Resources.Styles.PinnedSection);
		pinned_container.Visible = false; // Shown while any tool is pinned.
		pinned_container.Append (pinIcon);
		pinned_container.Append (pinned_section);
		Append (pinned_container);

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

			Gtk.FlowBox section = CreateSectionBox (classic_layout);
			sections[i] = section;
			Append (section);
		}
	}

	public static ToolBoxWidget New (ToolManager tools, bool classicLayout)
	{
		ToolBoxWidget widget = NewWithProperties ([]);
		widget.Configure (tools, classicLayout);
		return widget;
	}

	private void Configure (ToolManager tools, bool classicLayout)
	{
		this.tools = tools;
		this.classic_layout = classicLayout;

		BuildLayout ();

		tools.ToolAdded += (_, e) => HandleToolAdded (e.Tool);
		tools.ToolRemoved += (_, e) => HandleToolRemoved (e.Tool);
		tools.ToolActivated += (_, e) => HandleToolActivated (e.Tool);
		PintaCore.Shortcuts.ShortcutsChanged += (_, _) => RefreshTooltips ();
	}

	/// <summary>
	/// In classic layout every tool shares section 0, so the sections below it never
	/// populate and stay hidden, leaving one continuous column.
	/// </summary>
	internal int SectionIndex (int priority)
	{
		if (classic_layout)
			return 0;

		for (int i = 0; i < section_bounds.Length; i++)
			if (priority <= section_bounds[i])
				return i;

		return section_bounds.Length - 1;
	}

	internal static int[]? StackDefinition (int priority)
		=> stack_definitions.FirstOrDefault (s => s.Contains (priority));

	private string TooltipFor (BaseTool tool)
	{
		string shortcutText = "";
		KeyGesture shortcut = tools.GetEffectiveShortcutKey (tool);
		if (shortcut.IsValid) {
			string shortcutLabel = Translations.GetString ("Shortcut key");
			shortcutText = $"{shortcutLabel}: {shortcut.ToLabel ()}\n";
		}

		return $"{tool.Name}\n{shortcutText}\n{tool.StatusBarText}";
	}

	/// <summary>
	/// In the thin column layout each button is a square around its icon: even padding from
	/// the CSS class, and centered so the flow box cell doesn't stretch it out sideways.
	/// The sectioned layout wants its buttons filling the grid cell.
	/// </summary>
	private void StyleToolButton (Gtk.ToggleButton button)
	{
		if (!classic_layout) {
			button.SetCssClasses ([Resources.Styles.ToolBoxButton, AdwaitaStyles.Flat]);
			return;
		}

		button.SetCssClasses ([Resources.Styles.ToolBoxButton, Resources.Styles.ToolBoxButtonThin, AdwaitaStyles.Flat]);
		button.Halign = Gtk.Align.Center;
		button.Valign = Gtk.Align.Center;
	}

	private Gtk.ToggleButton CreateToolButton (BaseTool tool)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.IconName = tool.Icon;
		button.Name = tool.Name;
		button.CanFocus = false;

		StyleToolButton (button);

		button.TooltipText = TooltipFor (tool);

		return button;
	}

	private void RefreshTooltips ()
	{
		// Stacked buttons have no tooltip; their flyout entries are rebuilt on demand.
		foreach (var (tool, button) in tool_buttons)
			if (StackDefinition (tool.Priority) is null)
				button.TooltipText = TooltipFor (tool);

		foreach (var (tool, button) in pinned_buttons)
			button.TooltipText = TooltipFor (tool);
	}

	/// <summary>
	/// Hovering shows the tool's hint in a caption popover rather than a native tooltip. Only
	/// flyout entries need this: GTK4 mapping a tooltip popup while the flyout's own grabbing
	/// popover is open can leave the pointer grab stuck, freezing input until the next click.
	/// Buttons outside a flyout use ordinary tooltips, made opaque in style.css.
	/// </summary>
	private void AttachHint (Gtk.Widget widget, BaseTool tool)
	{
		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();

		motion.OnEnter += (_, _) => {
			CancelHintTimeout ();
			hint_timeout_id = GLib.Functions.TimeoutAdd (0, 500, () => {
				hint_timeout_id = 0;
				ShowHint (widget, TooltipFor (tool));
				return false;
			});
		};

		motion.OnLeave += (_, _) => {
			CancelHintTimeout ();
			HideHint ();
		};

		widget.AddController (motion);
	}

	private void CancelHintTimeout ()
	{
		if (hint_timeout_id == 0)
			return;

		GLib.Source.Remove (hint_timeout_id);
		hint_timeout_id = 0;
	}

	private void ShowHint (Gtk.Widget anchor, string text)
	{
		HideHint ();

		Gtk.Label caption = Gtk.Label.New (text);
		caption.Halign = Gtk.Align.Start;
		caption.Justify = Gtk.Justification.Left;
		caption.Wrap = true;
		caption.MaxWidthChars = 32;

		Gtk.Popover popup = Gtk.Popover.New ();
		// Not autohiding: an autohiding popover grabs the pointer, which is exactly what makes
		// a hint next to the flyout freeze input. This one never takes a grab and is dismissed
		// by hand when the cursor leaves.
		popup.Autohide = false;
		popup.CanTarget = false;
		popup.HasArrow = false; // Reads as a tooltip, not as a menu anchored to the entry.
		popup.AddCssClass ("toolbox-hint");
		popup.SetChild (caption);
		popup.SetParent (anchor);
		popup.Position = Gtk.PositionType.Right;

		hint_popup = popup;
		popup.Popup ();
	}

	private void HideHint ()
	{
		if (hint_popup is null)
			return;

		hint_popup.Popdown ();
		hint_popup.Unparent ();
		hint_popup = null;
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

		// Right click any standalone tool to pin/unpin it.
		Gtk.GestureClick rightClick = Gtk.GestureClick.New ();
		rightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
		rightClick.OnPressed += (_, _) => ShowPinMenu (toolButton, tool);
		toolButton.AddController (rightClick);

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
			marker.AddCssClass ("stack-marker");
			marker.Valign = Gtk.Align.End;
			marker.Halign = Gtk.Align.End;

			Gtk.Overlay overlay = Gtk.Overlay.New ();
			overlay.SetChild (icon);
			overlay.AddOverlay (marker);

			Gtk.ToggleButton button = Gtk.ToggleButton.New ();
			button.CanFocus = false;
			StyleToolButton (button);
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
	/// Hover, long press, or right click opens the flyout. A plain click still selects the
	/// current member, so the common case stays one click. Hover uses a short delay so just
	/// passing over the button doesn't pop it open.
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

		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnEnter += (_, _) => {
			CancelCloseTimeout (stack);
			CancelHoverTimeout (stack);
			stack.HoverTimeoutId = GLib.Functions.TimeoutAdd (0, 350, () => {
				stack.HoverTimeoutId = 0;
				ShowFlyout (stack);
				return false;
			});
		};
		motion.OnLeave += (_, _) => {
			CancelHoverTimeout (stack);
			ScheduleClose (stack);
		};
		stack.Button.AddController (motion);
	}

	private static void CancelHoverTimeout (ToolStack stack)
	{
		if (stack.HoverTimeoutId == 0)
			return;

		GLib.Source.Remove (stack.HoverTimeoutId);
		stack.HoverTimeoutId = 0;
	}

	private static void CancelCloseTimeout (ToolStack stack)
	{
		if (stack.CloseTimeoutId == 0)
			return;

		GLib.Source.Remove (stack.CloseTimeoutId);
		stack.CloseTimeoutId = 0;
	}

	/// <summary>
	/// Close the flyout shortly after the cursor leaves both the button and the flyout. The
	/// delay gives the cursor time to cross the gap between them.
	/// </summary>
	private void ScheduleClose (ToolStack stack)
	{
		if (stack.OpenFlyout is null)
			return;

		CancelCloseTimeout (stack);
		stack.CloseTimeoutId = GLib.Functions.TimeoutAdd (0, 400, () => {
			stack.CloseTimeoutId = 0;
			// Don't close while a pin menu is anchored to the flyout - it would take the menu with it.
			if (open_pin_menu is null)
				stack.OpenFlyout?.Popdown ();
			return false;
		});
	}

	private void ShowFlyout (ToolStack stack)
	{
		if (stack.OpenFlyout is not null || open_pin_menu is not null)
			return;

		Gtk.Box list = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (list);
		popover.SetParent (stack.Button);
		popover.Position = Gtk.PositionType.Right;

		foreach (BaseTool member in stack.Members) {

			Gtk.Button entry = Gtk.Button.New ();
			entry.SetCssClasses ([AdwaitaStyles.Flat]);

			Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
			row.Append (Gtk.Image.NewFromIconName (member.Icon));
			row.Append (Gtk.Label.New (member.Name));
			entry.SetChild (row);

			AttachHint (entry, member);

			entry.OnClicked += (_, _) => {
				popover.Popdown ();
				HandleToolButtonClicked (member);
			};

			// Right click a flyout entry to pin/unpin it. The pin menu is nested off the entry
			// itself and the flyout stays open, so it reads as belonging to that tool.
			Gtk.GestureClick entryRightClick = Gtk.GestureClick.New ();
			entryRightClick.SetButton (Gdk.Constants.BUTTON_SECONDARY);
			entryRightClick.OnPressed += (_, _) => {
				CancelCloseTimeout (stack); // Keep the flyout up while the pin menu is anchored to it.
				ShowPinMenu (entry, member);
			};
			entry.AddController (entryRightClick);

			list.Append (entry);
		}

		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnEnter += (_, _) => CancelCloseTimeout (stack);
		motion.OnLeave += (_, _) => ScheduleClose (stack);
		popover.AddController (motion);

		// The popover is rebuilt per showing, so release it once it closes.
		popover.OnClosed += (_, _) => {
			CancelHintTimeout ();
			HideHint (); // Its anchor is about to go away with the flyout.
			CancelCloseTimeout (stack);
			stack.OpenFlyout = null;
			popover.Unparent ();
		};

		stack.OpenFlyout = popover;
		popover.Popup ();
	}

	private void ShowPinMenu (Gtk.Widget anchor, BaseTool tool)
	{
		bool pinned = pinned_buttons.ContainsKey (tool);

		Gtk.Button action = Gtk.Button.New ();
		action.SetCssClasses ([AdwaitaStyles.Flat]);

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		row.Append (Gtk.Image.NewFromIconName (Resources.StandardIcons.Pin));
		row.Append (Gtk.Label.New (pinned
			? Translations.GetString ("Unpin this item")
			: Translations.GetString ("Pin this item")));
		action.SetChild (row);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (action);
		popover.SetParent (anchor);
		popover.Position = Gtk.PositionType.Right;
		popover.OnClosed += (_, _) => {
			open_pin_menu = null;
			popover.Unparent ();
		};

		action.OnClicked += (_, _) => {
			popover.Popdown ();
			SetPinned (tool, !pinned);
		};

		open_pin_menu = popover;
		popover.Popup ();
	}

	private void ShowClearPinnedMenu (Gtk.Widget anchor)
	{
		Gtk.Button action = Gtk.Button.New ();
		action.SetCssClasses ([AdwaitaStyles.Flat]);

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		row.Append (Gtk.Image.NewFromIconName (Resources.StandardIcons.Pin));
		row.Append (Gtk.Label.New (Translations.GetString ("Clear pinned tools")));
		action.SetChild (row);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (action);
		popover.SetParent (anchor);
		popover.Position = Gtk.PositionType.Right;
		popover.OnClosed += (_, _) => {
			open_pin_menu = null;
			popover.Unparent ();
		};

		action.OnClicked += (_, _) => {
			popover.Popdown ();
			foreach (BaseTool tool in pinned_buttons.Keys.ToList ())
				SetPinned (tool, false);
		};

		open_pin_menu = popover;
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
			button.Group = pinned_toggle_group;
			button.OnClicked += (_, _) => HandleToolButtonClicked (tool);

			// Pinning the tool that's already selected has to light the new copy itself -
			// nothing re-selects the tool on the way out of here.
			if (tools.CurrentTool == tool)
				button.Active = true;

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
		// No tooltip on the group button - the flyout opens on hover, and each flyout entry
		// carries its own hint.
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

		// A pinned tool has two buttons and they group separately, so both light. For a stacked
		// tool the first one is the shared stack button, which is the point - picking a member
		// from the flyout has to light the stack it came from.
		tool_buttons[tool].Active = true;

		if (pinned_buttons.TryGetValue (tool, out Gtk.ToggleButton? pinnedButton))
			pinnedButton.Active = true;
		else
			pinned_toggle_group.Active = true; // No pinned copy: clear the section.
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

		pinned_container.Visible = pinned_buttons.Count > 0;
		pinned_separator.Visible = pinned_buttons.Count > 0 && populated.Any (p => p);

		bool anyAbove = populated[0];
		for (int i = 1; i < sections.Length; i++) {
			separators[i - 1].Visible = anyAbove && populated[i];
			anyAbove |= populated[i];
		}
	}
}
