using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Undoes/redoes the coordinate shift <see cref="UserLayer.TranslateObjects"/> applies to a set of
/// layers when a canvas resize grows the canvas without baking their live shapes/text. A plain
/// re-application of the inverse offset, rather than a snapshot: the shift is exactly invertible, so
/// there is nothing a snapshot would capture that negating the delta does not already restore.
/// </summary>
public sealed class TranslateObjectsHistoryItem : BaseHistoryItem
{
	private readonly IReadOnlyList<UserLayer> layers;
	private readonly PointD delta;

	public TranslateObjectsHistoryItem (IReadOnlyList<UserLayer> layers, PointD delta)
		: base ()
	{
		this.layers = layers;
		this.delta = delta;
	}

	public override void Undo ()
	{
		foreach (UserLayer layer in layers) {
			layer.TranslateObjects (new PointD (-delta.X, -delta.Y));
			// Same reload a shape tool needs after the forward shift below - its own copy of the
			// shapes' control points (SEngines) has to be rebuilt from the just-moved ShapeObjects.
			LayerObjectSelection.RequestShapeReload (layer);
		}
	}

	public override void Redo ()
	{
		foreach (UserLayer layer in layers) {
			layer.TranslateObjects (delta);
			LayerObjectSelection.RequestShapeReload (layer);
		}
	}
}
