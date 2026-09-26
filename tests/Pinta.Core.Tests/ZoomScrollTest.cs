using NUnit.Framework;

namespace Pinta.Core.Tests;

// One axis of the view: a 1000px viewport showing an image zoomed from oldExtent to newExtent.
[TestFixture]
internal sealed class ZoomScrollTest
{
	private const double Page = 1000;

	[Test]
	public void PointerInMiddleStaysOverTheSameImagePoint ()
	{
		// Image fills the view, scrolled to 200; pointer at 400 sits over image pixel 600 (of 1200).
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 400, Page, scroll: 200, oldExtent: 1200, newExtent: 2400);

		// Image pixel 600 is now at 1200 and must still be under the pointer.
		Assert.That (1200 - scroll, Is.EqualTo (400).Within (1e-9));
	}

	[TestCase (950)]
	[TestCase (999)]
	[TestCase (600)]
	public void PointerOnRightHalfKeepsTheVisibleRightEdgeInView (double pointer)
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer, Page, scroll: 0, oldExtent: 1000, newExtent: 2000);

		Assert.That (scroll + Page, Is.EqualTo (2000).Within (1e-9));
	}

	[TestCase (60)]
	[TestCase (400)]
	public void PointerOnLeftHalfKeepsTheVisibleLeftEdgeInView (double pointer)
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer, Page, scroll: 0, oldExtent: 1000, newExtent: 4000);

		Assert.That (scroll, Is.Zero);
	}

	[Test]
	public void EdgeInsideTheViewStaysOnScreenAfterZoomingIn ()
	{
		// A 600px image is centered (200..800); the pointer at 450 is well inside it, left of middle.
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 450, Page, scroll: 0, oldExtent: 600, newExtent: 1200);

		// Pure pointer anchoring would scroll to 50 and cut off the image's left 50px.
		Assert.That (scroll, Is.Zero);
	}

	[Test]
	public void PointerOverSurroundPastTheImageKeepsThatEdgeInView ()
	{
		// A 500px image is centered (250..750); the pointer is in the empty surround at 900.
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 900, Page, scroll: 0, oldExtent: 500, newExtent: 1500);

		Assert.That (scroll + Page, Is.EqualTo (1500).Within (1e-9));
	}

	[Test]
	public void WithBothEdgesOffScreenZoomsAroundThePointer ()
	{
		// Image spans -1000..2000 on screen; the pointer at 300 is over image pixel 1300 (of 3000).
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 300, Page, scroll: 1000, oldExtent: 3000, newExtent: 6000);

		Assert.That (2600 - scroll, Is.EqualTo (300).Within (1e-9));
	}

	[Test]
	public void ZoomingOutKeepsTheImagePointUnderThePointer ()
	{
		// Image spans -1000..2000; the pointer at 300 is over image pixel 1300 (of 3000).
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 300, Page, scroll: 1000, oldExtent: 3000, newExtent: 1500);

		Assert.That (650 - scroll, Is.EqualTo (300).Within (1e-9));
	}

	[Test]
	public void ZoomingOutToFitResetsScroll ()
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 950, Page, scroll: 800, oldExtent: 2000, newExtent: 900);

		Assert.That (scroll, Is.Zero);
	}
}
