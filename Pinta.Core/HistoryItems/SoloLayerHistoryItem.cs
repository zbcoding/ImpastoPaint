using System;
using System.Collections.Generic;

namespace Pinta.Core;

internal sealed class SoloLayerHistoryItem : BaseHistoryItem
{
	private readonly (UserLayer Layer, bool Before, bool After)[] visibility;

	public bool HasChanges { get; }

	public SoloLayerHistoryItem (
		IReadOnlyList<UserLayer> layers,
		UserLayer soloLayer)
		: this (layers, soloLayer, IsSoloState (layers, soloLayer))
	{
	}

	private SoloLayerHistoryItem (
		IReadOnlyList<UserLayer> layers,
		UserLayer soloLayer,
		bool showAllLayers)
		: base (
			Resources.StandardIcons.ViewReveal,
			showAllLayers
				? Translations.GetString ("Show All Layers")
				: Translations.GetString ("Solo Layer"))
	{
		visibility = new (UserLayer, bool, bool)[layers.Count];
		bool foundSoloLayer = false;

		for (int i = 0; i < layers.Count; ++i) {
			UserLayer layer = layers[i];
			bool isSoloLayer = ReferenceEquals (layer, soloLayer);
			bool after = showAllLayers ? false : !isSoloLayer;
			visibility[i] = (layer, layer.Hidden, after);
			foundSoloLayer |= isSoloLayer;
			HasChanges |= layer.Hidden != after;
		}

		if (!foundSoloLayer)
			throw new ArgumentException ("The solo layer must belong to the supplied layer collection.", nameof (soloLayer));
	}

	internal static bool IsSoloState (
		IReadOnlyList<UserLayer> layers,
		UserLayer soloLayer)
	{
		bool foundSoloLayer = false;

		for (int i = 0; i < layers.Count; ++i) {
			UserLayer layer = layers[i];

			if (ReferenceEquals (layer, soloLayer)) {
				foundSoloLayer = true;
				if (layer.Hidden)
					return false;
			} else if (!layer.Hidden) {
				return false;
			}
		}

		return foundSoloLayer;
	}

	public override void Undo ()
		=> ApplyVisibility (useAfterState: false);

	public override void Redo ()
		=> ApplyVisibility (useAfterState: true);

	private void ApplyVisibility (bool useAfterState)
	{
		foreach (var item in visibility)
			item.Layer.Hidden = useAfterState ? item.After : item.Before;

		if (Document is not { } document)
			return;

		document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;
		document.Workspace.Invalidate ();
	}
}
