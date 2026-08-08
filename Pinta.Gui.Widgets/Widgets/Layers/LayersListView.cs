//
// LayersListWidget.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//       Greg Lowe <greg@vis.net.nz>
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.ScrolledWindow>]
public sealed partial class LayersListView
{
	private Gio.ListStore list_model;          // root model: one row per layer
	private Gtk.TreeListModel tree_model;       // wraps root, exposes each layer's objects as children
	private Gtk.SingleSelection selection_model;
	private Gtk.ListView list_view;
	private Document? active_document;
	private bool changing_selection = false;

	// Per-layer child model of object rows, kept so history changes can repopulate it in place
	// (returning the same instance from the create func lets the tree update live).
	private readonly Dictionary<UserLayer, Gio.ListStore> child_models = [];

	public static new LayersListView New ()
		=> NewWithProperties ([]);

	[MemberNotNull (nameof (list_model))]
	[MemberNotNull (nameof (tree_model))]
	[MemberNotNull (nameof (selection_model))]
	[MemberNotNull (nameof (list_view))]
	partial void Initialize ()
	{
		// --- Control creaton

		Gio.ListStore listModel = Gio.ListStore.New (LayersListViewItem.GetGType ());

		// Wrap the flat layer list so each layer can expand to show its re-editable objects.
		// Collapsed by default (autoexpand: false) to save space.
		Gtk.TreeListModel treeModel = Gtk.TreeListModel.New (listModel, passthrough: false, autoexpand: false, CreateChildModel);

		Gtk.SingleSelection selectionModel = Gtk.SingleSelection.New (treeModel);
		selectionModel.OnSelectionChanged += HandleSelectionChanged;

		Gtk.SignalListItemFactory factory = Gtk.SignalListItemFactory.New ();
		factory.OnSetup += HandleFactorySetup;
		factory.OnBind += HandleFactoryBind;

		Gtk.ListView listView = Gtk.ListView.New (selectionModel, factory);
		listView.CanFocus = false;
		listView.OnActivate += HandleRowActivated;

		// --- Initialization (Gtk.Widget)

		CanFocus = false;
		SetSizeRequest (200, 200);

		// --- Initialization (Gtk.ScrolledWindow)

		SetPolicy (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);
		SetChild (listView);

		// --- References to keep

		list_model = listModel;
		tree_model = treeModel;
		selection_model = selectionModel;
		list_view = listView;

		// --- Other initialization (TODO: remove references to PintaCore)

		PintaCore.Workspace.ActiveDocumentChanged += HandleActiveDocumentChanged;

		// Object lists can change without a history push (the text tool creates its object on click
		// and only pushes on commit), so refresh sub-rows on that seam too.
		LayerObjectSelection.ObjectsChanged += RefreshObjectRows;
	}

	// Returns the (cached, live) child model of object rows for a layer row, or null for object rows
	// (no grandchildren) and layers with no objects (no expander shown).
	private Gio.ListModel? CreateChildModel (GObject.Object obj)
	{
		if (active_document is null || obj is not LayersListViewItem item || item.UserLayer is null || item.IsObjectRow)
			return null;

		UserLayer layer = item.UserLayer;
		if (!layer.HasObjectSubNodes)
			return null;

		if (!child_models.TryGetValue (layer, out Gio.ListStore? store)) {
			store = Gio.ListStore.New (LayersListViewItem.GetGType ());
			child_models[layer] = store;
		}

		PopulateChildModel (store, layer);
		return store;
	}

	private void PopulateChildModel (Gio.ListStore store, UserLayer layer)
	{
		if (active_document is null)
			return;

		store.RemoveMultiple (0, store.GetNItems ());
		for (int i = 0; i < layer.TextObjects.Count; ++i)
			store.Append (LayersListViewItem.NewTextObject (active_document, layer, layer.TextObjects[i], i));
		// Skip Rasterize-on-finalize shapes: they are transient and fuse the moment you move on;
		// showing them as a sub-node that then vanishes is confusing. Index i stays the position
		// in ShapeObjects so click-to-select still maps correctly.
		for (int i = 0; i < layer.ShapeObjects.Count; ++i)
			if (!layer.ShapeObjects[i].RasterizeOnFinalize)
				store.Append (LayersListViewItem.NewShapeObject (active_document, layer, layer.ShapeObjects[i], i));
	}

	private static void HandleFactorySetup (
		Gtk.SignalListItemFactory factory,
		Gtk.SignalListItemFactory.SetupSignalArgs args)
	{
		var item = (Gtk.ListItem) args.Object;
		// A TreeExpander supplies the expand/collapse arrow and indentation; it wraps the row widget.
		Gtk.TreeExpander expander = Gtk.TreeExpander.New ();
		expander.SetChild (LayersListViewItemWidget.New ());
		item.SetChild (expander);
	}

	private static void HandleFactoryBind (
		Gtk.SignalListItemFactory factory,
		Gtk.SignalListItemFactory.BindSignalArgs args)
	{
		var list_item = (Gtk.ListItem) args.Object;
		var row = (Gtk.TreeListRow) list_item.GetItem ()!;
		var model_item = (LayersListViewItem) row.GetItem ()!;
		var expander = (Gtk.TreeExpander) list_item.GetChild ()!;
		expander.SetListRow (row);
		var widget = (LayersListViewItemWidget) expander.GetChild ()!;
		widget.SetItem (model_item);
	}

	// The item at a flattened tree position, or null if out of range.
	private LayersListViewItem? ItemAt (uint position)
	{
		Gtk.TreeListRow? row = tree_model.GetRow (position);
		return row?.GetItem () as LayersListViewItem;
	}

	private void HandleSelectionChanged (
		Gtk.SelectionModel sender,
		EventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		// If changing the current layer causes a history item to be added, ensure we
		// don't end up in an infinite loop when HandleHistoryChanged updates the
		// selection (see bug #1463)
		if (changing_selection)
			return;

		try {
			changing_selection = true;

			uint sel = selection_model.Selected;
			// GTK_INVALID_LIST_POSITION is uint.MaxValue.
			if (sel == uint.MaxValue)
				return;

			// Selecting a layer row or any of its object rows makes that layer current.
			LayersListViewItem? item = ItemAt (sel);
			if (item?.UserLayer is not { } layer)
				return;

			int doc_idx = active_document.Layers.IndexOf (layer);
			if (doc_idx >= 0 && active_document.Layers.CurrentUserLayerIndex != doc_idx)
				active_document.Layers.SetCurrentUserLayer (doc_idx);

			// Clicking a shape object row selects that shape in the editor and shows its control points.
			// Select by stored index, not by reference: Store() rebuilds ShapeObjects on every persist.
			if (item.ShapeObject is not null)
				LayerObjectSelection.RequestShapeSelect (layer, item.ObjectIndex);
			// Clicking a text object row activates the Text tool and starts editing it (shows its handles).
			else if (item.TextObject is not null)
				LayerObjectSelection.RequestTextSelect (layer, item.ObjectIndex);
		} finally {
			changing_selection = false;
		}
	}

	private void HandleRowActivated (
		Gtk.ListView sender,
		Gtk.ListView.ActivateSignalArgs args)
	{
		// Double-click opens Layer Properties — but only for a layer row. Object sub-rows are already
		// selected on single-click (HandleSelectionChanged); they have no properties dialog yet, so a
		// double-click on one must NOT open (and thereby rename via) the parent layer's properties.
		if (ItemAt (args.Position)?.IsObjectRow == true)
			return;

		PintaCore.Actions.Layers.Properties.Activate ();
	}

	private void HandleActiveDocumentChanged (object? sender, EventArgs e)
	{
		Document? doc =
			PintaCore.Workspace.HasOpenDocuments
			? PintaCore.Workspace.ActiveDocument
			: null;

		if (active_document == doc)
			return;

		if (active_document != null) {
			active_document.History.HistoryItemAdded -= HandleHistoryChanged;
			active_document.History.ActionUndone -= HandleHistoryChanged;
			active_document.History.ActionRedone -= HandleHistoryChanged;
			active_document.Layers.LayerAdded -= HandleLayerAdded;
			active_document.Layers.LayerRemoved -= HandleLayerRemoved;
			active_document.Layers.SelectedLayerChanged -= HandleSelectedLayerChanged;
			active_document.Layers.LayerPropertyChanged -= HandleLayerPropertyChanged;
		}

		// Clear out old items and rebuild.
		list_model.RemoveMultiple (0, list_model.GetNItems ());
		child_models.Clear ();

		active_document = doc;
		if (doc is null)
			return;

		foreach (var layer in doc.Layers.UserLayers.Reverse ())
			list_model.Append (LayersListViewItem.New (doc, layer));

		// Update our selection to match the document's active layer (if there is one).
		int cur = doc.Layers.CurrentUserLayerIndex;
		if (cur >= 0 && cur < doc.Layers.Count ())
			SelectLayerRow (doc.Layers[cur]);

		doc.History.HistoryItemAdded += HandleHistoryChanged;
		doc.History.ActionUndone += HandleHistoryChanged;
		doc.History.ActionRedone += HandleHistoryChanged;
		doc.Layers.LayerAdded += HandleLayerAdded;
		doc.Layers.LayerRemoved += HandleLayerRemoved;
		doc.Layers.SelectedLayerChanged += HandleSelectedLayerChanged;
		doc.Layers.LayerPropertyChanged += HandleLayerPropertyChanged;
	}

	private void HandleHistoryChanged (object? sender, EventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		// Update the list view items to refresh their corresponding widgets.
		// This update should ideally be done through gobject property bindings instead, but we don't have the ability to add custom properties yet
		for (uint i = 0; i < list_model.GetNItems (); ++i) {
			LayersListViewItem item = (LayersListViewItem) list_model.GetObject (i)!;
			item.NotifyLayerModified ();
		}

		// The shape-creation history item is pushed *before* the engine's ShapeObjects are persisted
		// (same synchronous gesture), so reading them now would lag one shape behind. Defer to idle so
		// the refresh runs after the gesture returns to the main loop and ShapeObjects is current.
		// ponytail: idle-deferred read; the alternative is cross-assembly persist notifications.
		GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
			RefreshObjectRows ();
			return false;
		});
	}

	// Keep each layer's object sub-rows in sync with its ShapeObjects/TextObjects after edits.
	// Layers that already show children are repopulated in place (keeps expand state, no collapse
	// on every point edit); layers that gained/lost their first object have their root row replaced
	// so the tree re-evaluates expandability.
	private void RefreshObjectRows ()
	{
		if (active_document is null)
			return;

		// Prune child models for layers no longer in the document.
		foreach (UserLayer stale in child_models.Keys.Where (l => active_document.Layers.IndexOf (l) < 0).ToList ())
			child_models.Remove (stale);

		bool replacedRow = false;
		for (uint i = 0; i < list_model.GetNItems (); ++i) {
			if (list_model.GetObject (i) is not LayersListViewItem item || item.UserLayer is not { } layer)
				continue;

			bool hasObjects = layer.HasObjectSubNodes;

			if (hasObjects) {
				// A layer that had no child model yet was not expandable, so the tree must re-run the
				// child create func — that needs the root row replaced. Once it has one, every later
				// add/remove is an in-place store update: the row (and the user's expansion) survives,
				// so adding a second/third object doesn't collapse the layer.
				bool isFirstObject = !child_models.TryGetValue (layer, out Gio.ListStore? store);
				if (isFirstObject) {
					store = Gio.ListStore.New (LayersListViewItem.GetGType ());
					child_models[layer] = store;
				}

				// Populate before touching the root row, so when the tree asks for children it gets a
				// ready model.
				PopulateChildModel (store!, layer);

				if (isFirstObject) {
					list_model.Remove (i);
					LayersListViewItem replacement = LayersListViewItem.New (active_document, layer);
					list_model.Insert (i, replacement);
					replacedRow = true;
					// Expand it so the object that just appeared is visible without a manual click.
					ExpandRowFor (replacement);
				}
			} else if (child_models.ContainsKey (layer)) {
				// Last object removed: drop the child model and replace the row so it's no longer expandable.
				child_models.Remove (layer);
				list_model.Remove (i);
				list_model.Insert (i, LayersListViewItem.New (active_document, layer));
				replacedRow = true;
			}
		}

		// Replacing a root row makes the SingleSelection drop its selection (often snapping the dock
		// highlight to another layer), even though the document's current layer is unchanged — e.g.
		// drawing the first shape on a new layer leaves the highlight on Background. Re-sync the dock
		// selection to the current layer; the handler no-ops if selection already sits on it or one of
		// its object rows. (object-layer BUG 2)
		if (replacedRow)
			HandleSelectedLayerChanged (this, EventArgs.Empty);
	}

	// Expands the tree row holding the given item, if it is currently in the tree.
	private void ExpandRowFor (LayersListViewItem item)
	{
		uint n = tree_model.GetNItems ();
		for (uint i = 0; i < n; ++i) {
			Gtk.TreeListRow? row = tree_model.GetRow (i);
			if (row?.GetItem () == item) {
				row.SetExpanded (true);
				return;
			}
		}
	}

	// Selects the row for a layer (the layer's own row, not one of its object rows).
	private void SelectLayerRow (UserLayer layer)
	{
		uint n = tree_model.GetNItems ();
		for (uint i = 0; i < n; ++i) {
			LayersListViewItem? item = ItemAt (i);
			if (item is not null && !item.IsObjectRow && item.UserLayer == layer) {
				selection_model.SelectItem (i, unselectRest: true);
				list_view.ScrollToSelectedItem (selection_model);
				return;
			}
		}
	}

	private void HandleLayerAdded (object? sender, IndexEventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		int count = active_document.Layers.Count ();
		if (e.Index < 0 || e.Index >= count)
			return;

		int index = count - 1 - e.Index;
		if (index < 0) index = 0;
		if (index > (int) list_model.GetNItems ()) index = (int) list_model.GetNItems ();

		try {
			list_model.Insert ((uint) index, LayersListViewItem.New (active_document, active_document.Layers[e.Index]));
		} catch {
			// Fallback: rebuild on unexpected state.
			return;
		}
		list_view.ScrollToSelectedItem (selection_model);
	}

	private void HandleLayerRemoved (object? sender, IndexEventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		int count = active_document.Layers.Count (); // count after removal
		// e.Index is the doc index that was removed.
		int modelIndex = count - e.Index;
		// Clamp to valid range; if out of range, ignore (move is handled via Added+Removed pair).
		if (modelIndex < 0 || modelIndex >= (int) list_model.GetNItems ())
			return;

		// Note: don't need to subtract 1 because the layer has already been removed from the document.
		try {
			list_model.Remove ((uint) modelIndex);
		} catch {
			// Ignore if already removed via move sequence.
		}
		list_view.ScrollToSelectedItem (selection_model);
	}

	private void HandleSelectedLayerChanged (object? sender, EventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		int count = active_document.Layers.Count ();
		if (count == 0)
			return;

		int cur = active_document.Layers.CurrentUserLayerIndex;
		if (cur < 0 || cur >= count)
			return;

		UserLayer layer = active_document.Layers[cur];

		// If the selection is already on this layer's row or one of its object rows, leave it —
		// otherwise clicking an object row would immediately bounce selection up to the layer row.
		LayersListViewItem? current = ItemAt (selection_model.Selected);
		if (current?.UserLayer == layer)
			return;

		SelectLayerRow (layer);
	}

	private void HandleLayerPropertyChanged (object? sender, EventArgs e)
	{
		// Treat the same as an undo event, and update the widgets.
		HandleHistoryChanged (sender, e);
	}
}
