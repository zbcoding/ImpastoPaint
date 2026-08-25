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
	/// outside the editing selection is not written (matching the on-canvas preview). When
	/// <paramref name="bakeMode"/> is true the object is drawn with Normal blend (its own pixels) —
	/// correct when compositing onto a base raster; the blend mode only composites against fellow
	/// objects on the layer's object surface.
	/// </summary>
	public static void Render (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null,
		bool bakeMode = false)
	{
		if (!obj.IsEmpty)
			RenderObject (surface, obj, chrome, antialias, clip, bakeMode);
	}

	private static void RenderObject (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null,
		bool bakeMode = false)
	{
		if (bakeMode)
			ObjectOpacity.DrawNormalForBake (surface, obj, target => RenderOpaque (target, obj, chrome, antialias, clip));
		else
			ObjectOpacity.Draw (surface, obj, target => RenderOpaque (target, obj, chrome, antialias, clip));
	}

	private static void RenderOpaque (
		ImageSurface surface,
		TextObject obj,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null)
		=> RenderOpaque (surface, obj, new TextLayout (chrome), chrome, antialias, clip);

	/// <summary>
	/// Draws one text object's ink onto <paramref name="surface"/>: background fill, stroke, then
	/// fill, rotated about the object's origin. The text tool draws the object it is editing
	/// through here too, passing its own reused <paramref name="layout"/> - the live text on screen
	/// and the text every other path renders (the layer composite, a bake, the paint bucket's hit
	/// probe) have to be the same pixels, and used to be two copies of this sequence that only the
	/// tool's half rotated.
	/// </summary>
	public static void RenderOpaque (
		ImageSurface surface,
		TextObject obj,
		TextLayout layout,
		IChromeService chrome,
		bool antialias,
		DocumentSelection? clip = null)
	{
		TextEngine engine = obj.Engine;
		layout.Engine = engine;

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

		//The clip is a canvas-space region (a frozen selection), so it is applied before the
		//rotation rather than through it.
		clip?.Clip (g);
		ApplyRotation (g, obj);

		g.MoveTo (engine.Origin.X, engine.Origin.Y);
		g.SetSourceColor (engine.PrimaryColor);

		//Fill in background
		if (backgroundFill) {
			using Context g2 = new (surface);
			clip?.Clip (g2);
			ApplyRotation (g2, obj);
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

	/// <summary>
	/// Turns <paramref name="g"/> about the object's origin so everything drawn after it - ink,
	/// and the tool's caret and selection highlight - lands rotated with the object.
	/// </summary>
	public static void ApplyRotation (Context g, TextObject obj)
	{
		if (obj.Rotation == 0)
			return;

		PointD pivot = obj.RotationPivot;
		g.Translate (pivot.X, pivot.Y);
		g.Rotate (obj.RotationRadians);
		g.Translate (-pivot.X, -pivot.Y);
	}
}
