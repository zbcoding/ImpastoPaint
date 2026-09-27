using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Layers > Import from File offers every format Open can read, so it has to decode them the same
/// way. It once went straight to gdk-pixbuf, which cannot read the formats Impasto parses itself
/// (OpenRaster, PDN, AVIF where no pixbuf loader is installed), and it added the layer before
/// decoding, so a failed import left a blank layer behind with no history to undo it.
/// </summary>
[TestFixture]
internal sealed class ImportLayerFromFileTest : DocumentHarness
{
	private string directory = null!;

	[SetUp]
	public void CreateWorkingDirectory ()
	{
		directory = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Directory.CreateDirectory (directory);
	}

	[TearDown]
	public void RemoveWorkingDirectory () => Directory.Delete (directory, recursive: true);

	[Test]
	public void AnOpenRasterFileImportsAsALayerOfItsPicture ()
	{
		FillRect (Layer (0).Surface, new RectangleI (4, 4, 8, 8), Red);
		Gio.File file = Export ("picture.ora");
		Fill (Layer (0).Surface, Transparent);

		PintaCore.Actions.Layers.ImportLayerFromFile (Document, file);

		Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (2));
		UserLayer imported = Document.Layers.CurrentUserLayer;
		Assert.That (imported.Surface.GetColorBgra (new PointI (5, 5)), Is.EqualTo (Red));
		Assert.That (imported.Surface.GetColorBgra (new PointI (20, 20)), Is.EqualTo (Transparent));

		// The import is one undoable step that removes the layer again.
		Document.History.Undo ();
		Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (1));
	}

	[Test]
	public void AFileThatFailsToDecodeLeavesTheDocumentUntouched ()
	{
		string path = Path.Combine (directory, "broken.ora");
		File.WriteAllText (path, "not a zip archive");
		int historyBefore = Document.History.Pointer;

		Assert.That (
			() => PintaCore.Actions.Layers.ImportLayerFromFile (Document, Gio.FileHelper.NewForPath (path)),
			Throws.Exception);

		Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (1));
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore));
	}

	private Gio.File Export (string name)
	{
		FormatDescriptor format =
			PintaCore.ImageFormats.GetFormatByFile (name)
			?? throw new AssertionException ($"No format for {name}.");
		Gio.File file = Gio.FileHelper.NewForPath (Path.Combine (directory, name));
		format.Exporter!.Export (Document, file, PintaCore.Chrome.MainWindow);
		return file;
	}
}
