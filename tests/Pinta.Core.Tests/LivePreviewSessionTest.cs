using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A live preview session spans a non-modal dialog, so the rest of the app keeps running while it is
/// open: history steps, tab switches, layer and document closes. These pin what the session does when
/// the document changes underneath it. The dialog is a task the test answers.
/// </summary>
[TestFixture]
internal sealed class LivePreviewSessionTest : DocumentHarness
{
	private static readonly ColorBgra InvertedRed = ColorBgra.FromBgra (255, 255, 0, 255);

	private Document? other_document;

	[OneTimeSetUp]
	public void InstallProgressDialog ()
		=> PintaCore.Chrome.InitializeProgessDialog (new SilentProgressDialog ());

	[TearDown]
	public void CloseOtherDocument ()
	{
		if (other_document is not null && PintaCore.Workspace.OpenDocuments.Contains (other_document))
			PintaCore.Workspace.CloseDocument (other_document);
		other_document = null;
	}

	/// <summary>
	/// Painting while the dialog is open pushes history, which rebuilds the layer's composite and frees
	/// the old one. A slider change afterwards re-renders; it must read the image the preview started
	/// from, not a freed surface.
	/// </summary>
	[Test]
	public async Task RestartAfterAHistoryStepRendersFromThePreviewsOwnSnapshot ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		layer.AddModifierNode (Invert ());
		Refresh (layer);

		ProbeEffect effect = new ();
		Task session = PintaCore.LivePreview.Run (effect);

		PaintRaster (layer, s => Fill (s, Blue), "stroke");
		effect.Reset ();
		effect.EffectData!.FirePropertyChanged ("Value");

		effect.Answer (true); // OK waits for the restarted render to finish
		await session;

		Assert.That (effect.Failure, Is.Null);
		Assert.That (effect.SeenAtOrigin, Is.EqualTo (InvertedRed));
	}

	/// <summary>
	/// Switching tabs while the dialog is open must not silently kill it: the other document is usable
	/// (no preview gating its tools), and OK still applies to the document the dialog was opened on.
	/// </summary>
	[Test]
	public async Task OkAfterSwitchingTabsAppliesToThePreviewedDocument ()
	{
		ProbeEffect effect = new ();
		Task session = PintaCore.LivePreview.Run (effect);

		ActivateOtherDocument ();
		Assert.That (PintaCore.LivePreview.IsEnabled, Is.False, "the preview gates tools on a document it is not shown on");

		effect.Answer (true);
		await session;

		Assert.That (Layer (0).HasModifiers, Is.True);
		Assert.That (other_document!.Layers.UserLayers.Any (l => l.HasModifiers), Is.False);
	}

	/// <summary>
	/// The poll timer reads the active canvas's zoom. Once the previewed (and only) document is closed
	/// there is no canvas, so the timer must have stopped rather than throw on every tick.
	/// </summary>
	[Test]
	// Pumping the GLib loop reaches AppKit, which aborts the process off the main thread.
	[Platform (Exclude = "MacOsX")]
	public async Task ClosingThePreviewedDocumentStopsPolling ()
	{
		ProbeEffect effect = new ();
		Task session = PintaCore.LivePreview.Run (effect);
		effect.RenderedOnce.Wait (TimeSpan.FromSeconds (5));

		PintaCore.Workspace.CloseDocument (Document);
		Assert.That (PintaCore.Workspace.HasOpenDocuments, Is.False);

		Thread.Sleep (250); // past the 100ms poll interval
		Assert.DoesNotThrow (() => {
			while (GLib.MainContext.Default ().Iteration (false)) { }
		});

		effect.Answer (true);
		await session;
	}

	/// <summary>
	/// Deleting the previewed layer while the dialog is open leaves nothing to apply to; OK must not
	/// record a step against a layer that is no longer in the document.
	/// </summary>
	[Test]
	public async Task OkAfterThePreviewedLayerWasDeletedRecordsNothing ()
	{
		UserLayer previewed = AddLayer ();
		Document.Layers.SetCurrentUserLayer (previewed);

		ProbeEffect effect = new ();
		Task session = PintaCore.LivePreview.Run (effect);

		Document.Layers.DeleteLayer (Document.Layers.IndexOf (previewed));
		int stepsBefore = Document.History.Items.Count ();

		effect.Answer (true);
		await session;

		Assert.That (Document.History.Items.Count (), Is.EqualTo (stepsBefore));
		Assert.That (previewed.HasModifiers, Is.False);
	}

	private void ActivateOtherDocument ()
	{
		other_document = new Document (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));
		other_document.Layers.AddNewLayer (string.Empty);
		PintaCore.Workspace.ActivateDocument (other_document);
	}

	/// <summary>
	/// Remembers what it read; its dialog stays open until answered. Renders run on clones of the
	/// effect, so what they saw is kept in a record the clones share.
	/// </summary>
	private sealed class ProbeEffect : BaseEffect
	{
		private sealed class Record
		{
			public readonly ManualResetEventSlim rendered_once = new ();
			public ColorBgra seen_at_origin;
			public Exception? failure;
		}

		private readonly TaskCompletionSource<bool> dialog = new ();
		private readonly Record record = new ();

		public ProbeEffect () => EffectData = new ProbeData ();

		public override bool IsTileable => true;
		public override bool IsConfigurable => true;
		public override string Name => "Probe (test)";
		public override bool SurvivesSaveAndReload => false;

		public ManualResetEventSlim RenderedOnce => record.rendered_once;
		public ColorBgra SeenAtOrigin => record.seen_at_origin;
		public Exception? Failure => Volatile.Read (ref record.failure);

		public void Answer (bool ok) => dialog.SetResult (ok);

		public void Reset ()
		{
			record.seen_at_origin = default;
			Volatile.Write (ref record.failure, null);
		}

		public override Task<bool> LaunchConfiguration () => dialog.Task;

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			try {
				record.seen_at_origin = src.GetColorBgra (PointI.Zero);
				dst.GetPixelData ().Clear ();
			} catch (Exception e) {
				Volatile.Write (ref record.failure, e);
			}
			record.rendered_once.Set ();
		}
	}

	private sealed class ProbeData : EffectData
	{
	}

	private sealed class SilentProgressDialog : IProgressDialog
	{
		public string Title { get; set; } = string.Empty;
		public string Text { get; set; } = string.Empty;
		public double Progress { get; set; }
#pragma warning disable CS0067 // never canceled in these tests
		public event EventHandler? Canceled;
#pragma warning restore CS0067
		public void Show () { }
		public void Hide () { }
	}
}
