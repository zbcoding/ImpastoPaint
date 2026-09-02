//
// MoveSelectedTool.cs
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
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MoveSelectedTool : BaseTransformTool
{
	private MovePixelsHistoryItem? hist;
	private DocumentSelection? original_selection;

	// Set when the user declined the rasterize prompt below. The gesture has already begun by then
	// (BaseTransformTool sets is_dragging before calling OnStartTransform), so the move is neutered
	// here rather than unwound there.
	private bool move_declined;

	// Whether a real selection was active before the drag started, captured in OnStartTransform.
	// When false, "no selection" was standing in for "move the whole layer" (see the fallback
	// below) and Selection/PreviousSelection must come out of the drag exactly as they went in -
	// otherwise the untouched, canvas-sized placeholder becomes a real, shifted selection that
	// silently clips every later paint tool to wherever the drag happened to end.
	private bool had_real_selection;
	private readonly Matrix original_transform = CairoExtensions.CreateIdentityMatrix ();

	private readonly SystemManager system_manager;
	public MoveSelectedTool (IServiceProvider services) : base (services)
	{
		system_manager = services.GetService<SystemManager> ();
	}

	public override string Name => Translations.GetString ("Move Selected Pixels");
	public override string Icon => Pinta.Resources.Icons.ToolMove;
	// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
	public override string StatusBarText => Translations.GetString (
		"Left click and drag the selection to move selected content." +
		"\nHold {0} to scale instead of move." +
		"\nRight click and drag the selection to rotate selected content." +
		"\nHold Shift to rotate in steps." +
		"\nUse arrow keys to move selected content by a single pixel.",
		system_manager.CtrlLabel ());

	// Rendered at 2x the default 16px and with a centered hotspot so the enlarged
	// four-way cross still points at the pixel under the cursor.
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.ToolMoveCursor, 32), 16, 16, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_M);
	public override int Priority => 5;

	public override bool WritesToCurrentLayer
		=> true;

	protected override RectangleD GetSourceRectangle (Document document)
		=> document.Selection.GetBounds ();

	protected override void OnStartTransform (Document document)
	{
		base.OnStartTransform (document);

		had_real_selection = document.Selection.Visible;

		// If there is no selection, select the whole image.
		if (document.Selection.SelectionPolygons.Count == 0) {
			RectangleD imageBounds = new (0, 0, document.ImageSize.Width, document.ImageSize.Height);
			document.Selection.CreateRectangleSelection (imageBounds);
		}

		// The lift below reads the layer's base raster and clears the moved region from it. Effect
		// nodes, shapes and text are not in that raster — the canvas shows them through the layer's
		// composite — so without this the drag carries un-effected pixels away, leaves the node
		// applying over the hole it left, and moves nothing at all when the selection covered a text
		// or shape object. Bake what the selection reaches, then lift.
		move_declined = !ObjectRasterizer.PrepareForSelectionRasterOp (
			document,
			PintaCore.Workspace,
			PintaCore.Chrome,
			document.Layers.CurrentUserLayer,
			document.Selection);

		if (move_declined)
			return;

		original_selection = document.Selection.Clone ();
		original_transform.InitMatrix (document.Layers.SelectionLayer.Transform);

		hist = new MovePixelsHistoryItem (Icon, Name, document);
		hist.TakeSnapshot (!document.Layers.ShowSelectionLayer);

		if (!document.Layers.ShowSelectionLayer) {
			// Copy the selection to the temp layer
			document.Layers.CreateSelectionLayer ();
			document.Layers.ShowSelectionLayer = true;
			// Use same BlendMode, Opacity and Visibility for SelectionLayer
			document.Layers.SelectionLayer.BlendMode = document.Layers.CurrentUserLayer.BlendMode;
			document.Layers.SelectionLayer.Opacity = document.Layers.CurrentUserLayer.Opacity;
			document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;

			using Context selection_ctx = new (document.Layers.SelectionLayer.Surface);
			selection_ctx.AppendPath (document.Selection.SelectionPath);
			selection_ctx.FillRule = FillRule.EvenOdd;
			selection_ctx.SetSourceSurface (document.Layers.CurrentUserLayer.Surface, 0, 0);
			selection_ctx.Clip ();
			selection_ctx.Paint ();

			// Clears the lifted region from the raster and folds the hole into the layer's composite;
			// without the fold, a drag that missed every effect node (so nothing was baked above)
			// showed no movement until the composite was rebuilt on mouse release.
			ObjectOpacity.LiftSelectionFromRaster (
				PintaCore.Chrome,
				document.Layers.CurrentUserLayer,
				document.Selection);
		}

		document.Workspace.Invalidate ();
	}

	protected override void OnUpdateTransform (Document document, Matrix transform)
	{
		base.OnUpdateTransform (document, transform);

		if (move_declined)
			return;

		// Whole-layer fallback move: leave the canvas-sized placeholder selection exactly as it
		// was: moving without a real selection must not fabricate one.
		if (had_real_selection) {
			document.Selection = original_selection!.Transform (transform); // NRT - Set in OnStartTransform
			document.Selection.Visible = true;
		}

		document.Layers.SelectionLayer.Transform.InitMatrix (original_transform);
		document.Layers.SelectionLayer.Transform.Multiply (transform);

		document.Workspace.Invalidate ();
	}

	protected override void OnFinishTransform (Document document, Matrix transform)
	{
		base.OnFinishTransform (document, transform);

		if (move_declined) {
			move_declined = false;
			return;
		}

		if (had_real_selection) {
			// Also transform the base selection used for the various select modes.
			var prev_selection = document.PreviousSelection;
			document.PreviousSelection = prev_selection.Transform (transform);
		}

		if (hist != null)
			document.History.PushNewItem (hist);

		hist = null;
		original_selection = null;
		original_transform.InitIdentity ();
	}

	protected override void OnCommit (Document? document)
	{
		document?.FinishSelection ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);

		document?.FinishSelection ();
	}
}
