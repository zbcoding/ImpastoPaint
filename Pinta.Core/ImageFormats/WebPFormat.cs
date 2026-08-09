using System;
using System.Runtime.InteropServices;

using Cairo;
using GdkPixbuf;

namespace Pinta.Core;

public sealed class WebPFormat : GdkPixbufFormat
{
	private const int DefaultQuality = 80;

	/// <summary>
	/// Quality 100 selects WebP's lossless mode rather than its lossy encoder at
	/// maximum quality: lossy-at-100 is both larger and still not pixel-exact.
	/// This keeps the shared compression dialog as the only control.
	/// </summary>
	public const int LosslessQuality = 100;

	// ponytail: webp-pixbuf-loader only understands "quality", "icc-profile" and
	// "preset" - it has no lossless switch, and silently drops unknown keys. So
	// lossless goes straight to libwebp, which is already present on every
	// platform we ship (the pixbuf loader links against it).
	private const string WebPLibraryName = "webp";

	/// <summary>
	/// Whether libwebp could be loaded. When it can't, quality 100 falls back to
	/// the lossy encoder at maximum quality rather than failing the save.
	/// </summary>
	public static bool IsLosslessAvailable { get; } = ProbeLibrary ();

	static WebPFormat ()
	{
		NativeImportResolver.RegisterLibrary (
			library: WebPLibraryName,
			windowsLibraryName: "libwebp-7.dll",
			linuxLibraryName: "libwebp.so.7",
			osxLibraryName: "libwebp.7.dylib");
	}

	private static bool ProbeLibrary ()
	{
		string name = OperatingSystem.IsWindows ()
			? "libwebp-7.dll"
			: OperatingSystem.IsMacOS ()
				? "libwebp.7.dylib"
				: "libwebp.so.7";

		return NativeLibrary.TryLoad (name, out _);
	}

	public WebPFormat ()
		: base ("webp")
	{
	}

	public override void Export (Document document, Gio.File file, Gtk.Window parent)
	{
		int level = PintaCore.Settings.GetSetting<int> (SettingNames.WEBP_QUALITY, DefaultQuality);

		if (!PintaCore.Workspace.ActiveDocument.HasBeenSavedInSession) {
			level = PintaCore.Actions.File.RaiseModifyCompression (level, parent);

			if (level == -1)
				throw new OperationCanceledException ();
		}

		PintaCore.Settings.PutSetting (SettingNames.WEBP_QUALITY, level);

		using ImageSurface flattenedImage = document.GetFlattenedImage ();

		if (level >= LosslessQuality && IsLosslessAvailable) {
			byte[] encoded = EncodeLossless (flattenedImage);
			using GioStream file_stream = new (file.Replace ());
			file_stream.Write (encoded, 0, encoded.Length);
			return;
		}

		using Pixbuf pb = flattenedImage.ToPixbuf (includeAlpha: true);
		using Gio.OutputStream stream = file.Replace ();
		try {
			pb.SaveToStreamv (stream, "webp", ["quality"], [level.ToString ()], null);
		} finally {
			stream.Close (null);
		}
	}

	internal static byte[] EncodeLossless (ImageSurface surface)
	{
		int width = surface.Width;
		int height = surface.Height;

		// Cairo stores premultiplied ARGB; WebP wants straight alpha, and
		// "lossless" is only true of the round trip if we undo that exactly.
		ReadOnlySpan<ColorBgra> source = surface.GetReadOnlyPixelData ();
		byte[] bgra = new byte[width * height * 4];
		for (int i = 0; i < width * height; i++) {
			ColorBgra color = source[i].ToStraightAlpha ();
			bgra[i * 4 + 0] = color.B;
			bgra[i * 4 + 1] = color.G;
			bgra[i * 4 + 2] = color.R;
			bgra[i * 4 + 3] = color.A;
		}

		nuint size = WebPEncodeLosslessBGRA (bgra, width, height, width * 4, out IntPtr output);

		if (size == 0 || output == IntPtr.Zero)
			throw new InvalidOperationException ("libwebp failed to encode the image losslessly.");

		try {
			byte[] encoded = new byte[(int) size];
			Marshal.Copy (output, encoded, 0, encoded.Length);
			return encoded;
		} finally {
			WebPFree (output);
		}
	}

	[DllImport (WebPLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPEncodeLosslessBGRA")]
	private static extern nuint WebPEncodeLosslessBGRA (byte[] bgra, int width, int height, int stride, out IntPtr output);

	[DllImport (WebPLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPFree")]
	private static extern void WebPFree (IntPtr pointer);
}
