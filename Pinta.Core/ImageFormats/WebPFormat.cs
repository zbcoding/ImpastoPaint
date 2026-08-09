using System;

using GdkPixbuf;

namespace Pinta.Core;

public sealed class WebPFormat : GdkPixbufFormat
{
	private const int DefaultQuality = 80;

	public WebPFormat ()
		: base ("webp")
	{
	}

	// ponytail: webp-pixbuf-loader only understands "quality", "icc-profile" and
	// "preset" - it has no lossless switch, and unknown keys are silently dropped.
	// Lossless export needs a direct libwebp binding, not gdk-pixbuf.
	internal static (string[] Keys, string[] Values) SaveOptions (int quality) =>
		(["quality"], [quality.ToString ()]);

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
