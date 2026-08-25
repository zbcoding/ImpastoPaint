using System;
using System.Collections.Generic;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Geometry generators shared by the shape engines (currently the ellipse's cardinal-spline
/// approximation and the line-curve series' per-segment splines). Both flatten curves to
/// polylines for hit-testing and rasterization, so the flattening itself lives in one place;
/// the tInterval values are shared deliberately — hit-testing compares clicks against these
/// generated points, so a sparser curve in one engine would shift what counts as "on" its edge.
/// </summary>
public static class CurveGeneration
{
	//Note: this must be low enough for mouse clicks to be properly considered on/off the curve at any given point.
	private const double DefaultTInterval = .025d;

	private const double PartialEllipseTInterval = .02d;

	/// <summary>
	/// Generate each point in a cubic Bezier curve given the end points and control points,
	/// from t = 0 up to (but not including) t = 1 — the caller supplies the segment's end
	/// point separately when it needs it (the full-curve callers pass the next segment's
	/// start; the partial-ellipse arcs chain four of these together).
	/// </summary>
	/// <param name="tInterval">The increment value for t. The default matches the line-curve
	/// engines' density; the partial-ellipse arcs use their historical sparser interval.</param>
	/// <param name="p0">The first end point that the curve passes through.</param>
	/// <param name="p1">The first control point that the curve does not necessarily pass through.</param>
	/// <param name="p2">The second control point that the curve does not necessarily pass through.</param>
	/// <param name="p3">The second end point that the curve passes through.</param>
	/// <param name="cPIndex">The index of the previous ControlPoint to the generated points.</param>
	public static IEnumerable<GeneratedPoint> CubicBezier (
		double tInterval,
		PointD p0,
		PointD p1,
		PointD p2,
		PointD p3,
		int cPIndex)
	{
		for (double t = 0d; t < 1d; t += tInterval) {
			//There are 3 "layers" in a cubic Bezier curve's calculation. These "layers"
			//must be calculated for each intermediate Point (for each value of t from
			//tInterval to 1d). The Points in each "layer" store [the distance between
			//two consecutive Points from the previous "layer" multiplied by the value
			//of t (which is between 0d-1d)] plus [the position of the first Point of
			//the two consecutive Points from the previous "layer"]. This must be
			//calculated for the X and Y of every consecutive Point in every layer
			//until the last Point possible is reached, which is the Point on the curve.

			//Note: the code below is an optimized version of the commented explanation above.

			double oneMinusT = 1d - t;
			double oneMinusTSquared = oneMinusT * oneMinusT;
			double oneMinusTCubed = oneMinusTSquared * oneMinusT;

			double tSquared = t * t;
			double tCubed = tSquared * t;

			double oneMinusTSquaredTimesTTimesThree = oneMinusTSquared * t * 3d;
			double oneMinusTTimesTSquaredTimesThree = oneMinusT * tSquared * 3d;

			//Resulting Point = (1 - t) ^ 3 * p0 + 3 * (1 - t) ^ 2 * t * p1 + 3 * (1 - t) * t ^ 2 * p2 + t ^ 3 * p3
			//This is done for both the X and Y given a value t going from 0d to 1d at a very small interval
			//and given 4 points p0, p1, p2, and p3, where p0 and p3 are end points and p1 and p2 are control points.

			yield return new (
				new PointD (
					X: oneMinusTCubed * p0.X + oneMinusTSquaredTimesTTimesThree * p1.X + oneMinusTTimesTSquaredTimesThree * p2.X + tCubed * p3.X,
					Y: oneMinusTCubed * p0.Y + oneMinusTSquaredTimesTTimesThree * p1.Y + oneMinusTTimesTSquaredTimesThree * p2.Y + tCubed * p3.Y),
				cPIndex);
		}
	}

	/// <summary>
	/// The tangent at each control point of a cardinal spline: the point's own tension scaled by
	/// the vector across its two neighbours. A closed chain wraps at both ends; an open one leans
	/// each end on its single neighbour. Middle tangents additionally fade in with the point's
	/// position along the chain, which is what gives a freeform curve its slack near the start.
	/// </summary>
	private static List<PointD> CardinalSplineTangents (IReadOnlyList<ControlPoint> controlPoints, bool closed)
	{
		int last = controlPoints.Count - 1;
		double lastAsDouble = last;

		List<PointD> tangents = new (controlPoints.Count);

		PointD beforeFirst = (closed ? controlPoints[last] : controlPoints[0]).Position;
		tangents.Add (new PointD (
			controlPoints[0].Tension * (controlPoints[1].Position.X - beforeFirst.X),
			controlPoints[0].Tension * (controlPoints[1].Position.Y - beforeFirst.Y)));

		for (int i = 1; i < last; ++i) {
			double tensionForPoint = controlPoints[i].Tension * i / lastAsDouble;
			tangents.Add (new PointD (
				tensionForPoint * (controlPoints[i + 1].Position.X - controlPoints[i - 1].Position.X),
				tensionForPoint * (controlPoints[i + 1].Position.Y - controlPoints[i - 1].Position.Y)));
		}

		PointD afterLast = (closed ? controlPoints[0] : controlPoints[last]).Position;
		tangents.Add (new PointD (
			controlPoints[last].Tension * (afterLast.X - controlPoints[last - 1].Position.X),
			controlPoints[last].Tension * (afterLast.Y - controlPoints[last - 1].Position.Y)));

		return tangents;
	}

	/// <summary>
	/// The whole cardinal spline through <paramref name="controlPoints"/>, flattened to generated
	/// points: one cubic Bezier per segment, the tangents above as its control points, plus the
	/// wrap-around segment when the shape is closed. Shared by the line-curve engines and the
	/// segmented ellipse, which must stay pixel-identical to each other - a click counts as on a
	/// shape's edge by its distance to these points.
	/// </summary>
	public static IEnumerable<GeneratedPoint> CardinalSpline (IReadOnlyList<ControlPoint> controlPoints, bool closed)
	{
		List<PointD> tangents = CardinalSplineTangents (controlPoints, closed);

		for (int i = 1; i < controlPoints.Count; ++i)
			foreach (GeneratedPoint p in Segment (controlPoints, tangents, i - 1, i, i))
				yield return p;

		if (!closed)
			yield break;

		foreach (GeneratedPoint p in Segment (controlPoints, tangents, controlPoints.Count - 1, 0, 0))
			yield return p;
	}

	/// <summary>One segment of the spline: out along the start point's tangent, in along the end
	/// point's.</summary>
	private static IEnumerable<GeneratedPoint> Segment (
		IReadOnlyList<ControlPoint> controlPoints,
		IReadOnlyList<PointD> tangents,
		int from,
		int to,
		int cPIndex)
		=> CubicBezierSegment (
			controlPoints[from].Position,
			new PointD (
				controlPoints[from].Position.X + tangents[from].X,
				controlPoints[from].Position.Y + tangents[from].Y),
			new PointD (
				controlPoints[to].Position.X - tangents[to].X,
				controlPoints[to].Position.Y - tangents[to].Y),
			controlPoints[to].Position,
			cPIndex);

	/// <summary>Full-segment convenience overload: same sampling as the line-curve engines have
	/// always used, including the final point at t = 1.</summary>
	public static IEnumerable<GeneratedPoint> CubicBezierSegment (
		PointD p0,
		PointD p1,
		PointD p2,
		PointD p3,
		int cPIndex)
	{
		//t will go from 0d to 1d inclusive at the interval of DefaultTInterval.
		for (double t = 0d; t < 1d + DefaultTInterval; t += DefaultTInterval)
			yield return EachCubicBezierPoint (DefaultTInterval, t, p0, p1, p2, p3, cPIndex);
	}

	private static GeneratedPoint EachCubicBezierPoint (
		double tInterval,
		double t,
		PointD p0,
		PointD p1,
		PointD p2,
		PointD p3,
		int cPIndex)
	{
		// Clamp so the inclusive loop above can only produce t = 1 exactly, never past it.
		t = Math.Min (t, 1d);

		double oneMinusT = 1d - t;
		double oneMinusTSquared = oneMinusT * oneMinusT;
		double oneMinusTCubed = oneMinusTSquared * oneMinusT;

		double tSquared = t * t;
		double tCubed = tSquared * t;

		double oneMinusTSquaredTimesTTimesThree = oneMinusTSquared * t * 3d;
		double oneMinusTTimesTSquaredTimesThree = oneMinusT * tSquared * 3d;

		return new (
			new PointD (
				X: oneMinusTCubed * p0.X + oneMinusTSquaredTimesTTimesThree * p1.X + oneMinusTTimesTSquaredTimesThree * p2.X + tCubed * p3.X,
				Y: oneMinusTCubed * p0.Y + oneMinusTSquaredTimesTTimesThree * p1.Y + oneMinusTTimesTSquaredTimesThree * p2.Y + tCubed * p3.Y),
			cPIndex);
	}
}
