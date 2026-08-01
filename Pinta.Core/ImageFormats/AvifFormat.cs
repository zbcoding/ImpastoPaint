// AvifFormat.cs
//
// AVIF image export via libavif, and import via gdk-pixbuf.
//
// ponytail: Impasto-only. gdk-pixbuf's AVIF loader is decode-only, so saving AVIF
// requires bundling the native libavif library (BSD-2-Clause) plus an AV1 encoder
// such as libaom (BSD-2-Clause) and libyuv (BSD-3-Clause). That native dependency
// means this feature cannot be PR'd upstream as-is; see THIRD-PARTY-NOTICES.md.

using System;
using System.Runtime.InteropServices;

using Cairo;
using GdkPixbuf;

namespace Pinta.Core;

public sealed class AvifFormat : IImageImporter, IImageExporter
{
	// AVIF export requires the native libavif library. When it is absent we still
	// support importing (through gdk-pixbuf), but "Save As AVIF" is unavailable.
	public static bool IsAvailable { get; } = ProbeLibrary ();

	private const int DefaultQuality = 80;

	private const int MaxQuality = 100;

	// libavif constants, pinned to the bundled libavif 1.4.x ABI (avif.h).
	private const int AvifPixelFormatYuv420 = 2;
	private const int AvifRgbFormatBgra = 4;
	private const uint AvifAddImageFlagSingle = 1u << 1;
	private const int AvifResultOk = 0;
	private const int AvifSpeedBalanced = 6;

	private const string AvifLibraryName = "avif";

	static AvifFormat ()
	{
		NativeImportResolver.RegisterLibrary (
			library: AvifLibraryName,
			windowsLibraryName: "libavif-16.dll",
			linuxLibraryName: "libavif.so.16",
			osxLibraryName: "libavif.16.dylib");
	}

	private static bool ProbeLibrary ()
	{
		string name = OperatingSystem.IsWindows ()
			? "libavif-16.dll"
			: OperatingSystem.IsMacOS ()
				? "libavif.16.dylib"
				: "libavif.so.16";

		return NativeLibrary.TryLoad (name, out _);
	}

	// ---------------------------------------------------------------------------
	// Importing (via gdk-pixbuf, mirrors GdkPixbufFormat)
	// ---------------------------------------------------------------------------

	public Document Import (Gio.File file)
	{
		using Pixbuf streamBuffer = ReadPixbuf (file);
		using Pixbuf effectiveBuffer = streamBuffer.ApplyEmbeddedOrientation () ?? streamBuffer;

		Size imageSize = new (effectiveBuffer.Width, effectiveBuffer.Height);

		Document newDocument = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			imageSize,
			file,
			"avif");

		Layer layer = newDocument.Layers.AddNewLayer (file.GetDisplayName ());

		using Context g = new (layer.Surface);

		g.DrawPixbuf (effectiveBuffer, PointD.Zero);

		return newDocument;
	}

	private static Pixbuf ReadPixbuf (Gio.File file)
	{
		// Handle any EXIF orientation flags
		using Gio.FileInputStream fs = file.Read (cancellable: null);
		try {
			return Pixbuf.NewFromStream (fs, cancellable: null)!; // NRT: only nullable when an error is thrown
		} finally {
			fs.Close (null);
		}
	}

	// ---------------------------------------------------------------------------
	// Exporting (via libavif)
	// ---------------------------------------------------------------------------

	public void Export (Document document, Gio.File file, Gtk.Window parent)
	{
		if (!IsAvailable)
			throw new InvalidOperationException ("AVIF export requires the libavif library.");

		int quality = PintaCore.Settings.GetSetting<int> (SettingNames.AVIF_QUALITY, DefaultQuality);

		// The first save in a session asks for the compression quality, like the WebP exporter.
		if (!PintaCore.Workspace.ActiveDocument.HasBeenSavedInSession) {
			quality = PintaCore.Actions.File.RaiseModifyCompression (quality, parent);

			if (quality == -1)
				throw new OperationCanceledException ();
		}

		PintaCore.Settings.PutSetting (SettingNames.AVIF_QUALITY, quality);

		using ImageSurface flattenedImage = document.GetFlattenedImage ();
		byte[] encoded = EncodeImage (flattenedImage, quality);

		using GioStream file_stream = new (file.Replace ());
		file_stream.Write (encoded, 0, encoded.Length);
	}

	internal byte[] EncodeImage (ImageSurface flattenedImage, int quality)
	{
		Span<byte> surfaceData = flattenedImage.GetData ();

		unsafe {
			fixed (byte* pixels = surfaceData) {
				return Encode (
					(IntPtr) pixels,
					flattenedImage.Width,
					flattenedImage.Height,
					flattenedImage.Stride,
					Math.Clamp (quality, 0, MaxQuality));
			}
		}
	}

	private static byte[] Encode (IntPtr pixels, int width, int height, int stride, int quality)
	{
		IntPtr image = AvifImageCreate ((uint) width, (uint) height, 8, AvifPixelFormatYuv420);
		if (image == IntPtr.Zero)
			throw new InvalidOperationException ("libavif failed to create an image.");

		try {
			AvifRGBImage rgb = default;
			AvifRGBImageSetDefaults (ref rgb, image);
			rgb.width = (uint) width;
			rgb.height = (uint) height;
			rgb.depth = 8;
			rgb.format = AvifRgbFormatBgra; // Cairo ARGB32 surfaces are stored in BGRA byte order
			rgb.alphaPremultiplied = 1;     // ...with premultiplied alpha
			rgb.pixels = pixels;
			rgb.rowBytes = (uint) stride;

			CheckResult (AvifImageRGBToYUV (image, ref rgb), "convert the image to YUV");

			IntPtr encoder = AvifEncoderCreate ();
			if (encoder == IntPtr.Zero)
				throw new InvalidOperationException ("libavif failed to create an encoder.");

			try {
				AvifEncoderSettings settings = Marshal.PtrToStructure<AvifEncoderSettings> (encoder);
				settings.maxThreads = Math.Max (1, Environment.ProcessorCount);
				settings.speed = AvifSpeedBalanced;
				settings.quality = quality;
				settings.qualityAlpha = quality;
				Marshal.StructureToPtr (settings, encoder, fDeleteOld: false);

				CheckResult (AvifEncoderAddImage (encoder, image, 0, AvifAddImageFlagSingle), "add the image to the encoder");

				AvifRWData output = default;
				try {
					CheckResult (AvifEncoderFinish (encoder, ref output), "finish encoding");

					byte[] bytes = new byte[output.size];
					Marshal.Copy (output.data, bytes, 0, (int) output.size);
					return bytes;
				} finally {
					AvifRWDataFree (ref output);
				}
			} finally {
				AvifEncoderDestroy (encoder);
			}
		} finally {
			AvifImageDestroy (image);
		}
	}

	private static void CheckResult (int result, string operation)
	{
		if (result != AvifResultOk)
			throw new InvalidOperationException ($"libavif failed to {operation} (error {result}).");
	}

	// ---------------------------------------------------------------------------
	// libavif interop (pinned to the bundled libavif 1.4.x ABI)
	// ---------------------------------------------------------------------------

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifImageCreate")]
	private static extern IntPtr AvifImageCreate (uint width, uint height, uint depth, int yuvFormat);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifImageDestroy")]
	private static extern void AvifImageDestroy (IntPtr image);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifRGBImageSetDefaults")]
	private static extern void AvifRGBImageSetDefaults (ref AvifRGBImage rgb, IntPtr image);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifImageRGBToYUV")]
	private static extern int AvifImageRGBToYUV (IntPtr image, ref AvifRGBImage rgb);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifEncoderCreate")]
	private static extern IntPtr AvifEncoderCreate ();

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifEncoderDestroy")]
	private static extern void AvifEncoderDestroy (IntPtr encoder);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifEncoderAddImage")]
	private static extern int AvifEncoderAddImage (IntPtr encoder, IntPtr image, ulong durationInTimescales, uint addImageFlags);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifEncoderFinish")]
	private static extern int AvifEncoderFinish (IntPtr encoder, ref AvifRWData output);

	[DllImport (AvifLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "avifRWDataFree")]
	private static extern void AvifRWDataFree (ref AvifRWData raw);

	[StructLayout (LayoutKind.Sequential)]
	private struct AvifRGBImage
	{
		public uint width;
		public uint height;
		public uint depth;
		public int format;
		public int chromaUpsampling;
		public int chromaDownsampling;
		public int avoidLibYUV;
		public int ignoreAlpha;
		public int alphaPremultiplied;
		public int isFloat;
		public int maxThreads;
		public IntPtr pixels;
		public uint rowBytes;
	}

	[StructLayout (LayoutKind.Sequential)]
	private struct AvifRWData
	{
		public IntPtr data;
		public nuint size;
	}

	// Prefix of the avifEncoder struct up to quality/qualityAlpha, used to set the
	// encode settings. Pinned to the bundled libavif 1.4.x ABI.
	[StructLayout (LayoutKind.Sequential)]
	private struct AvifEncoderSettings
	{
		public int codecChoice;
		public int maxThreads;
		public int speed;
		public int keyframeInterval;
		public ulong timescale;
		public int repetitionCount;
		public uint extraLayerCount;
		public int quality;
		public int qualityAlpha;
	}
}
