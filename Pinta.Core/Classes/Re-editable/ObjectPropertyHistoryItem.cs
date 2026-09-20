// ObjectPropertyHistoryItem.cs
//
// Undo/redo for the per-object sub-node properties (opacity, visibility, name, z-order). All of
// them are plain values on the object, and the object surface is a pure function of the object
// list, so a step is just "swap the value back and re-render" — no surface diffs.

using System;

namespace Pinta.Core;

/// <summary>
/// Undoable change of a single property of one object sub-node. The object is addressed by layer +
/// list + index rather than by reference, because the object lists are rebuilt with fresh instances
/// on every persist (a held reference goes stale; the position is stable).
/// </summary>
public sealed class ObjectPropertyHistoryItem<T> : BaseHistoryItem
{
	private readonly IWorkspaceService workspace;
	private readonly IChromeService chrome;
	private readonly UserLayer layer;
	private readonly int index;
	private readonly Func<ILayerObject, T> get;
	private readonly Action<ILayerObject, T> set;
	private T stored_value;

	public ObjectPropertyHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer layer,
		int index,
		Func<ILayerObject, T> get,
		Action<ILayerObject, T> set,
		T previousValue)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		this.index = index;
		this.get = get;
		this.set = set;
		stored_value = previousValue;
	}

	public override void Undo () => Swap ();
	public override void Redo () => Swap ();

	private void Swap ()
	{
		// The object may be gone (e.g. rasterized) if history was stepped oddly; then there is
		// nothing to swap.
		if (layer.FindObjectAt (index) is not { } obj)
			return;

		T restore = stored_value;
		stored_value = get (obj);
		set (obj, restore);

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
	}
}

/// <summary>
/// Undoable change of an object's position within its layer's object list — its z-order, since the
/// list is rendered in order.
/// </summary>
public sealed class ObjectReorderHistoryItem : BaseHistoryItem
{
	private readonly IWorkspaceService workspace;
	private readonly IChromeService chrome;
	private readonly UserLayer layer;
	private readonly int from;
	private readonly int to;

	public ObjectReorderHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer layer,
		int from,
		int to)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		this.from = from;
		this.to = to;
	}

	public override void Undo () => Move (to, from);
	public override void Redo () => Move (from, to);

	private void Move (int oldIndex, int newIndex)
	{
		if (!layer.MoveObjectAt (oldIndex, newIndex))
			return;

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
	}
}

/// <summary>
/// Undoable move of an object from one layer's object list into another's — the cross-layer
/// counterpart of <see cref="ObjectReorderHistoryItem"/>. Like it, the step is replayed rather than
/// snapshotted: the splice is exactly invertible and both layers' surfaces are pure functions of
/// their object lists, so undo re-splices and re-renders instead of restoring pixels. Replaying
/// also keeps the object instance itself, so a reference held elsewhere (the text tool edits its
/// <see cref="TextObject"/> in place) still points at the live object after an undo.
/// </summary>
public sealed class ObjectTransferHistoryItem : BaseHistoryItem
{
	private readonly IWorkspaceService workspace;
	private readonly IChromeService chrome;
	private readonly UserLayer source;
	private readonly int source_index;
	private readonly UserLayer destination;
	private readonly int destination_index;

	public ObjectTransferHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer source,
		int sourceIndex,
		UserLayer destination,
		int destinationIndex)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.source = source;
		source_index = sourceIndex;
		this.destination = destination;
		destination_index = destinationIndex;
	}

	public override void Undo () => Transfer (destination, destination_index, source, source_index);
	public override void Redo () => Transfer (source, source_index, destination, destination_index);

	private void Transfer (UserLayer from, int fromIndex, UserLayer to, int toIndex)
	{
		if (!from.TransferObjectTo (fromIndex, to, toIndex))
			return;

		// Both stacks changed, so both have to be re-rendered and both have to rebuild the live
		// shape engines that are bound to a layer by position.
		ObjectOpacity.RefreshLayer (workspace, chrome, from);
		ObjectOpacity.RefreshLayer (workspace, chrome, to);

		// The dock addresses object rows by (layer, index); every row of both layers moved.
		LayerObjectSelection.RaiseObjectsChanged ();
	}
}
