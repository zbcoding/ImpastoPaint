using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core;

public enum ShapeObjectType
{
	OpenLineCurveSeries,
	ClosedLineCurveSeries,
	Ellipse,
	RoundedLineSeries,
	Triangle,
}

public sealed class ShapeControlPoint
{
	public PointD Position { get; set; }
	public double Tension { get; set; }

	public ShapeControlPoint Clone () => new () {
		Position = Position,
		Tension = Tension,
	};
}

public sealed class ShapeArrow
{
	public bool Show { get; set; }
	public double Size { get; set; } = 10d;
	public double AngleOffset { get; set; } = 15d;
	public double LengthOffset { get; set; } = 10d;

	public ShapeArrow Clone () => new () {
		Show = Show,
		Size = Size,
		AngleOffset = AngleOffset,
		LengthOffset = LengthOffset,
	};
}

/// <summary>
/// Serializable, tool-independent state for one editable shape.
/// </summary>
public sealed class ShapeObject : ILayerObject
{
	public ShapeObjectType ShapeType { get; set; }
	public string Name { get; set; } = "";
	/// <summary>
	/// Per-shape mode intent, stamped from the tool's Object/Raster toggle when the shape is drawn.
	/// When true, this shape fuses into the layer's base raster on the next commit (and is then dropped
	/// as an object); when false it stays a live editable object. Per-shape so mixing modes on one
	/// layer and committing does not rasterize the Object-mode shapes.
	/// </summary>
	public bool RasterizeOnFinalize { get; set; }
	/// <summary>
	/// The selection the shape was drawn under, frozen at creation, or null if it was drawn with no
	/// active selection. Every render (live edit, reload, bake) clips to this so a shape drawn inside a
	/// selection keeps its clipped appearance permanently, instead of re-clipping to the live selection
	/// (which would redraw it in full once the selection changed or was cleared). Shared, never mutated.
	/// </summary>
	public DocumentSelection? Clip { get; set; }
	/// <summary>
	/// Per-object opacity, 0..1 (1 = fully opaque). Applied when the object is composited into its
	/// layer's object surface, so it is independent of the layer's own opacity and survives the
	/// object round-trip. Editable from the object's row in the layers dock.
	/// </summary>
	public double Opacity { get; set; } = 1.0;
	/// <summary>
	/// Hidden from every render path (so it also contributes nothing when rasterized). Toggled from
	/// the object's row in the layers dock; the shape stays fully editable while hidden.
	/// </summary>
	public bool Hidden { get; set; }
	/// <summary>
	/// How this shape blends with what is already drawn on its layer's shape surface. Mirrors the
	/// layer blend modes; Normal composites over. Picked from the object's row in the layers dock.
	/// </summary>
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;
	public List<ShapeControlPoint> ControlPoints { get; } = [];
	public bool AntiAliasing { get; set; } = true;
	public bool Closed { get; set; }
	public Color OutlineColor { get; set; } = Color.Black;
	public Color FillColor { get; set; } = Color.White;
	public int BrushWidth { get; set; } = 2;
	public LineCap LineCap { get; set; } = LineCap.Butt;
	public string DashPattern { get; set; } = "-";
	public int DashSpacing { get; set; } = 1;
	public int FillStyle { get; set; }
	public double RoundedRadius { get; set; }
	public int TriangleType { get; set; }
	public ShapeArrow Arrow1 { get; set; } = new ();
	public ShapeArrow Arrow2 { get; set; } = new ();
	public bool IsPartialEllipse { get; set; }
	public PointD PartialEllipseCenter { get; set; }
	public double PartialEllipseRadiusX { get; set; }
	public double PartialEllipseRadiusY { get; set; }

	public ShapeObject Clone ()
	{
		ShapeObject clone = new () {
			ShapeType = ShapeType,
			Name = Name,
			RasterizeOnFinalize = RasterizeOnFinalize,
			Clip = Clip, // frozen selection, never mutated — safe to share
			Opacity = Opacity,
			Hidden = Hidden,
			BlendMode = BlendMode,
			AntiAliasing = AntiAliasing,
			Closed = Closed,
			OutlineColor = OutlineColor,
			FillColor = FillColor,
			BrushWidth = BrushWidth,
			LineCap = LineCap,
			DashPattern = DashPattern,
			DashSpacing = DashSpacing,
			FillStyle = FillStyle,
			RoundedRadius = RoundedRadius,
			TriangleType = TriangleType,
			Arrow1 = Arrow1.Clone (),
			Arrow2 = Arrow2.Clone (),
			IsPartialEllipse = IsPartialEllipse,
			PartialEllipseCenter = PartialEllipseCenter,
			PartialEllipseRadiusX = PartialEllipseRadiusX,
			PartialEllipseRadiusY = PartialEllipseRadiusY,
		};

		clone.ControlPoints.AddRange (ControlPoints.ConvertAll (point => point.Clone ()));
		return clone;
	}

	public static List<ShapeObject> CloneAll (IReadOnlyList<ShapeObject> objects)
		=> [.. objects.Select (o => o.Clone ())];

	/// <summary>
	/// Approximate on-canvas bounds from the control-point positions, padded by the stroke width.
	/// Rasterize-on-touch intersects the actual selection with this box, so empty corners of a
	/// non-rectangular selection do not count. The shape side still deliberately errs large.
	/// ponytail: control-point bbox + brush pad; render an alpha mask if shape-side false positives
	/// become costly.
	/// </summary>
	public RectangleD GetApproximateBounds ()
	{
		if (ControlPoints.Count == 0)
			return RectangleD.Zero;

		double minX = double.MaxValue, minY = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue;
		foreach (ShapeControlPoint p in ControlPoints) {
			minX = System.Math.Min (minX, p.Position.X);
			minY = System.Math.Min (minY, p.Position.Y);
			maxX = System.Math.Max (maxX, p.Position.X);
			maxY = System.Math.Max (maxY, p.Position.Y);
		}

		double pad = BrushWidth / 2.0 + 1;
		return new (minX - pad, minY - pad, (maxX - minX) + 2 * pad, (maxY - minY) + 2 * pad);
	}
}
