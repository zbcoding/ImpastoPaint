//
// ToolManager.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Pinta.Core;

public interface IToolService
{
	/// <summary>
	/// Adds a new tool to the tool box.
	/// </summary>
	void AddTool (BaseTool tool);

	/// <summary>
	/// Instructs the current tool to commit any work that is in a temporary state.
	/// </summary>
	void Commit ();

	/// <summary>
	/// Gets the currently selected tool.
	/// </summary>
	BaseTool? CurrentTool { get; }

	/// <summary>
	/// Performs the mouse down event for the currently selected tool.
	/// </summary>
	void DoMouseDown (Document document, ToolMouseEventArgs e);

	/// <summary>
	/// Gives the currently selected tool a chance to handle Alt+Mouse Scroll.
	/// Returns 'true' if handled.
	/// </summary>
	bool DoMouseScroll (Document document, bool increase);

	/// <summary>
	/// Whether the currently selected tool responds to Alt+Mouse Scroll.
	/// </summary>
	bool CurrentToolSupportsMouseScroll { get; }

	/// <summary>
	/// Gets the previously selected tool.
	/// </summary>
	BaseTool? PreviousTool { get; }

	/// <summary>
	/// Removes the first found tool of the specified type from tool box.
	/// </summary>
	void RemoveInstanceOfTool<T> () where T : BaseTool;

	/// <summary>
	/// Sets the current tool to the specified tool.
	/// </summary>
	void SetCurrentTool (BaseTool tool);

	/// <summary>
	/// Sets the current tool to the first tool with the specified tool type name, like
	/// 'PencilTool'. Returns a value indicating if tool was successfully changed.
	/// </summary>
	bool SetCurrentTool (string tool);

	/// <summary>
	/// Sets the current tool to the next tool with the specified shortcut.
	/// </summary>
	bool SetCurrentTool (KeyGesture shortcut);
}

public sealed class ToolManager : IEnumerable<BaseTool>, IToolService
{
	private readonly SortedSet<BaseTool> tools = new (new ToolSorter ());

	// Impasto: user overrides for the toolbox activation key, keyed by tool type name
	// (e.g. "PaintBrushTool") since BaseTool.ShortcutKey is a hardcoded virtual property
	// on each Pinta.Tools subclass and those files should not be edited per-tool.
	private readonly Dictionary<string, KeyGesture> shortcut_key_overrides = [];

	private readonly WorkspaceManager workspace_manager;
	private readonly ChromeManager chrome_manager;
	public ToolManager (WorkspaceManager workspaceManager, ChromeManager chromeManager)
	{
		workspace_manager = workspaceManager;
		chrome_manager = chromeManager;

		// Before the active document has changed, the current tool should commit unfinished changes.
		workspace_manager.PreActiveDocumentChanged += (_, _) => Commit ();
	}

	/// <summary>
	/// The activation key for the toolbox, honoring any user override.
	/// </summary>
	public KeyGesture GetEffectiveShortcutKey (BaseTool tool)
		=> shortcut_key_overrides.TryGetValue (tool.GetType ().Name, out var gesture) ? gesture : new KeyGesture (tool.ShortcutKey);

	public void SetShortcutKeyOverride (BaseTool tool, KeyGesture gesture)
		=> SetShortcutKeyOverride (tool.GetType ().Name, gesture);

	public void SetShortcutKeyOverride (string toolTypeName, KeyGesture gesture)
		=> shortcut_key_overrides[toolTypeName] = gesture;

	public void ResetShortcutKeyOverride (BaseTool tool)
		=> shortcut_key_overrides.Remove (tool.GetType ().Name);

	private bool is_panning;
	private Gdk.Cursor? stored_cursor;

	public event EventHandler<ToolEventArgs>? ToolAdded;
	public event EventHandler<ToolEventArgs>? ToolRemoved;
	public event EventHandler<ToolEventArgs>? ToolActivated;

	public BaseTool? CurrentTool { get; private set; }

	public BaseTool? PreviousTool { get; private set; }

	public void AddTool (BaseTool tool)
	{
		if (!tools.Add (tool))
			throw new Exception ("Attempted to add a duplicate tool");

		ToolAdded?.Invoke (this, new ToolEventArgs (tool));

		if (CurrentTool is null)
			SetCurrentTool (tool);
	}

	public void RemoveInstanceOfTool<T> () where T : BaseTool
	{
		T? tool =
			tools.OfType<T> ()
			.FirstOrDefault ();

		if (tool is null)
			return;

		if (!tools.Remove (tool))
			throw new Exception ("Attempted to remove a tool that wasn't registered");

		// Are we trying to remove the current tool?
		if (CurrentTool == tool) {
			// Can we set it back to the previous tool?
			if (PreviousTool is not null && PreviousTool != CurrentTool)
				SetCurrentTool (PreviousTool);
			else if (tools.Count != 0)  // Any tool?
				SetCurrentTool (tools.First ());
			else {
				// There are no tools left.
				DeactivateTool (tool, null);
				PreviousTool = null;
				CurrentTool = null;
			}
		}

		ToolRemoved?.Invoke (this, new ToolEventArgs (tool));
	}

	private BaseTool? FindTool (string name)
	{
		return tools.FirstOrDefault (t => string.Compare (name, t.GetType ().Name, true) == 0);
	}

	public void Commit ()
	{
		CurrentTool?.DoCommit (workspace_manager.ActiveDocumentOrDefault);
	}

	public void SetCurrentTool (BaseTool tool)
	{
		// Bail if this is already the current tool
		if (CurrentTool == tool)
			return;

		// Unload previous tool if needed
		if (CurrentTool is not null) {
			PreviousTool = CurrentTool;
			DeactivateTool (PreviousTool, tool);
		}

		// Load new tool
		CurrentTool = tool;

		tool.DoActivated (workspace_manager.ActiveDocumentOrDefault);

		ToolImage.SetFromIconName (tool.Icon);

		BuildToolBar (tool);

		workspace_manager.Invalidate ();
		chrome_manager.SetStatusBarText ($" {tool.Name}: {tool.StatusBarText}");

		ToolActivated?.Invoke (this, new ToolEventArgs (tool));
	}

	public bool SetCurrentTool (string tool)
	{
		if (FindTool (tool) is not BaseTool t)
			return false;

		SetCurrentTool (t);
		return true;
	}

	public bool SetCurrentTool (KeyGesture shortcut)
	{
		if (FindNextTool (shortcut) is not BaseTool tool)
			return false;

		SetCurrentTool (tool);
		return true;
	}

	private BaseTool? FindNextTool (KeyGesture shortcut)
	{
		var shortcut_tools =
			tools
			.Where (t => GetEffectiveShortcutKey (t) == shortcut)
			.ToImmutableArray ();

		// No tools with this shortcut, bail
		if (shortcut_tools.Length == 0)
			return null;

		// Only one option, return it
		if (shortcut_tools.Length == 1 || CurrentTool is null)
			return shortcut_tools.First ();

		// Get the tool after the currently selected tool
		int next_index = shortcut_tools.IndexOf (CurrentTool) + 1;

		// Wrap if we're past the final tool
		if (next_index >= shortcut_tools.Length)
			next_index = 0;

		return shortcut_tools[next_index];
	}

	// Impasto: tools build into a plain box, then their widgets are regrouped so that each
	// "Label: [widget]" pair stays together on whichever row it lands on. The border goes
	// around the settings area as a whole, not around the individual groups.
	// When wrapping is off the same groups go into the horizontally scrolling row instead.
	private void BuildToolBar (BaseTool tool)
	{
		chrome_manager.ToolToolBar.Append (ToolNameChip);
		chrome_manager.ToolToolBar.Append (ToolSeparator);

		tool.DoBuildToolBar (ToolWidgetsBox);

		bool wrap = WrapToolBarRows;
		Gtk.Box? group = null;
		bool any_settings = false;

		// Zero-width, one row tall: keeps the settings area exactly as tall for tools with no
		// settings as for tools with them, so nothing below the toolbar moves when switching.
		AppendToolWidget (ToolSettingsSpacer, wrap);

		while (ToolWidgetsBox.GetFirstChild () is Gtk.Widget child) {

			ToolWidgetsBox.Remove (child);

			// Separators divide groups and are not part of any of them.
			if (child is Gtk.Separator) {
				group = null;
				AppendToolWidget (child, wrap);
				continue;
			}

			// A label starts a new group, since it labels whatever follows it.
			if (child is Gtk.Label label) {

				// Tools pad their labels with spaces (" Font: "). Drop that, the CSS
				// spaces the groups out instead - fake padding wastes width when the
				// settings have to wrap, and spacer-only labels leave stray gaps.
				string text = label.GetLabel ()?.Trim () ?? string.Empty;

				if (text.Length == 0)
					continue;

				label.SetLabel (text);

				group = null;
			}

			if (group is null) {
				group = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
				group.AddCssClass (TOOL_SETTING_CLUSTER_CLASS);
				group.Valign = Gtk.Align.Center;
				AppendToolWidget (group, wrap);
			}

			group.Append (child);
			any_settings |= child.Visible;
		}

		// The container is always in place, at the same size, so that switching between tools
		// with and without settings doesn't shift the rest of the window. Tools without any
		// settings just hide its border.
		Gtk.Widget container = wrap ? ToolWidgetsWrap : ToolWidgetsScroll;

		if (any_settings)
			container.RemoveCssClass (TOOL_SETTINGS_EMPTY_CLASS);
		else
			container.AddCssClass (TOOL_SETTINGS_EMPTY_CLASS);

		chrome_manager.ToolToolBar.Append (container);
	}

	private void AppendToolWidget (Gtk.Widget widget, bool wrap)
	{
		if (wrap)
			ToolWidgetsWrap.Append (widget);
		else
			ToolWidgetsRow.Append (widget);
	}

	private void ClearToolBar ()
	{
		ToolWidgetsBox.RemoveAll ();

		if (tool_widgets_wrap is not null)
			while (tool_widgets_wrap.GetFirstChild () is Gtk.Widget child) {
				UngroupToolWidgets (child);
				tool_widgets_wrap.Remove (child);
			}

		if (tool_widgets_row is not null)
			while (tool_widgets_row.GetFirstChild () is Gtk.Widget child) {
				UngroupToolWidgets (child);
				tool_widgets_row.Remove (child);
			}

		chrome_manager.ToolToolBar.RemoveAll ();
	}

	// The group boxes are rebuilt each time, but the widgets inside them are owned and reused
	// by the tools, so empty the group before it is dropped.
	private static void UngroupToolWidgets (Gtk.Widget child)
	{
		if (child is not Gtk.Box group || !group.HasCssClass (TOOL_SETTING_CLUSTER_CLASS))
			return;

		while (group.GetFirstChild () is Gtk.Widget widget)
			group.Remove (widget);
	}

	private void DeactivateTool (BaseTool tool, BaseTool? newTool)
	{
		ClearToolBar ();

		tool.DoDeactivated (workspace_manager.ActiveDocumentOrDefault, newTool);
	}

	// Issue #1334: effect dialogs are non-modal so they don't dim the canvas.
	// The trade-off is that nothing else blocks canvas edits, so lock tool
	// input while a live preview is running instead of relying on modality.
	// ponytail: gates painting only; layer/tool panel switches still get through.
	public void DoMouseDown (Document document, ToolMouseEventArgs args)
	{
		if (PintaCore.LivePreview.IsEnabled)
			return;

		// A paint stroke on a layer with an active transform node would draw into the local raster,
		// which the canvas does not paint for that layer — it paints the transformed composite. Offer
		// to bake the transform; once accepted the stroke proceeds on the plain layer, cancelled it is
		// swallowed. Tools that don't write to the current layer (selection, picker, zoom, pan) are
		// left alone.
		if (CurrentTool?.WritesToCurrentLayer == true
			&& args.MouseButton != MouseButton.Middle // middle button is pan, not a stroke
			&& document.Layers.CurrentUserLayer is { } paintLayer
			&& paintLayer.HasActiveTransform) {
			if (!ObjectRasterizer.ConfirmRasterizeToPaint (PintaCore.Chrome, paintLayer))
				return;
			if (!ObjectRasterizer.RasterizeModifierStack (document, PintaCore.Workspace, PintaCore.Chrome, paintLayer))
				return; // the bake failed; do not let the stroke land on a still-transformed layer
		}

		if (!TryMouseDownPanOverride (document, args))
			CurrentTool?.DoMouseDown (document, ApplySnapping (args));
	}

	public void DoMouseMove (Document document, ToolMouseEventArgs args)
	{
		if (PintaCore.LivePreview.IsEnabled)
			return;
		if (!TryMouseMovePanOverride (document, args))
			CurrentTool?.DoMouseMove (document, ApplySnapping (args));

		// Guides are feedback for a drag in progress, not for hovering over the canvas.
		if (args.MouseButton == MouseButton.None)
			PintaCore.CanvasGrid.ClearActiveGuides ();
	}

	public void DoMouseUp (Document document, ToolMouseEventArgs args)
	{
		if (PintaCore.LivePreview.IsEnabled)
			return;
		if (!TryMouseUpPanOverride (document, args))
			CurrentTool?.DoMouseUp (document, ApplySnapping (args));

		// The drag is over, so the guides it was holding stop being shown.
		PintaCore.CanvasGrid.ClearActiveGuides ();
	}

	/// <summary>
	/// Snaps the cursor position to the grid (or to ruler units when the grid is
	/// hidden) for tools that opt in, so every snapping tool shares one rule.
	/// </summary>
	private ToolMouseEventArgs ApplySnapping (ToolMouseEventArgs args)
	{
		if (CurrentTool?.UseSnapping != true)
			return args;

		PointD snapped = PintaCore.CanvasGrid.SnapPoint (args.PointDouble);

		if (snapped == args.PointDouble)
			return args;

		return args.WithPoint (snapped);
	}

	public bool DoKeyDown (Document document, ToolKeyEventArgs args)
		=> CurrentTool?.DoKeyDown (document, args) ?? false;

	public bool DoMouseScroll (Document document, bool increase)
		=> CurrentTool?.DoMouseScroll (document, increase) ?? false;

	public bool CurrentToolSupportsMouseScroll
		=> CurrentTool?.SupportsMouseScroll ?? false;

	public bool DoKeyUp (Document document, ToolKeyEventArgs args)
		=> CurrentTool?.DoKeyUp (document, args) ?? false;

	public void DoAfterSave (Document document)
		=> CurrentTool?.DoAfterSave (document);

	public Task<bool> DoHandlePaste (Document document, Gdk.Clipboard clipboard)
		=> CurrentTool?.DoHandlePaste (document, clipboard) ?? Task.FromResult (false);

	public IEnumerator<BaseTool> GetEnumerator ()
		=> tools.GetEnumerator ();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
		=> tools.GetEnumerator ();

	private bool TryMouseDownPanOverride (Document document, ToolMouseEventArgs args)
	{
		if (is_panning)
			return true;

		if (args.MouseButton != MouseButton.Middle || !TryGetPanTool (out BaseTool? pan))
			return false;

		is_panning = true;
		stored_cursor = document.Workspace.Canvas.Cursor;
		document.Workspace.Canvas.Cursor = pan.DefaultCursor;
		pan.DoMouseDown (document, args);
		return true;
	}

	private bool TryMouseMovePanOverride (Document document, ToolMouseEventArgs args)
	{
		if (!is_panning || !TryGetPanTool (out var pan))
			return false;

		pan.DoMouseMove (document, args);
		return true;
	}

	private bool TryMouseUpPanOverride (Document document, ToolMouseEventArgs args)
	{
		if (!is_panning || !TryGetPanTool (out var pan))
			return false;

		// Ignore any mouse button releases that aren't Middle
		if (args.MouseButton != MouseButton.Middle)
			return true;

		is_panning = false;
		pan.DoMouseUp (document, args);
		document.Workspace.Canvas.Cursor = stored_cursor;
		return true;
	}

	private bool TryGetPanTool ([NotNullWhen (true)] out BaseTool? tool)
	{
		tool = FindTool ("PanTool");

		return tool is not null;
	}

	private sealed class ToolSorter : Comparer<BaseTool>
	{
		public override int Compare (BaseTool? x, BaseTool? y)
		{
			int result = (x?.Priority ?? 0) - (y?.Priority ?? 0);

			if (result != 0)
				return result;

			// If two tools have the same priority, sort by type name so that both tools can still
			// be inserted into the set.
			string x_type = x?.GetType ().AssemblyQualifiedName ?? string.Empty;
			string y_type = y?.GetType ().AssemblyQualifiedName ?? string.Empty;
			return x_type.CompareTo (y_type);
		}
	}

	private Gtk.Label? tool_label;
	private Gtk.Image? tool_image;
	private Gtk.Separator? tool_sep;
	private Gtk.Box? tool_widgets_box;
	private Gtk.Box? tool_widgets_row;
	private Gtk.Box? tool_name_chip;
	private Gtk.Box? tool_settings_spacer;
	private Gtk.ScrolledWindow? tool_widgets_scroll;
	private Adw.WrapBox? tool_widgets_wrap;
	private bool wrap_tool_bar_rows = true;

	// Draws the border: the tool name chip, and the tool settings area as a whole.
	private const string TOOL_SETTING_GROUP_CLASS = "tool-setting-group";

	// Offsets the settings away from the tool name chip.
	private const string TOOL_SETTINGS_AREA_CLASS = "tool-settings-area";

	// Hides the border for tools that have no settings, without changing the size.
	private const string TOOL_SETTINGS_EMPTY_CLASS = "tool-settings-empty";

	// One row of settings, reserved whether or not the current tool has any.
	private const int TOOL_SETTINGS_ROW_HEIGHT = 34;

	// Unstyled marker for the boxes that keep a "Label: [widget]" pair together on
	// whichever row it wraps onto. These used to carry the border too, which put a
	// box around every individual setting - and a stray pill around groups that hold
	// nothing but spacing.
	private const string TOOL_SETTING_CLUSTER_CLASS = "tool-setting-cluster";

	// AdwWrapBox is only available in libadwaita 1.7+; older systems keep the scrolling toolbar.
	private static readonly bool wrap_box_supported = Adw.Functions.GetMajorVersion () > 1 || Adw.Functions.GetMinorVersion () >= 7;

	/// <summary>
	/// Impasto: wrap the tool settings onto additional rows when the window is too narrow
	/// to show them all, instead of scrolling them horizontally.
	/// </summary>
	public bool WrapToolBarRows {
		get => wrap_tool_bar_rows && wrap_box_supported;
		set {
			if (wrap_tool_bar_rows == value)
				return;

			wrap_tool_bar_rows = value;

			if (CurrentTool is not BaseTool tool)
				return;

			ClearToolBar ();
			BuildToolBar (tool);
		}
	}

	private Gtk.Label ToolLabel => tool_label ??= Gtk.Label.New (string.Format (" {0}:  ", Translations.GetString ("Tool")));
	private Gtk.Image ToolImage => tool_image ??= Gtk.Image.New ();
	private Gtk.Separator ToolSeparator => tool_sep ??= GtkExtensions.CreateToolBarSeparator ();
	// Staging area the tools build into; the widgets are then regrouped into the row or wrap box.
	private Gtk.Box ToolWidgetsBox => tool_widgets_box ??= Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
	private Gtk.Box ToolWidgetsRow => tool_widgets_row ??= Gtk.Box.New (Gtk.Orientation.Horizontal, 0);

	private Gtk.Box ToolSettingsSpacer {
		get {
			if (tool_settings_spacer == null) {
				tool_settings_spacer = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
				tool_settings_spacer.WidthRequest = 0;
				tool_settings_spacer.HeightRequest = TOOL_SETTINGS_ROW_HEIGHT;
			}

			return tool_settings_spacer;
		}
	}

	// Impasto: the tool name and icon, in their own bordered chip.
	private Gtk.Box ToolNameChip {
		get {
			if (tool_name_chip == null) {
				tool_name_chip = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
				tool_name_chip.AddCssClass (TOOL_SETTING_GROUP_CLASS);
				tool_name_chip.Valign = Gtk.Align.Center;
				tool_name_chip.Append (ToolLabel);
				tool_name_chip.Append (ToolImage);
			}

			return tool_name_chip;
		}
	}

	// Scroll the toolbar contents if they are very long (e.g. the line/curve tool).
	private Gtk.ScrolledWindow ToolWidgetsScroll {
		get {
			if (tool_widgets_scroll == null) {
				tool_widgets_scroll = Gtk.ScrolledWindow.New ();
				tool_widgets_scroll.Child = ToolWidgetsRow;
				tool_widgets_scroll.HscrollbarPolicy = Gtk.PolicyType.Automatic;
				tool_widgets_scroll.VscrollbarPolicy = Gtk.PolicyType.Never;
				tool_widgets_scroll.HasFrame = false;
				tool_widgets_scroll.OverlayScrolling = true;
				// Impasto: TopLeft puts the content there, i.e. the scrollbar goes below the widgets.
				tool_widgets_scroll.WindowPlacement = Gtk.CornerType.TopLeft;
				tool_widgets_scroll.Hexpand = true;
				tool_widgets_scroll.Halign = Gtk.Align.Fill;
				tool_widgets_scroll.AddCssClass (TOOL_SETTING_GROUP_CLASS);
				tool_widgets_scroll.AddCssClass (TOOL_SETTINGS_AREA_CLASS);
			}

			return tool_widgets_scroll;
		}
	}

	private Adw.WrapBox ToolWidgetsWrap {
		get {
			if (tool_widgets_wrap == null) {
				tool_widgets_wrap = Adw.WrapBox.New ();
				tool_widgets_wrap.ChildSpacing = 0;
				tool_widgets_wrap.LineSpacing = 4;
				tool_widgets_wrap.Hexpand = true;
				tool_widgets_wrap.Halign = Gtk.Align.Fill;
				tool_widgets_wrap.Valign = Gtk.Align.Center;
				tool_widgets_wrap.AddCssClass (TOOL_SETTING_GROUP_CLASS);
				tool_widgets_wrap.AddCssClass (TOOL_SETTINGS_AREA_CLASS);
			}

			return tool_widgets_wrap;
		}
	}
}
