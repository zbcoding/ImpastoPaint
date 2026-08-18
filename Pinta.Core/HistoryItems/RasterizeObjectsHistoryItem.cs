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

	// The layer mask's pre-bake state, restored on undo: a stack bake (RasterizeModifierStack) drops
	// the mask along with the objects, so undo has to put it back or the layer renders unmasked.
	ImageSurface? mask_surface;
	bool mask_hidden;
	bool had_mask;

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

		had_mask = user_layer.Mask is not null;
		mask_surface = user_layer.Mask?.Surface.Clone ();
		mask_hidden = user_layer.Mask?.Hidden ?? false;
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

		SwapMask ();

		// Rebuild the shape edit engine's live engines from the restored object list so the active
		// layer doesn't recomposite stale/empty engines over the restored surface.
		LayerObjectSelection.RequestShapeReload (user_layer);

		workspace.Invalidate ();
	}

	// Toggles the mask slot between the stored (pre-bake) state and whatever is on the layer now.
	// Reference hand-off, not a clone: each swap moves the surface between this item and the layer.
	private void SwapMask ()
	{
		ImageSurface? liveSurface = user_layer.Mask?.Surface;
		bool liveHidden = user_layer.Mask?.Hidden ?? false;
		bool liveHad = user_layer.Mask is not null;

		if (had_mask) {
			user_layer.ReplaceMaskSurface (mask_surface!);
			user_layer.Mask!.Hidden = mask_hidden;
		} else {
			user_layer.DropMask ();
		}

		had_mask = liveHad;
		mask_surface = liveSurface;
		mask_hidden = liveHidden;
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
