using System;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>What a stroke of the eraser does to the layer, undo, selections and the secondary colour.</summary>
[TestFixture]
internal sealed class EraserStrokeTest : EraserToolHarness
{
	private static double DistanceToSegment (double x, double y, (double X, double Y) from, (double X, double Y) to)
	{
		double dx = to.X - from.X;
		double dy = to.Y - from.Y;
		double lengthSquared = dx * dx + dy * dy;
		double along = lengthSquared == 0 ? 0 : Math.Clamp (((x - from.X) * dx + (y - from.Y) * dy) / lengthSquared, 0, 1);

		return Math.Sqrt (Math.Pow (x - (from.X + along * dx), 2) + Math.Pow (y - (from.Y + along * dy), 2));
	}

	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void HoveringWithoutAButtonErasesNothing (string type)
	{
		Configure (type);

		Drag (10, 10);
		Drag (20, 20);

		Assert.That (Snapshot (), Is.All.EqualTo (Red));
	}

	/// <summary>
	/// A stroke clears every pixel whose centre is within the brush radius of the line the pointer
	/// travelled - a solid capsule, however far the pointer jumped between two events.
	/// </summary>
	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void AStrokeClearsAnUnbrokenCapsuleAlongItsPath (string type)
	{
		const int width = 6;
		Configure (type, width, feather: 0, antialias: false);
		(double X, double Y) from = (5, 10);
		(double X, double Y) to = (26, 22);

		Stroke (from, to);

		for (int y = 0; y < CanvasSize; y++) {
			for (int x = 0; x < CanvasSize; x++) {
				bool inside = DistanceToSegment (x + 0.5, y + 0.5, (from.X + 0.5, from.Y + 0.5), (to.X + 0.5, to.Y + 0.5)) < width / 2.0;
				Assert.That (PixelAt (x, y).A, Is.EqualTo (inside ? 0 : 255), $"({x},{y})");
			}
		}
	}

	/// <summary>Fast drags deliver few, far-apart events; the result must not depend on how many.</summary>
	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void OneLongMoveErasesWhatManyShortOnesDo (string type)
	{
		Configure (type, 7);
		Stroke ((4, 16), (27, 16));
		ColorBgra[] oneJump = Snapshot ();

		Fill (Canvas, Red);
		Stroke ([.. Enumerable.Range (4, 24).Select (x => ((double) x, 16.0))]);

		Assert.That (Snapshot (), Is.EqualTo (oneJump));
	}

	[TestCase (1, true)]
	[TestCase (1, false)]
	public void AOnePixelBrushErasesExactlyOnePixel (int width, bool antialias)
	{
		Configure ("Normal", width, antialias: antialias);

		Stroke ((16, 16));

		Assert.That (Snapshot ().Count (p => !p.Equals (Red)), Is.EqualTo (1));
		Assert.That (PixelAt (16, 16), Is.EqualTo (Transparent));
	}

	[Test]
	public void WithoutAntialiasingTheEdgeIsEitherFullyErasedOrUntouched ()
	{
		Configure ("Normal", 7, antialias: false);

		Stroke ((6, 6), (24, 20));

		Assert.That (Snapshot ().Select (p => p.A).Distinct ().Order (), Is.EqualTo (new byte[] { 0, 255 }));
	}

	[Test]
	public void WithAntialiasingTheEdgeIsPartlyErased ()
	{
		Configure ("Normal", 7, antialias: true);

		Stroke ((6, 6), (24, 20));

		Assert.That (Snapshot ().Any (p => p.A is > 0 and < 255), Is.True);
	}

	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void ErasedPixelsAreFullyTransparentNotJustZeroAlpha (string type)
	{
		Configure (type, 10, feather: 0);

		Stroke ((16, 16));

		Assert.That (PixelAt (16, 16), Is.EqualTo (Transparent), "colour left behind would show when blended");
	}

	[Test]
	public void RightClickErasesToTheSecondaryColour ()
	{
		PintaCore.Palette.SecondaryColor = new Color (0, 0, 1, 1);
		Configure ("Normal", 8);

		Stroke (MouseButton.Right, (16, 16));

		Assert.That (PixelAt (16, 16), Is.EqualTo (ColorBgra.FromBgra (255, 0, 0, 255)));
	}

	[Test]
	public void RightClickBlendsTowardTheSecondaryColourByTheOpacity ()
	{
		PintaCore.Palette.SecondaryColor = new Color (0, 0, 1, 1);
		Configure ("Normal", 8, opacity: 50);

		Stroke (MouseButton.Right, (16, 16));

		ColorBgra blended = PixelAt (16, 16);
		Assert.That (blended.R, Is.EqualTo (127).Within (2), "half the red is left");
		Assert.That (blended.B, Is.EqualTo (127).Within (2), "half the blue has arrived");
		Assert.That (blended.A, Is.EqualTo (255));
	}

	[Test]
	public void OpacityErasesAShareOfWhatWasAlreadyPartlyTransparent ()
	{
		Fill (Canvas, ColorBgra.FromBgra (0, 0, 128, 128));
		Configure ("Normal", 8, opacity: 50);

		Stroke ((16, 16));

		Assert.That (PixelAt (16, 16).A, Is.EqualTo (64).Within (2));
	}

	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void ASecondStrokeErasesFurtherBecauseTheCapIsPerStroke (string type)
	{
		Configure (type, 8, opacity: 50, feather: 0);

		Stroke ((16, 16));
		byte afterOne = PixelAt (16, 16).A;
		Stroke ((16, 16));

		Assert.That (afterOne, Is.EqualTo (128).Within (2));
		Assert.That (PixelAt (16, 16).A, Is.EqualTo (64).Within (2));
	}

	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void TheSelectionLimitsWhereTheEraserWorks (string type)
	{
		Configure (type, 6, feather: 0);
		Document.Selection = SelectionOf (new RectangleI (0, 0, CanvasSize / 2, CanvasSize));
		Document.Selection.Visible = true;

		Stroke ((4, 16), (27, 16));

		Assert.That (PixelAt (8, 16).A, Is.EqualTo (0), "inside the selection");
		Assert.That (PixelAt (24, 16).A, Is.EqualTo (255), "outside the selection");
	}

	[TestCase ("Normal")]
	[TestCase ("Smooth")]
	public void UndoRestoresTheLayerAsTheStrokeFoundIt (string type)
	{
		Configure (type, 10);
		int before = Document.History.Pointer;

		Stroke ((8, 8), (24, 24));

		Assert.That (Document.History.Pointer, Is.EqualTo (before + 1), "one stroke is one history step");
		Assert.That (Snapshot ().Any (p => !p.Equals (Red)), Is.True);

		Document.History.Undo ();

		Assert.That (Snapshot (), Is.All.EqualTo (Red));
	}

	[Test]
	public void AStrokeEntirelyOffTheCanvasLeavesNoHistoryStep ()
	{
		Configure ("Normal", 4);
		int before = Document.History.Pointer;

		Stroke ((-60, -60), (-50, -50));

		Assert.That (Document.History.Pointer, Is.EqualTo (before));
	}

	[Test]
	public void AWideBrushDraggedAlongTheOutsideOfTheCanvasStillCountsAsAnEdit ()
	{
		Configure ("Normal", 20);
		int before = Document.History.Pointer;

		// The pointer never touches the canvas, but the brush reaches 10px onto it.
		Stroke ((-4, 8), (-4, 24));

		Assert.That (Document.History.Pointer, Is.EqualTo (before + 1));
		Assert.That (PixelAt (0, 16).A, Is.EqualTo (0));
	}
}
