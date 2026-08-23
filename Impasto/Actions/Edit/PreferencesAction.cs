using System;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class PreferencesAction : IActionHandler
{
	private readonly EditActions edit;
	private readonly ChromeManager chrome;
	private readonly SettingsManager settings;

	internal PreferencesAction (EditActions edit, ChromeManager chrome, SettingsManager settings)
	{
		this.edit = edit;
		this.chrome = chrome;
		this.settings = settings;
	}

	void IActionHandler.Initialize ()
	{
		edit.Preferences.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		edit.Preferences.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		string storedCanvasSurroundColor = settings.GetSetting (SettingNames.CANVAS_SURROUND_COLOR, SettingNames.DEFAULT_CANVAS_SURROUND_COLOR);
		bool canvasSurroundColorIsDefault = Cairo.Color.FromHex (storedCanvasSurroundColor) is null;
		Cairo.Color defaultCanvasSurroundColor = canvasSurroundColorIsDefault && PintaCore.Workspace.HasOpenDocuments
			? ((CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow).DefaultCanvasSurroundColor
			: new Cairo.Color (0.2, 0.2, 0.2);
		Cairo.Color canvasSurroundColor = Cairo.Color.FromHex (storedCanvasSurroundColor) ?? defaultCanvasSurroundColor;
		bool extendedPaletteRows = settings.GetSetting (SettingNames.EXTENDED_PALETTE_ROWS, false);
		bool toolboxClassicLayout = settings.GetSetting (SettingNames.TOOLBOX_CLASSIC_LAYOUT, false);
		bool toolSettingsWrapRows = settings.GetSetting (SettingNames.TOOL_SETTINGS_WRAP_ROWS, true);
		bool toolSelectorDropDown = settings.GetSetting (SettingNames.TOOL_SELECTOR_DROPDOWN, false);

		using PreferencesDialog dialog = PreferencesDialog.New (
			chrome,
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_WIDTH, 800),
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, 600),
			canvasSurroundColor,
			canvasSurroundColorIsDefault,
			defaultCanvasSurroundColor,
			settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, false),
			extendedPaletteRows,
			PintaCore.Palette.MaxRecentlyUsedColor,
			PintaCore.Palette.CurrentPalette.Colors.Count,
			toolboxClassicLayout,
			toolSettingsWrapRows,
			toolSelectorDropDown,
			(PopoverHintMode) settings.GetSetting (TransientHintPopover.SettingKey, (int) PopoverHintMode.All),
			settings.GetSetting (SettingNames.STATUSBAR_SHOW_CURSOR_POSITION, true),
			settings.GetSetting (SettingNames.STATUSBAR_SHOW_IMAGE_SIZE, true),
			settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true),
			settings.GetSetting (SettingNames.SKIP_RASTERIZE_OBJECTS_DIALOG, false));

		try {
			if (await dialog.RunAsync () != Gtk.ResponseType.Ok)
				return;

			settings.PutSetting (SettingNames.DEFAULT_CANVAS_WIDTH, dialog.DefaultCanvasWidth);
			settings.PutSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, dialog.DefaultCanvasHeight);
			Cairo.Color? selectedCanvasSurroundColor = dialog.CanvasSurroundColor;
			settings.PutSetting (SettingNames.CANVAS_SURROUND_COLOR, selectedCanvasSurroundColor?.ToHex (addAlpha: false) ?? SettingNames.DEFAULT_CANVAS_SURROUND_COLOR);
			settings.PutSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, dialog.PasteExternalImagesToNewLayer);
			settings.PutSetting (TransientHintPopover.SettingKey, (int) dialog.PopoverHintMode);
			settings.PutSetting (SettingNames.STATUSBAR_SHOW_CURSOR_POSITION, dialog.StatusBarShowCursorPosition);
			settings.PutSetting (SettingNames.STATUSBAR_SHOW_IMAGE_SIZE, dialog.StatusBarShowImageSize);
			settings.PutSetting (SettingNames.TOOL_SETTINGS_WRAP_ROWS, dialog.ToolSettingsWrapRows);
			PintaCore.Tools.WrapToolBarRows = dialog.ToolSettingsWrapRows;
			settings.PutSetting (SettingNames.TOOL_SELECTOR_DROPDOWN, dialog.ToolSelectorDropDown);
			PintaCore.Tools.UseToolSelectorDropDown = dialog.ToolSelectorDropDown;
			settings.PutSetting (SettingNames.SKIP_RASTERIZE_OBJECTS_DIALOG, dialog.SkipRasterizeObjectsDialog);
			settings.PutSetting (SettingNames.TOOLBAR_SHOWN, dialog.ShowMainToolBar);
			chrome.MainToolBar?.SetVisible (dialog.ShowMainToolBar);
			PintaCore.Actions.SetStatusBarCursorPositionVisible (dialog.StatusBarShowCursorPosition);
			PintaCore.Actions.SetStatusBarImageSizeVisible (dialog.StatusBarShowImageSize);

			// The dropdown replaces the tool box, so turning it on hides the column of tool
			// buttons and turning it off brings them back. Only on the change: View > Tool Box
			// stays in charge afterwards, for showing both at once.
			if (dialog.ToolSelectorDropDown != toolSelectorDropDown)
				PintaCore.Actions.View.ToolBox.Value = !dialog.ToolSelectorDropDown;

			// The toolbox is only ever built once at startup, so a layout change needs a
			// restart to take effect - only mention that when it actually changed.
			if (dialog.ToolboxClassicLayout != toolboxClassicLayout) {
				settings.PutSetting (SettingNames.TOOLBOX_CLASSIC_LAYOUT, dialog.ToolboxClassicLayout);
				await chrome.ShowMessageDialog (
					chrome.MainWindow,
					Translations.GetString ("Restart Impasto"),
					Translations.GetString ("Please restart Impasto for the toolbox layout change to take effect."));
			}

			// Reloading the default palette is the visible effect of the extra-row
			// toggle; doing it only when the value changed keeps custom palettes intact.
			if (dialog.ExtendedPaletteRows != extendedPaletteRows) {
				settings.PutSetting (SettingNames.EXTENDED_PALETTE_ROWS, dialog.ExtendedPaletteRows);
				PintaCore.Palette.CurrentPalette.LoadDefault (dialog.ExtendedPaletteRows);
			}

			if (dialog.RecentColorsCount != PintaCore.Palette.RecentlyUsedColors.Count)
				PintaCore.Palette.SetRecentlyUsedColorCount (dialog.RecentColorsCount);

			if (dialog.PaletteSize != PintaCore.Palette.CurrentPalette.Colors.Count)
				PintaCore.Palette.CurrentPalette.Resize (dialog.PaletteSize);

			foreach (Document document in PintaCore.Workspace.OpenDocuments)
				((CanvasWindow) document.Workspace.CanvasWindow).CanvasSurroundColor = selectedCanvasSurroundColor;
		} finally {
			dialog.Destroy ();
		}
	}
}
