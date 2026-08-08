using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class FileFormatTests
{
	[OneTimeSetUp]
	public void SetUp () => Utilities.EnsureNativeLibraries ();

	[TestCase ("sixcolorsinput.gif", "sixcolors_standard_lf.ppm")]
	[TestCase ("sixcolorsinput.gif", "sixcolors_chaotic.ppm")]
	public void Files_NotEqual (string file1, string file2)
	{
		string path1 = Utilities.GetAssetPath (file1);
		string path2 = Utilities.GetAssetPath (file2);
		Assert.That (Utilities.AreFilesEqual (path1, path2), Is.False);
	}

	[TestCaseSource (nameof (netpbm_pixmap_text_cases))]
	public void Export_NetpbmPixmap_TextBased (string inputFile, IEnumerable<string> acceptableOutputs)
	{
		string inputFilePath = Utilities.GetAssetPath (inputFile);
		using ImageSurface loaded = Utilities.LoadImage (inputFilePath);
		NetpbmPortablePixmap exporter = new ();
		using Gio.MemoryOutputStream memoryOutput = Gio.MemoryOutputStream.NewResizable ();
		using GioStream outputStream = new (memoryOutput);
		exporter.Export (loaded, outputStream);
		outputStream.Close ();
		memoryOutput.Close (null);
		var exportedBytes = memoryOutput.StealAsBytes ();
		bool matched = false;
		foreach (string fileName in acceptableOutputs) {
			var bytesStream = Gio.MemoryInputStream.NewFromBytes (exportedBytes);
			var bytesReader = Gio.DataInputStream.New (bytesStream);
			string filePath = Utilities.GetAssetPath (fileName);
			using var context = Utilities.OpenFile (filePath);
			if (!Utilities.AreFilesEqual (bytesReader, context.DataStream)) continue;
			matched = true;
			break;
		}
		Assert.That (matched, Is.True);
	}

	// TODO: This is just for reference. Find a way to get the image importers not to depend on PintaCore

	//[TestCase ("sixcolorsinput.gif", "sixcolors_standard_lf.ppm")]
	//[TestCase ("sixcolorsinput.gif", "sixcolors_chaotic.ppm")]
	//public void Import_NetpbmPixmap_TextBased (string referenceImageName, string ppmFileName)
	//{
	//	string ppmFilePath = Utilities.GetAssetPath (ppmFileName);
	//	string referenceImagePath = Utilities.GetAssetPath (referenceImageName);
	//	using ImageSurface loaded = Utilities.LoadImage (referenceImagePath);
	//	using Gio.File ppmFile = Gio.FileHelper.NewForPath (ppmFilePath);
	//	NetpbmPortablePixmap importer = new ();
	//	Document importedPpm = importer.Import (ppmFile);
	//	Utilities.CompareImages (importedPpm.Layers[0].Surface, loaded);
	//}

	static readonly IReadOnlyList<TestCaseData> netpbm_pixmap_text_cases = [
		new (
			"sixcolorsinput.gif",
			new[] { "sixcolors_standard_lf.ppm" }
		),
	];

	// A small ARGB32 image with a few distinct colors, so a lossless round-trip
	// through gdk-pixbuf can be checked for dimensions.
	static ImageSurface MakeTestImage ()
	{
		ImageSurface surface = new (Format.Argb32, 4, 3);
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int i = 0; i < data.Length; i++)
			data[i] = ColorBgra.FromBgra ((byte) (i * 20), (byte) (i * 10), (byte) (i * 5), 255);
		surface.MarkDirty ();
		return surface;
	}

	[Test]
	public void Export_Tga_HasVersion2Footer ()
	{
		// Regression: without the TGA 2.0 footer, strict readers (Qt/KImageFormats,
		// hence gwenview) fail to recognize the file.
		using ImageSurface surface = MakeTestImage ();
		using MemoryStream output = new ();
		new TgaExporter ().Export (surface, output);

		byte[] bytes = output.ToArray ();
		string tail = Encoding.ASCII.GetString (bytes, bytes.Length - 18, 18);
		Assert.That (tail, Is.EqualTo ("TRUEVISION-XFILE.\0"));
	}

	[Test]
	public void Export_Tga_WritesNullTerminatedImageId ()
	{
		// Regression: the ID field used to be written with BinaryWriter.Write(string),
		// which emits a length prefix and no terminator. Readers that treat the field
		// as a C string then overrun it and shift the whole image by a byte, turning
		// blue into red and opaque black into transparent.
		using ImageSurface surface = MakeTestImage ();
		using MemoryStream output = new ();
		new TgaExporter ().Export (surface, output);

		byte[] bytes = output.ToArray ();
		byte idLength = bytes[0];

		Assert.That (idLength, Is.EqualTo (17));
		Assert.That (Encoding.ASCII.GetString (bytes, 18, idLength), Is.EqualTo ("Created by Pinta\0"));
	}

	// A 4x3 image of distinct, fully opaque colors. Values are chosen to survive
	// chroma subsampling in the lossy formats reasonably well.
	static readonly ColorBgra[] opaque_colors = [
		ColorBgra.FromBgra (0, 0, 255, 255),      // red
		ColorBgra.FromBgra (0, 255, 0, 255),      // green
		ColorBgra.FromBgra (255, 0, 0, 255),      // blue
		ColorBgra.FromBgra (0, 255, 255, 255),    // yellow
		ColorBgra.FromBgra (255, 255, 0, 255),    // cyan
		ColorBgra.FromBgra (255, 0, 255, 255),    // magenta
		ColorBgra.FromBgra (0, 0, 0, 255),        // black
		ColorBgra.FromBgra (255, 255, 255, 255),  // white
		ColorBgra.FromBgra (128, 128, 128, 255),  // grey
		ColorBgra.FromBgra (0, 0, 128, 255),      // dark red
		ColorBgra.FromBgra (128, 0, 0, 255),      // dark blue
		ColorBgra.FromBgra (0, 128, 0, 255),      // dark green
	];

	// Each color fills a BLOCK x BLOCK square in a 4x3 grid of blocks. Solid blocks
	// rather than single pixels, so chroma subsampling in the lossy formats has
	// something to work with; colors are compared at block centers.
	const int BLOCK = 8;
	const int GRID_W = 4;
	const int GRID_H = 3;

	static ImageSurface MakeOpaqueImage ()
	{
		ImageSurface surface = new (Format.Argb32, GRID_W * BLOCK, GRID_H * BLOCK);
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int y = 0; y < surface.Height; y++)
			for (int x = 0; x < surface.Width; x++)
				data[y * surface.Width + x] = opaque_colors[(y / BLOCK) * GRID_W + (x / BLOCK)];
		surface.MarkDirty ();
		return surface;
	}

	// The exportable formats, as (name, tolerance per channel). Everything is written
	// through the same encoder path the app uses: our own exporters where we have one,
	// gdk-pixbuf otherwise.
	// (see the [TestCase] list on Export_AllFormats_RoundTripsColors)

	// Encode with the app's exporter for the format, or null if it isn't available here.
	static byte[]? TryEncode (string format, ImageSurface surface, bool includeAlpha)
	{
		switch (format) {
			case "tga": {
				using MemoryStream output = new ();
				new TgaExporter ().Export (surface, output);
				return output.ToArray ();
			}
			case "ppm": {
				if (includeAlpha) return null; // PPM has no alpha channel
				using MemoryStream output = new ();
				new NetpbmPortablePixmap ().Export (surface, output);
				return output.ToArray ();
			}
			case "avif":
				if (!AvifFormat.IsAvailable) return null;
				return new AvifFormat ().EncodeImage (surface, quality: 100);
			default: {
				using GdkPixbuf.Pixbuf pb = surface.ToPixbuf (includeAlpha);
				try {
					return pb.SaveToBuffer (format);
				} catch (Exception) {
					return null; // no writer for this format in this gdk-pixbuf build
				}
			}
		}
	}

	// Decode back through gdk-pixbuf and return straight-alpha pixels, row-major.
	// Goes via a temp file: the in-memory GLib.Bytes path crashes the test host.
	static ColorBgra[]? TryDecode (byte[] encoded, string extension, out int width, out int height)
	{
		width = height = 0;

		string path = System.IO.Path.Combine (System.IO.Path.GetTempPath (), $"pinta-format-test-{Guid.NewGuid ():N}.{extension}");
		File.WriteAllBytes (path, encoded);

		try {
			GdkPixbuf.Pixbuf? reloaded;
			try {
				reloaded = GdkPixbuf.Pixbuf.NewFromFile (path);
			} catch (Exception) {
				return null; // no loader for this format in this gdk-pixbuf build
			}

			if (reloaded is null) return null;

			using (reloaded) {

				width = reloaded.Width;
				height = reloaded.Height;

				int channels = reloaded.NChannels;
				int stride = reloaded.Rowstride;

				byte[] raw = new byte[stride * (height - 1) + width * channels];
				System.Runtime.InteropServices.Marshal.Copy (reloaded.Pixels, raw, 0, raw.Length);

				ColorBgra[] result = new ColorBgra[width * height];

				for (int y = 0; y < height; y++) {
					for (int x = 0; x < width; x++) {
						int i = y * stride + x * channels;
						result[y * width + x] = ColorBgra.FromBgra (
							b: raw[i + 2],
							g: raw[i + 1],
							r: raw[i + 0],
							a: channels == 4 ? raw[i + 3] : (byte) 255);
					}
				}

				return result;
			}
		} finally {
			File.Delete (path);
		}
	}

	static void AssertSimilar (ColorBgra expected, ColorBgra actual, int tolerance, string context)
	{
		Assert.Multiple (() => {
			Assert.That ((int) actual.R, Is.EqualTo ((int) expected.R).Within (tolerance), $"{context} red");
			Assert.That ((int) actual.G, Is.EqualTo ((int) expected.G).Within (tolerance), $"{context} green");
			Assert.That ((int) actual.B, Is.EqualTo ((int) expected.B).Within (tolerance), $"{context} blue");
			Assert.That ((int) actual.A, Is.EqualTo ((int) expected.A).Within (tolerance), $"{context} alpha");
		});
	}

	// The main guard: one sample image exported to every format the app can write,
	// reloaded, and checked for both dimensions and pixel colors. Catches channel-order
	// mistakes, row-order flips, and off-by-one header sizes.
	[TestCase ("png", 0)]
	[TestCase ("bmp", 0)]
	[TestCase ("tiff", 0)]
	[TestCase ("tga", 0)]
	[TestCase ("ppm", 0)]
	[TestCase ("webp", 0)]
	// Lossy: 8-bit YUV round-trips of saturated colors drift by a few counts.
	[TestCase ("jpeg", 24)]
	[TestCase ("avif", 24)]
	public void Export_AllFormats_RoundTripsColors (string format, int tolerance)
	{
		using ImageSurface surface = MakeOpaqueImage ();

		byte[]? encoded = TryEncode (format, surface, includeAlpha: format != "jpeg" && format != "ppm");
		Assume.That (encoded, Is.Not.Null, $"no encoder for {format} on this system");

		ColorBgra[]? actual = TryDecode (encoded!, format, out int width, out int height);
		Assume.That (actual, Is.Not.Null, $"no loader for {format} on this system");

		Assert.That (width, Is.EqualTo (surface.Width), $"{format} width");
		Assert.That (height, Is.EqualTo (surface.Height), $"{format} height");

		for (int i = 0; i < opaque_colors.Length; i++) {
			int cx = (i % GRID_W) * BLOCK + BLOCK / 2;
			int cy = (i / GRID_W) * BLOCK + BLOCK / 2;
			AssertSimilar (opaque_colors[i], actual![cy * width + cx], tolerance, $"{format} block {i}");
		}
	}

	// Same idea for the alpha channel, restricted to the formats that have one.
	// Cairo stores premultiplied alpha; exporters that forget to un-premultiply
	// produce colors darkened toward black (or vanishing entirely).
	[TestCase ("png")]
	[TestCase ("tga")]
	[TestCase ("webp")]
	public void Export_AlphaFormats_RoundTripsTransparency (string format)
	{
		ColorBgra[] straight = [
			ColorBgra.FromBgra (0, 0, 255, 255),  // opaque red
			ColorBgra.FromBgra (0, 0, 0, 255),    // opaque black
			ColorBgra.FromBgra (255, 0, 0, 128),  // half-transparent blue
			ColorBgra.FromBgra (0, 0, 0, 0),      // fully transparent
		];

		using ImageSurface surface = new (Format.Argb32, 4, 1);
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int i = 0; i < straight.Length; i++)
			data[i] = straight[i].ToPremultipliedAlpha ();
		surface.MarkDirty ();

		byte[]? encoded = TryEncode (format, surface, includeAlpha: true);
		Assume.That (encoded, Is.Not.Null, $"no encoder for {format} on this system");

		ColorBgra[]? actual = TryDecode (encoded!, format, out _, out _);
		Assume.That (actual, Is.Not.Null, $"no loader for {format} on this system");

		// Un-premultiplying an 8-bit value is lossy by a count or two; fully
		// transparent pixels carry no color at all, so only alpha is checked there.
		for (int i = 0; i < straight.Length; i++) {
			Assert.That ((int) actual![i].A, Is.EqualTo ((int) straight[i].A).Within (1), $"{format} pixel {i} alpha");
			if (straight[i].A > 0)
				AssertSimilar (straight[i], actual[i], 2, $"{format} pixel {i}");
		}
	}

	// FormatDescriptor and FileFilter need GTK's type system registered; skip
	// where GTK can't initialize (e.g. a headless CI without a display).
	static bool TryInitGtk ()
	{
		try {
			Gtk.Module.Initialize ();
			return true;
		} catch {
			return false;
		}
	}

	static FormatDescriptor MakeFormat (string prefix, string extension) =>
		new (prefix, [extension], [$"image/{extension}"], importer: null, exporter: new TgaExporter ());

	[Test]
	public void ResolveSelectedFormat_MatchesByIdentity ()
	{
		Assume.That (TryInitGtk (), "GTK is not available on this system");

		FormatDescriptor png = MakeFormat ("PNG", "png");
		var filetypes = new Dictionary<Gtk.FileFilter, FormatDescriptor> { [png.Filter] = png };

		Assert.That (ImageConverterManager.ResolveSelectedFormat (png.Filter, filetypes), Is.SameAs (png));
	}

	[Test]
	public void ResolveSelectedFormat_MatchesRenamedPortalFilterByPrefix ()
	{
		// Regression for the KeyNotFoundException: portal pickers return a fresh
		// FileFilter renamed to "<our name> (extra text)", missing from the dict.
		Assume.That (TryInitGtk (), "GTK is not available on this system");

		FormatDescriptor png = MakeFormat ("PNG", "png");
		var filetypes = new Dictionary<Gtk.FileFilter, FormatDescriptor> { [png.Filter] = png };

		Gtk.FileFilter portal = Gtk.FileFilter.New ();
		portal.Name = png.Filter.Name + " (image/png)";

		Assert.That (ImageConverterManager.ResolveSelectedFormat (portal, filetypes), Is.SameAs (png));
	}

	[Test]
	public void ResolveSelectedFormat_ReturnsNullWhenNoMatch ()
	{
		Assume.That (TryInitGtk (), "GTK is not available on this system");

		FormatDescriptor png = MakeFormat ("PNG", "png");
		var filetypes = new Dictionary<Gtk.FileFilter, FormatDescriptor> { [png.Filter] = png };

		Gtk.FileFilter unrelated = Gtk.FileFilter.New ();
		unrelated.Name = "Some Other Filter";

		Assert.That (ImageConverterManager.ResolveSelectedFormat (unrelated, filetypes), Is.Null);
		Assert.That (ImageConverterManager.ResolveSelectedFormat (null, filetypes), Is.Null);
	}

	[Test]
	public void Export_Avif_ProducesValidFile ()
	{
		// AVIF export needs the native libavif library; skip where it is not installed.
		Assume.That (AvifFormat.IsAvailable, Is.True, "libavif is not available on this system");

		using ImageSurface surface = Utilities.LoadImage (Utilities.GetAssetPath ("sixcolorsinput.gif"));

		AvifFormat exporter = new ();
		byte[] bytes = exporter.EncodeImage (surface, quality: 90);

		// ISO Base Media File Format: 4-byte size, then "ftyp" and the "avif" brand.
		Assert.That (bytes.Length, Is.GreaterThan (16));
		Assert.That (Encoding.ASCII.GetString (bytes, 4, 4), Is.EqualTo ("ftyp"));
		Assert.That (Encoding.ASCII.GetString (bytes, 8, 4), Is.EqualTo ("avif"));
	}
}
