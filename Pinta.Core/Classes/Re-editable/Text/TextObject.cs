using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// An editable block of text living on a <see cref="UserLayer"/>. A layer can hold
/// several of these, and each one keeps its own engine (content, font, colors) and
/// on-canvas bounds so it can be selected, moved and re-edited independently.
///
/// Extends the re-editable text engine originally built for Pinta by Andrew Davis
/// (GSoC 2012/2013), per the request by prokoudine in Pinta issue #1337.
/// </summary>
public sealed class TextObject : ILayerObject
{
	public TextEngine Engine { get; }

	//Rectangular boundary surrounding the editable text.
	public RectangleI TextBounds { get; set; } = RectangleI.Zero;
	public RectangleI PreviousTextBounds { get; set; } = RectangleI.Zero;

	//The text tool's style dropdown index this object was drawn with: 0 Normal,
	//1 Normal and Outline, 2 Outline, 3 Fill Background. Stored per object (rather
	//than read live from the toolbar) so objects on the same layer can each keep
	//their own style.
	public int FillStyle { get; set; } = 0;
	public int OutlineWidth { get; set; } = 2;
	public LineJoin LineJoin { get; set; } = LineJoin.Miter;

	//The rotation of the text, in degrees, counter-clockwise. 0 is unrotated.
	public double Rotation { get; set; } = 0;

	//Rotation as Cairo wants it: radians, negated because the canvas Y axis points down.
	public double RotationRadians => -Rotation * System.Math.PI / 180d;

	//The point the object rotates about, in canvas coordinates: its top-left origin, which does
	//NOT move when the content (and so the layout bounds) grows or shrinks. Rotating about this
	//fixed point keeps positioned, rotated text in place while the user types more or less of it.
	public PointD RotationPivot => Engine.Origin.ToDouble ();

	//Whether this object fuses into the layer's raster on commit (Raster mode) rather
	//than staying a live, re-editable object (Object mode). Stored per object, mirroring
	//ShapeObject.RasterizeOnFinalize, so the clip-to-selection and commit-time fuse both
	//key off the object's own mode instead of the toolbar's live selection. Not persisted:
	//raster objects fuse away immediately, so a saved/loaded text object is always Object mode.
	public bool RasterizeOnFinalize { get; set; }

	//Per-object opacity, 0..1 (1 = fully opaque), applied when the object is composited into the
	//layer's TextLayer surface. Independent of the layer's own opacity. Mirrors ShapeObject.Opacity.
	public double Opacity { get; set; } = 1.0;

	//Hidden from every render path (so it also contributes nothing when rasterized), and the
	//user-facing name shown on the object's row in the layers dock (empty = the default "Text").
	//Both mirror ShapeObject and are edited from that row.
	public bool Hidden { get; set; }
	public string Name { get; set; } = "";

	//How this text object blends with what is already on the layer's TextLayer surface. Mirrors the
	//layer blend modes; Normal composites over. Picked from the object's row in the layers dock.
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	public TextObject (TextEngine engine)
	{
		Engine = engine;
	}

	public bool IsEmpty
		=> Engine.IsEmpty ();

	//What FillStyle means when drawing, named once here instead of being re-derived from the
	//dropdown index at every draw site (the text tool's live draw and TextObjectRenderer).
	public bool StrokesText => FillStyle >= 1 && FillStyle != 3;
	public bool FillsText => FillStyle <= 1 || FillStyle == 3;
	public bool FillsBackground => FillStyle == 3;

	public TextObject Clone ()
		=> new (Engine.Clone ()) {
			TextBounds = TextBounds,
			PreviousTextBounds = PreviousTextBounds,
			FillStyle = FillStyle,
			OutlineWidth = OutlineWidth,
			LineJoin = LineJoin,
			Rotation = Rotation,
			RasterizeOnFinalize = RasterizeOnFinalize,
			Opacity = Opacity,
			Hidden = Hidden,
			Name = Name,
			BlendMode = BlendMode,
		};

	public static List<TextObject> CloneAll (IReadOnlyList<TextObject> objects)
		=> [.. objects.Select (o => o.Clone ())];
}
