using System;

using GdkPixbuf;

namespace Pinta.Core;

public sealed class WebPFormat : GdkPixbufFormat
{
	private const int DefaultQuality = 80;

	/// <summary>
	/// Quality 100 selects WebP's lossless mode rather than its lossy encoder at
	/// maximum quality: lossy-at-100 is both larger and still not pixel-exact, so
	/// there is no reason to prefer it. This keeps the shared compression dialog
	/// as the only control, with no extra checkbox to explain.
	/// </summary>
	public const int LosslessQuality = 100;

	public WebPFormat ()
		: base ("webp")
	{
	}

	internal static (string[] Keys, string[] Values) SaveOptions (int quality) =>
		quality >= LosslessQuality
			? (["lossless"], ["1"])
			: (["quality"], [quality.ToString ()]);

	protected override void DoSave (Pixbuf pb, Gio.File file, string fileType, Gtk.Window parent)
	{
		int level = PintaCore.Settings.GetSetting<int> (SettingNames.WEBP_QUALITY, DefaultQuality);

		if (!PintaCore.Workspace.ActiveDocument.HasBeenSavedInSession) {
			level = PintaCore.Actions.File.RaiseModifyCompression (level, parent);

			if (level == -1)
				throw new OperationCanceledException ();
		}

		PintaCore.Settings.PutSetting (SettingNames.WEBP_QUALITY, level);

		(string[] keys, string[] values) = SaveOptions (level);

		using var stream = file.Replace ();
		try {
			pb.SaveToStreamv (stream, fileType, keys, values, null);
		} finally {
			stream.Close (null);
		}
	}
}
