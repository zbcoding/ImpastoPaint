using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The magic wand's tolerance slider against the points already clicked. The user's model: the
/// slider tunes the area they can see, so moving it re-floods the clicked points instead of waiting
/// for another click - and one run of the slider is one thing to undo, not one per notch.
/// A slider move may never revive a selection the user has already replaced from elsewhere.
/// </summary>
[TestFixture]
internal sealed class MagicWandLiveToleranceTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// Four vertical bands, 8px wide. Each pair differs only by 20 in one channel: a color distance
	// of 400, which the flood accepts from tolerance 20 upwards and rejects at tolerance 0.
	private const int BandWidth = 8;
	private const int BandArea = BandWidth * CanvasSize;
	private static readonly ColorBgra NearRed = ColorBgra.FromBgra (0, 0, 235, 255);
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	private static readonly ColorBgra NearBlue = ColorBgra.FromBgra (235, 0, 0, 255);

	private MagicWandTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		// Same un-faking as PaintBucketObjectRecolorTest: drop subscriptions and the borrowed
		// ToolManager.CurrentTool so later fixtures start clean.
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	[SetUp]
	public void PaintBands ()
	{
		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);
		PaintBand (surface, 1, NearRed);
		PaintBand (surface, 2, Blue);
		PaintBand (surface, 3, NearBlue);
	}

	private static void PaintBand (ImageSurface surface, int band, ColorBgra color)
	{
		var pixels = surface.GetPixelData ();
		for (int y = 0; y < CanvasSize; ++y)
			for (int x = band * BandWidth; x < (band + 1) * BandWidth; ++x)
				pixels[y * CanvasSize + x] = color;
		surface.MarkDirty ();
	}

	private MagicWandTool ActivateWand ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		MagicWandTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		SetTolerance (t, 0);
		return t;
	}

	private void Click (MagicWandTool wand, PointI at, bool union = false)
	{
		ToolMouseEventArgs e = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
			State = union ? Gdk.ModifierType.ControlMask : 0,
		};
		typeof (FloodTool).GetMethod ("OnMouseDown", NonPublicInstance)!
			.Invoke (wand, [Document, e]);
	}

	private static void SetTolerance (MagicWandTool wand, double value)
		=> Slider (wand).SetValue (value);

	private static Gtk.Scale Slider (MagicWandTool wand)
		=> (Gtk.Scale) typeof (FloodTool).GetProperty ("ToleranceSlider", NonPublicInstance)!.GetValue (wand)!;

	/// <summary>The area the user sees marked, in pixels.</summary>
	private int SelectedArea ()
	{
		using ImageSurface mask = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);
		using (Context g = new (mask)) {
			g.Antialias = Antialias.None;
			g.AppendPath (Document.Selection.SelectionPath);
			g.FillRule = FillRule.EvenOdd;
			g.SetSourceRgb (1, 1, 1);
			g.Fill ();
		}
		mask.MarkDirty ();

		int area = 0;
		foreach (ColorBgra pixel in mask.GetReadOnlyPixelData ())
			if (pixel.A > 0)
				++area;

		return area;
	}

	private int HistoryCount () => Document.History.Items.Count ();

	// --- The feature: the slider retunes what is already selected ---------------------------------

	[Test]
	public void RaisingToleranceGrowsTheClickedRegionWithoutAnotherClick ()
	{
		MagicWandTool wand = ActivateWand ();
		Click (wand, new PointI (4, 16));

		Assert.That (SelectedArea (), Is.EqualTo (BandArea),
			"at tolerance 0 the click may only take its own band, or this test proves nothing");

		SetTolerance (wand, 40);

		Assert.That (SelectedArea (), Is.EqualTo (2 * BandArea),
			"the slider has to pull in the neighbouring band the new tolerance now matches");
	}

	[Test]
	public void LoweringToleranceShrinksTheRegionBackDown ()
	{
		MagicWandTool wand = ActivateWand ();
		SetTolerance (wand, 40);
		Click (wand, new PointI (4, 16));

		Assert.That (SelectedArea (), Is.EqualTo (2 * BandArea),
			"the click has to start wide, or shrinking it proves nothing");

		SetTolerance (wand, 0);

		Assert.That (SelectedArea (), Is.EqualTo (BandArea),
			"dragging the slider back down has to give back the area it had added");
	}

	[Test]
	public void EveryClickedPointFollowsTheSlider ()
	{
		MagicWandTool wand = ActivateWand ();
		Click (wand, new PointI (4, 16));
		Click (wand, new PointI (20, 16), union: true);

		Assert.That (SelectedArea (), Is.EqualTo (2 * BandArea),
			"the union of the two clicks has to be both bands at tolerance 0");

		SetTolerance (wand, 40);

		Assert.That (SelectedArea (), Is.EqualTo (4 * BandArea),
			"both clicked points have to grow, not just the most recent one");
	}

	// --- The slider's cost to the undo stack ------------------------------------------------------

	[Test]
	public void AWholeRunOfTheSliderIsOneUndoStep ()
	{
		MagicWandTool wand = ActivateWand ();
		Click (wand, new PointI (4, 16));
		int afterClick = HistoryCount ();

		SetTolerance (wand, 20);
		SetTolerance (wand, 30);
		SetTolerance (wand, 40);

		Assert.That (HistoryCount (), Is.EqualTo (afterClick + 1),
			"three notches of one slider drag are one adjustment, not three undo steps");

		Document.History.Undo ();

		Assert.That (SelectedArea (), Is.EqualTo (BandArea),
			"undoing the adjustment has to leave the selection the click made");
	}

	// --- The slider may not reach past the selection it made --------------------------------------

	[Test]
	public void TheSliderLeavesASelectionMadeElsewhereAlone ()
	{
		MagicWandTool wand = ActivateWand ();
		Click (wand, new PointI (4, 16));

		// Anything else claiming the selection - Select All, another tool, an undo - ends the wand's
		// claim on it.
		Document.Selection = SelectionOf (new RectangleI (0, 0, 4, 4));
		int afterReplacement = HistoryCount ();

		SetTolerance (wand, 40);

		Assert.Multiple (() => {
			Assert.That (SelectedArea (), Is.EqualTo (16),
				"the slider must not re-flood points the user's new selection has replaced");
			Assert.That (HistoryCount (), Is.EqualTo (afterReplacement),
				"a slider move with nothing of the wand's to retune has nothing to record");
		});
	}
}
