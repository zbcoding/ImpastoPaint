using System;

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

	/// <summary>
	/// Fired when a layer's <see cref="UserLayer.ShapeObjects"/> were swapped from outside the
	/// shape tool (e.g. rasterize/undo). The active shape edit engine rebuilds its live engines
	/// from the restored object list so it does not composite stale engines over the surface.
	/// </summary>
	public static event Action<UserLayer>? ShapeReloadRequested;

	public static void RequestShapeReload (UserLayer layer)
		=> ShapeReloadRequested?.Invoke (layer);
}
