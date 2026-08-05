// ObjectRasterizer.cs
//
// Bakes a subset of a layer's live editable objects (shapes + text) into its base raster as one
// undoable step, keeping the rest editable. Used by:
//   - the Edit > Cut/Erase path, which rasterizes only the objects the selection overlaps (after
//     prompting the user), and
//   - the layers dock "Rasterize All Objects" menu item, which rasterizes every object on a layer.
//
// The object surfaces already equal the render of the object lists (the object-layer invariant), so
// baking is: render the chosen objects onto the base raster, drop them from the object lists, then
// re-render the object surfaces from what remains.

using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core;

public static class ObjectRasterizer
{
	/// <summary>
	/// Returns the indices of the shapes/text objects on <paramref name="layer"/> whose bounds overlap
	/// <paramref name="region"/> (a selection's bounding box). Used to rasterize only what an op touches.
	/// </summary>
	public static void FindIntersecting (
		UserLayer layer,
		RectangleD region,
		out List<int> shapeIndices,
		out List<int> textIndices)
	{
		shapeIndices = [];
		textIndices = [];

		for (int i = 0; i < layer.ShapeObjects.Count; ++i)
			if (Overlaps (region, layer.ShapeObjects[i].GetApproximateBounds ()))
				shapeIndices.Add (i);

		for (int i = 0; i < layer.TextObjects.Count; ++i) {
			RectangleI b = layer.TextObjects[i].TextBounds;
			if (Overlaps (region, new (b.X, b.Y, b.Width, b.Height)))
				textIndices.Add (i);
		}
	}

	private static bool Overlaps (in RectangleD a, in RectangleD b)
		=> a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

	/// <summary>Display labels for the given objects, mirroring the layers dock naming.</summary>
	public static IEnumerable<string> Describe (
		UserLayer layer,
		IReadOnlyList<int> shapeIndices,
		IReadOnlyList<int> textIndices)
	{
		foreach (int i in shapeIndices)
			yield return ShapeLabel (layer.ShapeObjects[i]);
		foreach (int _ in textIndices)
			yield return Translations.GetString ("Text");
	}

	private static string ShapeLabel (ShapeObject s)
		=> string.IsNullOrEmpty (s.Name) ? ShapeTypeName (s.ShapeType) : s.Name;

	private static string ShapeTypeName (ShapeObjectType type) => type switch {
		ShapeObjectType.Ellipse => Translations.GetString ("Ellipse"),
		ShapeObjectType.RoundedLineSeries => Translations.GetString ("Rounded Rectangle"),
		ShapeObjectType.Triangle => Translations.GetString ("Triangle"),
		ShapeObjectType.OpenLineCurveSeries => Translations.GetString ("Line/Curve"),
		_ => Translations.GetString ("Shape"),
	};

	/// <summary>
	/// Prompts the user to confirm rasterizing the listed objects. Returns true if they accept. Runs a
	/// nested loop (blocking) so it fits the synchronous action handlers that call it.
	/// </summary>
	public static bool Confirm (IChromeService chrome, IReadOnlyList<string> labels)
	{
		const int max_listed = 12;
		string list = string.Join ("\n", labels.Take (max_listed).Select (l => "• " + l));
		if (labels.Count > max_listed)
			list += "\n" + Translations.GetString ("…and {0} more", labels.Count - max_listed);

		string body = Translations.GetString ("To perform this action, these objects must be rasterized (baked into the layer's pixels and no longer editable):")
			+ "\n\n" + list;

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (
			chrome.MainWindow,
			Translations.GetString ("Rasterize Objects?"),
			body);

		const string cancel_response = "cancel";
		const string rasterize_response = "rasterize";
		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		dialog.AddResponse (rasterize_response, Translations.GetString ("_Rasterize"));
		dialog.SetResponseAppearance (rasterize_response, Adw.ResponseAppearance.Destructive);
		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = rasterize_response;

		return dialog.RunBlocking () == rasterize_response;
	}

	/// <summary>
	/// Bakes the given shapes/text objects into <paramref name="layer"/>'s base raster as one undoable
	/// step, leaving the rest editable. Returns true if anything was baked.
	/// </summary>
	public static bool RasterizeSubset (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		UserLayer layer,
		IReadOnlyList<int> shapeIndices,
		IReadOnlyList<int> textIndices)
	{
		if (shapeIndices.Count == 0 && textIndices.Count == 0)
			return false;

		// Snapshot the full pre-bake state for undo (base + both object surfaces + both object lists).
		ImageSurface baseBefore = layer.Surface.Clone ();
		ImageSurface shapeBefore = layer.ShapeLayer.Layer.Surface.Clone ();
		ImageSurface textBefore = layer.TextLayer.Layer.Surface.Clone ();
		var shapesBefore = ShapeObject.CloneAll (layer.ShapeObjects);
		var textObjBefore = TextObject.CloneAll (layer.TextObjects);

		// Bake the chosen objects onto the base raster (shapes via the Tools renderer seam, text via
		// the Core text renderer) BEFORE removing them from the lists — the renderers read by index.
		LayerObjectSelection.RenderShapeSubset (layer.Surface, layer, shapeIndices);
		foreach (int i in textIndices)
			TextObjectRenderer.Render (layer.Surface, layer.TextObjects[i], chrome, antialias: true);

		// Drop the baked objects (descending index so earlier removals don't shift later ones).
		foreach (int i in shapeIndices.OrderByDescending (i => i))
			layer.ShapeObjects.RemoveAt (i);
		foreach (int i in textIndices.OrderByDescending (i => i))
			layer.TextObjects.RemoveAt (i);

		// Re-render the object surfaces from what remains so the baked pixels aren't composited twice.
		// Shapes: the Tools seam redraws the ShapeLayer surface and rebuilds the live editing engines.
		// Text: redraw the TextLayer surface here in Core from the remaining objects.
		LayerObjectSelection.RequestShapeReload (layer);
		layer.TextLayer.Layer.Surface.Clear ();
		TextObjectRenderer.RenderAll (layer.TextLayer.Layer.Surface, layer.TextObjects, chrome, antialias: true);

		doc.History.PushNewItem (new RasterizeObjectsHistoryItem (
			workspace,
			Resources.Icons.ImageFlatten,
			Translations.GetString ("Rasterize Objects"),
			baseBefore, shapeBefore, textBefore,
			shapesBefore, textObjBefore, layer));

		workspace.Invalidate ();
		return true;
	}
}
