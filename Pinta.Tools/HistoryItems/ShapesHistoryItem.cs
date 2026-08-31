// 
// ShapesHistoryItem.cs
//  
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
// 
// Copyright (c) 2013 Andrew Davis, GSoC 2013 & 2014
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
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// A history item for when editable shapes are created, deleted, or finalized (baked into the
/// layer's raster). Object-model based like <see cref="ShapesModifyHistoryItem"/>: it snapshots the
/// affected layer's <see cref="UserLayer.ShapeObjects"/>, plus a diff of the base raster for the
/// finalize case where geometry is baked into the layer's pixels. Bound to a specific layer so
/// stepping across a layer-changing history item can never desync onto the wrong layer.
/// </summary>
public sealed class ShapesHistoryItem : BaseHistoryItem
{
	private readonly BaseEditEngine ee;

	private readonly UserLayer user_layer;

	private readonly SurfaceDiff? user_surface_diff;
	private ImageSurface? user_surface;

	private List<ShapeObject> shape_objects;

	private int selected_point_index, selected_shape_index;

	private readonly bool redraw_everything;

	/// <summary>
	/// A history item for when shapes are finalized.
	/// </summary>
	/// <param name="passedEE">The EditEngine being used.</param>
	/// <param name="icon">The history item's icon.</param>
	/// <param name="text">The history item's title.</param>
	/// <param name="passedUserSurface">The stored UserLayer surface.</param>
	/// <param name="passedUserLayer">The UserLayer being modified.</param>
	/// <param name="passedSelectedPointIndex">The selected point's index.</param>
	/// <param name="passedSelectedShapeIndex">The selected point's shape index.</param>
	/// <param name="passedRedrawEverything">Whether every shape should be redrawn when undoing (e.g. finalization).</param>
	public ShapesHistoryItem (
		BaseEditEngine passedEE,
		string icon,
		string text,
		ImageSurface passedUserSurface,
		UserLayer passedUserLayer,
		int passedSelectedPointIndex,
		int passedSelectedShapeIndex,
		bool passedRedrawEverything
	)
		: base (icon, text)
	{
		ee = passedEE;

		user_layer = passedUserLayer;

		// SurfaceDiff.Create only reads original's pixels and never releases it, so a successful
		// diff has to dispose the caller's fresh Clone() itself or it leaks a full-canvas surface
		// (same pattern as SimpleHistoryItem/TextHistoryItem/RasterizeObjectsHistoryItem in Core).
		user_surface_diff = SurfaceDiff.Create (passedUserSurface, user_layer.Surface, true);

		if (user_surface_diff == null) {
			user_surface = passedUserSurface;
		} else {
			passedUserSurface.Dispose ();
		}

		// Capture the before-change object state. Sync from the live engines first so the snapshot
		// reflects any in-progress edits that have not yet been persisted.
		BaseEditEngine.PersistShapeObjectsIfLive (user_layer);
		shape_objects = ShapeObject.CloneAll (user_layer.ShapeObjects);

		selected_point_index = passedSelectedPointIndex;
		selected_shape_index = passedSelectedShapeIndex;

		redraw_everything = passedRedrawEverything;
	}

	public override void Undo ()
	{
		Swap (redraw_everything);
	}

	public override void Redo ()
	{
		Swap (false);
	}

	private void Swap (bool redraw)
	{
		// Swap the base raster (only finalization actually changes it; for create/delete the diff
		// is empty and this is a no-op).
		ImageSurface surf = user_layer.Surface;

		if (user_surface_diff != null) {
			user_surface_diff.ApplyAndSwap (surf);
			PintaCore.Workspace.Invalidate (user_surface_diff.GetBounds ());
		} else {
			user_layer.Surface = user_surface!; // NRT - userSurface will be not-null in this branch
			user_surface = surf;
			PintaCore.Workspace.Invalidate ();
		}

		// Snapshot the current (live) object state, then swap in the stored state.
		BaseEditEngine.PersistShapeObjectsIfLive (user_layer);
		List<ShapeObject> live = ShapeObject.CloneAll (user_layer.ShapeObjects);
		user_layer.ReplaceShapes (shape_objects);
		shape_objects = live;

		// Rebuild the object surface and (if active) the live editing engines from the restored objects.
		BaseEditEngine.ReloadLayerShapes (user_layer);

		Swap (ref selected_point_index, ref ee.SelectedPointIndex);
		Swap (ref selected_shape_index, ref ee.SelectedShapeIndex);

		//Determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		if (BaseEditEngine.ActivateCorrespondingTool (ee.SelectedShapeIndex, true) != null) {
			//The currently active tool now matches the shape's corresponding tool.

			if (redraw) {
				((ShapeTool?) PintaCore.Tools.CurrentTool)?.EditEngine.DrawAllShapes ();
			}
		}
	}
}
