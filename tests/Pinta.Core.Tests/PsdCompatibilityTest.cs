using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Photoshop files that use what Impasto cannot represent, opened the way the user opens them
/// (File > Open, and Layers > Import from File). Each must end one of two ways, never in a
/// crash: refused with a message and nothing opened, or opened with the unsupported parts
/// converted to something Impasto has.
/// </summary>
[TestFixture]
internal sealed class PsdCompatibilityTest : DocumentHarness
{
	private readonly List<string> errors = [];
	private readonly List<string> messages = [];
	private readonly List<Document> opened = [];

	[SetUp]
	public void CaptureDialogs ()
	{
		PintaCore.Chrome.InitializeErrorDialogHandler ((_, message, _, _) => {
			errors.Add (message);
			return Task.FromResult (ErrorDialogResponse.OK);
		});
		PintaCore.Chrome.InitializeMessageDialog ((_, message, _) => {
			messages.Add (message);
			return Task.CompletedTask;
		});
	}

	[TearDown]
	public void CloseOpenedDocuments ()
	{
		foreach (Document document in opened)
			PintaCore.Workspace.CloseDocument (document);
		opened.Clear ();
		errors.Clear ();
		messages.Clear ();
	}

	// --- Refused ------------------------------------------------------------------------------

	[TestCase ("psd-refused-16bit.psd", "16 bits per channel")]
	[TestCase ("psd-refused-32bit.psd", "32 bits per channel")]
	[TestCase ("psd-refused-cmyk.psd", "color mode 4")]
	public void OpeningAnUnsupportedPhotoshopFileShowsWhyAndOpensNothing (string fileName, string reason)
	{
		int documentsBefore = PintaCore.Workspace.OpenDocuments.Count;

		bool loaded = PintaCore.Workspace.OpenFile (Asset (fileName));

		Assert.That (loaded, Is.False);
		Assert.That (errors, Has.Count.EqualTo (1));
		Assert.That (errors[0], Does.Contain (reason));
		Assert.That (PintaCore.Workspace.OpenDocuments, Has.Count.EqualTo (documentsBefore));
	}

	[Test]
	public void OpeningALargeDocumentFormatFileIsRefusedAsAnUnsupportedFormat ()
	{
		// .psb is not a registered extension, so Open tries every reader and then lists the
		// formats it does support.
		int documentsBefore = PintaCore.Workspace.OpenDocuments.Count;

		bool loaded = PintaCore.Workspace.OpenFile (Asset ("psd-refused-large-document.psb"));

		Assert.That (loaded, Is.False);
		Assert.That (errors, Is.EqualTo (new[] { "Unsupported file format" }));
		Assert.That (PintaCore.Workspace.OpenDocuments, Has.Count.EqualTo (documentsBefore));
	}

	[Test]
	public void ImportingALargeDocumentFormatFileAsALayerIsRefusedAsAnUnsupportedFormat ()
	{
		// The message is the error dialog's heading: plain, as Open shows it, with each reader's
		// failure left to the details.
		Assert.That (
			() => PintaCore.Actions.Layers.ImportLayerFromFile (Document, Asset ("psd-refused-large-document.psb")),
			Throws.TypeOf<NotSupportedException> ().With.Message.EqualTo ("Unsupported file format"));

		Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (1));
	}

	[TestCase ("psd-refused-16bit.psd")]
	[TestCase ("psd-refused-cmyk.psd")]
	public void ImportingAnUnsupportedPhotoshopFileAsALayerLeavesTheDocumentUntouched (string fileName)
	{
		int historyBefore = Document.History.Pointer;

		Assert.That (
			() => PintaCore.Actions.Layers.ImportLayerFromFile (Document, Asset (fileName)),
			Throws.TypeOf<NotSupportedException> ());

		Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (1));
		Assert.That (Document.History.Pointer, Is.EqualTo (historyBefore));
	}

	// --- Converted ----------------------------------------------------------------------------

	[Test]
	public void BlendModesImpastoLacksOpenAsNormalAndAClippedLayerAsAnOrdinaryLayer ()
	{
		// Photoshop: Layer 0 (half off the canvas), then Group 1 holding Rectangle 1 (Exclusion)
		// and Rectangle 2 (Lighter Color, clipped to Rectangle 1).
		Document document = Open ("psd-converted-clipping-blend-modes.psd");

		Assert.That (
			document.Layers.UserLayers.Select (l => (l.Name, l.BlendMode)),
			Is.EqualTo (new[] {
				("Layer 0", BlendMode.Normal),
				("Rectangle 1", BlendMode.Normal),
				("Rectangle 2", BlendMode.Normal),
			}));
		Assert.That (document.ImageSize, Is.EqualTo (new Size (32, 32)));
	}

	[Test]
	public void AnAdjustmentLayerOpensAsAnEmptyLayerAndTextAsItsPixels ()
	{
		Document document = Open ("psd-converted-adjustment-text.psd");

		Assert.That (document.Layers.UserLayers.Select (l => l.Name), Is.EqualTo (new[] { "a", "Hue/Saturation 1" }));

		UserLayer text = document.Layers.UserLayers[0];
		UserLayer adjustment = document.Layers.UserLayers[1];
		Assert.That (HasInk (text), Is.True, "The text layer's rendered pixels come through.");
		Assert.That (HasInk (adjustment), Is.False, "An adjustment has no pixels of its own to import.");
	}

	[Test]
	public void LayersWithEffectsOpenWithoutTheirEffects ()
	{
		Document document = Open ("psd-converted-layer-effects.psd");

		Assert.That (document.Layers.UserLayers.Select (l => l.Name), Is.EqualTo (new[] { "Background", "Text", "Text 2" }));
		Assert.That (document.Layers.UserLayers.All (HasInk), Is.True);
	}

	[Test]
	public void AGrayscaleDocumentOpensAsGrayRgb ()
	{
		Document document = Open ("psd-converted-grayscale.psd");

		UserLayer layer = document.Layers.UserLayers.Single ();
		ReadOnlySpan<ColorBgra> pixels = layer.Surface.GetReadOnlyPixelData ();
		int inked = 0;
		foreach (ColorBgra pixel in pixels) {
			if (pixel.A == 0)
				continue;
			inked++;
			Assert.That ((pixel.R, pixel.G), Is.EqualTo ((pixel.B, pixel.B)), "Every channel carries the same gray.");
		}
		Assert.That (inked, Is.GreaterThan (0));
	}

	[Test]
	public void AnUnsupportedBlendModeOpensAsNormalKeepingItsOpacity ()
	{
		// A single layer set to Linear Dodge at 50% opacity.
		UserLayer layer = Open ("psd-layer-name-emoji.psd").Layers.UserLayers.Single ();

		Assert.That (layer.BlendMode, Is.EqualTo (BlendMode.Normal));
		Assert.That (layer.Opacity, Is.EqualTo (128 / 255.0).Within (1e-9));
	}

	/// <summary>Opens through File > Open, which must succeed with no error, only the can't-save notice.</summary>
	private Document Open (string fileName)
	{
		Assert.That (PintaCore.Workspace.OpenFile (Asset (fileName)), Is.True);
		Document document = PintaCore.Workspace.ActiveDocument;
		opened.Add (document);

		Assert.That (errors, Is.Empty);
		Assert.That (messages, Is.EqualTo (new[] { "Impasto can open .psd files but cannot save them" }));
		return document;
	}

	private static bool HasInk (UserLayer layer)
	{
		foreach (ColorBgra pixel in layer.Surface.GetReadOnlyPixelData ())
			if (pixel.A != 0)
				return true;
		return false;
	}

	private static Gio.File Asset (string fileName)
		=> Gio.FileHelper.NewForPath (Utilities.GetAssetPath (fileName));
}
