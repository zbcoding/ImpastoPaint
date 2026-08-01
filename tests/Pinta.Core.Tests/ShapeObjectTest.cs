using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ShapeObjectTest
{
	[Test]
	public void ClonePreservesEditableGeometryAndStyle ()
	{
		ShapeObject source = new () {
			ShapeType = ShapeObjectType.RoundedLineSeries,
			AntiAliasing = false,
			OutlineColor = new Color (0.1, 0.2, 0.3, 1),
			FillColor = new Color (0.7, 0.6, 0.5, 1),
			BrushWidth = 7,
			LineCap = LineCap.Round,
			DashPattern = "- .",
			DashSpacing = 4,
			FillStyle = 2,
			RoundedRadius = 23,
			TriangleType = 1,
		};
		source.ControlPoints.Add (new ShapeControlPoint { Position = new PointD (12, 34), Tension = 0.25 });
		source.Arrow1.Show = true;
		source.Arrow1.Size = 18;

		ShapeObject clone = source.Clone ();
		clone.ControlPoints[0].Position = new PointD (99, 100);
		clone.Arrow1.Size = 2;

		Assert.Multiple (() => {
			Assert.That (clone.ShapeType, Is.EqualTo (source.ShapeType));
			Assert.That (clone.FillStyle, Is.EqualTo (2));
			Assert.That (clone.RoundedRadius, Is.EqualTo (23));
			Assert.That (clone.TriangleType, Is.EqualTo (1));
			Assert.That (clone.ControlPoints[0].Tension, Is.EqualTo (0.25));
			Assert.That (source.ControlPoints[0].Position, Is.EqualTo (new PointD (12, 34)));
			Assert.That (source.Arrow1.Size, Is.EqualTo (18));
		});
	}
}
