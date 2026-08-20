using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Autosave exists to survive the moment everything else has gone wrong, so the ways it can
/// itself go wrong matter more than they would in a feature the user drives. Each test here
/// pins a failure that leaves autosaving silently not happening — the state in which the
/// feature looks fine and protects nothing.
/// </summary>
[TestFixture]
internal sealed class AutosaveResilienceTest : DocumentHarness
{
	private string root = null!;
	private FakeSettings settings = null!;
	private readonly List<Document> extra_documents = [];

	[SetUp]
	public void CreateSettingsDirectory ()
	{
		root = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Directory.CreateDirectory (root);
		settings = new FakeSettings { Directory = root };
	}

	[TearDown]
	public void RemoveSettingsDirectory ()
	{
		// Closing a dirty document rebuilds the window menu, which reads the active document -
		// so the flag comes off while every document is still open and there is one to read.
		Document.IsDirty = false;

		foreach (Document document in extra_documents)
			document.IsDirty = false;

		// Newest first: the workspace only keeps its active index straight when the document
		// being closed is the active one, which is always the most recently activated.
		extra_documents.Reverse ();

		try {
			foreach (Document document in extra_documents)
				PintaCore.Workspace.CloseDocument (document);
		} finally {
			extra_documents.Clear ();
		}

		Directory.Delete (root, recursive: true);
	}

	/// <summary>
	/// The export of one document can fail for reasons that say nothing about the next one:
	/// a full disk, a revoked permission, a surface the exporter chokes on. Before the fix an
	/// exception escaped the whole loop, so the first document that could never be exported
	/// took every document after it in the list down with it — for the rest of the session,
	/// on every tick, with no sign in the UI that autosaving had stopped.
	/// </summary>
	[Test]
	public void AFailedExportDoesNotStarveTheDocumentsAfterIt ()
	{
		Document first = Document;
		Document second = OpenAnotherDocument ();
		Document third = OpenAnotherDocument ();

		foreach (Document document in new[] { first, second, third })
			document.IsDirty = true;

		RecordingExporter exporter = new () { Fails = first };
		AutosaveManager autosave = CreateManager ();

		autosave.AutosaveDirtyDocuments (exporter);

		Assert.Multiple (() => {
			Assert.That (exporter.Attempted, Is.EquivalentTo (new[] { first, second, third }),
				"every dirty document must be attempted");
			Assert.That (exporter.Exported, Is.EquivalentTo (new[] { second, third }),
				"the documents after the failing one must still reach disk");
		});

		autosave.Stop ();
	}

	/// <summary>
	/// A document that fails is held off rather than retried on every tick — but only held
	/// off, never written down as saved, so it is attempted again once the backoff expires.
	/// </summary>
	[Test]
	public void AFailedExportIsBackedOffRatherThanRetriedEveryTick ()
	{
		Document.IsDirty = true;

		RecordingExporter exporter = new () { Fails = Document };
		AutosaveManager autosave = CreateManager ();

		autosave.AutosaveDirtyDocuments (exporter);
		autosave.AutosaveDirtyDocuments (exporter);

		Assert.That (exporter.Attempted, Has.Count.EqualTo (1));

		autosave.Stop ();
	}

	/// <summary>
	/// The timer used to be armed only if autosaving was enabled at startup, which made the
	/// setting one-way for the session: turning it off worked, turning it back on did nothing
	/// until the next launch.
	/// </summary>
	[Test]
	public void AutosavingCanBeTurnedBackOnWithoutARestart ()
	{
		settings.PutSetting (SettingNames.AUTOSAVE_ENABLED, false);

		AutosaveManager autosave = CreateManager ();
		autosave.Start ();

		uint armed = autosave.TimerId;

		Assert.That (armed, Is.Not.Zero, "the timer runs while disabled so it can notice being enabled");
		Assert.That (autosave.OnTimerTick (), Is.True, "a disabled tick does nothing but must keep the timer");
		Assert.That (autosave.TimerId, Is.EqualTo (armed));

		autosave.Stop ();
	}

	/// <summary>
	/// A GLib timeout's interval is fixed when it is added, so a changed setting only takes
	/// effect if the timer is replaced. It used to be read once at startup and never again.
	/// </summary>
	[Test]
	public void ChangingTheIntervalReArmsTheTimer ()
	{
		// Disabled, so ticking here decides only the timer's fate and queues no export.
		settings.PutSetting (SettingNames.AUTOSAVE_ENABLED, false);
		settings.PutSetting (SettingNames.AUTOSAVE_INTERVAL, 60);

		AutosaveManager autosave = CreateManager ();
		autosave.Start ();

		uint armed = autosave.TimerId;

		Assert.That (autosave.OnTimerTick (), Is.True, "an unchanged interval keeps its timer");
		Assert.That (autosave.TimerId, Is.EqualTo (armed));

		settings.PutSetting (SettingNames.AUTOSAVE_INTERVAL, 30);

		Assert.Multiple (() => {
			Assert.That (autosave.OnTimerTick (), Is.False, "the timer armed at the old interval ends");
			Assert.That (autosave.TimerId, Is.Not.Zero.And.Not.EqualTo (armed), "and a new one takes over");
		});

		autosave.Stop ();
	}

	private AutosaveManager CreateManager ()
		=> new (settings, PintaCore.Workspace, PintaCore.ImageFormats, PintaCore.Chrome);

	private Document OpenAnotherDocument ()
	{
		Document document = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));

		document.Layers.AddNewLayer (string.Empty);
		PintaCore.Workspace.ActivateDocument (document);

		extra_documents.Add (document);

		return document;
	}

	/// <summary>
	/// Stands in for the OpenRaster exporter, so a test can decide which document fails
	/// without needing a document that a real exporter would actually choke on.
	/// </summary>
	private sealed class RecordingExporter : IImageExporter
	{
		public required Document Fails { get; init; }

		public List<Document> Attempted { get; } = [];
		public List<Document> Exported { get; } = [];

		public void Export (Document document, Gio.File file, Gtk.Window parent)
		{
			Attempted.Add (document);

			if (ReferenceEquals (document, Fails))
				throw new IOException ("no space left on device");

			// The manager writes to a temporary name and moves it into place, so an export
			// that leaves nothing behind is not a successful one.
			System.IO.File.WriteAllText (file.GetPath ()!, "autosaved");

			Exported.Add (document);
		}
	}

	private sealed class FakeSettings : ISettingsService
	{
		private readonly Dictionary<string, object> values = [];

		public required string Directory { get; init; }

		public event EventHandler? SaveSettingsBeforeQuit { add { } remove { } }

		public T GetSetting<T> (string key, T defaultValue)
			=> values.TryGetValue (key, out object? value) ? (T) value : defaultValue;

		public void PutSetting (string key, object value) => values[key] = value;

		public string GetUserSettingsDirectory () => Directory;
	}
}
