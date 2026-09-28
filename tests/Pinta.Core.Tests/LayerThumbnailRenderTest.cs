using Cairo;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// A layer holding objects gets its thumbnail composited rather than borrowed from its raster.
/// That composite has to keep the image's shape: a thumbnail rendered in the row's shape shows a
/// transparent band (or a crop) the image does not have, and the row then draws it as if it did.
/// </summary>
[TestFixture]
internal sealed class LayerThumbnailRenderTest : DocumentHarness
{
	[Test]
	public void ComposedThumbnailKeepsTheImageAspectRatio ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Blue);
		AddObject (layer, Box (new Color (1, 0, 0, 1), new RectangleI (4, 4, 8, 8)), "Box");

		// The canvas is square; the row asks for 3:2, so only a 60x60 render fits without distortion.
		using ImageSurface thumbnail = LayersListViewItem.New (Document, layer).BuildThumbnail (90, 60);

		Assert.That (thumbnail.Width, Is.EqualTo (60));
		Assert.That (thumbnail.Height, Is.EqualTo (60));

		// The layer is opaque edge to edge, so the thumbnail's last row must be too.
		ColorBgra bottom_left = thumbnail.GetColorBgra (new PointI (0, thumbnail.Height - 1));
		Assert.That (bottom_left.A, Is.EqualTo (255));
	}
}
