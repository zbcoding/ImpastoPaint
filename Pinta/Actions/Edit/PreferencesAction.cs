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

		using PreferencesDialog dialog = PreferencesDialog.New (
			chrome,
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_WIDTH, 800),
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, 600),
			canvasSurroundColor,
			canvasSurroundColorIsDefault,
			defaultCanvasSurroundColor,
			settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, false));

		try {
			if (await dialog.RunAsync () != Gtk.ResponseType.Ok)
				return;

			settings.PutSetting (SettingNames.DEFAULT_CANVAS_WIDTH, dialog.DefaultCanvasWidth);
			settings.PutSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, dialog.DefaultCanvasHeight);
			Cairo.Color? selectedCanvasSurroundColor = dialog.CanvasSurroundColor;
			settings.PutSetting (SettingNames.CANVAS_SURROUND_COLOR, selectedCanvasSurroundColor?.ToHex (addAlpha: false) ?? SettingNames.DEFAULT_CANVAS_SURROUND_COLOR);
			settings.PutSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, dialog.PasteExternalImagesToNewLayer);

			foreach (Document document in PintaCore.Workspace.OpenDocuments)
				((CanvasWindow) document.Workspace.CanvasWindow).CanvasSurroundColor = selectedCanvasSurroundColor;
		} finally {
			dialog.Destroy ();
		}
	}
}
