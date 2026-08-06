using System;
using System.Collections.Generic;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Cross-assembly seam for selecting an object sub-layer from the layers dock.
/// The dock lives in Pinta.Gui.Widgets and the shape editing lives in Pinta.Tools;
/// neither references the other, so they meet here in Core. The dock raises the
/// request; the active shape edit engine fulfills it (activate the shape's tool,
/// select it, show its control points).
/// </summary>
public static class LayerObjectSelection
{
	/// <summary>Fired with the object's layer and its index in <see cref="UserLayer.ShapeObjects"/>.</summary>
	public static event Action<UserLayer, int>? ShapeSelectRequested;

	public static void RequestShapeSelect (UserLayer layer, int shapeIndex)
		=> ShapeSelectRequested?.Invoke (layer, shapeIndex);

	/// <summary>Fired with the object's layer and its index in <see cref="UserLayer.TextObjects"/>.
	/// The Text tool fulfills it: activate itself, make the layer current, and start editing the
	/// object so its handles show — the text counterpart of <see cref="ShapeSelectRequested"/>.</summary>
	public static event Action<UserLayer, int>? TextSelectRequested;

	public static void RequestTextSelect (UserLayer layer, int textIndex)
		=> TextSelectRequested?.Invoke (layer, textIndex);

	/// <summary>
	/// Fired when a layer's <see cref="UserLayer.ShapeObjects"/> were swapped from outside the
	/// shape tool (e.g. rasterize/undo). The active shape edit engine rebuilds its live engines
	/// from the restored object list so it does not composite stale engines over the surface.
	/// </summary>
	public static event Action<UserLayer>? ShapeReloadRequested;

	public static void RequestShapeReload (UserLayer layer)
		=> ShapeReloadRequested?.Invoke (layer);

	/// <summary>
	/// Renders specific shapes (by index in <see cref="UserLayer.ShapeObjects"/>) of a layer onto an
	/// arbitrary surface. Set by Pinta.Tools (which owns the shape renderer); Core calls it to bake a
	/// subset of shapes into a layer's base raster during selective rasterize.
	/// </summary>
	public static Action<ImageSurface, UserLayer, IReadOnlyList<int>>? ShapeSubsetRenderer;

	public static void RenderShapeSubset (ImageSurface surface, UserLayer layer, IReadOnlyList<int> shapeIndices)
		=> ShapeSubsetRenderer?.Invoke (surface, layer, shapeIndices);

	/// <summary>
	/// Fired when the user clears the selection (Deselect). The shape tool bakes any shape that was
	/// drawn clipped to that selection (its frozen <see cref="ShapeObject.Clip"/>) into the layer's
	/// raster — so a partially-clipped shape becomes plain pixels instead of an editable object that
	/// keeps an invisible clip boundary. Shapes lying fully inside their clip just drop it and stay
	/// editable.
	/// </summary>
	public static event Action? SelectionCleared;

	public static void RaiseSelectionCleared ()
		=> SelectionCleared?.Invoke ();
}
