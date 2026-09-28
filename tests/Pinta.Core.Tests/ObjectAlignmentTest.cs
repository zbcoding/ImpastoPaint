using System;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ObjectAlignmentTest
{
	/// <summary>A box of the given width, dragged from x = 0.</summary>
	private static AlignmentLines Caption (double width) => AlignmentLines.OfBox (new (0, 0, width, 10));

	// A line of the moved object other than its box edges - a text baseline - aligns too, and
	// carries the box along by the same amount.
	[Test]
	public void AlignExtent_LandsAnExtraLineOfTheMovedObject_OnATargetLine ()
	{
		// A text box starting at y = 0 whose baseline sits 30 below its top.
		AlignmentLines text = AlignmentLines.OfBox (new (0, 0, 50, 40)) with { YLines = [30] };
		AlignmentLines otherBaseline = AlignmentLines.OfBox (new (100, 90, 50, 60)) with { YLines = [132] };

		var aligned = ObjectAlignment.AlignExtent (
			origin: 100, text, [otherBaseline], horizontal: false, tolerance: 8);

		Assert.That (aligned, Is.EqualTo ((102.0, 132.0)));
	}

	// Placing a caption under an image: a drag that is merely near the image's centre line lands
	// exactly on it, which is what makes centred layouts possible by hand.
	[Test]
	public void AlignExtent_CentresBoxOnAnotherObjectsCentreLine ()
	{
		AlignmentLines image = AlignmentLines.OfBox (new (100, 50, 200, 100)); // centre line at x = 200

		var aligned = ObjectAlignment.AlignExtent (
			origin: 155, Caption (width: 80), [image], horizontal: true, tolerance: 8);

		Assert.That (aligned, Is.EqualTo ((160.0, 200.0)));
	}

	[Test]
	public void AlignExtent_LeavesBoxAlone_WhenNoObjectLineIsWithinTolerance ()
	{
		AlignmentLines image = AlignmentLines.OfBox (new (100, 50, 200, 100));

		var aligned = ObjectAlignment.AlignExtent (
			origin: 20, Caption (width: 40), [image], horizontal: true, tolerance: 8);

		Assert.That (aligned, Is.Null);
	}

	// With several objects in reach, the closest line wins, so the box never jumps past the
	// object the user is actually steering toward.
	[Test]
	public void AlignExtent_PicksTheNearestLine_AcrossObjects ()
	{
		AlignmentLines far = AlignmentLines.OfBox (new (0, 0, 10, 107)); // bottom edge 3 away from the box's top
		AlignmentLines near = AlignmentLines.OfBox (new (0, 0, 10, 105)); // bottom edge 1 away

		var aligned = ObjectAlignment.AlignExtent (
			origin: 104, AlignmentLines.OfBox (new (0, 0, 10, 20)), [far, near], horizontal: false, tolerance: 8);

		Assert.That (aligned, Is.EqualTo ((105.0, 105.0)));
	}

	[Test]
	public void PaintedBounds_IsTheBoxAroundNonTransparentPixels_IncludingItsLastRowAndColumn ()
	{
		const int width = 6, height = 5;
		ColorBgra[] pixels = new ColorBgra[width * height];
		pixels[1 * width + 2] = ColorBgra.Black; // (2, 1)
		pixels[3 * width + 4] = ColorBgra.Black; // (4, 3)

		RectangleI? bounds = ObjectAlignment.PaintedBounds (pixels, width, height);

		Assert.That (bounds, Is.EqualTo (new RectangleI (2, 1, 3, 3)));
	}

	[Test]
	public void PaintedBounds_IsNull_ForAnEmptyLayer ()
	{
		ColorBgra[] pixels = new ColorBgra[4 * 4];

		Assert.That (ObjectAlignment.PaintedBounds (pixels, 4, 4), Is.Null);
	}

	// The guide has to reach the object the box lined up with, or it doesn't show what the box
	// is aligned to.
	[Test]
	public void GuideFor_SpansTheMovedBoxAndTheObjectItAlignedWith ()
	{
		RectangleD moved = new (160, 200, 80, 20);
		AlignmentLines image = AlignmentLines.OfBox (new (100, 50, 200, 100));
		AlignmentLines unrelated = AlignmentLines.OfBox (new (500, 0, 10, 400));

		AlignmentGuide guide = ObjectAlignment.GuideFor (moved, 200, [image, unrelated], vertical: true);

		Assert.That (guide, Is.EqualTo (new AlignmentGuide (true, 200, 50, 220)));
	}
}
