using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// An accidental drop - a whole folder - must not be read as an image, and a large batch waits
/// for the user to confirm. Every other file is tried, as Open tries it: a name is no reliable
/// guide to whether some loader can read the file.
/// </summary>
[TestFixture]
internal sealed class DroppedFilesPlanTest : DocumentHarness
{
	private string directory = null!;

	[SetUp]
	public void CreateDirectory ()
	{
		directory = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Directory.CreateDirectory (directory);
	}

	[TearDown]
	public void DeleteDirectory ()
		=> Directory.Delete (directory, recursive: true);

	private Gio.File Make (string name, bool folder = false)
	{
		string path = Path.Combine (directory, name);
		if (folder)
			Directory.CreateDirectory (path);
		else
			File.WriteAllText (path, "x");

		return Gio.FileHelper.NewForPath (path);
	}

	private static DroppedFilesPlan Plan (params Gio.File[] dropped)
		=> DroppedFilesPlan.Create (dropped);

	[Test]
	public void AFolderIsSkippedNotOpened ()
	{
		DroppedFilesPlan plan = Plan (Make ("holiday photos", folder: true));

		Assert.That (plan.ToOpen, Is.Empty);
		Assert.That (plan.Folders, Is.EqualTo (new[] { "holiday photos" }));
	}

	[Test]
	public void AFolderNamedLikeAnImageIsStillAFolder ()
	{
		DroppedFilesPlan plan = Plan (Make ("scans.png", folder: true));

		Assert.That (plan.ToOpen, Is.Empty);
		Assert.That (plan.Folders, Has.Count.EqualTo (1));
	}

	[Test]
	public void ImagesAreOpenedInTheOrderDropped ()
	{
		Gio.File[] images = [Make ("b.png"), Make ("a.jpg"), Make ("c.ora")];

		DroppedFilesPlan plan = Plan (images);

		Assert.That (plan.ToOpen.Select (f => f.GetDisplayName ()), Is.EqualTo (new[] { "b.png", "a.jpg", "c.ora" }));
		Assert.That (plan.SkipsAnything, Is.False);
	}

	[Test]
	public void FilesWithAnExtensionNoImporterClaimsAreStillTried ()
	{
		string[] names = ["photo.jfif", "scan.pgm", "picture.dat", "image.php?id=3"];

		DroppedFilesPlan plan = Plan ([.. names.Select (name => Make (name))]);

		Assert.That (plan.ToOpen.Select (f => f.GetDisplayName ()), Is.EqualTo (names),
			"a loader may read these even though no importer is registered for the extension");
		Assert.That (plan.SkipsAnything, Is.False);
	}

	[Test]
	public void AFileWithNoExtensionIsStillTriedAsAnImage ()
	{
		DroppedFilesPlan plan = Plan (Make ("download"));

		Assert.That (plan.ToOpen, Has.Count.EqualTo (1), "a picture dragged from a browser often has no extension");
	}

	[Test]
	public void AMixedDropOpensTheFilesAndReportsTheFolders ()
	{
		DroppedFilesPlan plan = Plan (Make ("dir", folder: true), Make ("a.png"), Make ("b.txt"));

		Assert.That (plan.ToOpen.Select (f => f.GetDisplayName ()), Is.EqualTo (new[] { "a.png", "b.txt" }));
		Assert.That (plan.Folders, Is.EqualTo (new[] { "dir" }));
		Assert.That (plan.SkipsAnything, Is.True);
	}

	[Test]
	public void AnAmountOfFilesJustAtTheLimitOpensWithoutAsking ()
	{
		Gio.File[] files = [.. Enumerable.Range (0, DroppedFilesPlan.ConfirmAboveCount).Select (i => Make ($"{i}.png"))];

		Assert.That (Plan (files).NeedsConfirmation, Is.False);
	}

	[Test]
	public void OneFileOverTheLimitAsksFirst ()
	{
		Gio.File[] files = [.. Enumerable.Range (0, DroppedFilesPlan.ConfirmAboveCount + 1).Select (i => Make ($"{i}.png"))];

		Assert.That (Plan (files).NeedsConfirmation, Is.True);
	}

	[Test]
	public void OpeningAFolderSaysSoInsteadOfTryingEveryLoaderOnIt ()
	{
		List<string> errors = [];
		PintaCore.Chrome.InitializeErrorDialogHandler ((_, message, _, _) => {
			errors.Add (message);
			return Task.FromResult (ErrorDialogResponse.OK);
		});
		int documentsBefore = PintaCore.Workspace.OpenDocuments.Count;

		bool opened = PintaCore.Workspace.OpenFile (Make ("photos", folder: true));

		Assert.That (opened, Is.False);
		Assert.That (PintaCore.Workspace.OpenDocuments, Has.Count.EqualTo (documentsBefore));
		Assert.That (errors, Is.EqualTo (new[] { "Folders cannot be opened." }), "not the unsupported-file-format error");
	}
}
