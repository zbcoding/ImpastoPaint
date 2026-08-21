//
// ShapeEngineCollection.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2013 Andrew Davis, GSoC 2013 & 2014
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
using System.Collections.Immutable;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public static class ShapeEngineCollection
{
	public static ShapeEngine Create (UserLayer parentLayer, ShapeObject source)
	{
		ShapeEngine engine = source.ShapeType switch {
			ShapeObjectType.Ellipse => new EllipseEngine (parentLayer, null, source.AntiAliasing, source.OutlineColor, source.FillColor, source.BrushWidth, source.LineCap),
			ShapeObjectType.RoundedLineSeries => new RoundedLineEngine (parentLayer, null, source.RoundedRadius, source.AntiAliasing, source.OutlineColor, source.FillColor, source.BrushWidth, source.LineCap),
			_ => new LineCurveSeriesEngine (parentLayer, null, (BaseEditEngine.ShapeTypes) source.ShapeType, source.AntiAliasing, source.Closed, source.OutlineColor, source.FillColor, source.BrushWidth, source.LineCap),
		};

		engine.Name = source.Name;
		engine.RasterizeOnFinalize = source.RasterizeOnFinalize;
		engine.Clip = source.Clip;
		engine.Opacity = source.Opacity;
		engine.Hidden = source.Hidden;
		engine.BlendMode = source.BlendMode;
		engine.DashPattern = source.DashPattern;
		engine.DashSpacing = source.DashSpacing;
		engine.FillStyle = source.FillStyle;
		engine.ControlPoints = source.ControlPoints.ConvertAll (point => new ControlPoint (point.Position, point.Tension));

		if (engine is LineCurveSeriesEngine lineEngine) {
			lineEngine.Arrow1 = ToArrow (source.Arrow1);
			lineEngine.Arrow2 = ToArrow (source.Arrow2);
			lineEngine.TriangleType = source.TriangleType;
		}

		if (engine is EllipseEngine ellipse && source.IsPartialEllipse)
			ellipse.SetPartialGeometry (source.PartialEllipseCenter, source.PartialEllipseRadiusX, source.PartialEllipseRadiusY);

		return engine;
	}

	public static ShapeObject ToShapeObject (this ShapeEngine engine)
	{
		ShapeObject result = new () {
			ShapeType = (ShapeObjectType) engine.ShapeType,
			Name = engine.Name,
			RasterizeOnFinalize = engine.RasterizeOnFinalize,
			Clip = engine.Clip,
			Opacity = engine.Opacity,
			Hidden = engine.Hidden,
			BlendMode = engine.BlendMode,
			AntiAliasing = engine.AntiAliasing,
			Closed = engine.Closed,
			OutlineColor = engine.OutlineColor,
			FillColor = engine.FillColor,
			BrushWidth = engine.BrushWidth,
			LineCap = engine.LineCap,
			DashPattern = engine.DashPattern,
			DashSpacing = engine.DashSpacing,
			FillStyle = engine.FillStyle,
		};

		result.ControlPoints.AddRange (engine.ControlPoints.ConvertAll (point => new ShapeControlPoint {
			Position = point.Position,
			Tension = point.Tension,
		}));

		if (engine is LineCurveSeriesEngine lineEngine) {
			result.Arrow1 = FromArrow (lineEngine.Arrow1);
			result.Arrow2 = FromArrow (lineEngine.Arrow2);
			result.TriangleType = lineEngine.TriangleType;
		}

		if (engine is RoundedLineEngine rounded)
			result.RoundedRadius = rounded.Radius;

		if (engine is EllipseEngine ellipse && ellipse.TryGetPartialGeometry (out PointD center, out double radiusX, out double radiusY)) {
			result.IsPartialEllipse = true;
			result.PartialEllipseCenter = center;
			result.PartialEllipseRadiusX = radiusX;
			result.PartialEllipseRadiusY = radiusY;
		}

		return result;
	}

	public static void Store (UserLayer layer, IReadOnlyList<ShapeEngine> engines)
		// Rebuild the layer's shape objects in place, preserving the position of each shape relative
		// to the text objects it may be interleaved with (cross-kind z-order survives a persist), and
		// inserting anything past the old count - freshly drawn shapes, not reordered survivors - at
		// the bottom. UserLayer owns that merge; this is just the ShapeEngine -> ShapeObject leg of it.
		=> layer.ReplaceShapes (engines.Where (e => e.ParentLayer == layer).Select (e => e.ToShapeObject ()).ToList ());

	private static Arrow ToArrow (ShapeArrow arrow)
		=> new (arrow.Show, arrow.Size, arrow.AngleOffset, arrow.LengthOffset);

	private static ShapeArrow FromArrow (Arrow arrow)
		=> new () {
			Show = arrow.Show,
			Size = arrow.ArrowSize,
			AngleOffset = arrow.AngleOffset,
			LengthOffset = arrow.LengthOffset,
		};

	/// <summary>
	/// Calculate the closest ControlPoint to currentPoint.
	/// </summary>
	/// <param name="reference">The point to calculate the closest ControlPoint to.</param>
	/// <param name="closestShapeIndex">The index of the shape with the closest ControlPoint.</param>
	/// <param name="closestIndex">The index of the closest ControlPoint.</param>
	/// <param name="closestControlPoint">The closest ControlPoint to currentPoint.</param>
	/// <param name="closestCPDistance">The closest ControlPoint's distance from currentPoint.</param>
	public static void FindClosestControlPoint (
		this IReadOnlyList<ShapeEngine> source,
		PointD reference,
		out int closestShapeIndex,
		out int closestIndex,
		out ControlPoint? closestControlPoint,
		out double closestDistanceSquared)
	{
		closestShapeIndex = 0;
		closestIndex = 0;
		closestControlPoint = null;
		closestDistanceSquared = double.MaxValue;

		double currentDistanceSquared;

		for (int shapeIndex = 0; shapeIndex < source.Count; ++shapeIndex) {

			List<ControlPoint> controlPoints = source[shapeIndex].ControlPoints;

			for (int cPIndex = 0; cPIndex < controlPoints.Count; ++cPIndex) {

				currentDistanceSquared = controlPoints[cPIndex].Position.DistanceSquared (reference);

				if (currentDistanceSquared >= closestDistanceSquared) continue;

				closestShapeIndex = shapeIndex;
				closestIndex = cPIndex;
				closestControlPoint = controlPoints[cPIndex];
				closestDistanceSquared = currentDistanceSquared;
			}
		}
	}
}

public abstract class ShapeEngine : ILayerObject
{
	//A collection of the original ControlPoints that the shape is based on and that the user interacts with.
	public List<ControlPoint> ControlPoints { get; internal set; } = [];
	public List<MoveHandle> ControlPointHandles { get; private set; } = [];

	//A collection of calculated GeneratedPoints that make up the entirety of the shape being drawn.
	public GeneratedPoint[] GeneratedPoints { get; private protected set; } = [];

	//An organized collection of the GeneratedPoints's points for optimized nearest point detection.
	public OrganizedPointCollection OrganizedPoints { get; } = new ();

	private readonly UserLayer parent_layer;
	public ReEditableLayer DrawingLayer { get; }

	public bool AntiAliasing { get; internal set; }
	public string DashPattern { get; internal set; } = "-";
	public int DashSpacing { get; internal set; } = 1;
	public bool Closed { get; }

	public Color OutlineColor { get; internal set; }
	public Color FillColor { get; internal set; }

	public int BrushWidth { get; internal set; }
	public int FillStyle { get; internal set; }

	public BaseEditEngine.ShapeTypes ShapeType { get; }

	// User-facing default name (e.g. "Ellipse 1"), assigned at creation and carried through the
	// ShapeObject round-trip so it survives persist/reload. Shown in the layers dock.
	public string Name { get; set; } = "";

	// Per-shape Object/Raster mode intent, stamped from the tool toggle at creation. When true this
	// shape bakes into the base raster on the next commit; false stays editable. Carried through the
	// ShapeObject round-trip so mode is remembered per shape, not globally.
	public bool RasterizeOnFinalize { get; internal set; }

	// The selection the shape was drawn under, frozen at creation (null = no active selection). Every
	// render clips to this so the shape keeps its drawn-inside-a-selection appearance across reloads and
	// selection changes. Carried through the ShapeObject round-trip. Shared, never mutated.
	public DocumentSelection? Clip { get; internal set; }

	// Per-shape opacity (0..1), carried through the ShapeObject round-trip. Applied when the shape is
	// composited into the layer's shape surface. See ObjectOpacity.
	public double Opacity { get; set; } = 1.0;

	// Per-shape visibility, carried through the ShapeObject round-trip. A hidden shape renders
	// nothing but stays fully editable. See ObjectOpacity.Draw, the chokepoint that honors it.
	public bool Hidden { get; set; }

	// Per-shape blend mode, carried through the ShapeObject round-trip. How this shape mixes with
	// what is already on the layer's shape surface. See ObjectOpacity.Draw.
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	public LineCap LineCap { get; set; }
	public UserLayer ParentLayer => parent_layer ?? DrawingLayer.ParentLayer;

	/// <summary>
	/// Create a new ShapeEngine.
	/// </summary>
	/// <param name="parent_layer">The parent UserLayer for the ReEditable DrawingLayer.</param>
	/// <param name="drawing_layer">An existing ReEditableLayer to reuse. This is for cloning only. If not cloning, pass in null.</param>
	/// <param name="shape_type">The type of shape to create.</param>
	/// <param name="antialiasing">Whether or not antialiasing is enabled.</param>
	/// <param name="closed">Whether or not the shape is closed (first and last points are connected).</param>
	/// <param name="outline_color">The outline color for the shape.</param>
	/// <param name="fill_color">The fill color for the shape.</param>
	/// <param name="brush_width">The width of the outline of the shape.</param>
	public ShapeEngine (UserLayer parent_layer, ReEditableLayer? drawing_layer,
			    BaseEditEngine.ShapeTypes shape_type, bool antialiasing,
			    bool closed, Color outline_color, Color fill_color,
			    int brush_width, LineCap lineCap)
	{
		this.parent_layer = parent_layer;

		if (drawing_layer == null)
			DrawingLayer = new ReEditableLayer (parent_layer);
		else
			DrawingLayer = drawing_layer;

		// Object-layer system: the active layer's shapes composite through its shared ShapeLayer
		// surface, not per-shape overlays. Keep this vestigial DrawingLayer out of the drawing loop
		// so it never double-composites. ponytail: property kept only as engine identity/scratch;
		// remove it wholesale once no ShapeEngine paths reference it.
		DrawingLayer.TryRemoveLayer ();

		ShapeType = shape_type;
		AntiAliasing = antialiasing;
		Closed = closed;
		OutlineColor = outline_color;
		FillColor = fill_color;
		BrushWidth = brush_width;
		LineCap = lineCap;
	}

	protected ShapeEngine (ShapeEngine src)
	{
		DrawingLayer = src.DrawingLayer;
		DrawingLayer.TryRemoveLayer (); // See note in the primary constructor.
		Name = src.Name;
		RasterizeOnFinalize = src.RasterizeOnFinalize;
		Clip = src.Clip;
		Opacity = src.Opacity;
		Hidden = src.Hidden;
		BlendMode = src.BlendMode;
		ShapeType = src.ShapeType;
		AntiAliasing = src.AntiAliasing;
		Closed = src.Closed;
		OutlineColor = src.OutlineColor;
		FillColor = src.FillColor;
		BrushWidth = src.BrushWidth;
		FillStyle = src.FillStyle;
		LineCap = src.LineCap;

		// Don't clone the GeneratedPoints or OrganizedPoints, as they will be calculated.
		ControlPoints = src.ControlPoints.Select (i => i.Clone ()).ToList ();
		DashPattern = src.DashPattern;
		DashSpacing = src.DashSpacing;
		parent_layer = null!; // NRT - This constructor needs to set parent_layer somehow as code expects it to be not-null
	}

	public abstract ShapeEngine Clone ();

	/// <summary>
	/// Converts the ShapeEngine instance into a new instance of a different
	/// ShapeEngine (child) type, copying the common data.
	/// </summary>
	/// <param name="newShapeType">The new ShapeEngine type to create.</param>
	/// <param name="shapeIndex">The index to insert the ShapeEngine clone into SEngines at.
	/// This ensures that the clone is as transparent as possible.</param>
	/// <returns>A new ShapeEngine instance of the specified type with the common data copied over.</returns>
	public ShapeEngine Convert (BaseEditEngine.ShapeTypes newShapeType, int shapeIndex)
	{
		//Remove the old ShapeEngine instance.

		BaseEditEngine.SEngines.Remove (this);

		ShapeEngine clone = newShapeType switch {

			BaseEditEngine.ShapeTypes.ClosedLineCurveSeries or BaseEditEngine.ShapeTypes.Triangle => new LineCurveSeriesEngine (
				parent_layer,
				DrawingLayer,
				newShapeType,
				AntiAliasing,
				true,
				OutlineColor,
				FillColor,
				BrushWidth,
				LineCap
			),

			BaseEditEngine.ShapeTypes.Ellipse => new EllipseEngine (
				parent_layer,
				DrawingLayer,
				AntiAliasing,
				OutlineColor,
				FillColor,
				BrushWidth,
				LineCap
			),

			BaseEditEngine.ShapeTypes.RoundedLineSeries => new RoundedLineEngine (
				parent_layer,
				DrawingLayer,
				RoundedLineEditEngine.DefaultRadius,
				AntiAliasing,
				OutlineColor,
				FillColor,
				BrushWidth,
				LineCap
			),

			_ => new LineCurveSeriesEngine (
				parent_layer,
				DrawingLayer,
				newShapeType,
				AntiAliasing,
				false,
				OutlineColor,
				FillColor,
				BrushWidth,
				LineCap
			),//Defaults to OpenLineCurveSeries.
		};

		// Don't clone the GeneratedPoints or OrganizedPoints, as they will be calculated.
		clone.Name = Name;
		clone.RasterizeOnFinalize = RasterizeOnFinalize;
		clone.Opacity = Opacity;
		clone.Hidden = Hidden;
		clone.ControlPoints = ControlPoints.Select (i => i.Clone ()).ToList ();
		clone.DashPattern = DashPattern;
		clone.DashSpacing = DashSpacing;

		// Add the new ShapeEngine instance at the specified index to
		// ensure as transparent of a cloning as possible.
		BaseEditEngine.SEngines.Insert (shapeIndex, clone);

		return clone;
	}

	/// <summary>
	/// Generate the points that make up the entirety of the shape being drawn.
	/// <param name="brush_width">The width of the brush that will be used to draw the shape.</param>
	/// </summary>
	public abstract void GeneratePoints (int brush_width);

	public ImmutableArray<PointD> GetActualPoints ()
	{
		int n = GeneratedPoints.Length;
		var points = ImmutableArray.CreateBuilder<PointD> (n);
		points.Count = n;
		for (int i = 0; i < n; ++i)
			points[i] = GeneratedPoints[i].Position;
		return points.MoveToImmutable ();
	}
}
