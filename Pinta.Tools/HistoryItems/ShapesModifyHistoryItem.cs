// 
// ShapesModifyHistoryItem.cs
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
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// A history item for when editable shapes are modified (add/move/delete a control point,
/// restyle, reorder). Object-model based: it snapshots the affected layer's
/// <see cref="UserLayer.ShapeObjects"/> and restores them on undo/redo, re-rendering the object
/// surface and rebuilding the live editing engines. This is the shape counterpart of
/// <see cref="TextHistoryItem"/>, and it is bound to a specific layer so stepping across a
/// layer-changing history item (e.g. Add Layer) can never desync onto the wrong layer.
/// </summary>
public sealed class ShapesModifyHistoryItem : BaseHistoryItem
{
	private readonly BaseEditEngine ee;
	private readonly UserLayer user_layer;

	private List<ShapeObject> shape_objects;

	private int selected_point_index, selected_shape_index;

	/// <summary>
	/// A history item for when shapes are modified.
	/// </summary>
	/// <param name="passedEE">The EditEngine being used.</param>
	/// <param name="icon">The history item's icon.</param>
	/// <param name="text">The history item's title.</param>
	public ShapesModifyHistoryItem (BaseEditEngine passedEE, string icon, string text) : base (icon, text)
	{
		ee = passedEE;
		user_layer = PintaCore.Workspace.ActiveDocument.Layers.CurrentUserLayer;

		// Capture the before-change object state. Sync from the live engines first so the snapshot
		// reflects any in-progress edits that have not yet been persisted.
		BaseEditEngine.PersistShapeObjectsIfLive (user_layer);
		shape_objects = ShapeObject.CloneAll (user_layer.ShapeObjects);

		selected_point_index = ee.SelectedPointIndex;
		selected_shape_index = ee.SelectedShapeIndex;
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
		// Snapshot the current (live) state, then swap in the stored state.
		BaseEditEngine.PersistShapeObjectsIfLive (user_layer);
		List<ShapeObject> live = ShapeObject.CloneAll (user_layer.ShapeObjects);
		user_layer.ReplaceShapes (shape_objects);
		shape_objects = live;

		// Rebuild the object surface and (if active) the live editing engines from the restored objects.
		BaseEditEngine.ReloadLayerShapes (user_layer);

		Swap (ref selected_point_index, ref ee.SelectedPointIndex);
		Swap (ref selected_shape_index, ref ee.SelectedShapeIndex);

		PintaCore.Workspace.Invalidate ();

		//Determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		BaseEditEngine.ActivateCorrespondingTool (ee.SelectedShapeIndex, true);
	}
}
