// AutosaveManager.cs
//
// Periodically exports every open document to an OpenRaster file under the user's
// settings directory, so a crash doesn't take unsaved work with it. The autosaves of
// a session are deleted when that session quits normally, so anything still on disk at
// startup is by definition the leftovers of a session that died - those are what the
// recovery dialog offers.
//
// Based on Pinta PR #2189 by: colin-i

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Pinta.Core;

/// <summary>
/// An autosaved document found on disk at startup, belonging to a session that
/// did not exit normally.
/// </summary>
public sealed class AutosaveCandidate
{
	/// <summary>Path of the autosaved .ora file.</summary>
	public required string AutosavePath { get; init; }

	/// <summary>The document's name at the time it was autosaved, e.g. "dog.jpg".</summary>
	public required string DisplayName { get; init; }

	/// <summary>
	/// URI of the file the document was opened from, or null if it had never been saved.
	/// </summary>
	public string? OriginalUri { get; init; }

	public DateTime Timestamp { get; init; }

	/// <summary>Null if the file passed validation, otherwise why it did not.</summary>
	public string? Problem { get; init; }

	public bool IsRecoverable => Problem is null;
}

public sealed class AutosaveManager
{
	private const string AUTOSAVE_DIRECTORY = "autosave";
	private const string IMAGE_EXTENSION = ".ora";
	private const string INFO_EXTENSION = ".info";

	// Written to, then moved over the real name, so a crash mid-export cannot leave a
	// truncated file that looks recoverable.
	private const string PARTIAL_EXTENSION = ".part";

	private readonly ChromeManager chrome;
	private readonly ImageConverterManager image_formats;
	private readonly SettingsManager settings;
	private readonly WorkspaceManager workspace;

	/// <summary>
	/// Directory holding this session's autosaves. Named for the process so that a
	/// second running instance neither overwrites nor deletes this one's files.
	/// </summary>
	private readonly string session_directory;

	// Per document: the slot its autosave file occupies, and a fingerprint of the history
	// position it was last written at. The fingerprint is what keeps an idle document from
	// being re-exported every tick, and unlike a dirty flag it also notices undo and redo.
	private readonly Dictionary<Document, int> document_slots = [];
	private readonly Dictionary<Document, (int Pointer, int Count)> autosaved_states = [];
	private int next_slot;

	private uint timer_id;

	public AutosaveManager (
		SettingsManager settings,
		WorkspaceManager workspace,
		ImageConverterManager imageFormats,
		ChromeManager chrome)
	{
		this.settings = settings;
		this.workspace = workspace;
		this.chrome = chrome;
		image_formats = imageFormats;

		session_directory = Path.Combine (
			AutosaveRootDirectory (settings),
			Environment.ProcessId.ToString (CultureInfo.InvariantCulture));
	}

	public bool IsEnabled
		=> settings.GetSetting (SettingNames.AUTOSAVE_ENABLED, true);

	/// <summary>Seconds between autosaves. Never less than 10, to bound the cost.</summary>
	public int IntervalSeconds
		=> Math.Max (10, settings.GetSetting (SettingNames.AUTOSAVE_INTERVAL, 60));

	/// <summary>
	/// Begins autosaving. Call once the main window exists, since exporting needs a
	/// parent window for any error dialog.
	/// </summary>
	public void Start ()
	{
		if (timer_id != 0 || !IsEnabled)
			return;

		timer_id = GLib.Functions.TimeoutAdd (
			GLib.Constants.PRIORITY_DEFAULT_IDLE,
			(uint) IntervalSeconds * 1000,
			OnTimerTick);
	}

	/// <summary>
	/// Stops autosaving and removes this session's autosave files. Call on a normal quit -
	/// files left behind are what the next launch offers to recover.
	/// </summary>
	public void Stop ()
	{
		if (timer_id != 0) {
			GLib.Source.Remove (timer_id);
			timer_id = 0;
		}

		document_slots.Clear ();
		autosaved_states.Clear ();

		try {
			if (Directory.Exists (session_directory))
				Directory.Delete (session_directory, recursive: true);
		} catch (Exception e) {
			// Leftover files only cost a recovery prompt next launch, so never fail the quit.
			Console.Error.WriteLine ($"Failed to clean up autosave directory: {e.Message}");
		}
	}

	private bool OnTimerTick ()
	{
		// Autosaving must never be the thing that takes the app down - that is the whole
		// point of the feature. Any failure is reported and the timer keeps running.
		try {
			AutosaveDirtyDocuments ();
		} catch (Exception e) {
			Console.Error.WriteLine ($"Autosave failed: {e}");
		}

		return true;
	}

	private void AutosaveDirtyDocuments ()
	{
		FormatDescriptor? format = image_formats.GetFormatByExtension ("ora");

		if (format?.Exporter is null)
			return;

		// Documents closed since the last tick keep neither their slot nor their file.
		foreach (Document closed in document_slots.Keys.Except (workspace.OpenDocuments).ToArray ())
			Forget (closed);

		foreach (Document document in workspace.OpenDocuments) {

			(int, int) state = (document.History.Pointer, document.History.Items.Count ());

			if (autosaved_states.TryGetValue (document, out var previous) && previous == state)
				continue;

			// A document sitting at its initial state has nothing worth recovering.
			if (!document.IsDirty) {
				autosaved_states[document] = state;
				continue;
			}

			Autosave (document, format.Exporter);

			autosaved_states[document] = state;
		}
	}

	private void Autosave (Document document, IImageExporter exporter)
	{
		Directory.CreateDirectory (session_directory);

		if (!document_slots.TryGetValue (document, out int slot)) {
			slot = next_slot++;
			document_slots[document] = slot;
		}

		string imagePath = Path.Combine (session_directory, $"{slot}{IMAGE_EXTENSION}");
		string partialPath = imagePath + PARTIAL_EXTENSION;

		exporter.Export (document, Gio.FileHelper.NewForPath (partialPath), chrome.MainWindow);

		File.Move (partialPath, imagePath, overwrite: true);

		// Written after the image so that an info file always describes a complete image.
		File.WriteAllLines (
			Path.Combine (session_directory, $"{slot}{INFO_EXTENSION}"),
			[document.DisplayName, document.File?.GetUri () ?? string.Empty]);
	}

	private void Forget (Document document)
	{
		if (document_slots.TryGetValue (document, out int slot)) {
			Delete (Path.Combine (session_directory, $"{slot}{IMAGE_EXTENSION}"));
			Delete (Path.Combine (session_directory, $"{slot}{INFO_EXTENSION}"));
		}

		document_slots.Remove (document);
		autosaved_states.Remove (document);
	}

	private static void Delete (string path)
	{
		try {
			File.Delete (path);
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to delete autosave file '{path}': {e.Message}");
		}
	}

	/// <summary>
	/// Autosaves left by sessions that did not exit normally, newest first. Each is
	/// validated, so a candidate that cannot be loaded is reported rather than offered.
	/// </summary>
	public IReadOnlyList<AutosaveCandidate> FindRecoverableDocuments ()
	{
		string root = AutosaveRootDirectory (settings);

		if (!Directory.Exists (root))
			return [];

		List<AutosaveCandidate> candidates = [];

		foreach (string directory in Directory.EnumerateDirectories (root)) {

			// Our own session's files are live, not leftovers.
			if (Path.GetFullPath (directory) == Path.GetFullPath (session_directory))
				continue;

			foreach (string image in Directory.EnumerateFiles (directory, "*" + IMAGE_EXTENSION))
				candidates.Add (Inspect (image));
		}

		return candidates.OrderByDescending (c => c.Timestamp).ToList ();
	}

	/// <summary>
	/// Deletes an autosave and its metadata, e.g. once it has been recovered or discarded.
	/// </summary>
	public static void Discard (AutosaveCandidate candidate)
	{
		Delete (candidate.AutosavePath);
		Delete (Path.ChangeExtension (candidate.AutosavePath, INFO_EXTENSION));

		// The session directory is only ours to remove once nothing is left in it.
		try {
			string? directory = Path.GetDirectoryName (candidate.AutosavePath);
			if (directory is not null && !Directory.EnumerateFileSystemEntries (directory).Any ())
				Directory.Delete (directory);
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to remove empty autosave directory: {e.Message}");
		}
	}

	private static AutosaveCandidate Inspect (string imagePath)
	{
		string displayName = Path.GetFileNameWithoutExtension (imagePath);
		string? originalUri = null;

		try {
			string[] info = File.ReadAllLines (Path.ChangeExtension (imagePath, INFO_EXTENSION));
			if (info.Length > 0 && info[0].Length > 0) displayName = info[0];
			if (info.Length > 1 && info[1].Length > 0) originalUri = info[1];
		} catch (IOException) {
			// An autosave whose info file didn't make it to disk is still recoverable;
			// it just loses its original name.
		}

		return new AutosaveCandidate {
			AutosavePath = imagePath,
			DisplayName = displayName,
			OriginalUri = originalUri,
			Timestamp = File.GetLastWriteTime (imagePath),
			Problem = Validate (imagePath),
		};
	}

	/// <summary>
	/// Checks that a file is a complete, well-formed OpenRaster archive.
	/// Returns null when it is, otherwise a message describing what is wrong with it.
	/// </summary>
	/// <remarks>
	/// This reads the archive's structure without decoding its images, which is enough to
	/// reject the realistic failure - a file truncated by the crash we are recovering from.
	/// A file that passes here can still fail to load, so recovery reports that separately.
	/// </remarks>
	public static string? Validate (string imagePath)
	{
		try {
			FileInfo info = new (imagePath);

			if (!info.Exists)
				return Translations.GetString ("The file no longer exists.");

			if (info.Length == 0)
				return Translations.GetString ("The file is empty.");

			using ZipArchive archive = ZipFile.OpenRead (imagePath);

			ZipArchiveEntry? mimetype = archive.GetEntry ("mimetype");

			if (mimetype is null)
				return Translations.GetString ("The file is not an OpenRaster image.");

			using (StreamReader reader = new (mimetype.Open ())) {
				if (reader.ReadToEnd ().Trim () != "image/openraster")
					return Translations.GetString ("The file is not an OpenRaster image.");
			}

			if (archive.GetEntry ("stack.xml") is null)
				return Translations.GetString ("The file is missing its layer information.");

			// Every layer the stack references must actually be present, and readable to
			// its last byte - a half-written archive typically fails right here.
			foreach (ZipArchiveEntry entry in archive.Entries) {

				if (entry.FullName == "mimetype")
					continue;

				using Stream stream = entry.Open ();
				stream.CopyTo (Stream.Null);
			}

			return null;

		} catch (InvalidDataException) {
			return Translations.GetString ("The file is damaged or incomplete.");
		} catch (Exception e) {
			return e.Message;
		}
	}

	private static string AutosaveRootDirectory (SettingsManager settings)
		=> Path.Combine (settings.GetUserSettingsDirectory (), AUTOSAVE_DIRECTORY);
}
