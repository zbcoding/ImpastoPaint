using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A sampling tool (paint bucket, colour picker) that reads a layer's own <see cref="UserLayer.Surface"/>
/// instead of <see cref="UserLayer.CreateVisibleSnapshot"/> sees only the base raster, not the live
/// shape/text objects <see cref="UserLayer.GetLayersToPaint"/> paints on top of it for display — so a
/// click that lands on a shape samples whatever is underneath it instead of the shape's own colour.
/// </summary>
[TestFixture]
internal sealed class LayerVisibleSnapshotTest : DocumentHarness
{
	private static readonly Color ShapeFill = new (0, 0, 1, 1);

	private UserLayer Only => Layer (0);

	[Test]
	public void SnapshotIncludesALiveShapeOverAPlainRaster ()
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Box (ShapeFill, new RectangleI (4, 4, 8, 8)), "Box");

		PointI insideShape = new (6, 6);
		Assert.That (Shown (Only, insideShape).B, Is.EqualTo (255),
			"the scene has to start with the shape on screen, or this test proves nothing");

		using ImageSurface snapshot = Only.CreateVisibleSnapshot ();

		Assert.Multiple (() => {
			Assert.That (snapshot.GetColorBgra (insideShape), Is.EqualTo (Shown (Only, insideShape)),
				"the snapshot has to match what the canvas actually paints for this layer");
			Assert.That (Only.Surface.GetColorBgra (insideShape).B, Is.Not.EqualTo (255),
				"the base raster alone must not already show the shape - otherwise the snapshot isn't proving anything");
		});
	}

	private static bool HasInk (ImageSurface surface, RectangleI region)
	{
		for (int y = region.Top; y <= region.Bottom; ++y)
			for (int x = region.Left; x <= region.Right; ++x)
				if (surface.GetColorBgra (new PointI (x, y)).A != 0)
					return true;
		return false;
	}

	[Test]
	public void SnapshotIncludesLiveTextOverAPlainRaster ()
	{
		// Transparent background (no Fill call) so the text's own ink is what gives a pixel any alpha.
		PointI origin = new (2, 2);
		RectangleI glyphRegion = new (origin.X, origin.Y, 10, 10);
		AddObject (Only, Text ("Hi", origin), "Text");

		Assert.That (HasInk (Only.ObjectLayer.Layer.Surface, glyphRegion), Is.True,
			"the scene has to start with visible text, or this test proves nothing");
		Assert.That (HasInk (Only.Surface, glyphRegion), Is.False,
			"the base raster alone must not already show the text - otherwise the snapshot isn't proving anything");

		using ImageSurface snapshot = Only.CreateVisibleSnapshot ();

		Assert.That (HasInk (snapshot, glyphRegion), Is.True,
			"the snapshot has to include the live text, the same as what the canvas paints for this layer");
	}

	[Test]
	public void SnapshotIsACallerOwnedCopyNotAliasingLiveState ()
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Box (ShapeFill, new RectangleI (4, 4, 8, 8)), "Box");

		using ImageSurface snapshot = Only.CreateVisibleSnapshot ();

		// Mutating the layer afterwards must not reach back into a snapshot already handed to a caller.
		Fill (Only.Surface, Green);

		Assert.That (snapshot.GetColorBgra (new PointI (0, 0)), Is.EqualTo (Red),
			"the snapshot is a point-in-time copy, not a live view of the layer");
	}
}
