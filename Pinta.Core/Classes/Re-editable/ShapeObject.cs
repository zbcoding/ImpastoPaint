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
public sealed class ShapeObject
{
	public ShapeObjectType ShapeType { get; set; }
	public string Name { get; set; } = "";
	/// <summary>
	/// True once the shape has been baked into the layer's base raster (Rasterized mode). Its pixels
	/// live in the base layer, so it is no longer rendered into the object surface nor rebuilt as a
	/// live editing engine — it is kept only as a (non-editable) record shown in the layers dock.
	/// </summary>
	public bool Rasterized { get; set; }
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
			Rasterized = Rasterized,
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
}
