using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// A layer flip is destructive for pixels but not for live objects: shapes, text and the mask are
/// mirrored along with the raster, so they stay editable. Mirroring is its own inverse, which is what
/// lets the flip's history item undo by flipping again instead of storing every object's old position
/// — these pin that involution.
/// </summary>
[TestFixture]
internal sealed class LayerFlipTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	private static UserLayer LayerWithPixel (PointI point)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 8, 4);
		using (Context g = new (surface)) {
			g.Operator = Operator.Source;
			g.SetSourceRgba (1, 0, 0, 1);
			g.Rectangle (point.X, point.Y, 1, 1);
			g.Fill ();
		}
		surface.MarkDirty ();
		return new UserLayer (surface);
	}

	[Test]
	public void FlipMirrorsTheRasterAndTheMask ()
	{
		UserLayer layer = LayerWithPixel (new PointI (1, 0));
		layer.CreateMask ();
		using (Context g = new (layer.Mask!.Surface)) {
			g.Operator = Operator.Source;
			g.SetSourceRgba (1, 1, 1, 1);
			g.Rectangle (1, 0, 1, 1);
			g.Fill ();
		}
		layer.Mask.Surface.MarkDirty ();

		layer.FlipContents (horizontal: true);

		Assert.That (layer.Surface.GetColorBgra (new PointI (6, 0)).A, Is.EqualTo (255), "the pixel mirrored");
		Assert.That (layer.Surface.GetColorBgra (new PointI (1, 0)).A, Is.EqualTo (0), "and left its old spot");
		Assert.That (layer.Mask!.Surface.GetColorBgra (new PointI (6, 0)).A, Is.EqualTo (255),
			"the mask mirrored with the raster, or it would hide the wrong pixels");
	}

	// The whole reason a flip can be undone by re-flipping: it must land back on the exact pixel.
	[Test]
	public void FlippingTwiceRestoresTheOriginal ()
	{
		foreach (bool horizontal in new[] { true, false }) {
			UserLayer layer = LayerWithPixel (new PointI (1, 0));

			layer.FlipContents (horizontal);
			layer.FlipContents (horizontal);

			Assert.That (layer.Surface.GetColorBgra (new PointI (1, 0)).A, Is.EqualTo (255),
				$"horizontal={horizontal}: the pixel came back");
		}
	}

	// Shapes are mirrored as geometry, not baked to pixels: they stay editable across a flip, and a
	// second flip puts every control point back where it started.
	[Test]
	public void FlipMirrorsShapeControlPointsWithoutRasterizingThem ()
	{
		UserLayer layer = LayerWithPixel (new PointI (0, 0));
		ShapeObject shape = new () { ShapeType = ShapeObjectType.OpenLineCurveSeries };
		shape.ControlPoints.Add (new ShapeControlPoint { Position = new PointD (2, 1) });
		layer.AddShape (shape);

		layer.FlipContents (horizontal: true);

		Assert.That (layer.ShapeObjects, Has.Count.EqualTo (1), "the shape is still a live object");
		Assert.That (layer.ShapeObjects[0].ControlPoints[0].Position.X, Is.EqualTo (6).Within (0.001));

		layer.FlipContents (horizontal: true);
		Assert.That (layer.ShapeObjects[0].ControlPoints[0].Position.X, Is.EqualTo (2).Within (0.001));
	}
}
