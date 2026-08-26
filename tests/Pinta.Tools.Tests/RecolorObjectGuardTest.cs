using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Recolor tool's contract against live objects. Recolor edits pixels, so a stroke that starts
/// on a live shape/text object has to bake that object into the layer first — after the same
/// confirm cut/erase uses. Pre-guard, the stroke ran underneath the object's ink: the canvas never
/// changed, and the object kept the color the user was trying to change.
/// </summary>
[TestFixture]
internal sealed class RecolorObjectGuardTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
	// FromBgra is blue-first: (b, g, r, a).
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	private static readonly ColorBgra Red = ColorBgra.FromBgra (0, 0, 255, 255);

	private RecolorTool? tool;

	[SetUp]
	public void AutoAcceptPrompt ()
		=> ObjectRasterizer.ConfirmPrompt = _ => true;

	[TearDown]
	public void RestorePromptAndTool ()
	{
		ObjectRasterizer.ConfirmPrompt = null;

		if (tool is null)
			return;

		// Same un-faking as PaintBucketObjectRecolorTest: drop subscriptions and the borrowed
		// ToolManager.CurrentTool so later fixtures start clean.
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	private RecolorTool ActivateRecolor ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		RecolorTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	/// <summary>Delivers a full click (press + release) at <paramref name="at"/>.</summary>
	private void Click (RecolorTool recolor, PointI at)
	{
		ToolMouseEventArgs down = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
		};
		typeof (RecolorTool).GetMethod ("OnMouseDown", NonPublicInstance)!
			.Invoke (recolor, [Document, down]);

		ToolMouseEventArgs up = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
		};
		typeof (BaseBrushTool).GetMethod ("OnMouseUp", NonPublicInstance)!
			.Invoke (recolor, [Document, up]);
	}

	// --- Loop A: starting a stroke on a live object bakes it ------------------------------------

	[Test]
	public void ClickingALiveShapeBakesItIntoTheLayer ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Box (new Color (0, 0, 1), new RectangleI (8, 8, 16, 16)), "Box");

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Red),
			"the shape has to be a live object here, not baked pixels");

		Click (ActivateRecolor (), new PointI (12, 12));

		Assert.Multiple (() => {
			Assert.That (layer.Objects, Is.Empty,
				"recolor cannot edit vector ink, so the clicked object had to become pixels");
			Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Blue),
				"the bake lands the shape's own ink into the base raster the tool actually edits");
		});
	}

	// --- Loop B: declining the prompt aborts the click -------------------------------------------

	[Test]
	public void DecliningThePromptLeavesEverythingUntouched ()
	{
		ObjectRasterizer.ConfirmPrompt = _ => false;

		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Box (new Color (0, 0, 1), new RectangleI (8, 8, 16, 16)), "Box");

		Click (ActivateRecolor (), new PointI (12, 12));

		Assert.Multiple (() => {
			Assert.That (layer.Objects, Has.Count.EqualTo (1), "cancel keeps the object editable");
			Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Red),
				"a declined rasterize may not leak any pixels into the ground");
		});
	}

	// --- Loop C: strokes starting on bare ground never prompt -----------------------------------

	[Test]
	public void StrokeStartingOffObjectsDoesNotPrompt ()
	{
		int prompts = 0;
		ObjectRasterizer.ConfirmPrompt = _ => { prompts++; return true; };

		UserLayer layer = Layer (0);
		Fill (layer.Surface, Transparent);
		AddObject (layer, Box (new Color (0, 0, 1), new RectangleI (8, 8, 16, 16)), "Box");

		Click (ActivateRecolor (), new PointI (1, 1));

		Assert.Multiple (() => {
			Assert.That (prompts, Is.Zero,
				"the guard only fires for strokes that start on an object's ink");
			Assert.That (layer.Objects, Has.Count.EqualTo (1),
				"a ground-only stroke must leave every object alone");
		});
	}
}
