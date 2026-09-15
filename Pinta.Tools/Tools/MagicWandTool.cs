//
// MagicWandTool.cs
//
// Author:
//       Olivier Dufour <olivier.duff@gmail.com>
//
// Copyright (c) 2010 Olivier Dufour
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
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MagicWandTool : FloodTool
{
	private readonly IWorkspaceService workspace;

	private CombineMode combine_mode;

	/// <summary>A point the user clicked on, and how its region was combined into the selection.</summary>
	private sealed record WandPoint (PointI Position, bool GlobalMode, CombineMode Mode);

	// The clicked points the tolerance slider can still re-flood, and the selection they were
	// combined into. Forgotten as soon as anything other than this tool touches the selection, so
	// moving the slider never resurrects a region the user has already moved on from.
	private readonly List<WandPoint> live_points = [];
	private DocumentSelection? live_base_selection;
	private Document? live_document;

	// Set while a click is being handled, so the fill callback can tell a click apart from a
	// re-flood; the re-flood carries the combine mode of the point it is replaying instead.
	private WandPoint? clicked_point;
	private CombineMode replay_mode;

	// Our own selection writes must not be mistaken for someone else's.
	private bool writing_selection;

	private bool tolerance_change_recorded;

	public MagicWandTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
		LimitToSelection = false;

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};

		workspace.SelectionChanged += (_, _) => {
			if (!writing_selection)
				ForgetLivePoints ();
		};
	}

	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override string Name => Translations.GetString ("Magic Wand Select");
	public override string Icon => Pinta.Resources.Icons.ToolSelectMagicWand;
	public override string StatusBarText => Translations.GetString (
		"Click to select region of similar color." +
		"\nHold shift to use Global mode."
	);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.MagicWand.png"), 21, 10, null);
	public override int Priority => 19;
	public override bool IsSelectionTool => true;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (SelectionSeparator);

		workspace.SelectionHandler.BuildToolbar (tb, Settings);
	}


	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		combine_mode = workspace.SelectionHandler.DetermineCombineMode (e);
		clicked_point = new WandPoint (e.Point, IsGlobalMode || e.IsShiftPressed, combine_mode);

		writing_selection = true;
		try {
			base.OnMouseDown (document, e);

			document.Selection.Visible = true;
		} finally {
			writing_selection = false;
			clicked_point = null;
		}
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);

		ForgetLivePoints ();
	}

	protected override void OnFillRegionComputed (Document document, IReadOnlyList<IReadOnlyList<PointI>> polygonSet)
	{
		if (clicked_point is not WandPoint clicked) {
			// A re-flood at the new tolerance; its history item was pushed by OnToleranceChanged.
			CombineIntoSelection (document, replay_mode, polygonSet);
			return;
		}

		var undoAction = new SelectionHistoryItem (workspace, Icon, Name);
		undoAction.TakeSnapshot ();

		if (live_document != document || live_points.Count == 0) {
			live_document = document;
			live_base_selection = document.Selection.Clone ();
			live_points.Clear ();
		}

		live_points.Add (clicked);
		tolerance_change_recorded = false;

		document.PreviousSelection = document.Selection.Clone ();
		CombineIntoSelection (document, clicked.Mode, polygonSet);

		document.History.PushNewItem (undoAction);
	}

	/// <summary>
	/// Re-floods every point clicked since the selection was last changed from elsewhere, so
	/// dragging the tolerance slider grows and shrinks the selected area as the user watches. A
	/// whole run of slider moves folds into one history item; the next click starts a new one.
	/// </summary>
	protected override void OnToleranceChanged ()
	{
		if (live_document is null || live_base_selection is null || live_points.Count == 0)
			return;

		if (!workspace.HasOpenDocuments || workspace.ActiveDocument != live_document) {
			ForgetLivePoints ();
			return;
		}

		Document document = live_document;

		SelectionHistoryItem? undoAction = null;
		if (!tolerance_change_recorded) {
			undoAction = new SelectionHistoryItem (workspace, Icon, Name);
			undoAction.TakeSnapshot ();
		}

		writing_selection = true;
		try {
			for (int i = 0; i < live_points.Count; ++i) {
				// Each point combines into the result of the ones before it, exactly as it did
				// when it was clicked; the first one combines into the pre-click selection.
				document.PreviousSelection = (i == 0 ? live_base_selection : document.Selection).Clone ();
				replay_mode = live_points[i].Mode;
				ComputeFillRegion (document, live_points[i].Position, live_points[i].GlobalMode);
			}

			document.Selection.Visible = true;
		} finally {
			writing_selection = false;
		}

		if (undoAction is not null) {
			document.History.PushNewItem (undoAction);
			tolerance_change_recorded = true;
		}

		document.Workspace.Invalidate ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		workspace.SelectionHandler.OnSaveSettings (settings);
	}

	private static void CombineIntoSelection (Document document, CombineMode mode, IReadOnlyList<IReadOnlyList<PointI>> polygonSet)
	{
		document.Selection.SelectionPolygons.Clear ();
		SelectionModeHandler.PerformSelectionMode (document, mode, DocumentSelection.ConvertToPolygons (polygonSet));
	}

	private void ForgetLivePoints ()
	{
		live_points.Clear ();
		live_base_selection = null;
		live_document = null;
		tolerance_change_recorded = false;
	}

	private Separator? selection_sep;
	private Separator SelectionSeparator => selection_sep ??= GtkExtensions.CreateToolBarSeparator ();
}
