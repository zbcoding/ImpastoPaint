// RasterizeObjectsHistoryItem.cs
//
// Undoable "bake the layer's editable objects into its base raster" step. Emitted
// before a destructive raster op (cut/erase) touches a layer that still holds live
// Object-mode shapes/text, so those objects become real pixels first. Mirrors
// TextHistoryItem: it swaps the base surface, the single object surface, and the
// unified object list, and asks the shape edit engine to rebuild its live engines
// from the restored object list (the object-layer seam).

using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core;

public sealed class RasterizeObjectsHistoryItem : BaseHistoryItem
{
	readonly UserLayer user_layer;
	readonly IWorkspaceService workspace;

	readonly SurfaceDiff? base_diff;
	ImageSurface? base_surface;

	readonly SurfaceDiff? object_diff;
	ImageSurface? object_surface;

	List<ILayerObject> objects;

	/// <param name="passedBaseSurface">The layer's base raster before baking.</param>
	/// <param name="passedObjectSurface">The layer's ObjectLayer surface before baking.</param>
	/// <param name="passedObjects">The layer's unified object list before baking.</param>
	public RasterizeObjectsHistoryItem (
		IWorkspaceService workspace,
		string icon,
		string text,
		ImageSurface passedBaseSurface,
		ImageSurface passedObjectSurface,
		IReadOnlyList<ILayerObject> passedObjects,
		UserLayer passedUserLayer
	)
		: base (icon, text)
	{
		this.workspace = workspace;
		user_layer = passedUserLayer;

		base_diff = SurfaceDiff.Create (passedBaseSurface, user_layer.Surface, force: true);
		if (base_diff == null)
			base_surface = passedBaseSurface;

		object_diff = SurfaceDiff.Create (passedObjectSurface, user_layer.ObjectLayer.Layer.Surface, force: true);
		if (object_diff == null)
			object_surface = passedObjectSurface;

		objects = ObjectOpacity.CloneAll (passedObjects);
	}

	public override void Undo () => Swap ();
	public override void Redo () => Swap ();

	private void Swap ()
	{
		SwapSurface (base_diff, ref base_surface, user_layer.Surface, s => user_layer.Surface = s);
		SwapSurface (object_diff, ref object_surface, user_layer.ObjectLayer.Layer.Surface, s => user_layer.ObjectLayer.Layer.Surface = s);

		List<ILayerObject> old = objects;
		objects = ObjectOpacity.CloneAll (user_layer.Objects);
		user_layer.Objects.Clear ();
		user_layer.Objects.AddRange (old);

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
