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
	public void PointerNearRightSideKeepsTheImageEdgeInView (double pointer)
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer, Page, scroll: 0, oldExtent: 1000, newExtent: 2000);

		Assert.That (scroll + Page, Is.EqualTo (2000).Within (1e-9));
	}

	[Test]
	public void PointerNearLeftSideKeepsTheImageEdgeInView ()
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 60, Page, scroll: 0, oldExtent: 1000, newExtent: 4000);

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
	public void CenteredImageZoomsAroundThePointerOnScreenNotItsWidgetOffset ()
	{
		// A 700px image is centered (150..850); the pointer at 500 is over its middle.
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 500, Page, scroll: 0, oldExtent: 700, newExtent: 1400);

		// The image middle (700 of 1400) stays under the pointer.
		Assert.That (700 - scroll, Is.EqualTo (500).Within (1e-9));
	}

	[TestCase (0.1)]
	[TestCase (0.25)]
	[TestCase (0.75)]
	[TestCase (0.9)]
	public void AnchorDoesNotJumpAtZoneBoundaries (double fraction)
	{
		double pointer = fraction * Page;
		double below = DocumentWorkspace.ScrollAfterZoom (pointer - 1e-6, Page, scroll: 1000, oldExtent: 3000, newExtent: 6000);
		double above = DocumentWorkspace.ScrollAfterZoom (pointer + 1e-6, Page, scroll: 1000, oldExtent: 3000, newExtent: 6000);

		Assert.That (above, Is.EqualTo (below).Within (1e-3));
	}

	[Test]
	public void ZoomingOutToFitResetsScroll ()
	{
		double scroll = DocumentWorkspace.ScrollAfterZoom (pointer: 950, Page, scroll: 800, oldExtent: 2000, newExtent: 900);

		Assert.That (scroll, Is.Zero);
	}
}
