// RasterizeObjectsHistoryItem.cs
//
// Undoable "bake the layer's editable objects into its base raster" step. Emitted
// before a destructive raster op (cut/erase) touches a layer that still holds live
// Object-mode shapes/text, so those objects become real pixels first. Mirrors
// TextHistoryItem: it swaps the base surface, both object surfaces, and both object
// lists, and asks the shape edit engine to rebuild its live engines from the restored
// object list (the object-layer seam).

using System.Collections.Generic;
using Cairo;

namespace Pinta.Core;

public sealed class RasterizeObjectsHistoryItem : BaseHistoryItem
{
	readonly UserLayer user_layer;
	readonly IWorkspaceService workspace;

	readonly SurfaceDiff? base_diff;
	ImageSurface? base_surface;

	readonly SurfaceDiff? shape_diff;
	ImageSurface? shape_surface;

	readonly SurfaceDiff? text_diff;
	ImageSurface? text_surface;

	List<ShapeObject> shape_objects;
	List<TextObject> text_objects;

	/// <param name="passedBaseSurface">The layer's base raster before baking.</param>
	/// <param name="passedShapeSurface">The layer's ShapeLayer surface before baking.</param>
	/// <param name="passedTextSurface">The layer's TextLayer surface before baking.</param>
	/// <param name="passedShapeObjects">The layer's shape objects before baking.</param>
	/// <param name="passedTextObjects">The layer's text objects before baking.</param>
	public RasterizeObjectsHistoryItem (
		IWorkspaceService workspace,
		string icon,
		string text,
		ImageSurface passedBaseSurface,
		ImageSurface passedShapeSurface,
		ImageSurface passedTextSurface,
		IReadOnlyList<ShapeObject> passedShapeObjects,
		IReadOnlyList<TextObject> passedTextObjects,
		UserLayer passedUserLayer
	)
		: base (icon, text)
	{
		this.workspace = workspace;
		user_layer = passedUserLayer;

		base_diff = SurfaceDiff.Create (passedBaseSurface, user_layer.Surface, force: true);
		if (base_diff == null)
			base_surface = passedBaseSurface;

		shape_diff = SurfaceDiff.Create (passedShapeSurface, user_layer.ShapeLayer.Layer.Surface, force: true);
		if (shape_diff == null)
			shape_surface = passedShapeSurface;

		text_diff = SurfaceDiff.Create (passedTextSurface, user_layer.TextLayer.Layer.Surface, force: true);
		if (text_diff == null)
			text_surface = passedTextSurface;

		shape_objects = ShapeObject.CloneAll (passedShapeObjects);
		text_objects = TextObject.CloneAll (passedTextObjects);
	}

	public override void Undo () => Swap ();
	public override void Redo () => Swap ();

	private void Swap ()
	{
		SwapSurface (base_diff, ref base_surface, user_layer.Surface, s => user_layer.Surface = s);
		SwapSurface (shape_diff, ref shape_surface, user_layer.ShapeLayer.Layer.Surface, s => user_layer.ShapeLayer.Layer.Surface = s);
		SwapSurface (text_diff, ref text_surface, user_layer.TextLayer.Layer.Surface, s => user_layer.TextLayer.Layer.Surface = s);

		List<ShapeObject> oldShapes = shape_objects;
		shape_objects = ShapeObject.CloneAll (user_layer.ShapeObjects);
		user_layer.ShapeObjects.Clear ();
		user_layer.ShapeObjects.AddRange (oldShapes);

		List<TextObject> oldText = text_objects;
		text_objects = TextObject.CloneAll (user_layer.TextObjects);
		user_layer.TextObjects.Clear ();
		user_layer.TextObjects.AddRange (oldText);

		// Rebuild the shape edit engine's live engines from the restored object list so the active
		// layer doesn't recomposite stale/empty engines over the restored surface.
		LayerObjectSelection.RequestShapeReload (user_layer);

		workspace.Invalidate ();
	}

	private static void SwapSurface (SurfaceDiff? diff, ref ImageSurface? stored, ImageSurface current, System.Action<ImageSurface> setter)
	{
		if (diff != null) {
			diff.ApplyAndSwap (current);
		} else {
			setter (stored!); // NRT - stored is set whenever diff is null
			stored = current;
		}
	}
}
