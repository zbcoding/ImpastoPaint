// 
// SimpleHistoryItem.cs
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

using System;
using Cairo;

namespace Pinta.Core;

public sealed class SimpleHistoryItem : BaseHistoryItem
{
	private readonly SurfaceDiff? surface_diff;
	ImageSurface? old_surface;
	int layer_index;
	// When true, this item snapshots and restores the layer's mask surface instead of its raster.
	// Set by paint tools while the user is editing a layer's mask (see UserLayer.PaintSurface).
	private bool target_is_mask;

	public SimpleHistoryItem (string icon, string text, ImageSurface oldSurface, int layerIndex, bool targetIsMask = false) : base (icon, text)
	{
		var doc = PintaCore.Workspace.ActiveDocument;

		layer_index = layerIndex;
		target_is_mask = targetIsMask;

		surface_diff = SurfaceDiff.Create (oldSurface, TargetSurface (doc));

		// If the diff was too big, store the original surface, else, dispose it
		if (surface_diff == null)
			old_surface = oldSurface;
		else
			oldSurface.Dispose ();
	}

	public SimpleHistoryItem (string icon, string text) : base (icon, text)
	{
	}

	public override void Undo ()
	{
		Swap ();
	}

	public override void Redo ()
	{
		Swap ();
	}

	private void Swap ()
	{
		var doc = this.Document!;

		// Grab the original surface
		ImageSurface surf = TargetSurface (doc);

		if (surface_diff != null) {
			surface_diff.ApplyAndSwap (surf);
			PintaCore.Workspace.Invalidate (surface_diff.GetBounds ());
		} else {
			// Undo to the "old" surface
			UserLayer layer = doc.Layers[layer_index];
			if (target_is_mask)
				layer.ReplaceMaskSurface (old_surface!);
			else
				layer.Surface = old_surface!; // NRT - Will be not-null if surface_diff is null

			// Store the original surface for Redo
			old_surface = surf;

			PintaCore.Workspace.Invalidate ();
		}
	}

	// The surface this item swaps: the layer's raster, or its mask when target_is_mask. A mask
	// gone when this runs would be a sequencing bug elsewhere (undo/redo replaying out of order,
	// or against a layer whose mask a later step removed) - fail loudly rather than silently
	// applying a mask-derived diff to the layer's colour surface, which would corrupt raster
	// pixels instead. See LayerMaskHistoryItem.Set for the analogous mask-presence tracking.
	private ImageSurface TargetSurface (Document doc)
	{
		UserLayer layer = doc.Layers[layer_index];
		if (!target_is_mask)
			return layer.Surface;

		return layer.Mask?.Surface
			?? throw new InvalidOperationException ($"SimpleHistoryItem targets layer {layer_index}'s mask, but it has none.");
	}

	public void TakeSnapshotOfLayer (int layerIndex)
	{
		var doc = PintaCore.Workspace.ActiveDocument;

		layer_index = layerIndex;
		target_is_mask = false;
		old_surface = doc.Layers[layerIndex].Surface.Clone ();
	}

	public void TakeSnapshotOfLayer (UserLayer layer, bool targetIsMask = false)
	{
		var doc = PintaCore.Workspace.ActiveDocument;

		layer_index = doc.Layers.IndexOf (layer);
		target_is_mask = targetIsMask;
		old_surface = (targetIsMask
			? (layer.Mask?.Surface ?? layer.Surface)
			: layer.Surface).Clone ();
	}
}
