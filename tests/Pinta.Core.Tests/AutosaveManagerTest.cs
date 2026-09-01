using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Recovery only offers an autosave that it has checked, because the file it is most
/// likely to find is one the crash interrupted halfway through being written. Handing
/// such a file to the importer produces an exception where the user expects their work,
/// so every case below must be rejected before the offer is made.
/// </summary>
[TestFixture]
internal sealed class AutosaveManagerTest
{
	private string directory = null!;

	[SetUp]
	public void CreateWorkingDirectory ()
	{
		directory = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Directory.CreateDirectory (directory);
	}

	[TearDown]
	public void RemoveWorkingDirectory ()
		=> Directory.Delete (directory, recursive: true);

	[Test]
	public void CurrentSessionDirectoryHasLiveOwner ()
	{
		string session = AutosaveManager.CreateSessionDirectoryName ();

		Assert.That (
			session,
			Does.StartWith (Environment.ProcessId.ToString (CultureInfo.InvariantCulture) + "-"));
		Assert.That (AutosaveManager.IsSessionOwnerAlive (Path.Combine (directory, session)), Is.True);
	}

	[Test]
	public void ReusedProcessIdDoesNotOwnCrashedSessionDirectory ()
	{
		string staleSession = string.Join (
			'-',
			Environment.ProcessId.ToString (CultureInfo.InvariantCulture),
			"0");

		Assert.That (
			AutosaveManager.IsSessionOwnerAlive (Path.Combine (directory, staleSession)),
			Is.False);
	}

	[Test]
	public void ExitedProcessDoesNotOwnCrashedSessionDirectory ()
	{
		string staleSession = string.Join (
			'-',
			int.MaxValue.ToString (CultureInfo.InvariantCulture),
			"1");

		Assert.That (
			AutosaveManager.IsSessionOwnerAlive (Path.Combine (directory, staleSession)),
			Is.False);
	}

	[Test]
	public void CompleteArchiveIsRecoverable ()
	{
		string path = WriteOra ("valid.ora", "image/openraster", includeStack: true);

		Assert.That (AutosaveManager.Validate (path), Is.Null);
	}

	[Test]
	public void MissingFileIsRejected ()
		=> Assert.That (AutosaveManager.Validate (Path.Combine (directory, "absent.ora")), Is.Not.Null);

	[Test]
	public void EmptyFileIsRejected ()
	{
		// What a crash leaves when it hits between creating the file and writing it.
		string path = Path.Combine (directory, "empty.ora");
		File.WriteAllBytes (path, []);

		Assert.That (AutosaveManager.Validate (path), Is.Not.Null);
	}

	[Test]
	public void TruncatedArchiveIsRejected ()
	{
		// A partially flushed export: valid up to the point the process died.
		string path = WriteOra ("truncated.ora", "image/openraster", includeStack: true);
		byte[] complete = File.ReadAllBytes (path);
		File.WriteAllBytes (path, complete[..(complete.Length / 2)]);

		Assert.That (AutosaveManager.Validate (path), Is.Not.Null);
	}

	[Test]
	public void ArchiveWithoutLayerInformationIsRejected ()
	{
		string path = WriteOra ("no-stack.ora", "image/openraster", includeStack: false);

		Assert.That (AutosaveManager.Validate (path), Is.Not.Null);
	}

	[Test]
	public void ArchiveOfAnotherFormatIsRejected ()
	{
		// Guards against a stray file in the autosave directory being opened as a document.
		string path = WriteOra ("other.ora", "application/zip", includeStack: true);

		Assert.That (AutosaveManager.Validate (path), Is.Not.Null);
	}

	/// <summary>
	/// Exporting blocks the UI, so the interval stretches with what the last export cost.
	/// A big painting is the one a user can least afford to lose, so it is never dropped
	/// for its size - it just waits longer, up to the cap.
	/// </summary>
	[TestCase (0.01, 60, 60)]   // cheap document: the configured interval is the floor
	[TestCase (1.2, 60, 60)]    // still under the duty budget at 60s
	[TestCase (4.0, 60, 200)]   // 4s per export earns a ~3.3 minute wait
	[TestCase (30.0, 60, 300)]  // pathological document: capped, not abandoned
	public void IntervalStretchesWithExportCost (double exportSeconds, int configured, int expected)
		=> Assert.That (AutosaveManager.NextIntervalSeconds (exportSeconds, configured), Is.EqualTo (expected));

	[Test]
	public void ConfiguredIntervalBeyondTheCapIsHonoured ()
	{
		// The cap bounds what the adaptive backoff may impose, not what the user may ask for.
		Assert.That (AutosaveManager.NextIntervalSeconds (0.01, 900), Is.EqualTo (900));
		Assert.That (AutosaveManager.NextIntervalSeconds (30.0, 900), Is.EqualTo (900));
	}

	/// <summary>
	/// Pins the pure boundary MustForceThroughDeferral runs on. The state machine that feeds it -
	/// deferred_since accumulating across held-pointer ticks and resetting once it releases - has its
	/// own coverage in AutosaveResilienceTest, via PointerButtonHeldOverride and Clock standing in for
	/// the real seat and wall clock a headless run has neither of.
	/// </summary>
	[TestCase (0, false)]
	[TestCase (299, false)]
	[TestCase (300, true)]
	[TestCase (600, true)]
	public void DeferralIsForcedThroughOnceItReachesTheMaxInterval (double secondsDeferred, bool expected)
		=> Assert.That (AutosaveManager.MustForceThroughDeferral (secondsDeferred), Is.EqualTo (expected));

	private string WriteOra (string name, string mimetype, bool includeStack)
	{
		string path = Path.Combine (directory, name);

		using (FileStream stream = File.Create (path))
		using (ZipArchive archive = new (stream, ZipArchiveMode.Create)) {

			WriteEntry (archive, "mimetype", mimetype);

			if (includeStack)
				WriteEntry (archive, "stack.xml", "<image w=\"1\" h=\"1\"><stack /></image>");

			// Padding, so that halving the file leaves a plausible-looking prefix.
			WriteEntry (archive, "data/layer0.png", new string ('x', 4096));
		}

		return path;
	}

	private static void WriteEntry (ZipArchive archive, string name, string content)
	{
		using Stream entry = archive.CreateEntry (name).Open ();
		entry.Write (Encoding.UTF8.GetBytes (content));
	}
}
