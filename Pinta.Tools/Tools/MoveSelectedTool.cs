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
using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MoveSelectedTool : BaseTransformTool
{
	private MovePixelsHistoryItem? hist;
	// Set when this drag lifted the pixels onto a new layer; recorded with hist as one undo step.
	private AddLayerHistoryItem? added_layer_hist;
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

	// Skew and Perspective reshape the selection through their own grips; Move keeps the inherited
	// resize grips. Body drags, rotate and nudge behave the same in every mode.
	private TransformMode mode;
	private readonly SelectionWarp warp = new ();
	private readonly QuadHandle quad_handle;
	private int hover_grip = -1;

	private readonly SystemManager system_manager;
	private readonly IWorkspaceService workspace;
	public MoveSelectedTool (IServiceProvider services) : base (services)
	{
		system_manager = services.GetService<SystemManager> ();
		workspace = services.GetService<IWorkspaceService> ();
		quad_handle = new QuadHandle (workspace);
		mode = ModeFromIndex (Settings.GetSetting (SettingNames.MOVE_SELECTED_MODE, 0));

		// Undo, redo, nudges and plain moves all replace the selection; a warp session built on
		// the old one would resample from stale pixels, and the grips must follow the new outline.
		workspace.SelectionChanged += (_, _) => {
			if (!warp.IsApplying)
				warp.Forget ();
			RefreshQuadHandle ();
		};
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
		system_manager.CtrlLabel ())
		// Translators: Skew and Perspective are the Mode choices on this tool's toolbar.
		+ "\n" + Translations.GetString ("In Skew or Perspective mode, drag the grips on the selection's edges or corners.");

	// Rendered at 2x the default 16px and with a centered hotspot so the enlarged
	// four-way cross still points at the pixel under the cursor.
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.ToolMoveCursor, 32), 16, 16, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_M);
	public override int Priority => 5;

	public override bool WritesToCurrentLayer
		=> true;

	// OnStartTransform below runs the very same rasterize offer over the whole selection it is about
	// to lift, which is a superset of the down point ToolManager's guard probes. Leaving the guard on
	// asked twice for one drag: once for whatever object sat under the cursor, then again for every
	// object the selection reaches.
	public override bool HandlesLiveObjectsItself
		=> true;

	protected override RectangleD GetSourceRectangle (Document document)
		=> document.Selection.GetBounds ();

	public override IEnumerable<IToolHandle> Handles
		=> mode == TransformMode.Move ? base.Handles : [quad_handle];

	protected override bool ResizeGripsEnabled
		=> mode == TransformMode.Move;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (ModeLabel);
		tb.Append (ModeDropDown);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) mode);
	}

	private Gtk.Label? mode_label;
	private ToolBarDropDownButton? mode_button;

	private Gtk.Label ModeLabel => mode_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Mode")));

	private ToolBarDropDownButton ModeDropDown {
		get {
			if (mode_button is not null)
				return mode_button;

			mode_button = ToolBarDropDownButton.New (true);
			mode_button.AddItem (Translations.GetString ("Move"), Pinta.Resources.Icons.ToolMove, TransformMode.Move,
				Translations.GetString ("Drag the grips to resize the selected pixels."));
			// Translators: Move Selected Pixels mode that slants the pixels by sliding one edge along itself.
			mode_button.AddItem (Translations.GetString ("Skew"), Pinta.Resources.Icons.TransformSkew, TransformMode.Skew,
				Translations.GetString ("Drag an edge's grip to slide that edge along itself, slanting the selected pixels."));
			// Translators: Move Selected Pixels mode where each corner moves on its own, to fit the pixels onto an angled surface.
			mode_button.AddItem (Translations.GetString ("Perspective"), Pinta.Resources.Icons.TransformPerspective, TransformMode.Perspective,
				Translations.GetString ("Drag any corner on its own to fit the selected pixels onto an angled surface."));
			mode_button.SelectedIndex = (int) mode;
			mode_button.SelectedItemChanged += (_, _) => {
				mode = (TransformMode) mode_button.SelectedItem.Tag!;
				hover_grip = -1;
				RefreshQuadHandle ();
			};

			return mode_button;
		}
	}

	private static TransformMode ModeFromIndex (int index)
		=> Enum.IsDefined ((TransformMode) index) ? (TransformMode) index : TransformMode.Move;

	private void RefreshQuadHandle ()
	{
		PointD[]? quad = null;
		if (mode != TransformMode.Move && IsActiveTool () && workspace.HasOpenDocuments)
			quad = warp.CurrentQuad (workspace.ActiveDocument);

		quad_handle.Active = quad is not null;
		if (quad is not null)
			quad_handle.Update (quad, mode);

		if (workspace.HasOpenDocuments)
			workspace.Invalidate ();
	}

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);
		RefreshQuadHandle ();
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (!TryBeginWarp (document, e))
			base.OnMouseDown (document, e);
	}

	/// <summary>
	/// Starts a skew or perspective drag when a left click lands on one of their grips. Lifts the
	/// pixels first, exactly as a move would, so the drag reshapes floating content.
	/// </summary>
	private bool TryBeginWarp (Document document, ToolMouseEventArgs e)
	{
		if (mode == TransformMode.Move || IsActive || warp.IsDragging)
			return false;
		if (e.MouseButton != MouseButton.Left || e.IsAltPressed)
			return false;

		int grip = quad_handle.GripAt (e.WindowPoint);
		if (grip < 0)
			return false;

		OnStartTransform (document);
		if (move_declined) {
			move_declined = false;
			return true;
		}

		warp.Begin (document, mode, grip, e.PointDouble);
		quad_handle.Select (grip);
		return true;
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (warp.IsDragging) {
			warp.Update (document, e.PointDouble);
			return;
		}

		base.OnMouseMove (document, e);

		if (mode != TransformMode.Move && !IsActive)
			UpdateGripCursor (e.WindowPoint);
	}

	// Only on a change: DefaultCursor builds a fresh texture every time it is read.
	private void UpdateGripCursor (PointD windowPoint)
	{
		int grip = quad_handle.GripAt (windowPoint);
		if (grip == hover_grip)
			return;

		hover_grip = grip;
		SetCursor (grip >= 0 ? quad_handle.CursorOf (grip) : DefaultCursor);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (warp.IsDragging)
			FinishWarp (document);
		else
			base.OnMouseUp (document, e);
	}

	// A mid-drag key would nudge the pixels out from under the warp.
	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
		=> warp.IsDragging || base.OnKeyDown (document, e);

	private void FinishWarp (Document document)
	{
		string text = mode == TransformMode.Skew
			? Translations.GetString ("Skew")
			: Translations.GetString ("Perspective");

		BaseHistoryItem? pixels = warp.End (document, Icon, text);
		quad_handle.Select (-1);

		if (hist is not null)
			document.History.PushNewItem (GroupWarp (text, hist, pixels));

		ClearGestureState ();
	}

	// One undo step for the whole drag: the lift (with any layer it added) and the resampled
	// pixels. A click that never moved a grip still lifted the pixels, so it records the lift alone.
	private BaseHistoryItem GroupWarp (string text, MovePixelsHistoryItem move, BaseHistoryItem? pixels)
	{
		if (pixels is null)
			return added_layer_hist is null ? move : GroupWithAddedLayer (move, added_layer_hist);

		CompoundHistoryItem group = new (Icon, text);
		if (added_layer_hist is not null)
			group.Push (added_layer_hist);
		group.Push (move);
		group.Push (pixels);
		return group;
	}

	protected override void OnStartTransform (Document document)
	{
		base.OnStartTransform (document);

		had_real_selection = document.Selection.Visible;

		// If there is no selection, select the whole image.
		if (document.Selection.SelectionPolygons.Count == 0) {
			RectangleD imageBounds = new (0, 0, document.ImageSize.Width, document.ImageSize.Height);
			document.Selection.CreateRectangleSelection (imageBounds);
		}

		// A floating selection (a paste, or pixels an earlier drag already lifted) moves on the
		// selection layer alone: the base raster and the objects over it are untouched until the
		// selection is finished, so nothing needs baking.
		bool lifts = !document.Layers.ShowSelectionLayer;

		// The lift below reads the layer's base raster and clears the moved region from it. Effect
		// nodes, shapes and text are not in that raster — the canvas shows them through the layer's
		// composite — so without this the drag carries un-effected pixels away, leaves the node
		// applying over the hole it left, and moves nothing at all when the selection covered a text
		// or shape object. Bake what the selection reaches, or send the pixels to a new layer, then lift.
		SelectionLiftChoice choice = lifts
			? ObjectRasterizer.PrepareForSelectionLift (
				document,
				PintaCore.Workspace,
				PintaCore.Chrome,
				document.Layers.CurrentUserLayer,
				document.Selection)
			: SelectionLiftChoice.SameLayer;

		move_declined = choice == SelectionLiftChoice.Cancel;
		if (move_declined)
			return;

		original_selection = document.Selection.Clone ();
		original_transform.InitMatrix (document.Layers.SelectionLayer.Transform);

		hist = new MovePixelsHistoryItem (Icon, Name, document);
		hist.TakeSnapshot (lifts);

		if (lifts) {
			UserLayer source = document.Layers.CurrentUserLayer;

			// Copy the selection to the temp layer
			document.Layers.CreateSelectionLayer ();
			document.Layers.ShowSelectionLayer = true;

			using Context selection_ctx = new (document.Layers.SelectionLayer.Surface);
			selection_ctx.AppendPath (document.Selection.SelectionPath);
			selection_ctx.FillRule = FillRule.EvenOdd;
			selection_ctx.SetSourceSurface (source.Surface, 0, 0);
			selection_ctx.Clip ();
			selection_ctx.Paint ();

			// Clears the lifted region from the raster and folds the hole into the layer's composite;
			// without the fold, a drag that missed every effect node (so nothing was baked above)
			// showed no movement until the composite was rebuilt on mouse release.
			ObjectOpacity.LiftSelectionFromRaster (
				PintaCore.Chrome,
				source,
				document.Selection);

			if (choice == SelectionLiftChoice.NewLayer)
				AddLayerForLiftedPixels (document, source);

			// Use same BlendMode, Opacity and Visibility for SelectionLayer
			document.Layers.SelectionLayer.BlendMode = document.Layers.CurrentUserLayer.BlendMode;
			document.Layers.SelectionLayer.Opacity = document.Layers.CurrentUserLayer.Opacity;
			document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;
		}

		document.Workspace.Invalidate ();
	}

	// Finishing a selection draws it into the current layer, so making the new layer current is
	// what lands the lifted pixels on it. It copies the source's blend mode and opacity so the
	// pixels look the same after the move as before it.
	private void AddLayerForLiftedPixels (Document document, UserLayer source)
	{
		UserLayer target = document.Layers.AddNewLayer (string.Empty);
		target.BlendMode = source.BlendMode;
		target.Opacity = source.Opacity;

		added_layer_hist = new AddLayerHistoryItem (
			Pinta.Resources.Icons.LayerNew,
			Translations.GetString ("Add New Layer"),
			document.Layers.IndexOf (target));
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
			document.History.PushNewItem (added_layer_hist is null ? hist : GroupWithAddedLayer (hist, added_layer_hist));

		ClearGestureState ();
	}

	private void ClearGestureState ()
	{
		hist = null;
		added_layer_hist = null;
		original_selection = null;
		original_transform.InitIdentity ();
	}

	// Redo re-adds the layer before the lift floats the pixels again: re-adding switches layers,
	// which commits the current tool, and committing a move while the pixels float would finish
	// them onto the source. Undo runs children in reverse, so the lift is put back on the source
	// layer (whose index the new layer, inserted above it, never shifted) and then the layer goes.
	private CompoundHistoryItem GroupWithAddedLayer (MovePixelsHistoryItem move, AddLayerHistoryItem addLayer)
	{
		CompoundHistoryItem group = new (Icon, Translations.GetString ("Move to New Layer"));
		group.Push (addLayer);
		group.Push (move);
		return group;
	}

	protected override void OnCommit (Document? document)
	{
		document?.FinishSelection ();
		warp.Forget ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		// Same reason as the base's mid-drag reset: a tool switch during a drag must still land it.
		if (warp.IsDragging && document is not null)
			FinishWarp (document);

		base.OnDeactivated (document, newTool);

		document?.FinishSelection ();
		warp.Forget ();
		quad_handle.Active = false;
		hover_grip = -1;
	}
}
