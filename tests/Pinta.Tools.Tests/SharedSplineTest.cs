using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The ellipse engine falls back to a cardinal spline whenever its control points no longer fit an
// ellipse (a node dragged off the curve), and that spline has to be the very same curve the
// line-curve engines draw: hit-testing measures a click against these generated points, so an
// ellipse that flattened its fallback differently would accept clicks the freeform shapes reject.
// The two used to compute their tangents in separate copies of the same arithmetic - this pins
// them to one shared implementation.
[TestFixture]
internal sealed class SharedSplineTest : ToolsTestHarness
{
	private static readonly (double x, double y, double tension)[] OffEllipseChain = [
		(3, 4, 0d),
		(11, 2, 0.25d),
		(19, 9, 0.5d),
		(14, 17, 0.75d),
		(4, 14, 1d),
	];

	[Test]
	public void TheEllipseFallbackDrawsTheSameCurveAsAClosedLineSeries ()
	{
		EllipseEngine ellipse = new (
			Layer (0), null, antialiasing: true,
			new Color (1, 0, 0), new Color (0, 1, 0), brushWidth: 2, LineCap.Butt);

		LineCurveSeriesEngine closedSeries = new (
			Layer (0), null, BaseEditEngine.ShapeTypes.ClosedLineCurveSeries,
			antialiasing: true, closed: true,
			new Color (1, 0, 0), new Color (0, 1, 0), brushWidth: 2, LineCap.Butt);

		foreach (ShapeEngine engine in new ShapeEngine[] { ellipse, closedSeries }) {
			engine.ControlPoints = [.. OffEllipseChain.Select (c => new ControlPoint (new PointD (c.x, c.y), c.tension))];
			engine.GeneratePoints (brush_width: 2);
		}

		Assert.That (ellipse.GeneratedPoints.Length, Is.EqualTo (closedSeries.GeneratedPoints.Length),
			"both engines have to sample the curve at the same density");

		Assert.Multiple (() => {
			foreach ((GeneratedPoint fromEllipse, GeneratedPoint fromSeries) in ellipse.GeneratedPoints.Zip (closedSeries.GeneratedPoints)) {
				Assert.That (fromEllipse.Position, Is.EqualTo (fromSeries.Position), "outline point");
				Assert.That (fromEllipse.ControlPointIndex, Is.EqualTo (fromSeries.ControlPointIndex), "owning control point");
			}
		});
	}
}
