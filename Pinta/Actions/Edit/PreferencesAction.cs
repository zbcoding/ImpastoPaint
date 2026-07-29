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
		Cairo.Color canvasSurroundColor = Cairo.Color.FromHex (
			settings.GetSetting (SettingNames.CANVAS_SURROUND_COLOR, SettingNames.DEFAULT_CANVAS_SURROUND_COLOR))
			?? Cairo.Color.FromHex (SettingNames.DEFAULT_CANVAS_SURROUND_COLOR)!.Value;

		using PreferencesDialog dialog = PreferencesDialog.New (
			chrome,
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_WIDTH, 800),
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, 600),
			canvasSurroundColor);

		try {
			if (await dialog.RunAsync () != Gtk.ResponseType.Ok)
				return;

			settings.PutSetting (SettingNames.DEFAULT_CANVAS_WIDTH, dialog.DefaultCanvasWidth);
			settings.PutSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, dialog.DefaultCanvasHeight);
			settings.PutSetting (SettingNames.CANVAS_SURROUND_COLOR, dialog.CanvasSurroundColor.ToHex (addAlpha: false));

			foreach (Document document in PintaCore.Workspace.OpenDocuments)
				((CanvasWindow) document.Workspace.CanvasWindow).CanvasSurroundColor = dialog.CanvasSurroundColor;
		} finally {
			dialog.Destroy ();
		}
	}
}
