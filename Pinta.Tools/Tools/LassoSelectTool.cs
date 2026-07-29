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

public sealed class LassoSelectTool : BaseTool
{
	private readonly IWorkspaceService workspace;

	private bool is_dragging = false;
	private CombineMode combine_mode;
	private SelectionHistoryItem? hist;

	private readonly List<IntPoint> lasso_polygon = [];

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
	public override string StatusBarText => Translations.GetString ("In Freeform mode, click and drag to draw the outline for a selection area." +
									"\n\nIn Polygon mode, click and drag to add a new point to the selection." +
									"\nPress Enter to finish the selection." +
									"\nPress Backspace to delete the last point.");
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.LassoSelect.png"), 9, 18, null);
	public override int Priority => 17;
	public override bool IsSelectionTool => true;

	private bool IsPolygonMode => LassoModeButtom.SelectedItem.GetTagOrDefault (false);
	private bool IsFreeformMode => !IsPolygonMode;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);
		workspace.SelectionHandler.BuildToolbar (tb, Settings);

		tb.Append (Separator);
		tb.Append (LassoModeLabel);
		tb.Append (LassoModeButtom);
		tb.Append (ActionSeparator);
		tb.Append (BackButton);
		tb.Append (ConfirmButton);

		UpdateActionButtons ();
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (is_dragging)
			return;

		is_dragging = true;

		if (lasso_polygon.Count == 0) {
			hist = new SelectionHistoryItem (workspace, Icon, Name);
			hist.TakeSnapshot ();

			combine_mode = workspace.SelectionHandler.DetermineCombineMode (e);

			document.PreviousSelection = document.Selection.Clone ();
		}

		if (IsPolygonMode) {
			PointD p = document.ClampToImageSize (e.PointDouble);

			lasso_polygon.Add (new IntPoint ((long) p.X, (long) p.Y));

			ApplySelection (document);
			UpdateActionButtons ();
		}

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
		if (!is_dragging)
			return;

		PointD p = document.ClampToImageSize (e.PointDouble);

		if (IsFreeformMode) {
			lasso_polygon.Add (new IntPoint ((long) p.X, (long) p.Y));

			ApplySelection (document);
			return;
		}

		if (lasso_polygon.Count == 0)
			return;

		lasso_polygon[lasso_polygon.Count - 1] = new IntPoint (p.X, p.Y);

		ApplySelection (document);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (IsFreeformMode) {
			ApplySelection (document);

			FinalizeShape (document);
		}
		is_dragging = false;
	}

	private void FinalizeShape (Document document)
	{
		if (hist != null) {
			if (lasso_polygon.Count > 1)
				document.History.PushNewItem (hist);
			hist = null;
		}
		lasso_polygon.Clear ();
		UpdateActionButtons ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		if (document != null)
			FinalizeShape (document);
		else
			UpdateActionButtons ();
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (IsPolygonMode) {
			switch (e.Key.Value) {
				case Gdk.Constants.KEY_Return:
					FinalizeShape (document);
					return true;
				case Gdk.Constants.KEY_BackSpace:
					Backtrack (document);
					return true;
			}
		}

		return base.OnKeyDown (document, e);
	}

	private void Backtrack (Document document)
	{
		if (lasso_polygon.Count == 0) {
			return;
		}

		ArgumentNullException.ThrowIfNull (hist);

		lasso_polygon.RemoveAt (lasso_polygon.Count - 1);

		if (lasso_polygon.Count == 0) {
			hist.Undo ();
			UpdateActionButtons ();
			return;
		}

		ApplySelection (document);
		UpdateActionButtons ();
	}

	protected override void OnCommit (Document? document)
	{
		if (document != null)
			FinalizeShape (document);
		else
			UpdateActionButtons ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (lasso_mode_buttom is not null)
			settings.PutSetting (SettingNames.LASSO_MODE, lasso_mode_buttom.SelectedIndex);
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
					if (!workspace.HasOpenDocuments)
						return;

					// Reuse Backspace behavior
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
					if (!workspace.HasOpenDocuments)
						return;

					// Reuse Enter behavior
					FinalizeShape (workspace.ActiveDocument);
				};

				confirm_button.Visible = false;
			}

			return confirm_button;
		}
	}

	private void UpdateActionButtons ()
	{
		bool isPolygon = false;
		try {
			// LassoModeButtom may not be created yet, or may have no items
			if (lasso_mode_buttom is not null && lasso_mode_buttom.Items.Count > 0)
				isPolygon = lasso_mode_buttom.SelectedItem.GetTagOrDefault (false);
		} catch {
			isPolygon = false;
		}

		bool hasPoints = lasso_polygon.Count > 0;
		bool visible = isPolygon && hasPoints;

		if (back_button is not null) {
			back_button.Visible = visible;
			back_button.Sensitive = hasPoints;
		}

		if (confirm_button is not null) {
			confirm_button.Visible = visible;
			confirm_button.Sensitive = hasPoints;
		}

		if (action_sep is not null) {
			action_sep.Visible = visible;
		}
	}

	private ToolBarDropDownButton LassoModeButtom {
		get {
			if (lasso_mode_buttom is null) {
				lasso_mode_buttom = ToolBarDropDownButton.New (true);

				lasso_mode_buttom.AddItem (
					Translations.GetString ("Freeform"),
					Pinta.Resources.Icons.LassoFreeform,
					false,
					Translations.GetString ("In Freeform mode, click and drag to draw the outline for a selection area."));

				lasso_mode_buttom.AddItem (
					Translations.GetString ("Polygon"),
					Pinta.Resources.Icons.LassoPolygon,
					true,
					Translations.GetString ("In Polygon mode, click and drag to add a new point to the selection.\nPress Enter to finish the selection.\nPress Backspace to delete the last point."));

				lasso_mode_buttom.SelectedIndex = Settings.GetSetting (SettingNames.LASSO_MODE, 0);

				lasso_mode_buttom.SelectedItemChanged += (_, _) => {
					// If switching away from polygon while drawing, finalize/clear
					if (lasso_polygon.Count > 0) {
						if (workspace.HasOpenDocuments) {
							// If leaving polygon mode, commit current polygon
							if (!lasso_mode_buttom.SelectedItem.GetTagOrDefault (false)) {
								FinalizeShape (workspace.ActiveDocument);
							}
						} else {
							lasso_polygon.Clear ();
						}
					}

					UpdateActionButtons ();
				};
			}

			return lasso_mode_buttom;
		}
	}
}
