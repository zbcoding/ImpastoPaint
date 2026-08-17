// LayerObjectsHistoryItem.cs
//
// Swaps a layer's whole object list, so adding, removing or re-editing a modifier node is one
// undoable step. The list is small (a handful of nodes and shape/text objects) and cloning it is
// cheap next to the surfaces, so the whole-list swap costs less than tracking per-node deltas.

using System.Collections.Generic;

namespace Pinta.Core;

public sealed class LayerObjectsHistoryItem : BaseHistoryItem
{
	private readonly IWorkspaceService workspace;
	private readonly IChromeService chrome;
	private readonly UserLayer layer;
	private List<ILayerObject> stored_objects;

	public LayerObjectsHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer layer,
		IReadOnlyList<ILayerObject> objectsBefore)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		stored_objects = ObjectOpacity.CloneAll (objectsBefore);
	}

	public override void Undo () => Swap ();
	public override void Redo () => Swap ();

	private void Swap ()
	{
		List<ILayerObject> current = ObjectOpacity.CloneAll (layer.Objects);

		layer.Objects.Clear ();
		layer.Objects.AddRange (stored_objects);
		stored_objects = current;

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
	}
}
