//
// EllipseEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class EllipseEngine : ShapeEngine
{
	// Partial-arc state: when extra nodes are added, keep original ellipse geometry
	// so spans whose points still sit on the original ellipse stay true elliptical.
	private bool isPartial = false;
	private PointD partialCenter;
	private double partialRx;
	private double partialRy;

	// A control point within this distance of its parametric position counts as "still on the ellipse".
	private const double OnEllipseEps = 1.5;

	public bool IsPartialEllipse => isPartial;

	public bool TryGetPartialGeometry (out PointD center, out double r_x, out double r_y)
	{
		center = partialCenter;
		r_x = partialRx;
		r_y = partialRy;
		return isPartial && r_x > 0 && r_y > 0;
	}

	/// <summary>
	/// Create a new EllipseEngine.
	/// </summary>
	/// <param name="parentLayer">The parent UserLayer for the re-editable DrawingLayer.</param>
	/// <param name="drawingLayer">An existing ReEditableLayer to reuse. This is for cloning only. If not cloning, pass in null.</param>
	/// <param name="antialiasing">Whether or not antialiasing is enabled.</param>
	/// <param name="outlineColor">The outline color for the shape.</param>
	/// <param name="fillColor">The fill color for the shape.</param>
	/// <param name="brushWidth">The width of the outline of the shape.</param>
	/// <param name="lineCap">Defines the edge of the line drawn.</param>
	public EllipseEngine (
		UserLayer parentLayer,
		ReEditableLayer? drawingLayer,
		bool antialiasing,
		Color outlineColor,
		Color fillColor,
		int brushWidth,
		LineCap lineCap)
	: base (
		parentLayer,
		drawingLayer,
		BaseEditEngine.ShapeTypes.Ellipse,
		antialiasing,
		true,
		outlineColor,
		fillColor,
		brushWidth,
		lineCap)
	{ }

	private EllipseEngine (EllipseEngine src)
		: base (src)
	{
		isPartial = src.isPartial;
		partialCenter = src.partialCenter;
		partialRx = src.partialRx;
		partialRy = src.partialRy;
	}

	public override ShapeEngine Clone ()
	{
		return new EllipseEngine (this);
	}

	public static bool IsPerfectRectangle (
		PointD cp0,
		PointD cp1,
		PointD cp2,
		PointD cp3)
	{
		if (cp0.X == cp1.X) {
			if (cp0.Y == cp3.Y && cp1.Y == cp2.Y && cp2.X == cp3.X) {
				return true;
			}
		} else if (cp0.Y == cp1.Y) {
			if (cp0.X == cp3.X && cp1.X == cp2.X && cp2.Y == cp3.Y) {
				return true;
			}
		}
		return false;
	}

	public bool TryGetEllipseGeometry (
		out PointD topLeft,
		out PointD bottomRight,
		out PointD center,
		out double r_x,
		out double r_y)
	{
		topLeft = default;
		bottomRight = default;
		center = default;
		r_x = r_y = 0;

		if (ControlPoints.Count != 4)
			return false;

		PointD cp0 = ControlPoints[0].Position;
		PointD cp1 = ControlPoints[1].Position;
		PointD cp2 = ControlPoints[2].Position;
		PointD cp3 = ControlPoints[3].Position;

		if (!IsPerfectRectangle (cp0, cp1, cp2, cp3))
			return false;

		topLeft = cp0;
		bottomRight = cp0;

		if (cp1.X < topLeft.X || cp1.Y < topLeft.Y) {
			topLeft = cp1;
			if (cp2.X < topLeft.X || cp2.Y < topLeft.Y) {
				topLeft = cp2;
			} else {
				bottomRight = cp3;
			}
		} else {
			PointD secondPoint = cp1;
			if (cp2.X < secondPoint.X || cp2.Y < secondPoint.Y) {
				topLeft = cp3;
				bottomRight = cp1;
			} else {
				bottomRight = cp2;
			}
		}

		double width = bottomRight.X - topLeft.X;
		double height = bottomRight.Y - topLeft.Y;
		r_x = width / 2d;
		r_y = height / 2d;
		center = new PointD (topLeft.X + r_x, topLeft.Y + r_y);

		return r_x > 0 && r_y > 0;
	}

	/// <summary>
	/// Partial-arc conversion: when adding the first extra node to a perfect-rect ellipse,
	/// replace the rectangle points with 4 anchors on the ellipse (right, bottom, left, top)
	/// plus the new node, inserted by angular position. Spans whose endpoints still sit on
	/// the original ellipse keep rendering as true elliptical arcs; only spans touching a
	/// moved point become editable curves. Returns inserted index in ControlPoints.
	/// </summary>
	public int ConvertToSegmentedEllipseAndInsert (PointD insertPos, double defaultTension)
	{
		// If already partial, just insert by angle into existing sorted list.
		if (isPartial && partialRx > 0 && partialRy > 0) {
			return InsertIntoPartialEllipse (insertPos, defaultTension);
		}

		if (!TryGetEllipseGeometry (out _, out _, out PointD c, out double r_x, out double r_y))
			return -1;

		// Store original geometry for partial-arc preservation.
		isPartial = true;
		partialCenter = c;
		partialRx = r_x;
		partialRy = r_y;

		// 4 anchors on the ellipse (angle order, screen Y+ down: right 0°, bottom 90°, left 180°, top 270°)
		List<ControlPoint> all = [
			new (new PointD (c.X + r_x, c.Y), defaultTension),
			new (new PointD (c.X, c.Y + r_y), defaultTension),
			new (new PointD (c.X - r_x, c.Y), defaultTension),
			new (new PointD (c.X, c.Y - r_y), defaultTension),
		];

		int insertIdx = GetInsertionIndexByAngle (all, c, r_x, r_y, insertPos);
		all.Insert (insertIdx, new ControlPoint (new PointD (insertPos.X, insertPos.Y), defaultTension));

		ControlPoints.Clear ();
		foreach (var cp in all)
			ControlPoints.Add (cp);

		return insertIdx;
	}

	private int InsertIntoPartialEllipse (PointD insertPos, double defaultTension)
	{
		PointD c = partialCenter;
		double r_x = partialRx;
		double r_y = partialRy;

		// Existing points are already sorted by angle? Keep them sorted for generation.
		// For insertion, find angular position.
		int idx = GetInsertionIndexByAngle (ControlPoints, c, r_x, r_y, insertPos);
		ControlPoints.Insert (idx, new ControlPoint (new PointD (insertPos.X, insertPos.Y), defaultTension));
		return idx;
	}

	private static int GetInsertionIndexByAngle (
		IList<ControlPoint> existing,
		PointD center,
		double r_x,
		double r_y,
		PointD insertPos)
	{
		double AngleOf (PointD p)
		{
			double dx = r_x == 0 ? 0 : (p.X - center.X) / r_x;
			double dy = r_y == 0 ? 0 : (p.Y - center.Y) / r_y;
			double ang = Math.Atan2 (dy, dx);
			if (ang < 0) ang += 2 * Math.PI;
			return ang;
		}

		double insertAng = AngleOf (insertPos);

		// Build list of (angle, index) sorted.
		var sorted = existing
			.Select ((cp, i) => (ang: AngleOf (cp.Position), idx: i))
			.OrderBy (t => t.ang)
			.ToList ();

		// Find first with angle greater than insertAng - insert before it.
		for (int i = 0; i < sorted.Count; ++i) {
			if (insertAng < sorted[i].ang) {
				return sorted[i].idx;
			}
		}
		// Wrap around: insert at position of first (smallest angle) -> append at end = before first circularly.
		// For simplicity return count (append) which is after last; sorted order will be maintained on next generation
		// because we sort again there. Returning count keeps it at end, which is after last angle, i.e., before first.
		return existing.Count;
	}

	/// <summary>
	/// Generate each point in an elliptic shape and store the result in GeneratedPoints.
	/// <param name="brush_width">The width of the brush that will be used to draw the shape.</param>
	/// </summary>
	public override void GeneratePoints (int brush_width)
	{
		if (isPartial && partialRx > 0 && partialRy > 0) {
			GeneratedPoints = [.. GeneratePartialEllipse ()];
			return;
		}

		var points = CreatePoints ().ToImmutableArray ();
		var fallbackPoints = CreateFallbackPoints (points);
		GeneratedPoints = [.. points, .. fallbackPoints];
	}

	private IEnumerable<GeneratedPoint> GeneratePartialEllipse ()
	{
		PointD c = partialCenter;
		double r_x = partialRx;
		double r_y = partialRy;
		int n = ControlPoints.Count;

		if (n < 3)
			yield break;

		double AngleOf (PointD p)
		{
			double ang = Math.Atan2 ((p.Y - c.Y) / r_y, (p.X - c.X) / r_x);
			return ang < 0 ? ang + 2 * Math.PI : ang;
		}

		// Per-point: angle, whether still on the original ellipse, and unit tangent direction.
		// One uniform Bezier pass over the whole ring (list order = angular order) means there is
		// no seam between "true arc" and "smooth curve" spans, and a moved point only bends the
		// two segments it touches.
		var angles = new double[n];
		var onEllipse = new bool[n];
		var dir = new PointD[n];

		for (int i = 0; i < n; ++i) {
			PointD p = ControlPoints[i].Position;
			angles[i] = AngleOf (p);
			PointD parametric = new (c.X + r_x * Math.Cos (angles[i]), c.Y + r_y * Math.Sin (angles[i]));
			onEllipse[i] = p.DistanceSquared (parametric) <= OnEllipseEps * OnEllipseEps;
		}

		// A point near the outline only counts as on-ellipse if its angle is still between its
		// ring neighbors' angles. Otherwise a point dragged across the outline elsewhere would
		// flip to on-ellipse with an angle inconsistent with list order, and the arc handle
		// length below (tan (Δ/4) with Δ→2π) would explode asymptotically.
		const double twoPi = 2 * Math.PI;
		for (int i = 0; i < n; ++i) {
			if (!onEllipse[i])
				continue;
			double aPrev = angles[(i + n - 1) % n];
			double aNext = angles[(i + 1) % n];
			double span = (aNext - aPrev + twoPi) % twoPi;
			double part = (angles[i] - aPrev + twoPi) % twoPi;
			if (part > span)
				onEllipse[i] = false;
		}

		for (int i = 0; i < n; ++i) {
			PointD d;
			if (onEllipse[i]) {
				// True ellipse tangent (increasing-angle direction) → untouched spans stay elliptical
				// and spans next to a moved point blend into the ellipse without a kink.
				d = new PointD (-r_x * Math.Sin (angles[i]), r_y * Math.Cos (angles[i]));
			} else {
				// Moved point: Catmull-Rom direction from its immediate neighbors only.
				PointD prev = ControlPoints[(i + n - 1) % n].Position;
				PointD next = ControlPoints[(i + 1) % n].Position;
				d = new PointD (next.X - prev.X, next.Y - prev.Y);
			}
			double len = Math.Sqrt (d.X * d.X + d.Y * d.Y);
			dir[i] = len > 1e-9 ? new PointD (d.X / len, d.Y / len) : new PointD (1, 0);
		}

		for (int i = 0; i < n; ++i) {
			int j = (i + 1) % n;
			PointD p0 = ControlPoints[i].Position;
			PointD p1 = ControlPoints[j].Position;

		double a0 = angles[i];
			double a1 = angles[j];
			if (a1 <= a0) a1 += twoPi;

			double h0, h1;
			if (onEllipse[i] && onEllipse[j] && a1 - a0 <= Math.PI) {
				// Standard elliptical-arc Bezier approximation: handle length (4/3)·tan(Δ/4)·|derivative|.
				// Only trusted for spans ≤ π; beyond that tan (Δ/4) grows without bound.
				double alpha = 4.0 / 3.0 * Math.Tan ((a1 - a0) / 4.0);
				h0 = alpha * Math.Sqrt (r_x * Math.Sin (a0) * (r_x * Math.Sin (a0)) + r_y * Math.Cos (a0) * (r_y * Math.Cos (a0)));
				h1 = alpha * Math.Sqrt (r_x * Math.Sin (a1) * (r_x * Math.Sin (a1)) + r_y * Math.Cos (a1) * (r_y * Math.Cos (a1)));
			} else {
				// Chord-proportional handles keep a moved point's influence local to its own segments.
				double chord = p0.Distance (p1);
				h0 = ControlPoints[i].Tension * chord;
				h1 = ControlPoints[j].Tension * chord;
			}

			// Tag with the segment's END index ("insert before" convention, same as
			// LineCurveSeriesEngine): clicking this segment inserts the new node between i and j.
			foreach (var gp in GenerateCubicBezierCurvePoints (
				p0,
				new PointD (p0.X + dir[i].X * h0, p0.Y + dir[i].Y * h0),
				new PointD (p1.X - dir[j].X * h1, p1.Y - dir[j].Y * h1),
				p1,
				j)) {
				yield return gp;
			}
		}
	}

	private IEnumerable<GeneratedPoint> CreateFallbackPoints (ImmutableArray<GeneratedPoint> points)
	{
		//Make sure there are now generated points; otherwise, one of the ellipse conditions was not met.
		if (points.Length != 0)
			yield break;

		// Original Pinta fell back to a straight-line polygon, which turned a 5-point ellipse
		// into a 5-sided rectangle. Keep the elliptical look by converting to a smooth closed
		// curve through all control points (tension-based cardinal spline, same as freeform shapes).
		foreach (var gp in CreateSmoothClosedCurve ())
			yield return gp;
	}

	private IEnumerable<GeneratedPoint> CreateSmoothClosedCurve ()
	{
		if (ControlPoints.Count < 2)
			yield break;

		if (ControlPoints.Count == 2) {
			// Two points: just lerp, same as before but keeps hit-testing dense.
			for (int currentNum = 0; currentNum < 2; ++currentNum) {
				int nextNum = (currentNum + 1) % 2;
				PointD cur = ControlPoints[currentNum].Position;
				PointD nxt = ControlPoints[nextNum].Position;
				for (float t = 0f; t < 1f; t += 0.01f)
					yield return new GeneratedPoint (Utility.Lerp (cur, nxt, t), currentNum);
			}
			yield break;
		}

		// Closed cardinal spline using per-point tension (mirrors LineCurveSeriesEngine).
		int pointCount = ControlPoints.Count;
		List<PointD> tangents = new (pointCount);

		// First tangent (closed: wraps to last)
		tangents.Add (new PointD (
			ControlPoints[0].Tension * (ControlPoints[1].Position.X - ControlPoints[pointCount - 1].Position.X),
			ControlPoints[0].Tension * (ControlPoints[1].Position.Y - ControlPoints[pointCount - 1].Position.Y)));

		// Middle tangents
		for (int i = 1; i < pointCount - 1; ++i) {
			double tensionForPoint = ControlPoints[i].Tension * i / (double) (pointCount - 1);
			tangents.Add (new PointD (
				tensionForPoint * (ControlPoints[i + 1].Position.X - ControlPoints[i - 1].Position.X),
				tensionForPoint * (ControlPoints[i + 1].Position.Y - ControlPoints[i - 1].Position.Y)));
		}

		// Last tangent (closed: wraps to first)
		if (pointCount > 2) {
			tangents.Add (new PointD (
				ControlPoints[pointCount - 1].Tension * (ControlPoints[0].Position.X - ControlPoints[pointCount - 2].Position.X),
				ControlPoints[pointCount - 1].Tension * (ControlPoints[0].Position.Y - ControlPoints[pointCount - 2].Position.Y)));
		}

		// Emit cubic Bezier segments for each edge.
		for (int i = 1; i < pointCount; ++i) {
			int iMinusOne = i - 1;
			foreach (var p in GenerateCubicBezierCurvePoints (
				ControlPoints[iMinusOne].Position,
				new PointD (ControlPoints[iMinusOne].Position.X + tangents[iMinusOne].X,
				            ControlPoints[iMinusOne].Position.Y + tangents[iMinusOne].Y),
				new PointD (ControlPoints[i].Position.X - tangents[i].X,
				            ControlPoints[i].Position.Y - tangents[i].Y),
				ControlPoints[i].Position,
				i)) {
				yield return p;
			}
		}

		// Close the loop.
		int last = pointCount - 1;
		foreach (var p in GenerateCubicBezierCurvePoints (
			ControlPoints[last].Position,
			new PointD (ControlPoints[last].Position.X + tangents[last].X,
			            ControlPoints[last].Position.Y + tangents[last].Y),
			new PointD (ControlPoints[0].Position.X - tangents[0].X,
			            ControlPoints[0].Position.Y - tangents[0].Y),
			ControlPoints[0].Position,
			0)) {
			yield return p;
		}
	}

	private static IEnumerable<GeneratedPoint> GenerateCubicBezierCurvePoints (PointD p0, PointD p1, PointD p2, PointD p3, int cPIndex)
	{
		const double tInterval = .025d;
		for (double t = 0d; t < 1d + tInterval; t += tInterval) {
			double oneMinusT = 1d - t;
			double oneMinusTSquared = oneMinusT * oneMinusT;
			double oneMinusTCubed = oneMinusTSquared * oneMinusT;
			double tSquared = t * t;
			double tCubed = tSquared * t;
			double oneMinusTSquaredTimesTTimesThree = oneMinusTSquared * t * 3d;
			double oneMinusTTimesTSquaredTimesThree = oneMinusT * tSquared * 3d;
			yield return new GeneratedPoint (
				new PointD (
					oneMinusTCubed * p0.X + oneMinusTSquaredTimesTTimesThree * p1.X + oneMinusTTimesTSquaredTimesThree * p2.X + tCubed * p3.X,
					oneMinusTCubed * p0.Y + oneMinusTSquaredTimesTTimesThree * p1.Y + oneMinusTTimesTSquaredTimesThree * p2.Y + tCubed * p3.Y),
				cPIndex);
		}
	}

	private IEnumerable<GeneratedPoint> CreatePoints ()
	{
		//An ellipse requires exactly 4 control points in order to draw anything.
		if (ControlPoints.Count != 4)
			yield break;

		//This is mostly for time efficiency/optimization, but it can also help readability.
		PointD
			cp0 = ControlPoints[0].Position,
			cp1 = ControlPoints[1].Position,
			cp2 = ControlPoints[2].Position,
			cp3 = ControlPoints[3].Position;

		//An ellipse also requires that all 4 control points compose a perfect rectangle parallel/perpendicular to the window.
		//So, confirm that it is indeed a perfect rectangle.
		bool perfectRectangle = IsPerfectRectangle (cp0, cp1, cp2, cp3);

		if (!perfectRectangle)
			yield break;

		//It is expected that the 4 control points always form a perfect rectangle parallel/perpendicular to the window.
		//However, we must first determine which control point is at the top left and which is at the bottom right.
		//It is also expected that the 4 control points are adjacent to each other by index and position, e.g.: 0, 1, 2, 3.

		PointD topLeft = cp0;
		PointD bottomRight = cp0;

		//Compare the second point with the first.
		if (cp1.X < topLeft.X || cp1.Y < topLeft.Y) {
			//The second point is either more left or more up than the first.

			topLeft = cp1;

			//Compare the third point with the second.
			if (cp2.X < topLeft.X || cp2.Y < topLeft.Y) {
				//The third point is either more left or more up than the second.

				topLeft = cp2;

				//The first point remains the bottom right.
			} else {
				//The third point is neither more left nor more up than the second.

				//The second point remains the top left.

				bottomRight = cp3;
			}
		} else {
			//The second point is neither more left nor more up than the first.

			PointD secondPoint = cp1;

			//Compare the third point with the second.
			if (cp2.X < secondPoint.X || cp2.Y < secondPoint.Y) {
				//The third point is either more left or more up than the second.

				topLeft = cp3;
				bottomRight = cp1;
			} else {
				//The third point is neither more left nor more up than the second.

				//The first point remains the top left.

				bottomRight = cp2;
			}
		}

		//Now we can calculate the width and height.
		double width = bottomRight.X - topLeft.X;
		double height = bottomRight.Y - topLeft.Y;

		//Some elliptic math code taken from Cairo Extensions, and some from DocumentSelection code written for GSoC 2013.

		//Calculate an appropriate interval at which to increment t based on
		//the bounding rectangle's width and height properties. The increment
		//for t determines how many intermediate Points to calculate for the
		//ellipse. For each curve, t will go from tInterval to 1. The lower
		//the value of tInterval, the higher number of intermediate Points
		//that will be calculated and stored into the Polygon collection.
		double tInterval = .02d;

		double r_x = width / 2d; //1/2 of the bounding Rectangle Width.
		double r_y = height / 2d; //1/2 of the bounding Rectangle Height.

		//The middle of the bounding Rectangle...
		PointD c = new (
			X: topLeft.X + r_x, // ...Horizontally speaking
			Y: topLeft.Y + r_y); // ...Vertically speaking

		const double c_1 = 0.5522847498307933984022516322796d; //tan(pi / 8d) * 4d / 3d ~= 0.5522847498307933984022516322796d

		// Save first quadrant to later close the ellipse
		var first_quadrant = CalculateCurvePoints (
			tInterval,
			c.X + r_x, c.Y,
			c.X + r_x, c.Y - c_1 * r_y,
			c.X + c_1 * r_x, c.Y - r_y,
			c.X, c.Y - r_y,
			3);

		foreach (var p in first_quadrant)
			yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X, c.Y - r_y,
				c.X - c_1 * r_x, c.Y - r_y,
				c.X - r_x, c.Y - c_1 * r_y,
				c.X - r_x, c.Y,
				0
			)
		) yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X - r_x, c.Y,
				c.X - r_x, c.Y + c_1 * r_y,
				c.X - c_1 * r_x, c.Y + r_y,
				c.X, c.Y + r_y,
				1
			)
		) yield return p;

		foreach (
			var p in
			CalculateCurvePoints (
				tInterval,
				c.X, c.Y + r_y,
				c.X + c_1 * r_x, c.Y + r_y,
				c.X + r_x, c.Y + c_1 * r_y,
				c.X + r_x, c.Y,
				2
			)
		) yield return p;

		// Close the curve.
		// Do not close the curve if no dash pattern used, or else dash pattern wraps past the end of the ellipse
		if (!CairoExtensions.IsValidDashPattern (DashPattern)) {
			yield return first_quadrant.Take (2).Last ();
			// Closes the curve in more extreme, near-flat circle (width >>> height) cases.
			yield return first_quadrant.Take (3).Last ();
		}
	}

	/// <summary>
	/// Calculate each intermediate Point in the specified curve, returning Math.Round(1d / tInterval - 1d) number of Points.
	/// </summary>
	/// <param name="tInterval">The increment value for t (should be between 0-1).</param>
	/// <param name="x0">Starting point X (not included in the returned Point(s)).</param>
	/// <param name="y0">Starting point Y (not included in the returned Point(s)).</param>
	/// <param name="x1">Control point 1 X.</param>
	/// <param name="y1">Control point 1 Y.</param>
	/// <param name="x2">Control point 2 X.</param>
	/// <param name="y2">Control point 2 Y.</param>
	/// <param name="x3">Ending point X (included in the returned Point(s)).</param>
	/// <param name="y3">Ending point Y (included in the returned Point(s)).</param>
	/// <param name="cPIndex">The index of the previous ControlPoint to the generated points.</param>
	/// <returns></returns>
	private static IEnumerable<GeneratedPoint> CalculateCurvePoints (
		double tInterval,
		double x0, double y0,
		double x1, double y1,
		double x2, double y2,
		double x3, double y3,
		int cPIndex)
	{
		//Generates points of partial Polygon containing the calculated Points in the curve.
		for (double t = 0; t < 1d; t += tInterval) {
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

			yield return new (
				new PointD (
					X: oneMinusTCubed * x0 + oneMinusTSquaredTimesTTimesThree * x1 + oneMinusTTimesTSquaredTimesThree * x2 + tCubed * x3,
					Y: oneMinusTCubed * y0 + oneMinusTSquaredTimesTTimesThree * y1 + oneMinusTTimesTSquaredTimesThree * y2 + tCubed * y3),
				cPIndex
			);
		}
	}
}
