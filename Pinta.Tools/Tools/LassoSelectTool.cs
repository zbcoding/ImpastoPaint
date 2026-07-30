//
// LassoSelectTool.cs
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
using ClipperLib;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public class LassoSelectTool : BaseTool
{
	private const int SCISSORS_PREVIEW_STEP = 2;

	private enum LassoMode
	{
		Freeform = 0,
		Polygon = 1
	}

	private readonly IWorkspaceService workspace;

	private bool is_dragging;
	private CombineMode combine_mode;
	private SelectionHistoryItem? hist;

	private readonly List<IntPoint> lasso_polygon = [];
	private ScissorsEngine? scissors_engine;
	private readonly List<IntPoint> scissors_anchors = [];
	private readonly List<IReadOnlyList<IntPoint>> scissors_segments = [];
	private List<IntPoint>? scissors_preview;
	private IntPoint? scissors_cursor;
	private IntPoint? scissors_tree_anchor;
	private readonly List<MoveHandle> scissors_handles = [];
	private int dragged_scissors_anchor = -1;
	private MouseButton dragged_scissors_button;

	private Separator? mode_sep;
	private Label? lasso_mode_label;
	private ToolBarDropDownButton? lasso_mode_buttom;
	private Gtk.Button? back_button;
	private Gtk.Button? confirm_button;
	private Separator? action_sep;

	public LassoSelectTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
	}

	public override string Name => Translations.GetString ("Lasso Select");
	public override string Icon => Pinta.Resources.Icons.ToolSelectLasso;
	public override string StatusBarText => Translations.GetString (
		"In Freeform mode, click and drag to draw the outline for a selection area." +
		"\n\nIn Polygon mode, click and drag to add a new point to the selection." +
		"\nPress Enter to finish the selection." +
		"\nPress Backspace to delete the last point.");
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.LassoSelect.png"), 9, 18, null);
	public override int Priority => 17;
	public override bool IsSelectionTool => true;
	public override IEnumerable<IToolHandle> Handles => IsScissorsMode ? scissors_handles : [];

	protected virtual bool HasModeSelector => true;
	protected virtual bool IsFreeformMode => CurrentMode == LassoMode.Freeform;
	protected virtual bool IsPolygonMode => CurrentMode == LassoMode.Polygon;
	protected virtual bool IsScissorsMode => false;
	protected virtual int ScissorsEdgeTolerance => 50;
	protected virtual bool ShowActionButtons => IsPolygonMode;
	private LassoMode CurrentMode => LassoModeButtom.SelectedItem.GetTagOrDefault (LassoMode.Freeform);

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);
		workspace.SelectionHandler.BuildToolbar (tb, Settings);

		tb.Append (Separator);
		if (HasModeSelector) {
			tb.Append (LassoModeLabel);
			tb.Append (LassoModeButtom);
		}
		tb.Append (ActionSeparator);
		tb.Append (BackButton);
		tb.Append (ConfirmButton);

		UpdateActionButtons ();
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (is_dragging)
			return;

		if (IsScissorsMode && hist is not null) {
			PointD viewPoint = workspace.CanvasPointToView (e.PointDouble);
			for (int i = 0; i < scissors_handles.Count; i++) {
				if (!scissors_handles[i].ContainsPoint (viewPoint))
					continue;
				dragged_scissors_anchor = i;
				dragged_scissors_button = e.MouseButton;
				scissors_handles[i].Selected = true;
				document.Workspace.InvalidateWindowRect (scissors_handles[i].InvalidateRect);
				is_dragging = true;
				return;
			}
		}

		is_dragging = true;

		if (hist is null) {
			ScissorsEngine? newScissorsEngine = IsScissorsMode
				? new ScissorsEngine (document.Layers.CurrentUserLayer.Surface, new ScissorsOptions (ScissorsEdgeTolerance))
				: null;

			hist = new SelectionHistoryItem (workspace, Icon, Name);
			hist.TakeSnapshot ();

			combine_mode = workspace.SelectionHandler.DetermineCombineMode (e);
			document.PreviousSelection = document.Selection.Clone ();
			scissors_engine = newScissorsEngine;
		}

		PointD p = document.ClampToImageSize (e.PointDouble);
		IntPoint point = new ((long) p.X, (long) p.Y);

		if (IsPolygonMode) {
			lasso_polygon.Add (point);
			ApplySelection (document);
			UpdateActionButtons ();
			return;
		}

		if (!IsScissorsMode)
			return;

		point = ClampToScissorsSurface (point);
		IntPoint anchor = e.IsShiftPressed
			? point
			: scissors_engine!.FindMaxGradient (point);

		if (scissors_anchors.Count > 0 && PointsEqual (scissors_anchors[^1], anchor))
			return;

		if (scissors_anchors.Count > 0) {
			List<IntPoint> segment = FindScissorsPath (scissors_anchors[^1], anchor);
			scissors_segments.Add ([.. segment]);
			scissors_engine!.LearnFromPath (segment);
		}

		scissors_anchors.Add (anchor);
		scissors_handles.Add (CreateScissorsHandle (anchor));
		document.Workspace.InvalidateWindowRect (scissors_handles[^1].InvalidateRect);
		scissors_preview = null;
		scissors_cursor = anchor;

		RebuildScissorsPolygon ();
		ApplySelection (document);
		UpdateActionButtons ();
	}

	private void ApplySelection (Document document)
	{
		document.Selection.SelectionPolygons.Clear ();
		document.Selection.SelectionPolygons.Add ([.. lasso_polygon]);

		SelectionModeHandler.PerformSelectionMode (
			document,
			combine_mode,
			document.Selection.SelectionPolygons);
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (dragged_scissors_anchor >= 0) {
			UpdateDraggedScissorsAnchor (document, e);
			return;
		}

		PointD p = document.ClampToImageSize (e.PointDouble);
		IntPoint point = new ((long) p.X, (long) p.Y);

		if (IsFreeformMode) {
			if (!is_dragging)
				return;

			lasso_polygon.Add (point);
			ApplySelection (document);
			return;
		}

		if (IsPolygonMode) {
			if (!is_dragging || lasso_polygon.Count == 0)
				return;

			lasso_polygon[^1] = point;
			ApplySelection (document);
			return;
		}

		if (scissors_anchors.Count == 0)
			return;

		point = ClampToScissorsSurface (point);
		if (scissors_cursor.HasValue && !CursorMovedEnough (scissors_cursor.Value, point))
			return;

		scissors_cursor = point;
		scissors_preview = FindScissorsPath (scissors_anchors[^1], point);
		RebuildScissorsPolygon ();
		ApplySelection (document);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (dragged_scissors_anchor >= 0) {
			if (e.MouseButton == dragged_scissors_button) {
				scissors_handles[dragged_scissors_anchor].Selected = false;
				document.Workspace.InvalidateWindowRect (scissors_handles[dragged_scissors_anchor].InvalidateRect);
				dragged_scissors_anchor = -1;
				is_dragging = false;
			}
			return;
		}

		is_dragging = false;

		if (!IsFreeformMode)
			return;

		ApplySelection (document);
		FinalizeShape (document);
	}

	private void FinalizeShape (Document document)
	{
		if (hist is null) {
			ClearShapeState ();
			UpdateActionButtons ();
			return;
		}

		try {
			if (scissors_anchors.Count > 0) {
				FinalizeScissorsShape (document);
			} else if (lasso_polygon.Count > 1) {
				document.History.PushNewItem (hist);
			} else {
				hist.Undo ();
			}
		} catch {
			hist.Undo ();
			throw;
		} finally {
			ClearShapeState ();
			UpdateActionButtons ();
		}
	}

	private void FinalizeScissorsShape (Document document)
	{
		ArgumentNullException.ThrowIfNull (hist);

		if (!HasThreeDistinctAnchors ()) {
			hist.Undo ();
			return;
		}

		List<IntPoint> finalPolygon = [];
		foreach (IReadOnlyList<IntPoint> segment in scissors_segments)
			AppendSegment (finalPolygon, segment);

		IntPoint first = scissors_anchors[0];
		IntPoint last = scissors_anchors[^1];
		if (!PointsEqual (first, last))
			AppendSegment (finalPolygon, FindScissorsPath (last, first));

		if (finalPolygon.Count <= 2) {
			hist.Undo ();
			return;
		}

		document.Selection.SelectionPolygons.Clear ();
		document.Selection.SelectionPolygons.Add ([.. finalPolygon]);
		SelectionModeHandler.PerformSelectionMode (
			document,
			combine_mode,
			document.Selection.SelectionPolygons);
		document.History.PushNewItem (hist);
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		if (document is not null)
			FinalizeShape (document);
		else {
			ClearShapeState ();
			UpdateActionButtons ();
		}
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (hist is null)
			return base.OnKeyDown (document, e);

		// Impasto: Enter/Backspace/Escape are user-configurable (Keyboard Shortcuts dialog).
		if (e.Key.Value == Gdk.Constants.KEY_KP_Enter ||
			e.Gesture == PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.LassoFinalize)) {
			FinalizeShape (document);
			return true;
		}
		if (e.Gesture == PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.LassoBacktrack)) {
			Backtrack (document);
			return true;
		}
		if (e.Gesture == PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.LassoCancel)) {
			CancelShape ();
			return true;
		}

		return base.OnKeyDown (document, e);
	}

	protected override bool OnHandleUndo (Document document)
	{
		if (hist is null)
			return base.OnHandleUndo (document);

		Backtrack (document);
		return true;
	}

	private void Backtrack (Document document)
	{
		if (hist is null)
			return;

		if (scissors_anchors.Count > 0) {
			scissors_anchors.RemoveAt (scissors_anchors.Count - 1);
			MoveHandle removedHandle = scissors_handles[^1];
			RectangleI removedRect = removedHandle.InvalidateRect;
			removedHandle.Active = false;
			document.Workspace.InvalidateWindowRect (removedRect);
			scissors_handles.RemoveAt (scissors_handles.Count - 1);
			if (scissors_segments.Count > 0)
				scissors_segments.RemoveAt (scissors_segments.Count - 1);

			if (scissors_anchors.Count == 0) {
				CancelShape ();
				return;
			}

			scissors_preview = scissors_cursor.HasValue
				? FindScissorsPath (scissors_anchors[^1], scissors_cursor.Value)
				: null;
			RebuildScissorsPolygon ();
			ApplySelection (document);
			UpdateActionButtons ();
			return;
		}

		if (lasso_polygon.Count == 0)
			return;

		lasso_polygon.RemoveAt (lasso_polygon.Count - 1);
		if (lasso_polygon.Count == 0) {
			CancelShape ();
			return;
		}

		ApplySelection (document);
		UpdateActionButtons ();
	}

	private void CancelShape ()
	{
		hist?.Undo ();
		ClearShapeState ();
		UpdateActionButtons ();
	}

	protected override void OnCommit (Document? document)
	{
		if (document is not null)
			FinalizeShape (document);
		else {
			ClearShapeState ();
			UpdateActionButtons ();
		}
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (lasso_mode_buttom is not null)
			settings.PutSetting (SettingNames.LASSO_MODE, lasso_mode_buttom.SelectedIndex);
	}

	private IntPoint ClampToScissorsSurface (IntPoint point)
	{
		ArgumentNullException.ThrowIfNull (scissors_engine);
		return new IntPoint (
			Math.Clamp (point.X, 0, scissors_engine.Width - 1),
			Math.Clamp (point.Y, 0, scissors_engine.Height - 1));
	}

	private List<IntPoint> FindScissorsPath (IntPoint start, IntPoint end)
	{
		if (scissors_engine is null)
			return [start, end];

		if (!scissors_tree_anchor.HasValue || !PointsEqual (scissors_tree_anchor.Value, start)) {
			scissors_engine.SetAnchor (start);
			scissors_tree_anchor = start;
		}

		return scissors_engine.GetPathTo (end);
	}

	private MoveHandle CreateScissorsHandle (IntPoint point)
		=> new (workspace) {
			Active = true,
			CanvasPosition = new PointD (point.X, point.Y)
		};

	private void UpdateDraggedScissorsAnchor (Document document, ToolMouseEventArgs e)
	{
		if (scissors_engine is null || dragged_scissors_anchor < 0 || dragged_scissors_anchor >= scissors_anchors.Count)
			return;

		PointD p = document.ClampToImageSize (e.PointDouble);
		IntPoint raw = new ((long) p.X, (long) p.Y);
		IntPoint point = e.IsShiftPressed ? ClampToScissorsSurface (raw) : scissors_engine.FindMaxGradient (raw);
		if (PointsEqual (scissors_anchors[dragged_scissors_anchor], point))
			return;

		RectangleI oldRect = scissors_handles[dragged_scissors_anchor].InvalidateRect;
		scissors_anchors[dragged_scissors_anchor] = point;
		scissors_handles[dragged_scissors_anchor].CanvasPosition = new PointD (point.X, point.Y);
		if (dragged_scissors_anchor > 0)
			scissors_segments[dragged_scissors_anchor - 1] = FindScissorsPath (scissors_anchors[dragged_scissors_anchor - 1], point);
		if (dragged_scissors_anchor < scissors_segments.Count)
			scissors_segments[dragged_scissors_anchor] = FindScissorsPath (point, scissors_anchors[dragged_scissors_anchor + 1]);
		if (dragged_scissors_anchor == scissors_anchors.Count - 1)
			scissors_preview = null;

		RebuildScissorsPolygon ();
		ApplySelection (document);
		document.Workspace.InvalidateWindowRect (oldRect.Union (scissors_handles[dragged_scissors_anchor].InvalidateRect));
	}

	protected void SetScissorsEdgeTolerance (int tolerance)
	{
		if (scissors_engine is null)
			return;
		scissors_engine.SetEdgeTolerance (tolerance);
		if (!scissors_cursor.HasValue || scissors_anchors.Count == 0 || !workspace.HasOpenDocuments)
			return;
		scissors_preview = FindScissorsPath (scissors_anchors[^1], scissors_cursor.Value);
		RebuildScissorsPolygon ();
		ApplySelection (workspace.ActiveDocument);
	}
	private void RebuildScissorsPolygon ()
	{
		lasso_polygon.Clear ();

		foreach (IReadOnlyList<IntPoint> segment in scissors_segments)
			AppendSegment (lasso_polygon, segment);

		if (scissors_preview is not null)
			AppendSegment (lasso_polygon, scissors_preview);
	}

	private static void AppendSegment (List<IntPoint> destination, IReadOnlyList<IntPoint> segment)
	{
		if (segment.Count == 0)
			return;

		int startIndex = destination.Count > 0 && PointsEqual (destination[^1], segment[0]) ? 1 : 0;
		for (int i = startIndex; i < segment.Count; i++)
			destination.Add (segment[i]);
	}

	private bool HasThreeDistinctAnchors ()
	{
		HashSet<(long X, long Y)> distinct = [];
		foreach (IntPoint anchor in scissors_anchors) {
			distinct.Add ((anchor.X, anchor.Y));
			if (distinct.Count == 3)
				return true;
		}

		return false;
	}

	private static bool CursorMovedEnough (IntPoint previous, IntPoint current)
		=> Math.Abs (current.X - previous.X) >= SCISSORS_PREVIEW_STEP ||
		   Math.Abs (current.Y - previous.Y) >= SCISSORS_PREVIEW_STEP;

	private static bool PointsEqual (IntPoint first, IntPoint second)
		=> first.X == second.X && first.Y == second.Y;

	private void ClearShapeState ()
	{
		hist = null;
		is_dragging = false;
		lasso_polygon.Clear ();
		scissors_engine = null;
		scissors_tree_anchor = null;
		if (workspace.HasOpenDocuments)
			foreach (MoveHandle handle in scissors_handles)
				workspace.ActiveDocument.Workspace.InvalidateWindowRect (handle.InvalidateRect);
		foreach (MoveHandle handle in scissors_handles)
			handle.Active = false;
		dragged_scissors_anchor = -1;
		scissors_handles.Clear ();
		scissors_anchors.Clear ();
		scissors_segments.Clear ();
		scissors_preview = null;
		scissors_cursor = null;
	}

	private Separator Separator => mode_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Separator ActionSeparator => action_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Label LassoModeLabel => lasso_mode_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Lasso Mode")));

	private Gtk.Button BackButton {
		get {
			if (back_button is null) {
				back_button = GtkExtensions.CreateBackToolBarButton (
					Translations.GetString ("Remove last point (Backspace)"));

				back_button.OnClicked += (_, _) => {
					if (workspace.HasOpenDocuments)
						Backtrack (workspace.ActiveDocument);
				};

				back_button.Visible = false;
			}

			return back_button;
		}
	}

	private Gtk.Button ConfirmButton {
		get {
			if (confirm_button is null) {
				confirm_button = GtkExtensions.CreateConfirmToolBarButton (
					Translations.GetString ("Finish selection (Enter)"));

				confirm_button.OnClicked += (_, _) => {
					if (workspace.HasOpenDocuments)
						FinalizeShape (workspace.ActiveDocument);
				};

				confirm_button.Visible = false;
			}

			return confirm_button;
		}
	}

	private void UpdateActionButtons ()
	{
		bool hasPoints = lasso_polygon.Count > 0 || scissors_anchors.Count > 0;
		bool visible = ShowActionButtons && hasPoints;

		if (back_button is not null) {
			back_button.Visible = visible;
			back_button.Sensitive = hasPoints;
		}

		if (confirm_button is not null) {
			confirm_button.Visible = visible;
			confirm_button.Sensitive = hasPoints;
		}

		if (action_sep is not null)
			action_sep.Visible = visible;
	}

	private ToolBarDropDownButton LassoModeButtom {
		get {
			if (lasso_mode_buttom is null) {
				lasso_mode_buttom = ToolBarDropDownButton.New (true);

				lasso_mode_buttom.AddItem (
					Translations.GetString ("Freeform"),
					Pinta.Resources.Icons.LassoFreeform,
					LassoMode.Freeform,
					Translations.GetString ("In Freeform mode, click and drag to draw the outline for a selection area."));

				lasso_mode_buttom.AddItem (
					Translations.GetString ("Polygon"),
					Pinta.Resources.Icons.LassoPolygon,
					LassoMode.Polygon,
					Translations.GetString ("In Polygon mode, click and drag to add a new point to the selection.\nPress Enter to finish the selection.\nPress Backspace to delete the last point."));

				lasso_mode_buttom.SelectedIndex = Math.Clamp (
					Settings.GetSetting (SettingNames.LASSO_MODE, 0),
					0,
					1);

				lasso_mode_buttom.SelectedItemChanged += (_, _) => {
					if (hist is not null) {
						if (workspace.HasOpenDocuments)
							FinalizeShape (workspace.ActiveDocument);
						else
							ClearShapeState ();
					}

					UpdateActionButtons ();
				};
			}

			return lasso_mode_buttom;
		}
	}
}
