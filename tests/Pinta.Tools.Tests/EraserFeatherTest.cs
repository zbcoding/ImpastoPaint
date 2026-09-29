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

	/// <summary>Dabs the Smooth eraser once at the middle of a red canvas and returns the surface.</summary>
	private ImageSurface DabWithFeather (int featherPercent)
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();

		EraserTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);
		tool = t;

		typeof (EraserTool).GetField ("eraser_type", NonPublicInstance)!
			.SetValue (t, Enum.Parse (typeof (EraserTool).GetNestedType ("EraserType", NonPublicInstance)!, "Smooth"));
		((Gtk.SpinButton) typeof (BaseBrushTool).GetProperty ("BrushWidthSpinButton", NonPublicInstance)!.GetValue (t)!)
			.Value = BrushWidth;
		((Gtk.Scale) typeof (EraserTool).GetProperty ("FeatherSlider", NonPublicInstance)!.GetValue (t)!)
			.SetValue (featherPercent);

		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);

		ToolMouseEventArgs e = new () {
			PointDouble = new PointD (CanvasSize / 2, CanvasSize / 2),
			MouseButton = MouseButton.Left,
		};
		typeof (BaseTool).GetMethod ("DoMouseDown", NonPublicInstance)!.Invoke (t, [Document, e]);
		typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!.Invoke (t, [Document, e]);
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

	[Test]
	public void FeatherFadeStartsFurtherInAsTheSliderGrows ()
	{
		Assert.That (EraserTool.SurvivingAlpha (0.6, 0.0), Is.EqualTo (0), "no feather: solid up to the rim");
		Assert.That (EraserTool.SurvivingAlpha (0.6, 1.0), Is.GreaterThan (0), "full feather: already fading at 60%");
		Assert.That (EraserTool.SurvivingAlpha (0.6, 0.2), Is.EqualTo (0), "narrow feather: still solid at 60%");
		Assert.That (EraserTool.SurvivingAlpha (1.0, 0.7), Is.EqualTo (255), "the rim itself is untouched");
	}
}
