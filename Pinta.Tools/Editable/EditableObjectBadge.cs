using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Draws the small "Obj." badge that marks a live, re-editable shape/text object. Used as a
/// semi-transparent grey on-canvas affordance (drawn on the tool overlay layer, never baked into
/// artwork) positioned at the lower-left of a text field / below the leftmost corner of a shape.
/// The layers dock shows the same mark via the object-editable-symbolic icon.
/// </summary>
internal static class EditableObjectBadge
{
	public const double Width = 26;
	public const double Height = 14;

	// A muted, slightly transparent grey — reads as an affordance (like a cursor), not as artwork.
	public static readonly Color CanvasColor = new (0.35, 0.35, 0.35, 0.75);

	/// <summary>Draws the badge with its top-left corner at <paramref name="topLeft"/>.</summary>
	public static void Draw (Context g, PointD topLeft, Color color)
	{
		const double r = 3.5;
		double x = topLeft.X, y = topLeft.Y;

		g.Save ();

		// Rounded-rectangle outline.
		g.NewPath ();
		g.Arc (x + Width - r, y + r, r, -Math.PI / 2, 0);
		g.Arc (x + Width - r, y + Height - r, r, 0, Math.PI / 2);
		g.Arc (x + r, y + Height - r, r, Math.PI / 2, Math.PI);
		g.Arc (x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
		g.ClosePath ();
		g.LineWidth = 1.2;
		g.SetSourceColor (color);
		g.Stroke ();

		// "Obj." label (Cairo toy font is fine for a tiny fixed overlay).
		g.SelectFontFace ("sans-serif", FontSlant.Normal, FontWeight.Bold);
		g.SetFontSize (9);
		g.MoveTo (x + 4, y + Height - 3.5);
		g.SetSourceColor (color);
		g.ShowText ("Obj.");

		g.Restore ();
	}
}
