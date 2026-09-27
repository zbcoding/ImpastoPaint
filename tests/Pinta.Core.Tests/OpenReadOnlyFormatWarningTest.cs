using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Opening a file in a format Impasto reads but cannot write warns at once, so nobody edits a
/// Photoshop file expecting Save to write it back. The check asks the format registry, so it
/// follows whichever formats have an exporter.
/// </summary>
[TestFixture]
internal sealed class OpenReadOnlyFormatWarningTest : DocumentHarness
{
	private readonly List<string> messages = [];
	private readonly List<Document> opened = [];

	[SetUp]
	public void CaptureMessages ()
		=> PintaCore.Chrome.InitializeMessageDialog ((_, message, _) => {
			messages.Add (message);
			return Task.CompletedTask;
		});

	[TearDown]
	public void CloseOpenedDocuments ()
	{
		foreach (Document document in opened)
			PintaCore.Workspace.CloseDocument (document);
		opened.Clear ();
		messages.Clear ();
	}

	[Test]
	public void OpeningAFormatWithoutAnExporterWarnsThatItCannotBeSaved ()
	{
		Open (Utilities.GetAssetPath ("psd-opacity-fill.psd"));

		Assert.That (messages, Has.Count.EqualTo (1));
		Assert.That (messages[0], Does.Contain (".psd"));
	}

	[Test]
	public void OpeningAFormatImpastoCanSaveDoesNotWarn ()
	{
		string path = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName () + ".ora");
		try {
			PintaCore.ImageFormats.GetFormatByFile (path)!.Exporter!.Export (
				Document, Gio.FileHelper.NewForPath (path), PintaCore.Chrome.MainWindow);

			Open (path);
		} finally {
			File.Delete (path);
		}

		Assert.That (messages, Is.Empty);
	}

	private void Open (string path)
	{
		Assert.That (PintaCore.Workspace.OpenFile (Gio.FileHelper.NewForPath (path)), Is.True);
		opened.Add (PintaCore.Workspace.ActiveDocument);
	}
}
