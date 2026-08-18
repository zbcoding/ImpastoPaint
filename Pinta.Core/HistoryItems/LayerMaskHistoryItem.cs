// LayerMaskHistoryItem.cs
//
// Undoable add/remove of a layer's mask slot. Emitted when the user adds a mask to a layer (the
// slot changes from absent to an empty, fully-transparent mask) or deletes one (the slot changes
// from a mask back to absent). Undo restores the prior mask state; redo re-applies the change.

using Cairo;

namespace Pinta.Core;

public sealed class LayerMaskHistoryItem : BaseHistoryItem
{
	private readonly UserLayer layer;
	private readonly IWorkspaceService workspace;

	// The pre-change mask state, restored on undo.
	private ImageSurface? undo_surface;
	private readonly bool undo_had_mask;
	private readonly bool undo_hidden;

	// The post-change mask state (captured once the change was applied), restored on redo.
	private readonly ImageSurface? redo_surface;
	private readonly bool redo_had_mask;
	private readonly bool redo_hidden;

	/// <param name="beforeSurface">The layer's mask surface before the change, or null if there was no mask.</param>
	/// <param name="afterSurface">The layer's mask surface after the change, or null if the change removed the mask.</param>
	public LayerMaskHistoryItem (
		IWorkspaceService workspace,
		string icon,
		string text,
		UserLayer layer,
		ImageSurface? beforeSurface,
		ImageSurface? afterSurface,
		bool beforeHidden = false,
		bool afterHidden = false)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.layer = layer;

		undo_surface = beforeSurface;
		undo_had_mask = beforeSurface is not null;
		undo_hidden = beforeHidden;
		redo_surface = afterSurface;
		redo_had_mask = afterSurface is not null;
		redo_hidden = afterHidden;
	}

	public override void Undo () => Set (undo_surface, undo_had_mask, undo_hidden);
	public override void Redo () => Set (redo_surface, redo_had_mask, redo_hidden);

	private void Set (ImageSurface? surface, bool hadMask, bool hidden)
	{
		if (hadMask) {
			// Restore a copy, not the stored surface itself: the layer's mask surface is the live
			// paint target, so handing the stored one over and keeping the reference would let a
			// later paint mutate what the next Undo/Redo replays.
			layer.ReplaceMaskSurface (EffectModifierNode.CopyOf (surface!));
			layer.Mask!.Hidden = hidden;
		} else {
			layer.DropMask ();
		}

		// The layer's rendered result changed (or the mask stopped/started applying).
		ObjectOpacity.RefreshLayer (workspace, PintaCore.Chrome, layer);
		workspace.Invalidate ();
	}
}

/// <summary>Undoable show/hide of a layer's mask (its <see cref="LayerMask.Hidden"/> flag).</summary>
public sealed class LayerMaskVisibleHistoryItem : BaseHistoryItem
{
	private readonly UserLayer layer;
	private readonly IWorkspaceService workspace;
	private readonly bool hidden;

	public LayerMaskVisibleHistoryItem (
		IWorkspaceService workspace,
		string icon,
		string text,
		UserLayer layer,
		bool newHidden)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.layer = layer;
		hidden = newHidden;
	}

	public override void Undo () => Apply (!hidden);
	public override void Redo () => Apply (hidden);

	private void Apply (bool h)
	{
		if (layer.Mask is not { } mask)
			return;

		mask.Hidden = h;
		ObjectOpacity.RefreshLayer (workspace, PintaCore.Chrome, layer);
		workspace.Invalidate ();
	}
}
