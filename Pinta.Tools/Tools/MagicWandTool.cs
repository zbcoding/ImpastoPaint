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

	/// <summary>A point the user clicked on, and how its region was combined into the selection.</summary>
	private sealed record WandPoint (PointI Position, bool GlobalMode, CombineMode Mode);

	/// <summary>The clicks the tolerance slider can still re-flood, and the selection they were combined into.</summary>
	private sealed class LiveRun (Document document, DocumentSelection baseSelection)
	{
		public Document Document { get; } = document;
		public DocumentSelection BaseSelection { get; } = baseSelection;
		public List<WandPoint> Points { get; } = [];
		/// <summary>Whether this run already pushed the one history item a slider run costs.</summary>
		public bool HistoryRecorded { get; set; }
	}

	// Forgotten as soon as anything other than this tool touches the selection, so moving the
	// slider never resurrects a region the user has already moved on from.
	private LiveRun? live_run;

	// The point whose flood is on its way back through OnFillRegionComputed, set for as long as
	// the click that started it is being handled. A re-flood never goes through that callback.
	private WandPoint? clicked_point;

	// Our own selection writes must not be mistaken for someone else's.
	private bool writing_selection;

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
				ForgetLiveRun ();
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
		CombineMode mode = workspace.SelectionHandler.DetermineCombineMode (e);
		clicked_point = new WandPoint (e.Point, IsGlobalFlood (e), mode);

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

		ForgetLiveRun ();
	}

	protected override void OnFillRegionComputed (Document document, IReadOnlyList<IReadOnlyList<PointI>> polygonSet)
	{
		// Only a click arrives here; the tolerance slider re-floods its points directly.
		WandPoint clicked = clicked_point ?? throw new InvalidOperationException ("a flooded region reached the wand outside a click");

		var undoAction = new SelectionHistoryItem (workspace, Icon, Name);
		undoAction.TakeSnapshot ();

		// A click on a different document, or the first one after the run was forgotten, starts a
		// new run: the selection as it stands now is what its points get combined into.
		LiveRun run = live_run is not null && live_run.Document == document
			? live_run
			: live_run = new LiveRun (document, document.Selection.Clone ());

		run.Points.Add (clicked);

		// The click closes any slider run before it, so the next one records its own undo step.
		run.HistoryRecorded = false;

		document.PreviousSelection = document.Selection.Clone ();
		CombineIntoSelection (document, clicked.Mode, polygonSet);

		document.History.PushNewItem (undoAction);
	}

	/// <summary>
	/// Re-floods every point clicked since the selection was last changed from elsewhere, so
	/// dragging the tolerance slider grows and shrinks the selected area as the user watches. A
	/// whole run of slider moves folds into one history item; the next click starts a new one.
	/// </summary>
	/// <remarks>
	/// ponytail: every notch of the slider re-floods every stored point, each flood taking its own
	/// visible snapshot of the layer - fine for the handful of clicks a selection is usually built
	/// from, and the ceiling is the click count, not the image. If that stops holding, cache the
	/// snapshot for the run and flood from it.
	/// </remarks>
	protected override void OnToleranceChanged ()
	{
		if (live_run is null)
			return;

		if (!workspace.HasOpenDocuments || workspace.ActiveDocument != live_run.Document) {
			ForgetLiveRun ();
			return;
		}

		LiveRun run = live_run;
		Document document = run.Document;

		SelectionHistoryItem? undoAction = null;
		if (!run.HistoryRecorded) {
			undoAction = new SelectionHistoryItem (workspace, Icon, Name);
			undoAction.TakeSnapshot ();
		}

		// The wand never limits a flood to the selection (LimitToSelection is false), so this is
		// built once for the whole run to satisfy the flood; it is not read.
		Cairo.Region limitRegion = CairoExtensions.CreateRegion (document.GetSelectedBounds (true));

		writing_selection = true;
		try {
			for (int i = 0; i < run.Points.Count; ++i) {
				WandPoint point = run.Points[i];

				// Each point combines into the result of the ones before it, exactly as it did
				// when it was clicked; the first one combines into the pre-click selection.
				document.PreviousSelection = (i == 0 ? run.BaseSelection : document.Selection).Clone ();

				FloodedRegion flooded = ComputeFloodedRegion (document, point.Position, point.GlobalMode, limitRegion);

				if (flooded.Polygons is not null)
					CombineIntoSelection (document, point.Mode, flooded.Polygons);
			}

			document.Selection.Visible = true;
		} finally {
			writing_selection = false;
		}

		if (undoAction is not null) {
			document.History.PushNewItem (undoAction);
			run.HistoryRecorded = true;
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

	private void ForgetLiveRun () => live_run = null;

	private Separator? selection_sep;
	private Separator SelectionSeparator => selection_sep ??= GtkExtensions.CreateToolBarSeparator ();
}
