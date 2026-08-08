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
public sealed class TextObject
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

	//Whether this object fuses into the layer's raster on commit (Raster mode) rather
	//than staying a live, re-editable object (Object mode). Stored per object, mirroring
	//ShapeObject.RasterizeOnFinalize, so the clip-to-selection and commit-time fuse both
	//key off the object's own mode instead of the toolbar's live selection. Not persisted:
	//raster objects fuse away immediately, so a saved/loaded text object is always Object mode.
	public bool RasterizeOnFinalize { get; set; }

	//Per-object opacity, 0..1 (1 = fully opaque), applied when the object is composited into the
	//layer's TextLayer surface. Independent of the layer's own opacity. Mirrors ShapeObject.Opacity.
	public double Opacity { get; set; } = 1.0;

	public TextObject (TextEngine engine)
	{
		Engine = engine;
	}

	public bool IsEmpty
		=> Engine.IsEmpty ();

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
		};

	public static List<TextObject> CloneAll (IReadOnlyList<TextObject> objects)
		=> [.. objects.Select (o => o.Clone ())];
}
