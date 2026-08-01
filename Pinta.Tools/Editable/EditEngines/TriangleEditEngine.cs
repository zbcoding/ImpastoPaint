//
// TriangleEditEngine.cs
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
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class TriangleEditEngine : BaseEditEngine
{
	protected override string ShapeName
		=> Translations.GetString ("Triangle Shape");

	private readonly IWorkspaceService workspace;
	public TriangleEditEngine (
		IServiceProvider services,
		ShapeTool passedOwner
	) : base (services, passedOwner)
	{
		workspace = services.GetService<IWorkspaceService> ();
	}

	protected override ShapeEngine CreateShape (
		bool ctrlKey,
		bool clickedOnControlPoint,
		PointD prevSelPoint)
	{
		Document doc = workspace.ActiveDocument;

		LineCurveSeriesEngine newEngine = new (doc.Layers.CurrentUserLayer, null, BaseEditEngine.ShapeTypes.Triangle,
			owner.UseAntialiasing, true, BaseEditEngine.OutlineColor, BaseEditEngine.FillColor, owner.EditEngine.BrushWidth, LineCap.Square);

		AddTrianglePoints (ctrlKey, clickedOnControlPoint, newEngine, prevSelPoint);

		//Set the new shape's DashPattern option.
		newEngine.DashPattern = dash_pattern_box.ComboBox!.ComboBox.GetActiveText ()!; // NRT - Code assumes this is not-null

		return newEngine;
	}

	protected override void MovePoint (List<ControlPoint> controlPoints)
	{
		if (controlPoints.Count == 3 && ctrl_key_down) {
			MoveEquilateralPoint (controlPoints);
			return;
		}

		MoveTriangularPoint (controlPoints);
		base.MovePoint (controlPoints);
	}

	//Holding Ctrl while dragging a triangle draws an equilateral triangle (apex up,
	//level base centered underneath) instead of the default right triangle. The base
	//follows the mouse's vertical drag; its half-width is derived so all sides are equal.
	private void MoveEquilateralPoint (List<ControlPoint> controlPoints)
	{
		PointD apex = controlPoints[0].Position;

		//Keep the altitude positive so the base always stays below the apex (a zero or
		//inverted altitude would produce a degenerate or upside-down triangle).
		double altitude = Math.Max (current_point.Y - apex.Y, 0.01d);
		double halfBase = altitude / Math.Sqrt (3d);

		double baseY = apex.Y + altitude;

		controlPoints[1].Position = new PointD (apex.X - halfBase, baseY);
		controlPoints[2].Position = new PointD (apex.X + halfBase, baseY);
	}
}
