using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Renders <see cref="ShapeObject"/>s onto a surface as a pure function of the object
/// list (no control points, no selection clip). This is the shape-side counterpart of
/// <see cref="TextObjectRenderer"/>: it lets a layer's <c>ShapeLayer</c> surface be
/// rebuilt from <c>ShapeObjects</c> anywhere, which is what keeps shapes on a
/// non-active layer visible instead of desyncing into a white rectangle.
/// </summary>
public static class ShapeObjectRenderer
{
	/// <summary>
	/// Clears the surface and redraws every shape in the layer's object list onto it.
	/// Establishes the invariant: <c>ShapeLayer</c> surface == render of <c>ShapeObjects</c>.
	/// </summary>
	public static void RenderAll (ImageSurface surface, UserLayer layer)
	{
		surface.Clear ();
		foreach (ShapeObject source in layer.ShapeObjects)
			if (!source.Rasterized) // rasterized shapes live in the base raster; don't composite them twice
				Render (surface, layer, source);
	}

	/// <summary>
	/// Draws a single shape's fill, stroke, and arrows onto the surface.
	/// </summary>
	public static void Render (ImageSurface surface, UserLayer layer, ShapeObject source)
	{
		ShapeEngine engine = ShapeEngineCollection.Create (layer, source);

		// The engine spins up a composited DrawingLayer in its constructor; this is a
		// throwaway renderer, so keep it out of the drawing loop.
		engine.DrawingLayer.TryRemoveLayer ();

		if (engine.ControlPoints.Count == 0)
			return;

		using Context g = new (surface);

		g.Antialias = source.AntiAliasing ? Antialias.Subpixel : Antialias.None;

		string dashPattern = source.DashSpacing > 1
			? source.DashPattern.Replace (" ", new string (' ', source.DashSpacing))
			: source.DashPattern;
		bool isDashedLine = g.SetDashFromString (dashPattern, source.BrushWidth, LineCap.Square);

		g.LineWidth = source.BrushWidth;

		engine.GeneratePoints (source.BrushWidth);
		var points = engine.GetActualPoints ();

		bool strokeShape = source.FillStyle % 2 == 0;
		bool fillShape = source.FillStyle >= 1;

		if (fillShape)
			g.FillPolygonal (points.AsSpan (), strokeShape ? source.FillColor : source.OutlineColor);

		if (strokeShape) {
			// Dash patterns cannot work with Butt, so default to Square when dashed.
			LineCap lineCap = isDashedLine ? LineCap.Square : source.LineCap;
			g.DrawPolygonal (points.AsSpan (), source.OutlineColor, lineCap);
		}

		g.SetDash ([], 0.0);

		DrawArrows (g, engine);
	}

	private static void DrawArrows (Context g, ShapeEngine engine)
	{
		if (engine is not LineCurveSeriesEngine lce)
			return;

		var genPoints = engine.GeneratedPoints;
		if (genPoints.Length < 2)
			return;

		if (lce.Arrow1.Show)
			lce.Arrow1.Draw (g, lce.OutlineColor, genPoints[0].Position, genPoints[1].Position);

		if (lce.Arrow2.Show)
			lce.Arrow2.Draw (g, lce.OutlineColor, genPoints[^1].Position, genPoints[^2].Position);
	}
}
