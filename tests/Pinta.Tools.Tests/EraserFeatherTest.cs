using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Smooth eraser fades out over its brush. The fade has to stay inside the cursor's circle, and
/// the Feather slider sets how much of the radius it takes up - down to none, a hard edge.
/// </summary>
[TestFixture]
internal sealed class EraserFeatherTest : ToolsTestHarness
{
	private const int BrushWidth = 20;
	private const double BrushRadius = BrushWidth / 2.0;

	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private EraserTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	/// <summary>Dabs the eraser at the middle of a red canvas, <paramref name="passes"/> times in one stroke, and returns the surface.</summary>
	private ImageSurface DabWithFeather (int featherPercent, int opacityPercent = 100, int passes = 1, string type = "Smooth")
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();

		EraserTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);
		tool = t;

		typeof (EraserTool).GetField ("eraser_type", NonPublicInstance)!
			.SetValue (t, Enum.Parse (typeof (EraserTool).GetNestedType ("EraserType", NonPublicInstance)!, type));
		((Gtk.SpinButton) typeof (BaseBrushTool).GetProperty ("BrushWidthSpinButton", NonPublicInstance)!.GetValue (t)!)
			.Value = BrushWidth;
		((Gtk.Scale) typeof (EraserTool).GetProperty ("FeatherSlider", NonPublicInstance)!.GetValue (t)!)
			.SetValue (featherPercent);
		((Gtk.Scale) typeof (EraserTool).GetProperty ("OpacitySlider", NonPublicInstance)!.GetValue (t)!)
			.SetValue (opacityPercent);

		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);

		ToolMouseEventArgs e = new () {
			PointDouble = new PointD (CanvasSize / 2, CanvasSize / 2),
			MouseButton = MouseButton.Left,
		};
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, e]);
		// Scrubbing: the pointer swings one pixel to the side and back, so the dabs overlap without
		// being identical.
		for (int pass = 0; pass < passes; pass++) {
			ToolMouseEventArgs swing = new () {
				PointDouble = new PointD (CanvasSize / 2 + pass % 2, CanvasSize / 2),
				MouseButton = MouseButton.Left,
			};
			typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, swing]);
		}
		typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!.Invoke (t, [Document, e]);

		return surface;
	}

	private static double DistanceFromCentre (int x, int y)
		=> Math.Sqrt (Math.Pow (x - CanvasSize / 2, 2) + Math.Pow (y - CanvasSize / 2, 2));

	[TestCase (0)]
	[TestCase (50)]
	[TestCase (100)]
	public void NothingOutsideTheCursorCircleIsErased (int featherPercent)
	{
		ImageSurface surface = DabWithFeather (featherPercent);
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();

		for (int y = 0; y < CanvasSize; y++) {
			for (int x = 0; x < CanvasSize; x++) {
				if (DistanceFromCentre (x, y) >= BrushRadius)
					Assert.That (pixels[y * CanvasSize + x].A, Is.EqualTo (Red.A), $"({x},{y}) is outside the brush");
			}
		}
	}

	[Test]
	public void ZeroFeatherErasesTheWholeCircleWithoutAnyFade ()
	{
		ImageSurface surface = DabWithFeather (0);
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();

		for (int y = 0; y < CanvasSize; y++) {
			for (int x = 0; x < CanvasSize; x++) {
				byte alpha = pixels[y * CanvasSize + x].A;
				byte expected = DistanceFromCentre (x, y) < BrushRadius ? (byte) 0 : Red.A;
				Assert.That (alpha, Is.EqualTo (expected), $"({x},{y})");
			}
		}
	}

	[Test]
	public void FullFeatherFadesTheEdgeAndAnInnerCoreIsGone ()
	{
		ImageSurface surface = DabWithFeather (100);
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();

		byte edge = pixels[CanvasSize / 2 * CanvasSize + CanvasSize / 2 + (int) BrushRadius - 2].A;

		Assert.That (pixels[CanvasSize / 2 * CanvasSize + CanvasSize / 2].A, Is.EqualTo (0));
		Assert.That (edge, Is.GreaterThan (0).And.LessThan (Red.A), "near the rim the erase is partial");
	}

	private static byte CentreAlpha (ImageSurface surface)
		=> surface.GetReadOnlyPixelData ()[CanvasSize / 2 * CanvasSize + CanvasSize / 2].A;

	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void OpacityLeavesThatShareOfThePixelAtTheCentre (string type)
	{
		ImageSurface surface = DabWithFeather (0, opacityPercent: 40, type: type);

		Assert.That (CentreAlpha (surface), Is.EqualTo (255 * 60 / 100).Within (2));
	}

	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void ScrubbingTheSameSpotNeverErasesPastTheOpacity (string type)
	{
		byte once = CentreAlpha (DabWithFeather (100, opacityPercent: 40, passes: 1, type: type));
		byte scrubbed = CentreAlpha (DabWithFeather (100, opacityPercent: 40, passes: 25, type: type));

		Assert.That (scrubbed, Is.EqualTo (once), "a stroke is capped, it does not build up");
	}

	[Test]
	public void FullOpacityStillErasesTheCentreCompletely ()
	{
		Assert.That (CentreAlpha (DabWithFeather (100)), Is.EqualTo (0));
		Assert.That (CentreAlpha (DabWithFeather (100, type: "Normal")), Is.EqualTo (0));
	}

	[Test]
	public void FeatherFadeStartsFurtherInAsTheSliderGrows ()
	{
		Assert.That (EraserTool.SurvivingAlpha (0.6, 0.0), Is.EqualTo (0), "no feather: solid up to the rim");
		Assert.That (EraserTool.SurvivingAlpha (0.6, 1.0), Is.GreaterThan (0), "full feather: already fading at 60%");
		Assert.That (EraserTool.SurvivingAlpha (0.6, 0.2), Is.EqualTo (0), "narrow feather: still solid at 60%");
		Assert.That (EraserTool.SurvivingAlpha (1.0, 0.7), Is.EqualTo (255), "the rim itself is untouched");
	}
}
