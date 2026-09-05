using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class HistogramTest
{
	private static ImageSurface CreateOpaqueSurface (int width, int height)
	{
		Utilities.EnsureNativeLibraries ();
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		using Context g = new (surface);
		g.SetSourceRgba (1, 1, 1, 1);
		g.Paint ();
		return surface;
	}

	[Test]
	public void RegionCoveringWholeSurfaceCountsEveryPixel ()
	{
		using ImageSurface surface = CreateOpaqueSurface (7, 5);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (surface, surface.GetBounds ());

		Assert.That (histogram.GetOccurrences (0, 255), Is.EqualTo (35));
	}

	/// <summary>
	/// A selection is not confined to the canvas - Offset Selection and the move tools
	/// can both push it past the edge - so the histogram has to clip the region it was
	/// handed instead of indexing past the end of the surface.
	/// </summary>
	[Test]
	public void RegionExtendingPastSurfaceCountsOnlyTheOverlap ()
	{
		using ImageSurface surface = CreateOpaqueSurface (7, 5);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (surface, new RectangleI (5, 3, 100, 100));

		Assert.That (histogram.GetOccurrences (0, 255), Is.EqualTo (4));
	}

	[Test]
	public void RegionStartingOutsideSurfaceCountsOnlyTheOverlap ()
	{
		using ImageSurface surface = CreateOpaqueSurface (7, 5);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (surface, new RectangleI (-4, -2, 6, 4));

		Assert.That (histogram.GetOccurrences (0, 255), Is.EqualTo (4));
	}

	[Test]
	public void UpdatingTwiceReplacesTheCountsInsteadOfAccumulating ()
	{
		using ImageSurface surface = CreateOpaqueSurface (7, 5);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (surface, surface.GetBounds ());
		histogram.UpdateHistogram (surface, new RectangleI (0, 0, 2, 2));

		Assert.That (histogram.GetOccurrences (0, 255), Is.EqualTo (4));
	}

	[Test]
	public void RegionFullyOutsideSurfaceCountsNothing ()
	{
		using ImageSurface surface = CreateOpaqueSurface (7, 5);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (surface, new RectangleI (20, 20, 4, 4));

		Assert.That (histogram.GetMax (), Is.EqualTo (0));
	}
}

/// <summary>
/// The Levels dialog measures the current layer over the selection's bounds, which is how an
/// off-canvas selection used to crash it.
/// </summary>
[TestFixture]
internal sealed class HistogramSelectionRegionTest : DocumentHarness
{
	[Test]
	public void OffsetSelectionReachesPastTheCanvas ()
	{
		Document.Selection.Offset (12);

		RectangleI bounds = Document.Selection.GetBounds ().ToInt ();

		Assert.Multiple (() => {
			Assert.That (bounds.Right, Is.GreaterThan (Document.ImageSize.Width - 1));
			Assert.That (bounds.Bottom, Is.GreaterThan (Document.ImageSize.Height - 1));
		});
	}

	[Test]
	public void MeasuringAnOffCanvasSelectionStaysInsideTheLayer ()
	{
		Document.Selection.Offset (12);
		HistogramRgb histogram = new ();

		histogram.UpdateHistogram (
			Document.Layers.CurrentUserLayer.Surface,
			Document.Selection.GetBounds ().ToInt ());

		// The default layer is transparent, so every counted pixel lands in bucket zero.
		Assert.That (
			histogram.GetOccurrences (0, 0),
			Is.EqualTo (Document.ImageSize.Width * Document.ImageSize.Height));
	}
}
