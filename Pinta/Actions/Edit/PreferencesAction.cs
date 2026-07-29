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
		using PreferencesDialog dialog = PreferencesDialog.New (
			chrome,
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_WIDTH, 800),
			settings.GetSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, 600));

		try {
			if (await dialog.RunAsync () != Gtk.ResponseType.Ok)
				return;

			settings.PutSetting (SettingNames.DEFAULT_CANVAS_WIDTH, dialog.DefaultCanvasWidth);
			settings.PutSetting (SettingNames.DEFAULT_CANVAS_HEIGHT, dialog.DefaultCanvasHeight);
		} finally {
			dialog.Destroy ();
		}
	}
}
