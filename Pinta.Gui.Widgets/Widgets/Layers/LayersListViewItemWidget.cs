//
// LayersListViewItemWidget.cs
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
using Cairo;
using GObject;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

// GObject subclass for use with Gio.ListStore
[GObject.Subclass<GObject.Object>]
public sealed partial class LayersListViewItem
{
	private CanvasRenderer? canvas_renderer;

	// NRT - GObject requires a parameterless constructor, and these don't have simple defaults
	private Document? document;
	public UserLayer? UserLayer { get; private set; }

	// When set, this row represents a re-editable object (shape or text) nested under UserLayer,
	// rather than the layer itself. Object rows support select, show/hide, rename and reorder.
	public ShapeObject? ShapeObject { get; private set; }
	public TextObject? TextObject { get; private set; }
	// A modifier node (adjustment/effect/transform) nested under UserLayer. Unlike a shape or text
	// row, it applies to every row beneath it, which the label marks so the ordering reads correctly.
	public ILayerModifierNode? ModifierNode { get; private set; }
	// A mask row stands for the layer's mask slot (see UserLayer.Mask). It is displayed like an
	// object sub-row (no thumbnail, tinted, badge) but is not a z-ordered object: it applies to the
	// whole layer, is not draggable, and has no opacity/blend/property editor.
	public bool IsMaskRow { get; private set; }
	public bool IsObjectRow => ShapeObject is not null || TextObject is not null || ModifierNode is not null;

	// Index of this object within its layer's ShapeObjects/TextObjects list. Used to select the shape
	// by position rather than by reference, because ShapeEngineCollection.Store rebuilds ShapeObjects
	// with new instances on every persist (a held reference goes stale, but the ordering is stable).
	public int ObjectIndex { get; private set; }

	public static LayersListViewItem New (Document doc, UserLayer userLayer)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		return item;
	}

	public static LayersListViewItem NewShapeObject (Document doc, UserLayer userLayer, ShapeObject shape, int index)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		item.ShapeObject = shape;
		item.ObjectIndex = index;
		return item;
	}

	public static LayersListViewItem NewTextObject (Document doc, UserLayer userLayer, TextObject text, int index)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		item.TextObject = text;
		item.ObjectIndex = index;
		return item;
	}

	public static LayersListViewItem NewModifierNode (Document doc, UserLayer userLayer, ILayerModifierNode node, int index)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		item.ModifierNode = node;
		item.ObjectIndex = index;
		return item;
	}

	public static LayersListViewItem NewMaskRow (Document doc, UserLayer userLayer)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		item.IsMaskRow = true;
		item.ObjectIndex = -1;
		return item;
	}

	/// <summary>Whether the mask this row stands for is hidden (disabled).</summary>
	public bool MaskHidden => UserLayer?.Mask?.Hidden ?? false;

	public string Label {
		get {
			if (IsMaskRow)
				return Translations.GetString ("Layer Mask");
			if (ModifierNode is not null)
				// Translators: a layer modifier row in the layers dock; it applies to everything below it.
				return Translations.GetString ("▼ {0}", ModifierNode.DisplayName);
			if (ShapeObject is not null)
				return string.IsNullOrEmpty (ObjectName) ? ShapeTypeName (ShapeObject.ShapeType) : ObjectName;
			if (TextObject is not null)
				return string.IsNullOrEmpty (ObjectName) ? Translations.GetString ("Text") : ObjectName;
			return UserLayer?.Name ?? string.Empty;
		}
	}

	private static string ShapeTypeName (ShapeObjectType type) => type switch {
		ShapeObjectType.Ellipse => Translations.GetString ("Ellipse"),
		ShapeObjectType.RoundedLineSeries => Translations.GetString ("Rounded Rectangle"),
		ShapeObjectType.Triangle => Translations.GetString ("Triangle"),
		ShapeObjectType.OpenLineCurveSeries => Translations.GetString ("Line/Curve"),
		_ => Translations.GetString ("Shape"),
	};

	public bool Visible => IsObjectRow ? !ObjectHidden : IsMaskRow ? !MaskHidden : !UserLayer?.Hidden ?? false;

	/// <summary>
	/// Whether this object row is the bottom one under its layer, which ends the hierarchy line with
	/// an elbow instead of carrying it on down. Object rows are listed top-first, so the bottom row is
	/// the one at index 0 of the layer's object list.
	/// ponytail: index 0 may be a rasterize-on-finalize shape that gets no row, in which case the line
	/// runs one row too far; give the row its position in the child model if that ever shows.
	/// </summary>
	public bool IsLastObjectRow
		=> IsObjectRow
			? ObjectIndex == 0
			: IsMaskRow && !(UserLayer?.HasObjectSubNodes ?? false);

	public string TooltipText {
		get {
			if (UserLayer is null)
				return string.Empty;
			string blend = UserBlendOps.GetBlendModeName (UserLayer.BlendMode);
			int opacity = (int) Math.Round (UserLayer.Opacity * 100);
			return Translations.GetString ("Blend Mode: {0}", blend) + "\n"
				+ Translations.GetString ("Opacity: {0}%", opacity) + "\n\n"
				+ Translations.GetString ("Double-click for Layer Properties") + "\n"
				+ Translations.GetString ("Drag and drop to reorder");
		}
	}

	public ImageSurface BuildThumbnail (
		int widthRequest,
		int heightRequest)
	{
		if (document is null || UserLayer is null)
			throw new InvalidOperationException ($"{nameof (LayersListViewItem)} is not initialized");

		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, widthRequest, heightRequest);

		List<Layer> layers = UserLayer.GetLayersToPaint ().ToList ();
		// For the current layer, show the selection layer too (e.g. when moving the selection's contents).
		if (UserLayer == document.Layers.CurrentUserLayer && document.Layers.ShowSelectionLayer)
			layers.Add (document.Layers.SelectionLayer);

		// Directly use the layer's surface if there isn't any blending required.
		if (layers.Count == 1)
			return layers[0].Surface;

		canvas_renderer ??= new CanvasRenderer (
			PintaCore.LivePreview,
			PintaCore.Workspace,
			enableLivePreview: false,
			enableBackgroundPattern: true);
		canvas_renderer.Initialize (document.ImageSize, new Size (widthRequest, heightRequest));
		canvas_renderer.Render (layers, surface, PointI.Zero);

		return surface;
	}

	public void HandleVisibilityToggled (bool visible)
	{
		if (document is null || UserLayer is null)
			throw new InvalidOperationException ($"{nameof (LayersListViewItem)} is not initialized");

		if (Visible == visible)
			return;

		if (IsObjectRow) {
			SetObjectHidden (!visible);
			return;
		}

		if (IsMaskRow) {
			SetMaskHidden (!visible);
			return;
		}

		Document doc = PintaCore.Workspace.ActiveDocument;

		LayerProperties initial = new (UserLayer.Name, visible, UserLayer.Opacity, UserLayer.BlendMode);
		LayerProperties updated = new (UserLayer.Name, !visible, UserLayer.Opacity, UserLayer.BlendMode);

		UpdateLayerPropertiesHistoryItem historyItem = new (
			visible ? Resources.StandardIcons.ViewReveal : Resources.StandardIcons.ViewConceal,
			visible ? Translations.GetString ("Show Layer") : Translations.GetString ("Hide Layer"),
			doc.Layers.IndexOf (UserLayer),
			initial,
			updated);

		doc.History.PushNewItem (historyItem);

		historyItem.Redo ();
	}

	/// <summary>
	/// The live object this row stands for, resolved by index rather than through the held reference
	/// (which goes stale when the object lists are rebuilt on persist). Null when this is a layer row
	/// or the object is gone (e.g. rasterized).
	/// </summary>
	private ILayerObject? LiveObject
		=> IsObjectRow ? UserLayer?.FindObjectAt (ObjectIndex) : null;

	/// <summary>Whether this row's object is still on its layer (false once it is deleted or baked).</summary>
	public bool ObjectExists => LiveObject is not null;

	/// <summary>
	/// Bakes just this row's object into its layer's base raster (the per-object counterpart of the
	/// layer menu's "Rasterize All Objects"), prompting first. The object stops being editable.
	/// </summary>
	public void RasterizeObject ()
	{
		if (document is null || UserLayer is null || LiveObject is not { } obj)
			return;

		// A modifier node's pixels are not separable from the objects beneath it once the accumulator
		// has run, so rasterizing one bakes the layer's whole stack. Say so before doing it.
		if (obj is ILayerModifierNode) {
			if (!ObjectRasterizer.Confirm (
				PintaCore.Chrome,
				[Translations.GetString ("every effect and object on this layer")]))
				return;

			BakeSnapshot stackSnapshot = BakeSnapshot.Create (UserLayer, includeMask: true);

			if (!UserLayer.RasterizeModifierStack ())
				return;

			Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
			document.History.PushNewItem (
				new RasterizeObjectsHistoryItem (
					PintaCore.Workspace,
					Resources.Icons.LayerMergeDown,
					Translations.GetString ("Rasterize Layer Effects"),
					stackSnapshot,
					UserLayer));
			return;
		}

		bool isText = obj is TextObject;
		int kindIndex = UserLayer.UserLayerIndexOfKind (UserLayer, isText, ObjectIndex);
		if (kindIndex < 0)
			return;

		if (!ObjectRasterizer.Confirm (PintaCore.Chrome, [Label]))
			return;

		ObjectRasterizer.RasterizeSubset (
			document,
			PintaCore.Workspace,
			PintaCore.Chrome,
			UserLayer,
			isText ? [] : [kindIndex],
			isText ? [kindIndex] : []);
	}

	/// <summary>
	/// Removes this row's object from its layer entirely, as one undoable step. Reuses the rasterize
	/// history item, which is a plain swap of the base surface, the object surface and the object
	/// list — exactly what a delete has to restore.
	/// </summary>
	public void DeleteObject ()
	{
		if (document is null || UserLayer is null || LiveObject is not { } obj)
			return;

		BakeSnapshot snapshot = BakeSnapshot.Create (UserLayer);

		UserLayer.RemoveObject (obj);
		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);

		document.History.PushNewItem (
			new RasterizeObjectsHistoryItem (
				PintaCore.Workspace,
				Resources.Icons.LayerDelete,
				Translations.GetString ("Delete Object"),
				snapshot,
				UserLayer));

		// The object's on-canvas editing chrome (handles, re-edit rectangles) lives on the tool layer
		// and would otherwise hover over an object that no longer exists.
		document.Layers.ToolLayer.Clear ();
		LayerObjectSelection.RaiseObjectsChanged ();
	}

	/// <summary>
	/// Removes this layer's mask entirely, as one undoable step. Ends mask editing (the mask row is
	/// gone, so the paint tools return to the layer raster).
	/// </summary>
	public void DeleteMask ()
	{
		if (UserLayer is null || !UserLayer.HasMask)
			return;

		UserLayer layer = UserLayer;
		LayerMask mask = layer.Mask!;

		LayerMaskHistoryItem hist = new (
			PintaCore.Workspace,
			Resources.Icons.LayerDelete,
			Translations.GetString ("Delete Layer Mask"),
			layer,
			beforeSurface: mask.Surface.Clone (),
			afterSurface: null,
			beforeHidden: mask.Hidden);

		LayerMaskSelection.SetActiveMaskLayer (null);
		layer.DropMask ();

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, layer);
		document?.History.PushNewItem (hist);

		LayerObjectSelection.RaiseObjectsChanged ();
	}

	/// <summary>Current opacity (0..1) of the object this row represents.</summary>
	public double ObjectOpacity
		=> LiveObject?.Opacity ?? 1.0;

	/// <summary>Whether the object this row represents is hidden from every render path.</summary>
	public bool ObjectHidden
		=> LiveObject?.Hidden ?? false;

	/// <summary>The object's user-given name, or empty when it still uses the type default.</summary>
	public string ObjectName
		=> LiveObject?.Name ?? string.Empty;

	/// <summary>Current blend mode of the object this row represents.</summary>
	public BlendMode ObjectBlendMode
		=> LiveObject?.BlendMode ?? BlendMode.Normal;

	/// <summary>
	/// Toggles the mask this row represents between hidden (disabled) and active, as one undoable
	/// step. A hidden mask lets the layer render unmasked without deleting the mask.
	/// </summary>
	public void SetMaskHidden (bool hidden)
	{
		if (UserLayer is null || UserLayer.Mask is null)
			return;

		if (UserLayer.Mask.Hidden == hidden)
			return;

		// The mask visibility is a plain one-field swap: Undo applies the opposite state.
		UserLayer.Mask.Hidden = hidden;

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);

		document?.History.PushNewItem (
			new LayerMaskVisibleHistoryItem (
				PintaCore.Workspace,
				Resources.Icons.LayerProperties,
				hidden ? Translations.GetString ("Hide Layer Mask") : Translations.GetString ("Show Layer Mask"),
				UserLayer, hidden));
	}

	/// <summary>
	/// Applies an opacity to this row's object and re-renders, with no history item — used for the
	/// live drag of the opacity slider. <see cref="PushObjectOpacityHistory"/> records the whole
	/// drag as one undoable step once the slider is dismissed.
	/// </summary>
	public void SetObjectOpacity (double opacity)
	{
		if (UserLayer is null || LiveObject is not { } obj)
			return;

		obj.Opacity = opacity;
		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
	}

	public void PushObjectOpacityHistory (double previousOpacity)
		=> PushObjectProperty (
			Translations.GetString ("Object Opacity"),
			o => o.Opacity,
			(o, v) => o.Opacity = v,
			previousOpacity);

	/// <summary>
	/// Applies a blend mode to this row's object and re-renders, with no history item — used for the
	/// live dropdown in the popover. <see cref="PushObjectBlendModeHistory"/> records the change as
	/// one undoable step once the popover closes.
	/// </summary>
	public void SetObjectBlendMode (BlendMode mode)
	{
		if (UserLayer is null || LiveObject is not { } obj || obj.BlendMode == mode)
			return;

		obj.BlendMode = mode;
		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
	}

	public void PushObjectBlendModeHistory (BlendMode previousMode)
		=> PushObjectProperty (
			Translations.GetString ("Object Blend Mode"),
			o => o.BlendMode,
			(o, v) => o.BlendMode = v,
			previousMode);

	public void SetObjectHidden (bool hidden)
	{
		if (LiveObject is not { } obj || obj.Hidden == hidden)
			return;

		bool before = obj.Hidden;
		SetObjectHiddenLive (hidden);
		PushObjectHiddenHistory (before);
	}

	/// <summary>
	/// Applies visibility to this row's object and re-renders, with no history item — the object
	/// properties dialog's live checkbox, which records one step when the dialog is accepted.
	/// </summary>
	public void SetObjectHiddenLive (bool hidden)
	{
		if (UserLayer is null || LiveObject is not { } obj || obj.Hidden == hidden)
			return;

		obj.Hidden = hidden;
		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
	}

	public void PushObjectHiddenHistory (bool previousHidden)
		=> PushObjectProperty (
			previousHidden ? Translations.GetString ("Show Object") : Translations.GetString ("Hide Object"),
			o => o.Hidden,
			(o, v) => o.Hidden = v,
			previousHidden);

	public void RenameObject (string name)
	{
		if (LiveObject is not { } obj || obj.Name == name)
			return;

		string before = obj.Name;
		obj.Name = name;
		PushObjectProperty (
			Translations.GetString ("Rename Object"),
			o => o.Name,
			(o, v) => o.Name = v,
			before);

		// The row label reads the object, so ask the dock to rebuild its object rows.
		LayerObjectSelection.RaiseObjectsChanged ();
	}

	/// <summary>
	/// Moves the object to <paramref name="index"/> in its layer's unified object list — its
	/// z-order, since the list is rendered in order. Driven by dragging the row onto another object
	/// row; works across kinds (a text can be dragged beneath a shape).
	/// </summary>
	public void MoveObjectTo (int index)
	{
		if (UserLayer is null || document is null || !IsObjectRow)
			return;

		ObjectReorderHistoryItem historyItem = new (
			PintaCore.Workspace,
			PintaCore.Chrome,
			Resources.Icons.LayerProperties,
			Translations.GetString ("Reorder Object"),
			UserLayer,
			ObjectIndex,
			index);

		if (!UserLayer.MoveObjectAt (ObjectIndex, index))
			return;

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
		document.History.PushNewItem (historyItem);
		LayerObjectSelection.RaiseObjectsChanged ();
	}

	/// <summary>Whether this row and <paramref name="other"/> are object rows on the same layer — the
	/// only case where one can be dragged onto the other to reorder (cross-kind allowed).</summary>
	public bool IsReorderablePeer (LayersListViewItem other)
		=> IsObjectRow
			&& other.IsObjectRow
			&& ReferenceEquals (UserLayer, other.UserLayer);

	/// <summary>
	/// Pushes an already-applied per-object property change as one undoable step. The value is set
	/// first (so the canvas updates immediately) and the item carries the value to go back to.
	/// </summary>
	private void PushObjectProperty<T> (string label, Func<ILayerObject, T> get, Action<ILayerObject, T> set, T previousValue)
	{
		if (UserLayer is null || document is null)
			return;

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);

		document.History.PushNewItem (
			new ObjectPropertyHistoryItem<T> (
				PintaCore.Workspace,
				PintaCore.Chrome,
				Resources.Icons.LayerProperties,
				label,
				UserLayer,
				ObjectIndex,
				get,
				set,
				previousValue));
	}

	public event EventHandler? LayerModified;

	/// <summary>
	/// Signal that the layer has been modified.
	/// In the future this should be replaced by GObject properties and bindings.
	/// </summary>
	public void NotifyLayerModified ()
	{
		LayerModified?.Invoke (this, EventArgs.Empty);
	}
}

[GObject.Subclass<Gtk.Box>]
public sealed partial class LayersListViewItemWidget
{
	private static readonly Pattern transparent_pattern = CairoExtensions.CreateTransparentBackgroundPattern (8);

	private LayersListViewItem? item;
	private ImageSurface? thumbnail_surface;

	private Gtk.DrawingArea item_thumbnail;
	private Gtk.Label item_label;
	private Gtk.CheckButton visible_button;
	private Gtk.DrawingArea object_badge;
	private Gtk.DrawingArea tree_line;

	// Which badge the current row shows: "Obj." for a shape/text, "Fx" for a layer effect.
	private string badge_label = EditableObjectBadge.ObjectLabel;

	public static LayersListViewItemWidget New ()
		=> NewWithProperties ([]);

	[MemberNotNull (nameof (item_thumbnail))]
	[MemberNotNull (nameof (item_label))]
	[MemberNotNull (nameof (visible_button))]
	[MemberNotNull (nameof (object_badge))]
	[MemberNotNull (nameof (tree_line))]
	partial void Initialize ()
	{
		Gtk.DrawingArea itemThumbnail = Gtk.DrawingArea.New ();
		itemThumbnail.SetDrawFunc ((area, context, width, height) => DrawThumbnail (context, width, height));
		itemThumbnail.WidthRequest = 60;
		itemThumbnail.HeightRequest = 40;

		Gtk.Label itemLabel = Gtk.Label.New (string.Empty);
		itemLabel.Halign = Gtk.Align.Start;
		itemLabel.Hexpand = true;
		itemLabel.Ellipsize = Pango.EllipsizeMode.End;

		Gtk.CheckButton visibleButton = Gtk.CheckButton.New ();
		visibleButton.Halign = Gtk.Align.End;
		visibleButton.Hexpand = false;
		visibleButton.OnToggled += (_, _) => item?.HandleVisibilityToggled (visibleButton.Active);

		// Leading badge marking a live, re-editable shape/text object row. Drawn with the
		// same Cairo "Obj." badge as the canvas (not an icon-theme image), so it can't fall
		// back to a bare square when the theme SVG text doesn't render.
		Gtk.DrawingArea objectBadge = Gtk.DrawingArea.New ();
		objectBadge.SetDrawFunc ((area, context, width, height) => DrawObjectBadge (context, width, height));
		objectBadge.WidthRequest = (int) EditableObjectBadge.Width;
		objectBadge.HeightRequest = (int) EditableObjectBadge.Height;
		objectBadge.Halign = Gtk.Align.Start;
		objectBadge.Visible = false;

		// Hierarchy line, shown only on object rows: it runs down from the parent layer row and turns
		// in towards this row's badge, so a sub-row reads as belonging to the layer above it. The
		// TreeExpander's indentation is to the left of this, so the line sits between the two.
		Gtk.DrawingArea treeLine = Gtk.DrawingArea.New ();
		treeLine.SetDrawFunc ((area, context, width, height) => DrawTreeLine (context, width, height));
		treeLine.WidthRequest = 10;
		treeLine.Visible = false;

		Gtk.GestureClick menuGesture = Gtk.GestureClick.New ();
		menuGesture.SetButton (Gdk.Constants.BUTTON_SECONDARY);
		menuGesture.OnPressed += MenuGesture_OnPressed;

		// Drag and drop to reorder layers. The dragged LayersListViewItem (a GObject)
		// is carried directly as the content, so the drop handler gets it back typed.
		Gtk.DragSource dragSource = Gtk.DragSource.New ();
		dragSource.SetActions (Gdk.DragAction.Move);
		dragSource.OnPrepare += DragSource_OnPrepare;
		this.AddController (dragSource);

		// Accept the base object GType: the transferred GObject.Value reports G_TYPE_OBJECT,
		// so requiring the specific subclass here would make the formats never intersect and
		// the drop would be silently rejected. We re-check the concrete type in the handler.
		Gtk.DropTarget dropTarget = Gtk.DropTarget.New (GObject.Type.Object, Gdk.DragAction.Move);
		dropTarget.OnDrop += DropTarget_OnDrop;
		this.AddController (dropTarget);

		// --- Initialization (Gtk.Widget)

		this.SetAllMargins (2);
		this.AddController (menuGesture);

		// --- Initialization (Gtk.Box)

		Spacing = 6;

		SetOrientation (Gtk.Orientation.Horizontal);

		Append (treeLine);
		Append (visibleButton);
		Append (objectBadge);
		Append (itemLabel);
		Append (itemThumbnail);

		// --- References to keep

		item_thumbnail = itemThumbnail;
		item_label = itemLabel;
		visible_button = visibleButton;
		object_badge = objectBadge;
		tree_line = treeLine;
	}

	// A row is "bound" when it is showing a layer of an open document. Layer-only operations
	// (context menu, drag/drop reorder) additionally exclude object sub-rows.
	private static bool IsBoundRow ([NotNullWhen (true)] LayersListViewItem? row)
		=> row?.UserLayer is not null && PintaCore.Workspace.HasOpenDocuments;

	private static bool IsLayerRow ([NotNullWhen (true)] LayersListViewItem? row)
		=> IsBoundRow (row) && !row.IsObjectRow && !row.IsMaskRow;

	private void MenuGesture_OnPressed (
		Gtk.GestureClick _,
		Gtk.GestureClick.PressedSignalArgs args)
	{
		if (!IsBoundRow (item))
			return;

		// Object sub-rows get their own small editor popover instead of the layer menu.
		if (item.IsObjectRow) {
			ShowObjectPopover (item);
			return;
		}

		// A mask sub-row gets its own minimal popover (delete; the mask has no blend/opacity).
		if (item.IsMaskRow) {
			ShowMaskPopover (item);
			return;
		}

		Document doc = PintaCore.Workspace.ActiveDocument;
		// Ensure this is the current layer before opening the menu, since the menu actions
		// apply to the current layer.
		if (doc.Layers.CurrentUserLayer != item.UserLayer)
			doc.Layers.SetCurrentUserLayer (item.UserLayer!);

		LayerActions actions = PintaCore.Actions.Layers;

		Gio.Menu visibilitySection = Gio.Menu.New ();
		visibilitySection.AppendItem (actions.CreateSoloLayerMenuItem (item.UserLayer!));

		Gio.Menu operationsSection = Gio.Menu.New ();
		operationsSection.AppendItem (actions.DeleteLayer.CreateMenuItem ());
		operationsSection.AppendItem (actions.DuplicateLayer.CreateMenuItem ());
		operationsSection.AppendItem (actions.MergeLayerDown.CreateMenuItem ());
		operationsSection.AppendItem (actions.MoveLayerUp.CreateMenuItem ());
		operationsSection.AppendItem (actions.MoveLayerDown.CreateMenuItem ());

		Gio.Menu flipSection = Gio.Menu.New ();
		flipSection.AppendItem (actions.FlipHorizontal.CreateMenuItem ());
		flipSection.AppendItem (actions.FlipVertical.CreateMenuItem ());
		flipSection.AppendItem (actions.RotateZoom.CreateMenuItem ());

		Gio.Menu propertiesSection = Gio.Menu.New ();
		// Rename reuses the Layer Properties dialog (which contains the name field).
		propertiesSection.AppendItem (Gio.MenuItem.New (Translations.GetString ("Rename Layer..."), actions.Properties.FullName));
		propertiesSection.AppendItem (actions.Properties.CreateMenuItem ());

		Gio.Menu menu = Gio.Menu.New ();
		menu.AppendSection (null, visibilitySection);
		menu.AppendSection (null, operationsSection);
		menu.AppendSection (null, flipSection);

		// Only offer "Rasterize All Objects" for layers that actually hold editable objects.
		if (item.UserLayer!.HasObjectSubNodes) {
			Gio.Menu objectsSection = Gio.Menu.New ();
			objectsSection.AppendItem (actions.RasterizeAllObjects.CreateMenuItem ());
			menu.AppendSection (null, objectsSection);
		}

		// A layer with no mask yet can gain one here; once it has one, the mask sub-row handles it.
		if (!item.UserLayer!.HasMask) {
			Gio.Menu maskSection = Gio.Menu.New ();
			maskSection.AppendItem (actions.AddLayerMask.CreateMenuItem ());
			menu.AppendSection (null, maskSection);
		}

		menu.AppendSection (null, propertiesSection);

		Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel (menu);
		popover.SetParent (this);
		popover.Popup ();
	}

	// Right-clicking an object sub-row opens its quick editor: blend mode and opacity (z-order is drag
	// and drop). A popover of plain widgets rather than a Gio.Menu, since these need slider controls
	// and would otherwise each have to be registered as an application action.
	//
	// Deliberately holds no text entry: an entry inside the popover has to grab keyboard focus (the
	// main window forwards key presses to the focus widget before its tool shortcuts, so without the
	// grab, typing switches tools) — and that grab also stopped a click outside from dismissing the
	// popover. Renaming, and every other per-object setting, lives in the object properties window
	// reached from the button at the bottom.
	private void ShowObjectPopover (LayersListViewItem row)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.SetAllMargins (6);

		Gtk.Popover popover = Gtk.Popover.New ();

		// --- Blend mode. Matches the layer properties dialog dropdown; a non-Normal mode composites
		// the object against the pixels beneath it on the layer. One history item on close.
		BlendMode beforeBlend = row.ObjectBlendMode;

		// An inline scrolling list, not a dropdown. Any control that opens its own popup — ComboBoxText
		// (a separate toplevel) and DropDown (a nested popover) alike — takes a grab that this popover
		// never gets back when the popup closes, leaving it dismissable only with Escape. A list that
		// is simply part of the popover has no popup to take a grab, so click-outside keeps working.
		string[] blendNames = [.. UserBlendOps.GetAllBlendModeNames ()];

		Gtk.ListBox blendList = Gtk.ListBox.New ();
		blendList.SelectionMode = Gtk.SelectionMode.Single;
		foreach (string blendName in blendNames) {
			Gtk.Label label = Gtk.Label.New (blendName);
			label.Halign = Gtk.Align.Start;
			label.Xalign = 0;
			label.SetAllMargins (4);
			blendList.Append (label);
		}

		int selectedBlend = Array.IndexOf (blendNames, UserBlendOps.GetBlendModeName (beforeBlend));
		if (blendList.GetRowAtIndex (Math.Max (0, selectedBlend)) is { } selectedRow)
			blendList.SelectRow (selectedRow);

		blendList.OnRowActivated += (_, args) => {
			int index = args.Row.GetIndex ();
			if (index >= 0 && index < blendNames.Length)
				row.SetObjectBlendMode (UserBlendOps.GetBlendModeByName (blendNames[index]));
		};

		// Tall enough to show a few modes at a time; the rest scroll. The list is the popover's bulk,
		// so keep it from growing with the number of blend modes.
		Gtk.ScrolledWindow blendScroll = Gtk.ScrolledWindow.New ();
		blendScroll.SetPolicy (Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
		blendScroll.HeightRequest = 140;
		blendScroll.WidthRequest = 180;
		blendScroll.SetChild (blendList);

		// A modifier has no pixels of its own, so its blend mode mixes the effect's result back into
		// the unmodified input — the same control, a different thing being blended.
		bool isModifier = row.ModifierNode is not null;

		Gtk.Label blendLabel = Gtk.Label.New (Translations.GetString ("Blend Mode:"));
		blendLabel.Halign = Gtk.Align.Start;
		if (isModifier)
			blendLabel.SetTooltipText (Translations.GetString ("How the effect's result is mixed back into the image it was applied to."));
		box.Append (blendLabel);
		box.Append (blendScroll);

		// --- Opacity. The drag updates the canvas live; a single history item is pushed when the
		// popover closes, so one undo restores the value the drag started from.
		double beforeOpacity = row.ObjectOpacity;

		Gtk.Scale scale = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 100, 1);
		scale.WidthRequest = 150;
		scale.DrawValue = true;
		scale.SetValue (beforeOpacity * 100);
		scale.OnValueChanged += (_, _) => row.SetObjectOpacity (scale.GetValue () / 100.0);

		Gtk.Box opacityBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		// On a modifier the same value is effect strength: 0 leaves the image untouched.
		Gtk.Label opacityLabel = Gtk.Label.New (
			isModifier ? Translations.GetString ("Strength:") : Translations.GetString ("Opacity:"));
		if (isModifier)
			opacityLabel.SetTooltipText (Translations.GetString ("How much of the effect is applied. At 0 the layer looks as it did before."));
		opacityBox.Append (opacityLabel);
		opacityBox.Append (scale);
		box.Append (opacityBox);

		// --- The operations, below a separator: the settings above act on the object in place, these
		// three take you elsewhere or end the object's life, which is the split the layer row's menu
		// draws with its own section lines.
		box.Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));

		// A modifier row reopens its own configuration dialog: a transform through the numeric
		// dialog, an effect through its effect dialog. Editing settings in place is the whole point
		// of a non-destructive node, so it goes above Rasterize.
		if (row.ModifierNode is ILayerModifierNode modifier) {
			UserLayer nodeLayer = row.UserLayer!;
			if (modifier is LayerTransformNode transform) {
				box.Append (MenuOption (
					Translations.GetString ("Transform Settings..."),
					Translations.GetString ("Change this transform's settings; the layer re-renders with the new values."),
					async () => { popover.Popdown (); await TransformNodeDialog.Edit (PintaCore.Chrome, PintaCore.Workspace, nodeLayer, transform, Resources.Icons.LayerRotateZoom); }));
			} else if (modifier is EffectModifierNode effect && effect.Effect.IsConfigurable) {
				box.Append (MenuOption (
					Translations.GetString ("Effect Settings..."),
					Translations.GetString ("Change this effect's settings; the layer re-renders with the new values."),
					() => { popover.Popdown (); ReconfigureModifier (nodeLayer, effect); }));
			}
		}

		// Each closes the popover first; the OnClosed handler below skips its property history when the
		// object is gone.
		box.Append (MenuOption (
			Translations.GetString ("Rasterize"),
			isModifier
				? Translations.GetString ("Bake this layer's effects and objects into its pixels; they stop being editable.")
				: Translations.GetString ("Bake this object into the layer's pixels; it stops being editable."),
			() => { popover.Popdown (); row.RasterizeObject (); }));

		box.Append (MenuOption (
			Translations.GetString ("Properties..."),
			null,
			() => { popover.Popdown (); ObjectPropertiesDialog.Show (PintaCore.Chrome.MainWindow, row); }));

		box.Append (MenuOption (
			Translations.GetString ("Delete"),
			null,
			() => { popover.Popdown (); row.DeleteObject (); }));

		popover.SetChild (box);
		popover.SetParent (this);
		popover.OnClosed += (_, _) => {
			// Nothing to record once the object is gone — and reading its "current" opacity/blend would
			// give the defaults, so an object that had been faded would push a bogus history item.
			if (!row.ObjectExists)
				return;

			if (row.ObjectOpacity != beforeOpacity)
				row.PushObjectOpacityHistory (beforeOpacity);
			if (row.ObjectBlendMode != beforeBlend)
				row.PushObjectBlendModeHistory (beforeBlend);
		};
		popover.Popup ();
	}

	// Right-clicking a mask sub-row: delete the mask (the only operation a mask has beyond painting
	// and show/hide). A mask is an alpha channel, not an object with blend/opacity/properties, so it
	// gets a one-button popover instead of the object editor.
	private void ShowMaskPopover (LayersListViewItem row)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.SetAllMargins (6);

		Gtk.Label hint = Gtk.Label.New (Translations.GetString (
			"Paint on the canvas to reveal the layer; erase to hide it. Right-click the row again for options."));
		hint.Wrap = true;
		hint.MaxWidthChars = 36;
		hint.Halign = Gtk.Align.Start;
		box.Append (hint);

		Gtk.Popover popover = Gtk.Popover.New ();
		popover.SetChild (box);

		box.Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		box.Append (MenuOption (
			Translations.GetString ("Delete Mask"),
			Translations.GetString ("Remove this layer's mask; the layer renders unmasked."),
			() => { popover.Popdown (); row.DeleteMask (); }));

		popover.SetParent (this);
		popover.Popup ();
	}

	// Reopens a modifier node's effect dialog and re-renders the layer with whatever the user leaves
	// behind. Deliberately not routed through LivePreviewManager: that path exists to add a new node,
	// so reusing it here would stack a second copy of the effect on top of this one.
	private static async void ReconfigureModifier (UserLayer layer, EffectModifierNode node)
	{
		if (!node.Effect.IsConfigurable)
			return;

		List<ILayerObject> objectsBefore = Pinta.Core.ObjectOpacity.CloneAll (layer.Objects);

		if (!await node.Effect.LaunchConfiguration ())
			return;

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, layer);
		PintaCore.Workspace.ActiveDocument.History.PushNewItem (
			new LayerObjectsHistoryItem (
				PintaCore.Workspace,
				PintaCore.Chrome,
				node.Effect.Icon,
				node.Effect.Name,
				layer,
				objectsBefore));
	}

	// One row of the object popover's operations section, styled to read as a menu option rather than a
	// button: flat (no frame) and left-aligned full width, which is what a Gio.Menu item looks like.
	// ponytail: plain widgets, not a real Gtk.PopoverMenu — that needs every item registered as a
	// GAction, and the settings above would each have to be a "custom" menu item anyway. Swap to a
	// PopoverMenu if these operations ever need keyboard accelerators or to appear in the main menus.
	private static Gtk.Button MenuOption (string label, string? tooltip, System.Action onActivated)
	{
		Gtk.Button button = Gtk.Button.NewWithLabel (label);
		button.AddCssClass ("flat");
		button.SetHasFrame (false);

		if (button.GetChild () is Gtk.Label buttonLabel) {
			buttonLabel.Halign = Gtk.Align.Start;
			buttonLabel.Xalign = 0;
		}

		if (tooltip is not null)
			button.SetTooltipText (tooltip);

		button.OnClicked += (_, _) => onActivated ();

		return button;
	}

	private Gdk.ContentProvider? DragSource_OnPrepare (
		Gtk.DragSource _,
		Gtk.DragSource.PrepareSignalArgs args)
	{
		if (!IsBoundRow (item))
			return null;

		return Gdk.ContentProvider.NewForValue (new GObject.Value ((GObject.Object) item));
	}

	private bool DropTarget_OnDrop (
		Gtk.DropTarget _,
		Gtk.DropTarget.DropSignalArgs args)
	{
		if (!IsBoundRow (item))
			return false;

		if (args.Value.GetObject () is not LayersListViewItem source || !IsBoundRow (source))
			return false;

		// Rows are dropped above when the pointer is in the upper half of the target, below otherwise.
		bool dropAbove = args.Y < GetHeight () / 2.0;

		if (item.IsObjectRow || source.IsObjectRow)
			return DropObjectRow (source, dropAbove);

		return DropLayerRow (source, dropAbove);
	}

	// Reordering an object sub-node within its layer's object list (its z-order). Only objects of
	// the same kind on the same layer can be reordered against each other: shapes and text live in
	// separate lists rendered into separate surfaces, and moving an object between layers would be
	// a different operation (its geometry belongs to the layer it was drawn on).
	private bool DropObjectRow (LayersListViewItem source, bool dropAbove)
	{
		if (item is null || !source.IsReorderablePeer (item))
			return false;

		int from = source.ObjectIndex;

		// Object rows are drawn top-first, exactly like layer rows: a higher index in layer.Objects is
		// higher up the list. Dropping on the upper half of the target lands above it (higher index).
		int insert = dropAbove ? item.ObjectIndex + 1 : item.ObjectIndex;
		if (from < insert)
			insert--; // removing the source first shifts everything above it down.

		if (insert == from)
			return false;

		AfterDrop (() => source.MoveObjectTo (insert));
		return true;
	}

	// Runs the reorder once GTK has finished with the drop, rather than inside the drop handler.
	// Moving an object rebuilds the dock's rows — which destroys the very widget whose drop handler is
	// running, and selecting the moved row can start a text edit that pumps the main loop. GTK then
	// begins a second drop while the first is still active and aborts the process on an assertion.
	private static void AfterDrop (System.Action reorder)
		=> GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
			reorder ();
			return false;
		});

	private bool DropLayerRow (LayersListViewItem source, bool dropAbove)
	{
		if (!IsLayerRow (item) || !IsLayerRow (source))
			return false;

		Document doc = PintaCore.Workspace.ActiveDocument;
		int from = doc.Layers.IndexOf (source.UserLayer!);
		int target = doc.Layers.IndexOf (item.UserLayer!);
		if (from < 0 || target < 0 || from == target)
			return false;

		// Rows are drawn top-first (higher doc index = higher up). Dropping on the
		// upper half of the target lands above it (higher doc index), lower half below.
		int insert = dropAbove ? target + 1 : target;
		if (from < insert)
			insert--; // removing the source first shifts everything above it down.

		if (insert == from)
			return false;

		// Deferred for the same reason as the object reorder above.
		AfterDrop (() => {
			MoveLayerHistoryItem hist = new (
				Resources.StandardIcons.LayerMoveUp,
				Translations.GetString ("Move Layer"),
				from,
				insert);
			doc.History.PushNewItem (hist);
			hist.Redo ();
		});
		return true;
	}

	/// <summary>
	/// Bind the widget to a different LayersListViewItem.
	/// </summary>
	public void SetItem (LayersListViewItem newItem)
	{
		if (item != null)
			item.LayerModified -= OnLayerModified;

		item = newItem;
		item.LayerModified += OnLayerModified;
		UpdateFromLayer ();
	}

	/// <summary>
	/// Event handler for modifications to the item's layer.
	/// </summary>
	private void OnLayerModified (object? sender, EventArgs e)
	{
		UpdateFromLayer ();
	}

	/// <summary>
	/// Update the widget to reflect the current state of the item's layer.
	/// </summary>
	private void UpdateFromLayer ()
	{
		if (item is null)
			throw new InvalidOperationException ($"{nameof (item)} is null");

		item_label.SetText (item.Label);

		// Object and mask rows get no thumbnail (the TreeExpander supplies their indentation), but
		// they do keep the visibility checkbox — it toggles the object's/mask's own Hidden flag.
		bool isObject = item.IsObjectRow || item.IsMaskRow;
		item_thumbnail.SetVisible (!isObject);
		visible_button.SetActive (item.Visible);

		// Widgets are recycled between rows, so both states have to be set explicitly.
		if (isObject)
			AddCssClass (ObjectRowCssClass);
		else
			RemoveCssClass (ObjectRowCssClass);
		tree_line.SetVisible (isObject);
		tree_line.QueueDraw ();

		if (isObject) {
			if (item.IsMaskRow) {
				// A mask row carries the "M" badge and a mask-specific tooltip; it marks the layer's
				// alpha channel, which applies to the whole layer.
				badge_label = EditableObjectBadge.MaskLabel;
				object_badge.Visible = true;
				object_badge.QueueDraw ();
				SetTooltipText (Translations.GetString ("Layer mask: an alpha channel applied to the whole layer.")
					+ "\n" + Translations.GetString ("Select this row to paint the mask; paint reveals, erase hides")
					+ "\n" + Translations.GetString ("Right-click to delete the mask"));
				return;
			}

			// Object rows are always live/editable (rasterizing drops the object entirely), so they
			// always get a badge — "Obj." for something that contributes pixels, "Fx" for an effect,
			// "Tr" for a transform, each marking what kind of modifier the row is.
			badge_label = item.ModifierNode switch {
				LayerTransformNode => EditableObjectBadge.TransformLabel,
				not null => EditableObjectBadge.EffectLabel,
				null => EditableObjectBadge.ObjectLabel,
			};
			object_badge.Visible = true;
			object_badge.QueueDraw ();
			SetTooltipText (item.ModifierNode is not null
				? (item.ModifierNode is LayerTransformNode
					? Translations.GetString ("Layer transform: applies to everything below it on this layer, and stays editable.")
					: Translations.GetString ("Layer effect: applies to everything below it on this layer, and stays editable."))
					+ "\n" + Translations.GetString ("Right-click to change its settings, blending and strength") + "\n"
					+ Translations.GetString ("Drag and drop to reorder")
				: Translations.GetString ("Re-editable object: a live shape or text you can keep editing until you rasterize it.")
					+ "\n" + Translations.GetString ("Right-click to set blend mode and opacity, or open its properties") + "\n"
					+ Translations.GetString ("Drag and drop to reorder"));
			return;
		}

		object_badge.Visible = false;

		SetTooltipText (item.TooltipText);

		thumbnail_surface = null;
		item_thumbnail.QueueDraw ();
	}

	// Styles an object sub-row as a child of its layer (tinted background, smaller label). Defined in
	// Pinta.Resources' style.css.
	private const string ObjectRowCssClass = "layer-object-row";

	/// <summary>
	/// Draws the hierarchy line for an object row: a vertical run down the left of the row, plus a
	/// horizontal turn towards the row's content. The bottom row of a layer stops the vertical at the
	/// turn, so the group visibly ends there.
	/// </summary>
	private void DrawTreeLine (Context g, int width, int height)
	{
		if (item is null || (!item.IsObjectRow && !item.IsMaskRow))
			return;

		// Half-pixel offsets keep a 1px line crisp.
		double x = Math.Floor (width / 2.0) + 0.5;
		double mid = Math.Floor (height / 2.0) + 0.5;
		bool isLast = item.IsLastObjectRow;

		g.Save ();
		g.SetSourceColor (new Color (0.5, 0.5, 0.5, 0.8));
		g.LineWidth = 1;

		g.MoveTo (x, 0);
		g.LineTo (x, isLast ? mid : height);
		g.MoveTo (x, mid);
		g.LineTo (width, mid);
		g.Stroke ();

		g.Restore ();
	}

	/// <summary>
	/// Draws the "Obj." badge at (0,0) of the object-badge drawing area, scaled to fit.
	/// </summary>
	private void DrawObjectBadge (Context g, int width, int height)
	{
		// Scale the badge down from its natural 26x14 to fit the allocated area, so it reads at the
		// same visual size as other 16px dock markers rather than dominating the row.
		double scale = Math.Min (width / EditableObjectBadge.Width, height / EditableObjectBadge.Height);
		if (scale <= 0 || double.IsNaN (scale))
			scale = 1;
		g.Save ();
		g.Scale (scale, scale);
		// Monochrome white so the badge stands out against the dock's grey row background.
		EditableObjectBadge.Draw (g, new PointD (0, 0), new Color (1.0, 1.0, 1.0, 1.0), badge_label);
		g.Restore ();
	}

	private void DrawThumbnail (
		Context g,
		int width,
		int height)
	{
		if (item is null)
			throw new InvalidOperationException ($"{nameof (item)} is null");

		thumbnail_surface ??= item.BuildThumbnail (width, height);

		double scale;
		int draw_width;
		int draw_height;

		// The image is more constrained by height than width
		if (width / (double) thumbnail_surface.Width >= height / (double) thumbnail_surface.Height) {
			scale = height / (double) (thumbnail_surface.Height);
			draw_width = thumbnail_surface.Width * height / thumbnail_surface.Height;
			draw_height = height;
		} else {
			scale = width / (double) (thumbnail_surface.Width);
			draw_width = width;
			draw_height = thumbnail_surface.Height * width / thumbnail_surface.Width;
		}

		PointI offset = new (
			X: (int) ((width - draw_width) / 2f),
			Y: (int) ((height - draw_height) / 2f)
		);

		g.Save ();

		g.Rectangle (offset.X, offset.Y, draw_width, draw_height);
		g.Clip ();

		g.SetSource (transparent_pattern);
		g.Paint ();

		g.Scale (scale, scale);
		g.SetSourceSurface (thumbnail_surface, (int) (offset.X / scale), (int) (offset.Y / scale));
		g.Paint ();

		g.Restore ();

		// TODO: scale this box correctly to match layer aspect ratio
		g.SetSourceColor (new Color (0.5, 0.5, 0.5));
		g.Rectangle (offset.X + 0.5, offset.Y + 0.5, draw_width, draw_height);
		g.LineWidth = 1;

		g.Stroke ();

		g.Dispose ();
	}
}
