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

	// Round-trip every gdk-pixbuf-backed writable format plus our TGA exporter, and
	// confirm the bytes re-load with the expected dimensions. Guards against writing
	// files that no loader will accept.
	[TestCase ("png")]
	[TestCase ("bmp")]
	[TestCase ("tga")]
	public void Export_Format_ReloadsWithSameSize (string extension)
	{
		using ImageSurface surface = MakeTestImage ();
		using MemoryStream output = new ();

		if (extension == "tga") {
			new TgaExporter ().Export (surface, output);
		} else {
			using GdkPixbuf.Pixbuf pb = surface.ToPixbuf ();
			byte[] saved = pb.SaveToBuffer (extension);
			output.Write (saved, 0, saved.Length);
		}

		var bytes = GLib.Bytes.New (output.ToArray ());
		using var stream = Gio.MemoryInputStream.NewFromBytes (bytes);
		using GdkPixbuf.Pixbuf reloaded = GdkPixbuf.Pixbuf.NewFromStream (stream, cancellable: null)!;

		Assert.That (reloaded.Width, Is.EqualTo (surface.Width));
		Assert.That (reloaded.Height, Is.EqualTo (surface.Height));
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
