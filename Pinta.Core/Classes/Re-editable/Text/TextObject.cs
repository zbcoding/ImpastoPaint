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
		};

	public static List<TextObject> CloneAll (IReadOnlyList<TextObject> objects)
		=> [.. objects.Select (o => o.Clone ())];
}
