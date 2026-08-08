//
// HistoryTreeView.cs
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
	public bool IsObjectRow => ShapeObject is not null || TextObject is not null;

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

	public string Label {
		get {
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

	public bool Visible => IsObjectRow ? !ObjectHidden : !UserLayer?.Hidden ?? false;

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
		=> IsObjectRow ? UserLayer?.FindObject (TextObject is not null, ObjectIndex) : null;

	/// <summary>Current opacity (0..1) of the object this row represents.</summary>
	public double ObjectOpacity
		=> LiveObject?.Opacity ?? 1.0;

	/// <summary>Whether the object this row represents is hidden from every render path.</summary>
	public bool ObjectHidden
		=> LiveObject?.Hidden ?? false;

	/// <summary>The object's user-given name, or empty when it still uses the type default.</summary>
	public string ObjectName
		=> LiveObject?.Name ?? string.Empty;

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

	public void SetObjectHidden (bool hidden)
	{
		if (LiveObject is not { } obj || obj.Hidden == hidden)
			return;

		bool before = obj.Hidden;
		obj.Hidden = hidden;
		PushObjectProperty (
			hidden ? Translations.GetString ("Hide Object") : Translations.GetString ("Show Object"),
			o => o.Hidden,
			(o, v) => o.Hidden = v,
			before);
	}

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
	/// Moves the object one step through its layer's object list — its z-order, since the list is
	/// rendered in order. <paramref name="delta"/> is +1 to raise, -1 to lower.
	/// </summary>
	// ponytail: steps one list position, which for shapes may be a transient rasterize-on-finalize
	// shape the dock doesn't show. Those fuse away the moment you move on, so it self-corrects.
	public void MoveObject (int delta)
	{
		if (UserLayer is null || document is null || !IsObjectRow)
			return;

		int target = ObjectIndex + delta;
		bool isText = TextObject is not null;

		ObjectReorderHistoryItem historyItem = new (
			PintaCore.Workspace,
			PintaCore.Chrome,
			Resources.Icons.LayerProperties,
			Translations.GetString ("Reorder Object"),
			UserLayer,
			isText,
			ObjectIndex,
			target);

		if (!UserLayer.MoveObject (isText, ObjectIndex, target))
			return;

		Pinta.Core.ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, UserLayer);
		document.History.PushNewItem (historyItem);
		LayerObjectSelection.RaiseObjectsChanged ();
	}

	/// <summary>Whether the object can still move in the given direction (+1 raise / -1 lower).</summary>
	public bool CanMoveObject (int delta)
	{
		if (UserLayer is null || !IsObjectRow)
			return false;

		int count = TextObject is not null ? UserLayer.TextObjects.Count : UserLayer.ShapeObjects.Count;
		int target = ObjectIndex + delta;
		return target >= 0 && target < count;
	}

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
				isText: TextObject is not null,
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

	public static LayersListViewItemWidget New ()
		=> NewWithProperties ([]);

	[MemberNotNull (nameof (item_thumbnail))]
	[MemberNotNull (nameof (item_label))]
	[MemberNotNull (nameof (visible_button))]
	[MemberNotNull (nameof (object_badge))]
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

		Append (visibleButton);
		Append (objectBadge);
		Append (itemLabel);
		Append (itemThumbnail);

		// --- References to keep

		item_thumbnail = itemThumbnail;
		item_label = itemLabel;
		visible_button = visibleButton;
		object_badge = objectBadge;
	}

	// A row is "bound" when it is showing a layer of an open document. Layer-only operations
	// (context menu, drag/drop reorder) additionally exclude object sub-rows.
	private static bool IsBoundRow ([NotNullWhen (true)] LayersListViewItem? row)
		=> row?.UserLayer is not null && PintaCore.Workspace.HasOpenDocuments;

	private static bool IsLayerRow ([NotNullWhen (true)] LayersListViewItem? row)
		=> IsBoundRow (row) && !row.IsObjectRow;

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

		Document doc = PintaCore.Workspace.ActiveDocument;
		// Ensure this is the current layer before opening the menu, since the menu actions
		// apply to the current layer.
		if (doc.Layers.CurrentUserLayer != item.UserLayer)
			doc.Layers.SetCurrentUserLayer (item.UserLayer);

		LayerActions actions = PintaCore.Actions.Layers;

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
		menu.AppendSection (null, operationsSection);
		menu.AppendSection (null, flipSection);

		// Only offer "Rasterize All Objects" for layers that actually hold editable objects.
		if (item.UserLayer.HasObjectSubNodes) {
			Gio.Menu objectsSection = Gio.Menu.New ();
			objectsSection.AppendItem (actions.RasterizeAllObjects.CreateMenuItem ());
			menu.AppendSection (null, objectsSection);
		}

		menu.AppendSection (null, propertiesSection);

		Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel (menu);
		popover.SetParent (this);
		popover.Popup ();
	}

	// Right-clicking an object sub-row opens its little editor: rename, z-order, and opacity. A
	// popover of plain widgets rather than a Gio.Menu, since these need entry/slider controls and
	// would otherwise each have to be registered as an application action.
	private void ShowObjectPopover (LayersListViewItem row)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.SetAllMargins (6);

		Gtk.Popover popover = Gtk.Popover.New ();

		// --- Rename. Applied on Enter or when the popover closes, so a single history step covers
		// the whole edit rather than one per keystroke.
		Gtk.Entry nameEntry = Gtk.Entry.New ();
		nameEntry.WidthRequest = 150;
		nameEntry.SetPlaceholderText (row.Label);
		nameEntry.SetText (row.ObjectName);
		nameEntry.OnActivate += (_, _) => popover.Popdown ();

		Gtk.Box nameBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		nameBox.Append (Gtk.Label.New (Translations.GetString ("Name:")));
		nameBox.Append (nameEntry);
		box.Append (nameBox);

		// --- Z-order within the layer's object list.
		Gtk.Button raise = Gtk.Button.NewWithLabel (Translations.GetString ("Raise"));
		Gtk.Button lower = Gtk.Button.NewWithLabel (Translations.GetString ("Lower"));
		raise.Sensitive = row.CanMoveObject (1);
		lower.Sensitive = row.CanMoveObject (-1);
		raise.OnClicked += (_, _) => { popover.Popdown (); row.MoveObject (1); };
		lower.OnClicked += (_, _) => { popover.Popdown (); row.MoveObject (-1); };

		Gtk.Box orderBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		orderBox.Append (raise);
		orderBox.Append (lower);
		box.Append (orderBox);

		// --- Opacity. The drag updates the canvas live; a single history item is pushed when the
		// popover closes, so one undo restores the value the drag started from.
		double beforeOpacity = row.ObjectOpacity;

		Gtk.Scale scale = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 100, 1);
		scale.WidthRequest = 150;
		scale.DrawValue = true;
		scale.SetValue (beforeOpacity * 100);
		scale.OnValueChanged += (_, _) => row.SetObjectOpacity (scale.GetValue () / 100.0);

		Gtk.Box opacityBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		opacityBox.Append (Gtk.Label.New (Translations.GetString ("Opacity:")));
		opacityBox.Append (scale);
		box.Append (opacityBox);

		popover.SetChild (box);
		popover.SetParent (this);
		popover.OnClosed += (_, _) => {
			if (row.ObjectOpacity != beforeOpacity)
				row.PushObjectOpacityHistory (beforeOpacity);
			row.RenameObject (nameEntry.GetText ());
		};
		popover.Popup ();
	}

	private Gdk.ContentProvider? DragSource_OnPrepare (
		Gtk.DragSource _,
		Gtk.DragSource.PrepareSignalArgs args)
	{
		if (!IsLayerRow (item))
			return null;

		return Gdk.ContentProvider.NewForValue (new GObject.Value ((GObject.Object) item));
	}

	private bool DropTarget_OnDrop (
		Gtk.DropTarget _,
		Gtk.DropTarget.DropSignalArgs args)
	{
		if (!IsLayerRow (item))
			return false;

		if (args.Value.GetObject () is not LayersListViewItem source || !IsLayerRow (source))
			return false;

		Document doc = PintaCore.Workspace.ActiveDocument;
		int from = doc.Layers.IndexOf (source.UserLayer);
		int target = doc.Layers.IndexOf (item.UserLayer);
		if (from < 0 || target < 0 || from == target)
			return false;

		// Rows are drawn top-first (higher doc index = higher up). Dropping on the
		// upper half of the target lands above it (higher doc index), lower half below.
		bool dropAbove = args.Y < GetHeight () / 2.0;
		int insert = dropAbove ? target + 1 : target;
		if (from < insert)
			insert--; // removing the source first shifts everything above it down.

		if (insert == from)
			return false;

		MoveLayerHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveUp,
			Translations.GetString ("Move Layer"),
			from,
			insert);
		doc.History.PushNewItem (hist);
		hist.Redo ();
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

		// Object rows get no thumbnail (the TreeExpander supplies their indentation), but they do
		// keep the visibility checkbox — it toggles the object's own Hidden flag.
		bool isObject = item.IsObjectRow;
		item_thumbnail.SetVisible (!isObject);
		visible_button.SetActive (item.Visible);

		if (isObject) {
			// Object rows are always live/editable (rasterizing drops the object entirely), so they
			// always get the "editable object" badge.
			object_badge.Visible = true;
			object_badge.QueueDraw ();
			SetTooltipText (Translations.GetString ("Re-editable object: a live shape or text you can keep editing until you rasterize it.")
				+ "\n" + Translations.GetString ("Right-click to rename, reorder, or set opacity"));
			return;
		}

		object_badge.Visible = false;

		SetTooltipText (item.TooltipText);

		thumbnail_surface = null;
		item_thumbnail.QueueDraw ();
	}

	/// <summary>
	/// Draws the "Obj." badge at (0,0) of the object-badge drawing area, scaled to fit.
	/// </summary>
	private static void DrawObjectBadge (Context g, int width, int height)
	{
		// Scale the badge down from its natural 26x14 to fit the allocated area, so it reads at the
		// same visual size as other 16px dock markers rather than dominating the row.
		double scale = Math.Min (width / EditableObjectBadge.Width, height / EditableObjectBadge.Height);
		if (scale <= 0 || double.IsNaN (scale))
			scale = 1;
		g.Save ();
		g.Scale (scale, scale);
		// Monochrome white so the badge stands out against the dock's grey row background.
		EditableObjectBadge.Draw (g, new PointD (0, 0), new Color (1.0, 1.0, 1.0, 1.0));
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
