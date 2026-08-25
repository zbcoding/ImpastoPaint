//
// LineCurveSeriesEngine.cs
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

using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class LineCurveSeriesEngine : ShapeEngine
{
	public Arrow Arrow1 { get; internal set; }
	public Arrow Arrow2 { get; internal set; }
	public int TriangleType { get; internal set; }

	/// <summary>
	/// Create a new LineCurveSeriesEngine.
	/// </summary>
	/// <param name="parentLayer">The parent UserLayer for the re-editable DrawingLayer.</param>
	/// <param name="drawingLayer">An existing ReEditableLayer to reuse. This is for cloning only. If not cloning, pass in null.</param>
	/// <param name="shapeType">The owner EditEngine.</param>
	/// <param name="antialiasing">Whether or not antialiasing is enabled.</param>
	/// <param name="closed">Whether or not the shape is closed (first and last points are connected).</param>
	/// <param name="outlineColor">The outline color for the shape.</param>
	/// <param name="fillColor">The fill color for the shape.</param>
	/// <param name="brushWidth">The width of the outline of the shape.</param>
	/// <param name="lineCap">Defines the edge of the line drawn.</param>
	public LineCurveSeriesEngine (
		UserLayer parentLayer,
		ReEditableLayer? drawingLayer,
		BaseEditEngine.ShapeTypes shapeType,
		bool antialiasing,
		bool closed,
		Color outlineColor,
		Color fillColor,
		int brushWidth,
		LineCap lineCap
	) : base (
		parentLayer,
		drawingLayer,
		shapeType,
		antialiasing,
		closed,
		outlineColor,
		fillColor,
		brushWidth,
		lineCap)
	{
		Arrow1 = new ();
		Arrow2 = new ();
	}

	private LineCurveSeriesEngine (LineCurveSeriesEngine src)
		: base (src)
	{
		Arrow1 = src.Arrow1;
		Arrow2 = src.Arrow2;
		TriangleType = src.TriangleType;
	}

	public override LineCurveSeriesEngine Clone ()
	{
		return new (this);
	}

	/// <summary>
	/// Generate each point in an line/curve series (cardinal spline polynomial curve) shape that passes through the control points,
	/// and store the result in GeneratedPoints.
	/// <param name="brush_width">The width of the brush that will be used to draw the shape.</param>
	/// </summary>
	public override void GeneratePoints (int brush_width)
	{
		if (ControlPoints.Count < 2) {
			GeneratedPoints = [new GeneratedPoint (ControlPoints[0].Position, 0)];
			return;
		}

		GeneratedPoints = [.. CurveGeneration.CardinalSpline (ControlPoints, Closed)];
	}
}
