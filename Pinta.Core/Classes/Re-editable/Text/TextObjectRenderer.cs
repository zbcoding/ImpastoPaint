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
	/// Renders every non-empty text object onto the given surface, using each
	/// object's own fill style, outline width, and line join.
	/// </summary>
	public static void RenderAll (
		ImageSurface surface,
		IReadOnlyList<TextObject> objects,
		IChromeService chrome,
		bool antialias)
	{
		foreach (TextObject obj in objects) {
			if (obj.IsEmpty)
				continue;

			RenderObject (surface, obj, chrome, antialias);
		}
	}

	/// <summary>
	/// Renders a single non-empty text object onto the given surface. When <paramref name="clip"/>
	/// is given, drawing is clipped to it — used when baking Raster-mode text so the portion
	/// outside the editing selection is not written (matching the on-canvas preview).
	/// </summary>
	public static void Render (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null)
	{
		if (!obj.IsEmpty)
			RenderObject (surface, obj, chrome, antialias, clip);
	}

	private static void RenderObject (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null)
		=> ObjectOpacity.Draw (surface, obj, target => RenderOpaque (target, obj, chrome, antialias, clip));

	private static void RenderOpaque (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null)
	{
		TextEngine engine = obj.Engine;
		TextLayout layout = new (chrome) {
			Engine = engine,
		};

		bool strokeText = obj.StrokesText;
		bool fillText = obj.FillsText;
		bool backgroundFill = obj.FillsBackground;

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

		clip?.Clip (g);

		g.MoveTo (engine.Origin.X, engine.Origin.Y);
		g.SetSourceColor (engine.PrimaryColor);

		//Fill in background
		if (backgroundFill) {
			using Context g2 = new (surface);
			clip?.Clip (g2);
			g2.FillRectangle (layout.GetLayoutBounds ().ToDouble (), engine.SecondaryColor);
		}

		// Draws the text stroke
		if (strokeText) {
			g.SetSourceColor (fillText ? engine.SecondaryColor : engine.PrimaryColor);
			g.LineWidth = obj.OutlineWidth;
			g.LineJoin = obj.LineJoin;

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
