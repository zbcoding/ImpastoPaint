using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The Line/Curve tool's Close-shape toggle flips an open series to closed, which is what draws
// the finish-to-start segment and gives fill modes a closed region. Before it, an open fill
// silently painted a region its outline never showed, and a two-point fill painted nothing at
// all. This pins the two behaviors the toggle relies on: closing actually returns the stroke to
// its start, and the flag survives the engine -> object -> engine round-trip (persist, reload,
// and the dock's sub-row list all read the object, not the live engine).
[TestFixture]
internal sealed class ClosedLineCurveTest : ToolsTestHarness
{
	private static LineCurveSeriesEngine Series (UserLayer layer, bool closed)
	{
		LineCurveSeriesEngine engine = new (
			layer, null, BaseEditEngine.ShapeTypes.OpenLineCurveSeries,
			antialiasing: true, closed,
			new Color (1, 0, 0), new Color (0, 1, 0), brushWidth: 2, LineCap.Butt);
		engine.ControlPoints = [
			new ControlPoint (new PointD (2, 3), 0d),
			new ControlPoint (new PointD (20, 3), 0d),
			new ControlPoint (new PointD (20, 14), 0d),
		];
		engine.GeneratePoints (brush_width: 2);
		return engine;
	}

	[Test]
	public void ClosedSeriesReturnsToItsStartWhileOpenEndsAtItsLastPoint ()
	{
		UserLayer layer = Layer (0);
		LineCurveSeriesEngine open = Series (layer, closed: false);
		LineCurveSeriesEngine closed = Series (layer, closed: true);

		Assert.That (open.GeneratedPoints[^1].Position, Is.EqualTo (new PointD (20, 14)),
			"an open stroke has no finish-to-start segment, so it ends at its last point");
		Assert.That (closed.GeneratedPoints[^1].Position, Is.EqualTo (new PointD (2, 3)),
			"a closed stroke ends where it started, which is the segment the toggle adds");
	}

	[Test]
	public void ClosedSurvivesTheObjectRoundTrip ()
	{
		UserLayer layer = Layer (0);
		ShapeObject stored = Series (layer, closed: true).ToShapeObject ();

		Assert.That (LiveEngine (layer, stored).Closed, Is.True,
			"toggling a line closed has to survive persist and reload, not just the live session");
	}
}
