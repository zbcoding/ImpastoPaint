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
	private readonly bool is_text;
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
		bool isText,
		int index,
		Func<ILayerObject, T> get,
		Action<ILayerObject, T> set,
		T previousValue)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		is_text = isText;
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
		if (layer.FindObject (is_text, index) is not { } obj)
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
	private readonly bool is_text;
	private readonly int from;
	private readonly int to;

	public ObjectReorderHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer layer,
		bool isText,
		int from,
		int to)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		is_text = isText;
		this.from = from;
		this.to = to;
	}

	public override void Undo () => Move (to, from);
	public override void Redo () => Move (from, to);

	private void Move (int oldIndex, int newIndex)
	{
		if (!layer.MoveObject (is_text, oldIndex, newIndex))
			return;

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
	}
}
