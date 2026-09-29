using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Smooth eraser fades out over its brush. The fade has to stay inside the cursor's circle, and
/// the Feather slider sets how much of the radius it takes up - down to none, a hard edge.
/// </summary>
[TestFixture]
internal sealed class EraserFeatherTest : EraserToolHarness
{
	private const int BrushWidth = 20;
	private const double BrushRadius = BrushWidth / 2.0;
	private const int Centre = CanvasSize / 2;

	/// <summary>Dabs the eraser at the middle of a red canvas, <paramref name="passes"/> swings in one stroke.</summary>
	private void Dab (int feather, int opacity = 100, int passes = 1, string type = "Smooth")
	{
		Configure (type, BrushWidth, opacity, feather);

		Press (Centre, Centre);
		// Scrubbing: the pointer swings one pixel to the side and back, so the dabs overlap without
		// being identical.
		for (int pass = 0; pass < passes; pass++)
			Drag (Centre + pass % 2, Centre);
		Release (Centre, Centre);
	}

	private static double DistanceFromCentre (int x, int y)
		=> Math.Sqrt (Math.Pow (x - Centre, 2) + Math.Pow (y - Centre, 2));

	[TestCase (0)]
	[TestCase (50)]
	[TestCase (100)]
	public void NothingOutsideTheCursorCircleIsErased (int feather)
	{
		Dab (feather);

		for (int y = 0; y < CanvasSize; y++) {
			for (int x = 0; x < CanvasSize; x++) {
				if (DistanceFromCentre (x, y) >= BrushRadius)
					Assert.That (PixelAt (x, y).A, Is.EqualTo (Red.A), $"({x},{y}) is outside the brush");
			}
		}
	}

	[Test]
	public void ZeroFeatherErasesTheWholeCircleWithoutAnyFade ()
	{
		Dab (0);

		for (int y = 0; y < CanvasSize; y++) {
			for (int x = 0; x < CanvasSize; x++) {
				byte expected = DistanceFromCentre (x, y) < BrushRadius ? (byte) 0 : Red.A;
				Assert.That (PixelAt (x, y).A, Is.EqualTo (expected), $"({x},{y})");
			}
		}
	}

	[Test]
	public void FullFeatherFadesTheEdgeAndAnInnerCoreIsGone ()
	{
		Dab (100);

		byte edge = PixelAt (Centre + (int) BrushRadius - 2, Centre).A;

		Assert.That (PixelAt (Centre, Centre).A, Is.EqualTo (0));
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

	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void OpacityLeavesThatShareOfThePixelAtTheCentre (string type)
	{
		Dab (0, opacity: 40, type: type);

		Assert.That (PixelAt (Centre, Centre).A, Is.EqualTo (255 * 60 / 100).Within (2));
	}

	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void ScrubbingTheSameSpotNeverErasesPastTheOpacity (string type)
	{
		Dab (100, opacity: 40, passes: 1, type: type);
		byte once = PixelAt (Centre, Centre).A;

		Fill (Canvas, Red);
		Dab (100, opacity: 40, passes: 25, type: type);

		Assert.That (PixelAt (Centre, Centre).A, Is.EqualTo (once), "a stroke is capped, it does not build up");
	}

	[Test]
	public void FullOpacityStillErasesTheCentreCompletely ()
	{
		Dab (100);
		Assert.That (PixelAt (Centre, Centre).A, Is.EqualTo (0));

		Fill (Canvas, Red);
		Dab (100, type: "Normal");
		Assert.That (PixelAt (Centre, Centre).A, Is.EqualTo (0));
	}
}
