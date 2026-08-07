using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta;

// Impasto: application-wide user preferences. First setting is the default size of
// the blank canvas created on startup. Grows as more preferences are added.
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class PreferencesDialog
{
	private Gtk.SpinButton canvas_width_spinner;
	private Gtk.SpinButton canvas_height_spinner;
	private PintaColorButton canvas_surround_color_button;
	private Gtk.CheckButton paste_external_images_to_new_layer_check_button;
	private Gtk.CheckButton extended_palette_rows_check_button;
	private Gtk.SpinButton palette_size_spinner;
	private Gtk.CheckButton toolbox_classic_layout_check_button;
	private Gtk.CheckButton tool_settings_wrap_rows_check_button;
	private Gtk.CheckButton skip_rasterize_objects_dialog_check_button;
	private Gtk.CheckButton show_main_toolbar_check_button;
	private Gtk.CheckButton statusbar_show_cursor_position_check_button;
	private Gtk.CheckButton statusbar_show_image_size_check_button;
	private Gtk.ToggleButton popover_hint_mode_all_button;
	private Gtk.ToggleButton popover_hint_mode_essential_button;
	private Gtk.ToggleButton popover_hint_mode_none_button;
	private bool canvas_surround_color_is_default;
	private Cairo.Color default_canvas_surround_color;

	private const int SPACING = 6;
	internal const int MAX_CANVAS_DIMENSION = 10000;
	private const long MAX_CANVAS_PIXELS = 100_000_000;

	internal static bool IsValidCanvasSize (int width, int height)
		=> width > 0 && height > 0
			&& width <= MAX_CANVAS_DIMENSION
			&& height <= MAX_CANVAS_DIMENSION
			&& (long) width * height <= MAX_CANVAS_PIXELS;

	public int DefaultCanvasWidth => canvas_width_spinner.GetValueAsInt ();
	public int DefaultCanvasHeight => canvas_height_spinner.GetValueAsInt ();
	public Cairo.Color? CanvasSurroundColor => canvas_surround_color_is_default ? null : canvas_surround_color_button.DisplayColor;
	public bool PasteExternalImagesToNewLayer => paste_external_images_to_new_layer_check_button.Active;
	public bool ExtendedPaletteRows => extended_palette_rows_check_button.Active;
	public int PaletteSize => palette_size_spinner.GetValueAsInt ();
	public bool ToolboxClassicLayout => toolbox_classic_layout_check_button.Active;
	public bool ToolSettingsWrapRows => tool_settings_wrap_rows_check_button.Active;
	public bool SkipRasterizeObjectsDialog => skip_rasterize_objects_dialog_check_button.Active;
	public bool ShowMainToolBar => show_main_toolbar_check_button.Active;
	public bool StatusBarShowCursorPosition => statusbar_show_cursor_position_check_button.Active;
	public bool StatusBarShowImageSize => statusbar_show_image_size_check_button.Active;
	public PopoverHintMode PopoverHintMode
		=> popover_hint_mode_all_button.Active ? PopoverHintMode.All
			: popover_hint_mode_essential_button.Active ? PopoverHintMode.Essential
			: PopoverHintMode.None;

	internal static PreferencesDialog New (ChromeManager chrome, int defaultCanvasWidth, int defaultCanvasHeight, Cairo.Color canvasSurroundColor, bool canvasSurroundColorIsDefault, Cairo.Color defaultCanvasSurroundColor, bool pasteExternalImagesToNewLayer, bool extendedPaletteRows, int paletteSize, bool toolboxClassicLayout, bool toolSettingsWrapRows, PopoverHintMode popoverHintMode, bool statusBarShowCursorPosition, bool statusBarShowImageSize, bool showMainToolBar, bool skipRasterizeObjectsDialog)
	{
		PreferencesDialog dialog = NewWithProperties ([]);
		dialog.canvas_width_spinner.Value = defaultCanvasWidth;
		dialog.canvas_height_spinner.Value = defaultCanvasHeight;
		dialog.canvas_surround_color_button.DisplayColor = canvasSurroundColor;
		dialog.canvas_surround_color_is_default = canvasSurroundColorIsDefault;
		dialog.default_canvas_surround_color = defaultCanvasSurroundColor;
		dialog.paste_external_images_to_new_layer_check_button.Active = pasteExternalImagesToNewLayer;
		dialog.extended_palette_rows_check_button.Active = extendedPaletteRows;
		int rows = PaletteHelper.GetPaletteRowCount ();
		dialog.palette_size_spinner.Value = PaletteHelper.RoundDownToRowMultiple (paletteSize, rows);
		dialog.toolbox_classic_layout_check_button.Active = toolboxClassicLayout;
		dialog.tool_settings_wrap_rows_check_button.Active = toolSettingsWrapRows;
		dialog.skip_rasterize_objects_dialog_check_button.Active = skipRasterizeObjectsDialog;
		dialog.show_main_toolbar_check_button.Active = showMainToolBar;
		dialog.SetPopoverHintMode (popoverHintMode);
		dialog.statusbar_show_cursor_position_check_button.Active = statusBarShowCursorPosition;
		dialog.statusbar_show_image_size_check_button.Active = statusBarShowImageSize;
		dialog.TransientFor = chrome.MainWindow;
		return dialog;
	}

	[MemberNotNull (nameof (canvas_width_spinner))]
	[MemberNotNull (nameof (canvas_height_spinner))]
	[MemberNotNull (nameof (canvas_surround_color_button))]
	[MemberNotNull (nameof (paste_external_images_to_new_layer_check_button))]
	[MemberNotNull (nameof (extended_palette_rows_check_button))]
	[MemberNotNull (nameof (palette_size_spinner))]
	[MemberNotNull (nameof (toolbox_classic_layout_check_button))]
	[MemberNotNull (nameof (tool_settings_wrap_rows_check_button))]
	[MemberNotNull (nameof (skip_rasterize_objects_dialog_check_button))]
	[MemberNotNull (nameof (show_main_toolbar_check_button))]
	[MemberNotNull (nameof (statusbar_show_cursor_position_check_button))]
	[MemberNotNull (nameof (statusbar_show_image_size_check_button))]
	[MemberNotNull (nameof (popover_hint_mode_all_button))]
	[MemberNotNull (nameof (popover_hint_mode_essential_button))]
	[MemberNotNull (nameof (popover_hint_mode_none_button))]
	partial void Initialize ()
	{
		Gtk.SpinButton widthSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		widthSpinner.SetActivatesDefaultImmediate (true);

		Gtk.SpinButton heightSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		heightSpinner.SetActivatesDefaultImmediate (true);

		PintaColorButton canvasSurroundColorButton = PintaColorButton.New ();
		canvasSurroundColorButton.TooltipText = Translations.GetString ("Choose color...");
		canvasSurroundColorButton.OnClicked += ChooseCanvasSurroundColor;

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = SPACING;
		grid.ColumnSpacing = SPACING;
		grid.ColumnHomogeneous = false;

		grid.Attach (CreateLabel (Translations.GetString ("Default canvas size (on application open):"), Gtk.Align.Start), 0, 0, 4, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Width:"), Gtk.Align.End), 0, 1, 1, 1);
		grid.Attach (widthSpinner, 1, 1, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 1, 1, 1);
		grid.Attach (CreateResetButton (ResetCanvasWidth), 3, 1, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Height:"), Gtk.Align.End), 0, 2, 1, 1);
		grid.Attach (heightSpinner, 1, 2, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 2, 1, 1);
		grid.Attach (CreateResetButton (ResetCanvasHeight), 3, 2, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Canvas surround color:"), Gtk.Align.End), 0, 3, 1, 1);
		grid.Attach (canvasSurroundColorButton, 1, 3, 2, 1);
		grid.Attach (CreateResetButton (ResetCanvasSurroundColor), 3, 3, 1, 1);

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);

		Gtk.Box canvasPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		canvasPage.SetAllMargins (12);
		canvasPage.Append (grid);

		Gtk.CheckButton pasteExternalImagesCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Paste external images onto a new layer by default"));
		Gtk.Box clipboardPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		clipboardPage.SetAllMargins (12);
		Gtk.Box clipboardRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		pasteExternalImagesCheckButton.Hexpand = true;
		clipboardRow.Append (pasteExternalImagesCheckButton);
		clipboardRow.Append (CreateResetButton (ResetClipboard));
		clipboardPage.Append (clipboardRow);

		Gtk.ToggleButton popoverHintModeAllButton = CreateHintModeButton (
			"All",
			"Show all UI popover hints.");
		Gtk.ToggleButton popoverHintModeEssentialButton = CreateHintModeButton (
			"Essential",
			"Show only essential tool hints.");
		Gtk.ToggleButton popoverHintModeNoneButton = CreateHintModeButton (
			"None",
			"Hide all UI popover hints.");
		popoverHintModeEssentialButton.SetGroup (popoverHintModeAllButton);
		popoverHintModeNoneButton.SetGroup (popoverHintModeAllButton);

		Gtk.Box popoverHintPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		popoverHintPage.SetAllMargins (12);
		popoverHintPage.Append (Gtk.Label.New (Translations.GetString ("Popover hints:")));
		Gtk.Box popoverHintRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		popoverHintModeAllButton.Hexpand = true;
		popoverHintModeEssentialButton.Hexpand = true;
		popoverHintModeNoneButton.Hexpand = true;
		popoverHintRow.Append (popoverHintModeAllButton);
		popoverHintRow.Append (popoverHintModeEssentialButton);
		popoverHintRow.Append (popoverHintModeNoneButton);
		popoverHintRow.Append (CreateResetButton (ResetPopoverHintMode));
		popoverHintPage.Append (popoverHintRow);

		Gtk.CheckButton extendedPaletteRowsCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Show a third row of darker colors in the palette"));
		extendedPaletteRowsCheckButton.TooltipText = Translations.GetString ("Adds a darker shade of each color beneath the standard palette. The status bar grows taller to fit the extra row.");
		Gtk.Box extendedPaletteRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		extendedPaletteRowsCheckButton.Hexpand = true;
		extendedPaletteRow.Append (extendedPaletteRowsCheckButton);
		extendedPaletteRow.Append (CreateResetButton (ResetExtendedPaletteRows));
		popoverHintPage.Append (extendedPaletteRow);

		// Step by the row count so every column stays full - 2 normally, 3 once the
		// extra darker row above is on.
		int paletteRows = PaletteHelper.GetPaletteRowCount ();
		Gtk.SpinButton paletteSizeSpinner = Gtk.SpinButton.NewWithRange (paletteRows, 96, paletteRows);
		paletteSizeSpinner.SetActivatesDefaultImmediate (true);
		Gtk.Box paletteSizeRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		Gtk.Label paletteSizeLabel = Gtk.Label.New (Translations.GetString ("Palette size:"));
		paletteSizeLabel.Hexpand = true;
		paletteSizeLabel.Halign = Gtk.Align.Start;
		paletteSizeRow.Append (paletteSizeLabel);
		paletteSizeRow.Append (paletteSizeSpinner);
		paletteSizeRow.Append (CreateResetButton (ResetPaletteSize));
		popoverHintPage.Append (paletteSizeRow);

		Gtk.CheckButton toolboxClassicLayoutCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Use thin column toolbox layout"));
		toolboxClassicLayoutCheckButton.TooltipText = Translations.GetString ("Tools fill one column instead of the sectioned 2-column grid, overflowing into another column once it runs out of vertical space. Requires a restart.");
		Gtk.Box toolboxClassicLayoutRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		toolboxClassicLayoutCheckButton.Hexpand = true;
		toolboxClassicLayoutRow.Append (toolboxClassicLayoutCheckButton);
		toolboxClassicLayoutRow.Append (CreateResetButton (ResetToolboxClassicLayout));
		popoverHintPage.Append (toolboxClassicLayoutRow);

		Gtk.CheckButton toolSettingsWrapRowsCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Wrap tool settings onto extra rows"));
		toolSettingsWrapRowsCheckButton.TooltipText = Translations.GetString ("When the window is too narrow for all of the current tool's settings, they continue on another row instead of scrolling horizontally.");
		Gtk.Box toolSettingsWrapRowsRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		toolSettingsWrapRowsCheckButton.Hexpand = true;
		toolSettingsWrapRowsRow.Append (toolSettingsWrapRowsCheckButton);
		toolSettingsWrapRowsRow.Append (CreateResetButton (ResetToolSettingsWrapRows));
		popoverHintPage.Append (toolSettingsWrapRowsRow);

		Gtk.CheckButton skipRasterizeObjectsDialogCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Skip the \"Rasterize Objects?\" confirmation"));
		skipRasterizeObjectsDialogCheckButton.TooltipText = Translations.GetString ("Editable shapes and text stay editable on their own sub-layers. Operations that need flat pixels — cut, erase, crop, resize, rotate, flip, flatten — first bake them into the layer, which cannot be undone without also undoing the operation. Normally a dialog warns you before this happens; turn this on to bake silently and skip the warning.");
		Gtk.Box skipRasterizeObjectsDialogRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		skipRasterizeObjectsDialogCheckButton.Hexpand = true;
		skipRasterizeObjectsDialogRow.Append (skipRasterizeObjectsDialogCheckButton);
		skipRasterizeObjectsDialogRow.Append (CreateResetButton (ResetSkipRasterizeObjectsDialog));
		popoverHintPage.Append (skipRasterizeObjectsDialogRow);

		Gtk.CheckButton showMainToolBarCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Show quick access toolbar"));
		showMainToolBarCheckButton.TooltipText = Translations.GetString ("The row of icon buttons below the menu bar (New, Open, Save, Undo, Redo, Cut, Copy, Paste, and so on).");
		Gtk.Box showMainToolBarRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		showMainToolBarCheckButton.Hexpand = true;
		showMainToolBarRow.Append (showMainToolBarCheckButton);
		showMainToolBarRow.Append (CreateResetButton (ResetShowMainToolBar));
		popoverHintPage.Append (showMainToolBarRow);

		Gtk.CheckButton statusbarShowCursorPositionCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Show cursor position in the status bar"));
		Gtk.Box statusbarCursorPositionRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		statusbarShowCursorPositionCheckButton.Hexpand = true;
		statusbarCursorPositionRow.Append (statusbarShowCursorPositionCheckButton);
		statusbarCursorPositionRow.Append (CreateResetButton (ResetStatusBarShowCursorPosition));
		popoverHintPage.Append (statusbarCursorPositionRow);

		Gtk.CheckButton statusbarShowImageSizeCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Show image size and aspect ratio in the status bar"));
		Gtk.Box statusbarImageSizeRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		statusbarShowImageSizeCheckButton.Hexpand = true;
		statusbarImageSizeRow.Append (statusbarShowImageSizeCheckButton);
		statusbarImageSizeRow.Append (CreateResetButton (ResetStatusBarShowImageSize));
		popoverHintPage.Append (statusbarImageSizeRow);

		Gtk.Button exportSettingsButton = Gtk.Button.NewWithLabel (Translations.GetString ("Export Settings..."));
		exportSettingsButton.OnClicked += ExportSettings;
		Gtk.Button importSettingsButton = Gtk.Button.NewWithLabel (Translations.GetString ("Import Settings..."));
		importSettingsButton.OnClicked += ImportSettings;

		Gtk.Box backupPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		backupPage.SetAllMargins (12);
		backupPage.Append (Gtk.Label.New (Translations.GetString ("Save your settings and preferences to a file, or load them from a previously exported file.")));
		Gtk.Box backupRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		backupRow.Append (exportSettingsButton);
		backupRow.Append (importSettingsButton);
		backupPage.Append (backupRow);

		Gtk.Notebook notebook = Gtk.Notebook.New ();
		notebook.AppendPage (canvasPage, Gtk.Label.New (Translations.GetString ("Canvas")));
		notebook.AppendPage (clipboardPage, Gtk.Label.New (Translations.GetString ("Clipboard")));
		notebook.AppendPage (popoverHintPage, Gtk.Label.New (Translations.GetString ("UI")));
		notebook.AppendPage (backupPage, Gtk.Label.New (Translations.GetString ("Backup")));
		contentArea.Append (notebook);

		Title = Translations.GetString ("Settings");
		Modal = true;
		IconName = Resources.StandardIcons.KeyboardShortcuts;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		canvas_width_spinner = widthSpinner;
		canvas_height_spinner = heightSpinner;
		canvas_surround_color_button = canvasSurroundColorButton;
		paste_external_images_to_new_layer_check_button = pasteExternalImagesCheckButton;
		extended_palette_rows_check_button = extendedPaletteRowsCheckButton;
		palette_size_spinner = paletteSizeSpinner;
		toolbox_classic_layout_check_button = toolboxClassicLayoutCheckButton;
		tool_settings_wrap_rows_check_button = toolSettingsWrapRowsCheckButton;
		skip_rasterize_objects_dialog_check_button = skipRasterizeObjectsDialogCheckButton;
		show_main_toolbar_check_button = showMainToolBarCheckButton;
		statusbar_show_cursor_position_check_button = statusbarShowCursorPositionCheckButton;
		statusbar_show_image_size_check_button = statusbarShowImageSizeCheckButton;
		popover_hint_mode_all_button = popoverHintModeAllButton;
		popover_hint_mode_essential_button = popoverHintModeEssentialButton;
		popover_hint_mode_none_button = popoverHintModeNoneButton;
	}

	private void ResetCanvasWidth (Gtk.Button sender, EventArgs e)
		=> canvas_width_spinner.Value = 800;

	private void ResetCanvasHeight (Gtk.Button sender, EventArgs e)
		=> canvas_height_spinner.Value = 600;

	private void ResetCanvasSurroundColor (Gtk.Button sender, EventArgs e)
	{
		canvas_surround_color_button.DisplayColor = default_canvas_surround_color;
		canvas_surround_color_is_default = true;
	}

	private void ResetClipboard (Gtk.Button sender, EventArgs e)
		=> paste_external_images_to_new_layer_check_button.Active = false;

	private void ResetExtendedPaletteRows (Gtk.Button sender, EventArgs e)
		=> extended_palette_rows_check_button.Active = false;

	private void ResetPaletteSize (Gtk.Button sender, EventArgs e)
		=> palette_size_spinner.Value = PaletteHelper.EnumerateDefaultColors (extended_palette_rows_check_button.Active).Count ();

	private void ResetToolboxClassicLayout (Gtk.Button sender, EventArgs e)
		=> toolbox_classic_layout_check_button.Active = false;

	private void ResetToolSettingsWrapRows (Gtk.Button sender, EventArgs e)
		=> tool_settings_wrap_rows_check_button.Active = true;

	private void ResetSkipRasterizeObjectsDialog (Gtk.Button sender, EventArgs e)
		=> skip_rasterize_objects_dialog_check_button.Active = false;

	private void ResetShowMainToolBar (Gtk.Button sender, EventArgs e)
		=> show_main_toolbar_check_button.Active = true;

	private void ResetStatusBarShowCursorPosition (Gtk.Button sender, EventArgs e)
		=> statusbar_show_cursor_position_check_button.Active = true;

	private void ResetStatusBarShowImageSize (Gtk.Button sender, EventArgs e)
		=> statusbar_show_image_size_check_button.Active = true;

	private void ResetPopoverHintMode (Gtk.Button sender, EventArgs e)
		=> SetPopoverHintMode (PopoverHintMode.All);

	private void SetPopoverHintMode (PopoverHintMode mode)
	{
		popover_hint_mode_all_button.Active = mode == PopoverHintMode.All;
		popover_hint_mode_essential_button.Active = mode == PopoverHintMode.Essential;
		popover_hint_mode_none_button.Active = mode == PopoverHintMode.None;
	}

	private async void ChooseCanvasSurroundColor (Gtk.Button sender, EventArgs e)
	{
		using ColorPickerDialog dialog = ColorPickerDialog.New (
			this,
			PintaCore.Palette,
			new SingleColor (canvas_surround_color_button.DisplayColor),
			primarySelected: true,
			livePalette: false,
			Translations.GetString ("Choose Color"));

		try {
			if (await dialog.RunAsync () == Gtk.ResponseType.Ok) {
				Cairo.Color color = ((SingleColor) dialog.Colors).Color;
				canvas_surround_color_button.DisplayColor = new Cairo.Color (color.R, color.G, color.B, 1);
				canvas_surround_color_is_default = false;
			}
		} finally {
			dialog.Destroy ();
		}
	}

	private async void ExportSettings (Gtk.Button sender, EventArgs e)
	{
		using Gtk.FileChooserNative fcd = Gtk.FileChooserNative.New (
			Translations.GetString ("Export Settings"),
			this,
			Gtk.FileChooserAction.Save,
			Translations.GetString ("Export"),
			Translations.GetString ("Cancel"));
		fcd.SetCurrentName ("impasto-settings.json");

		if (await fcd.RunAsync () != Gtk.ResponseType.Accept)
			return;

		string? path = fcd.GetFile ()?.GetPath ();
		if (path is null)
			return;

		try {
			SettingsExportFile export = new () {
				Settings = [
					.. PintaCore.Settings.AllSettings.Select (kv => new SettingsExportEntry {
						Name = kv.Key,
						Type = kv.Value.GetType ().ToString (),
						Value = kv.Value.ToString () ?? string.Empty,
					})
				],
				KeyboardShortcuts = PintaCore.Shortcuts.ExportOverrides (),
			};
			File.WriteAllText (path, JsonSerializer.Serialize (export, new JsonSerializerOptions { WriteIndented = true }));
		} catch (Exception ex) {
			await ShowMessage (Translations.GetString ("Failed to export settings: {0}", ex.Message));
		}
	}

	private async void ImportSettings (Gtk.Button sender, EventArgs e)
	{
		using Gtk.FileFilter jsonFilter = Gtk.FileFilter.New ();
		jsonFilter.Name = Translations.GetString ("Settings files");
		jsonFilter.AddPattern ("*.json");

		using Gio.ListStore filters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		filters.Append (jsonFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Import Settings"));
		fileDialog.SetFilters (filters);
		fileDialog.Modal = true;

		Gio.File? choice = await fileDialog.OpenFileAsync (this);
		string? path = choice?.GetPath ();
		if (path is null)
			return;

		SettingsExportFile? import;
		try {
			import = JsonSerializer.Deserialize<SettingsExportFile> (File.ReadAllText (path));
		} catch (Exception ex) {
			await ShowMessage (Translations.GetString ("Failed to read settings file: {0}", ex.Message));
			return;
		}

		if (import?.Settings is null) {
			await ShowMessage (Translations.GetString ("This does not look like a valid settings file."));
			return;
		}

		int applied = 0;
		int skipped = 0;
		foreach (SettingsExportEntry entry in import.Settings) {
			if (string.IsNullOrEmpty (entry.Name)) {
				skipped++;
				continue;
			}

			if (PintaCore.Settings.ImportSetting (entry.Name, entry.Type, entry.Value ?? string.Empty))
				applied++;
			else
				skipped++;
		}

		RefreshFromSettings ();

		bool hasShortcuts = import.KeyboardShortcuts is { } ks
			&& (ks.Commands?.Count > 0 || ks.Tools?.Count > 0 || ks.ToolBindings?.Count > 0);
		bool importedShortcuts = hasShortcuts && await ConfirmImportKeyboardShortcuts ();
		if (importedShortcuts)
			PintaCore.Shortcuts.ImportOverrides (import.KeyboardShortcuts!);

		string shortcutsSummary = !hasShortcuts
			? Translations.GetString ("The file had no keyboard shortcuts to import.")
			: importedShortcuts
				? Translations.GetString ("Keyboard shortcuts were imported, replacing your current ones.")
				: Translations.GetString ("Keyboard shortcuts were left unchanged.");

		await ShowMessage (Translations.GetString (
			"Imported {0} setting(s); {1} entry(ies) were skipped (unrecognized or invalid). {2} Restart Impasto for all changes to fully take effect.",
			applied, skipped, shortcutsSummary));
	}

	// Importing shortcuts overwrites keyboard-shortcuts.json outright, so the user
	// gets a chance to keep their current hand-tuned bindings instead.
	private async Task<bool> ConfirmImportKeyboardShortcuts ()
	{
		const string keep_response = "keep";
		const string import_response = "import";

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (
			this,
			Translations.GetString ("Import Keyboard Shortcuts?"),
			Translations.GetString ("This file also contains keyboard shortcut overrides. Importing them will overwrite your current keyboard-shortcuts.json."));

		dialog.AddResponse (keep_response, Translations.GetString ("Keep Mine"));
		dialog.AddResponse (import_response, Translations.GetString ("Import"));
		dialog.SetResponseAppearance (import_response, Adw.ResponseAppearance.Suggested);
		dialog.CloseResponse = keep_response;
		dialog.DefaultResponse = keep_response;

		string response = await dialog.RunAsync ();
		return response == import_response;
	}

	private void RefreshFromSettings ()
	{
		SettingsManager settings = PintaCore.Settings;

		canvas_width_spinner.Value = settings.GetSetting (SettingNames.DEFAULT_CANVAS_WIDTH, DefaultCanvasWidth);
		canvas_height_spinner.Value = settings.GetSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, DefaultCanvasHeight);
		paste_external_images_to_new_layer_check_button.Active = settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, PasteExternalImagesToNewLayer);
		extended_palette_rows_check_button.Active = settings.GetSetting (SettingNames.EXTENDED_PALETTE_ROWS, ExtendedPaletteRows);
		toolbox_classic_layout_check_button.Active = settings.GetSetting (SettingNames.TOOLBOX_CLASSIC_LAYOUT, ToolboxClassicLayout);
		tool_settings_wrap_rows_check_button.Active = settings.GetSetting (SettingNames.TOOL_SETTINGS_WRAP_ROWS, ToolSettingsWrapRows);
		skip_rasterize_objects_dialog_check_button.Active = settings.GetSetting (SettingNames.SKIP_RASTERIZE_OBJECTS_DIALOG, SkipRasterizeObjectsDialog);
		show_main_toolbar_check_button.Active = settings.GetSetting (SettingNames.TOOLBAR_SHOWN, ShowMainToolBar);
		statusbar_show_cursor_position_check_button.Active = settings.GetSetting (SettingNames.STATUSBAR_SHOW_CURSOR_POSITION, StatusBarShowCursorPosition);
		statusbar_show_image_size_check_button.Active = settings.GetSetting (SettingNames.STATUSBAR_SHOW_IMAGE_SIZE, StatusBarShowImageSize);
		SetPopoverHintMode ((PopoverHintMode) settings.GetSetting (SettingNames.POPOVER_HINT_MODE, (int) PopoverHintMode));

		string storedColor = settings.GetSetting (SettingNames.CANVAS_SURROUND_COLOR, SettingNames.DEFAULT_CANVAS_SURROUND_COLOR);
		if (Cairo.Color.FromHex (storedColor) is Cairo.Color color) {
			canvas_surround_color_button.DisplayColor = color;
			canvas_surround_color_is_default = false;
		} else {
			canvas_surround_color_is_default = true;
		}
	}

	private async Task ShowMessage (string message)
	{
		using Adw.MessageDialog dialog = Adw.MessageDialog.New (this, Translations.GetString ("Settings"), message);
		dialog.AddResponse ("ok", Translations.GetString ("OK"));
		await dialog.RunAsync ();
	}

	private static Gtk.Label CreateLabel (string text, Gtk.Align horizontalAlign)
	{
		Gtk.Label result = Gtk.Label.New (text);
		result.Halign = horizontalAlign;
		return result;
	}

	private static Gtk.ToggleButton CreateHintModeButton (string label, string tooltip)
	{
		Gtk.ToggleButton result = Gtk.ToggleButton.NewWithLabel (Translations.GetString (label));
		result.TooltipText = Translations.GetString (tooltip);
		return result;
	}

	private static Gtk.Button CreateResetButton (GObject.SignalHandler<Gtk.Button> handler)
	{
		Gtk.Button result = Gtk.Button.New ();
		result.IconName = "edit-undo-symbolic";
		result.TooltipText = Translations.GetString ("Reset to default");
		result.OnClicked += handler;
		return result;
	}

	// On-disk shape for exported settings. A list rather than a dictionary so that
	// individual entries added, renamed, or removed by a future version of the app
	// don't break parsing of the entries around them.
	private sealed class SettingsExportFile
	{
		public List<SettingsExportEntry>? Settings { get; set; }
		public KeyboardShortcutManager.ShortcutFile? KeyboardShortcuts { get; set; }
	}

	private sealed class SettingsExportEntry
	{
		public string? Name { get; set; }
		public string? Type { get; set; }
		public string? Value { get; set; }
	}
}
