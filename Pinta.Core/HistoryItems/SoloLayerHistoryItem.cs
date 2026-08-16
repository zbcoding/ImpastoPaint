using System;
using System.Collections.Generic;

namespace Pinta.Core;

internal sealed class SoloLayerHistoryItem : BaseHistoryItem
{
	private readonly (UserLayer Layer, bool Before, bool After)[] visibility;

	public bool HasChanges { get; }

	public SoloLayerHistoryItem (
		string icon,
		string text,
		IReadOnlyList<UserLayer> layers,
		UserLayer soloLayer)
		: base (icon, text)
	{
		visibility = new (UserLayer, bool, bool)[layers.Count];
		bool foundSoloLayer = false;

		for (int i = 0; i < layers.Count; ++i) {
			UserLayer layer = layers[i];
			bool after = !ReferenceEquals (layer, soloLayer);
			visibility[i] = (layer, layer.Hidden, after);
			foundSoloLayer |= !after;
			HasChanges |= layer.Hidden != after;
		}

		if (!foundSoloLayer)
			throw new ArgumentException ("The solo layer must belong to the supplied layer collection.", nameof (soloLayer));
	}

	public override void Undo ()
		=> ApplyVisibility (useSoloState: false);

	public override void Redo ()
		=> ApplyVisibility (useSoloState: true);

	private void ApplyVisibility (bool useSoloState)
	{
		foreach (var item in visibility)
			item.Layer.Hidden = useSoloState ? item.After : item.Before;

		if (Document is not { } document)
			return;

		document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;
		document.Workspace.Invalidate ();
	}
}
