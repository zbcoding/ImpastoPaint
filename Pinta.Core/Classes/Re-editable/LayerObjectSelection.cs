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
}
