using System.Collections.Generic;
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
