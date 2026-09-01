// AutosaveManager.cs
//
// Periodically exports every open document to an OpenRaster file under the user's
// settings directory, so a crash doesn't take unsaved work with it. The autosaves of
// a session are deleted when that session quits normally, so anything still on disk at
// startup is by definition the leftovers of a session that died - those are what the
// recovery dialog offers.
//
// ponytail: the export itself runs on the main loop, so it is scheduled for a moment when
// the user isn't interacting rather than being made interruptible. Ceiling: a document slow
// enough to export that a stall is visible even from an idle start. Upgrade path is to
// snapshot the layer surfaces on the main thread and encode off it, which needs the whole
// document - objects and text engines included - to be safe to read from another thread.
//
// Based on Pinta PR #2189 by: colin-i

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

	/// <summary>
	/// Share of wall-clock time a document may spend being autosaved. Exporting blocks the
	/// UI, so an expensive document waits proportionally longer between autosaves rather
	/// than stalling the app every interval - or, worse, being skipped for its size.
	/// </summary>
	private const int EXPORT_DUTY_DIVISOR = 50;

	/// <summary>Longest a document may go without being autosaved.</summary>
	private const int MAX_INTERVAL_SECONDS = 300;

	/// <summary>How long to wait before looking again for a gap in the user's work.</summary>
	private const uint RETRY_MILLISECONDS = 2000;

	/// <summary>
	/// How many autosaves may wait to be recovered. A session that crashes leaves its
	/// documents behind until they are recovered or discarded, and dismissing the prompt
	/// keeps them - so without a cap, repeated crashes accumulate without bound.
	/// </summary>
	private const int MAX_RECOVERABLE = 10;

	private readonly ChromeManager chrome;
	private readonly ImageConverterManager image_formats;
	private readonly ISettingsService settings;
	private readonly WorkspaceManager workspace;

	/// <summary>
	/// Directory holding this session's autosaves. Its process id and process start time
	/// identify the owner without colliding with a later process that reuses the same id.
	/// </summary>
	private readonly string session_directory;

	// Per document: the slot its autosave file occupies, and a fingerprint of the history
	// position it was last written at. The fingerprint is what keeps an idle document from
	// being re-exported every tick, and unlike a dirty flag it also notices undo and redo.
	private readonly Dictionary<Document, int> document_slots = [];
	private readonly Dictionary<Document, (int Pointer, int Count)> autosaved_states = [];

	// When each document is next allowed to be autosaved, from how long its last export took.
	private readonly Dictionary<Document, DateTime> next_autosave_times = [];

	private int next_slot;

	private uint timer_id;
	private uint retry_timer_id;
	private bool autosave_scheduled;

	// The interval the running timer was armed at, so a tick can notice the setting changing
	// underneath it. Meaningless while timer_id is 0.
	private int armed_interval_seconds;

	// When the current run of pointer-held deferrals began. Null while nothing is deferred.
	private DateTime? deferred_since;

	public AutosaveManager (
		ISettingsService settings,
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
			CreateSessionDirectoryName ());
	}

	public bool IsEnabled
		=> settings.GetSetting (SettingNames.AUTOSAVE_ENABLED, true);

	/// <summary>The running timer's identifier, or 0 when autosaving has not been started.</summary>
	internal uint TimerId => timer_id;

	/// <summary>Seconds between autosaves. Never less than 10, to bound the cost.</summary>
	public int IntervalSeconds
		=> Math.Max (10, settings.GetSetting (SettingNames.AUTOSAVE_INTERVAL, 60));

	/// <summary>
	/// Begins autosaving. Call once the main window exists, since exporting needs a
	/// parent window for any error dialog.
	/// </summary>
	/// <remarks>
	/// The timer runs whether or not autosaving is enabled; each tick asks. That is what lets
	/// the setting be turned back on mid-session without a restart.
	/// </remarks>
	public void Start ()
	{
		if (timer_id != 0)
			return;

		armed_interval_seconds = IntervalSeconds;

		timer_id = GLib.Functions.TimeoutAdd (
			GLib.Constants.PRIORITY_DEFAULT_IDLE,
			(uint) armed_interval_seconds * 1000,
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

		if (retry_timer_id != 0) {
			GLib.Source.Remove (retry_timer_id);
			retry_timer_id = 0;
		}

		document_slots.Clear ();
		autosaved_states.Clear ();
		next_autosave_times.Clear ();

		try {
			if (Directory.Exists (session_directory))
				Directory.Delete (session_directory, recursive: true);
		} catch (Exception e) {
			// Leftover files only cost a recovery prompt next launch, so never fail the quit.
			Console.Error.WriteLine ($"Failed to clean up autosave directory: {e.Message}");
		}
	}

	/// <returns>Whether the timer keeps running, i.e. false once it has been re-armed.</returns>
	internal bool OnTimerTick ()
	{
		// Both settings are read here rather than at Start, so changing either one in the
		// settings dialog takes effect on the next tick instead of on the next launch.
		if (IsEnabled)
			// The interval only says autosaving is due. When it actually happens is decided by
			// TryAutosave, which waits for a moment that won't interrupt the user.
			ScheduleAutosave ();

		if (IntervalSeconds == armed_interval_seconds)
			return true;

		// A timer's interval is fixed once added, so a new one replaces it.
		timer_id = 0;
		Start ();

		return false;
	}

	/// <summary>
	/// Queues an autosave attempt at a priority below input handling and drawing, so it
	/// can only start once the main loop has nothing else to do.
	/// </summary>
	private void ScheduleAutosave ()
	{
		if (autosave_scheduled)
			return;

		autosave_scheduled = true;

		GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_LOW, TryAutosave);
	}

	/// <summary>Test seam: real time by default, overridden to drive the deferral clock without sleeping.</summary>
	internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

	internal bool TryAutosave ()
	{
		autosave_scheduled = false;

		// Exporting blocks the main loop for as long as it takes to write the file, so it
		// must not begin in the middle of a brush stroke, a drag, or any other gesture.
		// Strokes end, so this defers the work rather than skipping it - but only up to
		// MAX_INTERVAL_SECONDS. Left unbounded, one very long continuous stroke would defeat
		// this class's own documented "longest a document may go without being autosaved"
		// guarantee for exactly the large slow-export documents that guarantee exists for.
		if (IsPointerButtonHeld ()) {
			deferred_since ??= Clock ();

			if (!MustForceThroughDeferral ((Clock () - deferred_since.Value).TotalSeconds)) {
				// One retry at a time. The interval timer keeps firing throughout a long stroke,
				// and each tick would otherwise add a retry that outlives the one before it.
				if (retry_timer_id == 0)
					retry_timer_id = GLib.Functions.TimeoutAdd (GLib.Constants.PRIORITY_LOW, RETRY_MILLISECONDS, () => {
						retry_timer_id = 0;
						ScheduleAutosave ();
						return false;
					});

				return false;
			}
			// Deferred long enough - fall through and force the export mid-gesture rather than
			// let the deferral itself defeat the guarantee.
		}

		deferred_since = null;

		// Autosaving must never be the thing that takes the app down - that is the whole
		// point of the feature. Any failure is reported and the timer keeps running.
		try {
			AutosaveDirtyDocuments ();
		} catch (Exception e) {
			Console.Error.WriteLine ($"Autosave failed: {e}");
		}

		return false;
	}

	/// <summary>Test seam: needs a real GDK seat a headless run does not have.</summary>
	internal Func<bool>? PointerButtonHeldOverride { get; set; }

	/// <summary>
	/// Whether the user currently has a mouse button down. Asked of the seat rather than of
	/// the active tool, so it holds for every tool and for drags on any part of the window.
	/// </summary>
	private bool IsPointerButtonHeld ()
	{
		if (PointerButtonHeldOverride is not null)
			return PointerButtonHeldOverride ();

		const Gdk.ModifierType buttons =
			Gdk.ModifierType.Button1Mask |
			Gdk.ModifierType.Button2Mask |
			Gdk.ModifierType.Button3Mask |
			Gdk.ModifierType.Button4Mask |
			Gdk.ModifierType.Button5Mask;

		Gdk.Device? pointer = chrome.MainWindow.GetDisplay ().GetDefaultSeat ()?.GetPointer ();

		return pointer is not null && (pointer.GetModifierState () & buttons) != 0;
	}

	/// <summary>Test seam: stands in for the registered "ora" exporter lookup, so TryAutosave's
	/// deferral decision can be driven with the same fake exporter the other resilience tests use.</summary>
	internal IImageExporter? ExporterOverride { get; set; }

	private void AutosaveDirtyDocuments ()
	{
		IImageExporter? exporter = ExporterOverride ?? image_formats.GetFormatByExtension ("ora")?.Exporter;

		if (exporter is null)
			return;

		AutosaveDirtyDocuments (exporter);
	}

	internal void AutosaveDirtyDocuments (IImageExporter exporter)
	{
		// Documents closed since the last tick keep neither their slot nor their file.
		foreach (Document closed in document_slots.Keys.Except (workspace.OpenDocuments).ToArray ())
			Forget (closed);

		foreach (Document document in workspace.OpenDocuments) {

			(int, int) state = (document.History.Pointer, document.History.Items.Count ());

			if (autosaved_states.TryGetValue (document, out var previous) && previous == state)
				continue;

			// A document that is slow to export is autosaved less often, never not at all.
			if (next_autosave_times.TryGetValue (document, out DateTime due) && DateTime.UtcNow < due)
				continue;

			// A document sitting at its initial state has nothing worth recovering.
			if (!document.IsDirty) {
				autosaved_states[document] = state;
				continue;
			}

			// One document that cannot be exported - a corrupt surface, a full disk, a
			// permission the user revoked - must not cost every document after it in the
			// list its autosave, which is what an exception escaping this loop would do.
			try {
				Autosave (document, exporter);
			} catch (Exception e) {
				Console.Error.WriteLine ($"Failed to autosave '{document.DisplayName}': {e}");

				// Left out of autosaved_states so the next attempt still happens, but held
				// off for an interval so a document that always fails is not always retried.
				next_autosave_times[document] = DateTime.UtcNow.AddSeconds (IntervalSeconds);
				continue;
			}

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

		System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew ();

		exporter.Export (document, Gio.FileHelper.NewForPath (partialPath), chrome.MainWindow);

		File.Move (partialPath, imagePath, overwrite: true);

		elapsed.Stop ();

		next_autosave_times[document] = DateTime.UtcNow.AddSeconds (
			NextIntervalSeconds (elapsed.Elapsed.TotalSeconds, IntervalSeconds));

		// Written after the image so that an info file always describes a complete image.
		File.WriteAllLines (
			Path.Combine (session_directory, $"{slot}{INFO_EXTENSION}"),
			[document.DisplayName, document.File?.GetUri () ?? string.Empty]);
	}

	/// <summary>
	/// How long to wait before autosaving a document again, given how long its last export
	/// took. Never shorter than the configured interval, never longer than five minutes.
	/// </summary>
	internal static int NextIntervalSeconds (double exportSeconds, int configuredSeconds)
	{
		int proportional = (int) Math.Ceiling (exportSeconds * EXPORT_DUTY_DIVISOR);

		// A configured interval longer than the cap is the user's call, so it wins.
		int longest = Math.Max (configuredSeconds, MAX_INTERVAL_SECONDS);

		return Math.Clamp (proportional, configuredSeconds, longest);
	}

	/// <summary>
	/// Whether a pointer-held deferral that has already lasted <paramref name="secondsDeferred"/>
	/// must be forced through rather than deferred again. MAX_INTERVAL_SECONDS is the longest a
	/// document may go without being autosaved; an unbounded deferral loop would defeat that for
	/// the length of a single continuous stroke.
	/// </summary>
	internal static bool MustForceThroughDeferral (double secondsDeferred)
		=> secondsDeferred >= MAX_INTERVAL_SECONDS;

	private void Forget (Document document)
	{
		if (document_slots.TryGetValue (document, out int slot)) {
			Delete (Path.Combine (session_directory, $"{slot}{IMAGE_EXTENSION}"));
			Delete (Path.Combine (session_directory, $"{slot}{INFO_EXTENSION}"));
		}

		document_slots.Remove (document);
		autosaved_states.Remove (document);
		next_autosave_times.Remove (document);
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

			bool? ownerAlive = IsSessionOwnerAlive (directory);

			// Files owned by any running instance are still being written, not recoverable.
			if (Path.GetFullPath (directory) == Path.GetFullPath (session_directory)
				|| ownerAlive == true)
				continue;

			// Only sweep once the owner is known to have exited - an unknown owner may still
			// be writing, but its finished autosaves are worth offering either way.
			bool ownerExited = ownerAlive == false;

			// A failed export leaves its temporary file behind, and a session that never
			// managed to autosave leaves nothing but the directory. Neither is recoverable
			// and nothing else will ever remove them, so sweep them up on the way past.
			if (ownerExited)
				foreach (string partial in Directory.EnumerateFiles (directory, "*" + PARTIAL_EXTENSION))
					Delete (partial);

			string[] images = Directory.GetFiles (directory, "*" + IMAGE_EXTENSION);

			if (images.Length == 0) {
				if (ownerExited)
					DeleteDirectoryIfEmpty (directory);
				continue;
			}

			foreach (string image in images)
				candidates.Add (Inspect (image));
		}

		List<AutosaveCandidate> newest = candidates.OrderByDescending (c => c.Timestamp).ToList ();

		if (newest.Count <= MAX_RECOVERABLE)
			return newest;

		// Delete the stale autosaves first, assume the user has decided not to come back for these
		foreach (AutosaveCandidate stale in newest[MAX_RECOVERABLE..])
			Discard (stale);

		return newest[..MAX_RECOVERABLE];
	}

	/// <summary>
	/// Deletes an autosave and its metadata, e.g. once it has been recovered or discarded.
	/// </summary>
	public static void Discard (AutosaveCandidate candidate)
	{
		Delete (candidate.AutosavePath);
		Delete (Path.ChangeExtension (candidate.AutosavePath, INFO_EXTENSION));

		DeleteDirectoryIfEmpty (Path.GetDirectoryName (candidate.AutosavePath));
	}

	/// <summary>
	/// Removes a finished session's directory. Only ours to remove once nothing is left in it.
	/// </summary>
	private static void DeleteDirectoryIfEmpty (string? directory)
	{
		try {
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

	internal static string CreateSessionDirectoryName ()
	{
		using Process process = Process.GetCurrentProcess ();
		return string.Join (
			'-',
			process.Id.ToString (CultureInfo.InvariantCulture),
			process.StartTime.ToUniversalTime ().Ticks.ToString (CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Whether the instance that owns a session directory is still running, or null when the
	/// platform will not say. Unknown owners keep their files: those are still worth offering
	/// for recovery, but not worth deleting on a guess.
	/// </summary>
	internal static bool? IsSessionOwnerAlive (string directory)
	{
		string[] owner = Path.GetFileName (directory).Split ('-');

		if (owner.Length != 2
			|| !int.TryParse (owner[0], NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
			|| !long.TryParse (owner[1], NumberStyles.None, CultureInfo.InvariantCulture, out long processStartTicks))
			return false;

		try {
			using Process process = Process.GetProcessById (processId);
			return !process.HasExited
				&& process.StartTime.ToUniversalTime ().Ticks == processStartTicks;
		} catch (ArgumentException) {
			return false;
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to inspect autosave session owner: {e.Message}");
			return null;
		}
	}

	private static string AutosaveRootDirectory (ISettingsService settings)
		=> Path.Combine (settings.GetUserSettingsDirectory (), AUTOSAVE_DIRECTORY);
}
