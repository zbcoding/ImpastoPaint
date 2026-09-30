using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class BestFitScaleTest
{
	private const int Surround = 2 * DocumentWorkspace.CanvasMargin;

	// Best Fit must never leave scrollbars: the zoomed image plus its surround has to fit on
	// both axes, even when the raw image and viewport aspect ratios are equal or nearly so.
	[TestCase (2000, 1200, 1000, 600)]
	[TestCase (2000, 1190, 1000, 600)]
	[TestCase (1190, 2000, 600, 1000)]
	[TestCase (4000, 1000, 1000, 800)]
	public void ZoomedImageAndSurroundFitTheViewport (int imageWidth, int imageHeight, int viewportWidth, int viewportHeight)
	{
		double scale = DocumentWorkspace.BestFitScale (new Size (imageWidth, imageHeight), new Size (viewportWidth, viewportHeight));

		Assert.That (imageWidth * scale + Surround, Is.LessThanOrEqualTo (viewportWidth + 1e-9));
		Assert.That (imageHeight * scale + Surround, Is.LessThanOrEqualTo (viewportHeight + 1e-9));
	}

	[Test]
	public void LimitingAxisIsFilled ()
	{
		// A wide image is limited by width, so it spans the viewport width minus the surround.
		double scale = DocumentWorkspace.BestFitScale (new Size (4000, 1000), new Size (1000, 800));

		Assert.That (4000 * scale + Surround, Is.EqualTo (1000).Within (1e-9));
	}
}
