// 
// CompoundHistoryItem.cs
//  
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
// 
// Copyright (c) 2010 Jonathan Pobst
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System.Collections.Generic;
using Cairo;

namespace Pinta.Core;

public class CompoundHistoryItem : BaseHistoryItem
{
	protected List<BaseHistoryItem> history_stack = [];
	private List<ImageSurface>? snapshots;
	private List<(ReEditableLayer layer, ImageSurface snapshot)>? sublayer_snapshots;
	private List<(UserLayer layer, ImageSurface? snapshot, bool hidden)>? mask_snapshots;

	public CompoundHistoryItem () : base ()
	{
	}

	public CompoundHistoryItem (string icon, string text) : base (icon, text)
	{
	}

	public void Push (BaseHistoryItem item)
	{
		history_stack.Add (item);
	}

	public override void Undo ()
	{
		// Children are pushed onto this compound, not onto the document history,
		// so they never get stamped by PushNewItem — propagate our document to them.
		for (int i = history_stack.Count - 1; i >= 0; i--) {
			history_stack[i].Document = Document;
			history_stack[i].Undo ();
		}
	}

	public override void Redo ()
	{
		// We want to redo the actions in the
		// opposite order than the undo order
		foreach (var item in history_stack) {
			item.Document = Document;
			item.Redo ();
		}
	}

	public void StartSnapshotOfImage ()
	{
		snapshots = [];
		sublayer_snapshots = [];
		mask_snapshots = [];
		foreach (UserLayer item in PintaCore.Workspace.ActiveDocument.Layers.UserLayers) {
			snapshots.Add (item.Surface.Clone ());
			foreach (ReEditableLayer rel in item.ReEditableLayers)
				if (rel.IsLayerSetup)
					sublayer_snapshots.Add ((rel, rel.Layer.Surface.Clone ()));
			if (item.Mask is { } mask)
				mask_snapshots.Add ((item, mask.Surface.Clone (), mask.Hidden));
		}
	}

	public void FinishSnapshotOfImage ()
	{
		for (int i = 0; i < snapshots!.Count; ++i) { // NRT - Set in StartSnapshotOfImage
			history_stack.Add (new SimpleHistoryItem (string.Empty, string.Empty, snapshots[i], i));
		}
		snapshots.Clear ();

		// The base surfaces above are index-addressable via doc.Layers, but the re-editable
		// Shape/Text sublayer surfaces are resized alongside the base and aren't. Without restoring
		// them, undoing a whole-image resize leaves the sublayer surfaces at the new size while the
		// base returns to the old size; a bundled object-bake un-apply then indexes past the smaller
		// sublayer surface and crashes (object-layer BUG 1).
		foreach (var (layer, snapshot) in sublayer_snapshots!) // NRT - Set in StartSnapshotOfImage
			history_stack.Add (new ReEditableSurfaceHistoryItem (layer, snapshot));
		sublayer_snapshots.Clear ();

		// The mask surface is resized alongside the base too (UserLayer.ResizeCanvas / Resize /
		// Crop / ApplyTransform), so it must come back with it on undo — otherwise ApplyMask reads a
		// surface of the new size against a base of the old size, or the mask stays at the new
		// geometry over the old pixels.
		foreach (var (layer, snapshot, hidden) in mask_snapshots!) // NRT - Set in StartSnapshotOfImage
			history_stack.Add (new MaskSurfaceHistoryItem (layer, snapshot, hidden));
		mask_snapshots.Clear ();
	}

	// Swaps a re-editable sublayer's surface with a stored snapshot. Full-surface swap (not a diff):
	// these sublayer surfaces are near-empty after a bake, so the memory cost is negligible.
	// ponytail: full swap; switch to SurfaceDiff if a resize with large unbaked sublayer content shows up.
	private sealed class ReEditableSurfaceHistoryItem : BaseHistoryItem
	{
		private readonly ReEditableLayer layer;
		private ImageSurface surface;

		public ReEditableSurfaceHistoryItem (ReEditableLayer layer, ImageSurface snapshot)
			: base (string.Empty, string.Empty)
		{
			this.layer = layer;
			surface = snapshot;
		}

		public override void Undo () => Swap ();
		public override void Redo () => Swap ();

		private void Swap ()
		{
			ImageSurface current = layer.Layer.Surface;
			layer.Layer.Surface = surface;
			surface = current;
		}
	}

	// Swaps a layer's mask slot (surface + hidden flag) with the stored snapshot, presence included.
	// Same shape as ReEditableSurfaceHistoryItem: full swap, since a mask is at most one canvas.
	private sealed class MaskSurfaceHistoryItem : BaseHistoryItem
	{
		private readonly UserLayer layer;
		private ImageSurface? surface;
		private bool hidden;
		private bool had_mask;

		public MaskSurfaceHistoryItem (UserLayer layer, ImageSurface? snapshot, bool hidden)
			: base (string.Empty, string.Empty)
		{
			this.layer = layer;
			surface = snapshot;
			this.hidden = hidden;
			had_mask = snapshot is not null;
		}

		public override void Undo () => Swap ();
		public override void Redo () => Swap ();

		private void Swap ()
		{
			ImageSurface? current = layer.Mask?.Surface;
			bool currentHidden = layer.Mask?.Hidden ?? false;
			bool currentHad = layer.Mask is not null;

			if (had_mask) {
				layer.ReplaceMaskSurface (surface!);
				layer.Mask!.Hidden = hidden;
			} else {
				layer.DropMask ();
			}

			surface = current;
			hidden = currentHidden;
			had_mask = currentHad;
		}
	}
}
