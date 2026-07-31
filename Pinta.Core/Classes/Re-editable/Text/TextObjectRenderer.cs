using System.Collections.Generic;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Renders text objects onto a surface. Shared by the .ora importer so text objects
/// are visible immediately after opening a file, before the text tool ever activates.
///
/// Companion to the re-editable text system built for Pinta by Andrew Davis
/// (GSoC 2012/2013), extended for text objects per Pinta issue #1337.
/// </summary>
public static class TextObjectRenderer
{
	/// <summary>
	/// Renders every non-empty text object onto the given surface.
	/// </summary>
	public static void RenderAll (
		ImageSurface surface,
		IReadOnlyList<TextObject> objects,
		IChromeService chrome,
		bool antialias,
		int fillStyle,
		int outlineWidth,
		LineJoin lineJoin)
	{
		foreach (TextObject obj in objects) {
			if (obj.IsEmpty)
				continue;

			RenderObject (surface, obj, chrome, antialias, fillStyle, outlineWidth, lineJoin);
		}
	}

	private static void RenderObject (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		int fillStyle,
		int outlineWidth,
		LineJoin lineJoin)
	{
		TextEngine engine = obj.Engine;
		TextLayout layout = new (chrome) {
			Engine = engine,
		};

		//Fill style index matches the text tool's style dropdown:
		//0 Normal, 1 Normal and Outline, 2 Outline, 3 Fill Background.
		bool strokeText = fillStyle >= 1 && fillStyle != 3;
		bool fillText = fillStyle <= 1 || fillStyle == 3;
		bool backgroundFill = fillStyle == 3;

		using Context g = new (surface);

		FontOptions options = new ();

		if (antialias) {
			g.Antialias = Antialias.Gray; // Adjusts antialiasing JUST for the outline brush
			options.Antialias = Antialias.Gray; // Adjusts antialiasing for PangoCairo's text draw function
		} else {
			g.Antialias = Antialias.None;
			options.Antialias = Antialias.None;
		}

		g.Save ();
		PangoCairo.Functions.ContextSetFontOptions (chrome.MainWindow.GetPangoContext (), options);

		g.MoveTo (engine.Origin.X, engine.Origin.Y);
		g.SetSourceColor (engine.PrimaryColor);

		//Fill in background
		if (backgroundFill) {
			using Context g2 = new (surface);
			g2.FillRectangle (layout.GetLayoutBounds ().ToDouble (), engine.SecondaryColor);
		}

		// Draws the text stroke
		if (strokeText) {
			g.SetSourceColor (fillText ? engine.SecondaryColor : engine.PrimaryColor);
			g.LineWidth = outlineWidth;
			g.LineJoin = lineJoin;

			PangoCairo.Functions.LayoutPath (g, layout.Layout);
			g.Stroke ();

			// Position resets after g.Stroke ();
			if (fillText) {
				g.MoveTo (engine.Origin.X, engine.Origin.Y);
				g.SetSourceColor (engine.PrimaryColor);
			}
		}

		// Draws the text fill
		if (fillText) {
			PangoCairo.Functions.ShowLayout (g, layout.Layout);
		}

		g.Restore ();
	}
}
