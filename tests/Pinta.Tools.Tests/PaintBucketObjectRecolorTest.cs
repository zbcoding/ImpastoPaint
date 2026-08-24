using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The paint bucket's contract against live objects. The user's model (reported as a bug):
/// clicking a shape/text run recolors THAT object's own ink - the fill must not dump into the
/// raster underneath an object any more than it may sample past it (which 05b0028d fixed).
/// These tests go red on exactly that: pre-fix, a click inside a live object paints the base
/// raster beneath it and the visible picture never changes.
/// </summary>
[TestFixture]
internal sealed class PaintBucketObjectRecolorTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
	private static readonly ColorBgra Green = ColorBgra.FromBgra (0, 255, 0, 255);

	private PaintBucketTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		// Same un-faking as TextToolSelectionColorTest: drop subscriptions and the borrowed
		// ToolManager.CurrentTool so later fixtures start clean.
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	private PaintBucketTool ActivateBucket ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		PaintBucketTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private void Click (PaintBucketTool bucket, PointI at)
	{
		ToolMouseEventArgs e = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
		};
		typeof (FloodTool).GetMethod ("OnMouseDown", NonPublicInstance)!
			.Invoke (bucket, [Document, e]);
	}

	// --- Loop B: the classic ground fill must keep working --------------------------------------

	[Test]
	public void ClickingEmptyGroundStillFillsTheRaster ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Transparent);
		PintaCore.Palette.PrimaryColor = new Color (0, 1, 0);

		Click (ActivateBucket (), new PointI (4, 4));

		Assert.That (layer.Surface.GetColorBgra (new PointI (4, 4)), Is.EqualTo (Green),
			"a click on bare ground has to fill the base raster like it always has");
	}

	// --- Loop A: clicking a live shape recolors the shape, not the ground ------------------------

	[Test]
	public void ClickingALiveShapeRecolorsTheShapeInsteadOfTheGround ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Box (new Color (0, 0, 1), new RectangleI (8, 8, 16, 16)), "Box");

		Assert.That (Shown (layer, 12, 12).B, Is.EqualTo (255),
			"the scene has to start with the blue shape on screen, or this test proves nothing");
		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Red),
			"the shape has to be a live object here, not baked pixels");

		PintaCore.Palette.PrimaryColor = new Color (0, 1, 0);
		Click (ActivateBucket (), new PointI (12, 12));

		Assert.Multiple (() => {
			Assert.That (Shown (layer, 12, 12), Is.EqualTo (Green),
				"the user clicked the shape, so the shape the user sees has to turn green");
			Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Red),
				"the ground underneath must not absorb the fill - it was never clicked");
		});
	}

	// --- Loop B: a translucent object may not silently route the fill into the raster ------------

	[Test]
	public void ClickingThroughATranslucentShapeDoesNotPaintTheGroundUnderIt ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Transparent);
		Color translucentBlue = new (0, 0, 1, 0.5);
		AddObject (layer, Box (translucentBlue, new RectangleI (8, 8, 16, 16)), "Box");

		Assert.That (Shown (layer, 12, 12).A, Is.GreaterThan (0),
			"the translucent shape has to contribute visible ink, or this test proves nothing");
		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)).A, Is.Zero,
			"the ground has to start transparent, or the leak below would be invisible");

		PintaCore.Palette.PrimaryColor = new Color (0, 1, 0);
		Click (ActivateBucket (), new PointI (12, 12));

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)).A, Is.Zero,
			"the click landed on the object's ink; the raster underneath it must stay untouched");
	}
}
