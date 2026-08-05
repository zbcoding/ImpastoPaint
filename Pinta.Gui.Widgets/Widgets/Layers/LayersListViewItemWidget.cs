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
	// rather than the layer itself. Object rows are read-only in this first pass (select only).
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

	public static LayersListViewItem NewTextObject (Document doc, UserLayer userLayer, TextObject text)
	{
		LayersListViewItem item = NewWithProperties ([]);
		item.document = doc;
		item.UserLayer = userLayer;
		item.TextObject = text;
		return item;
	}

	public string Label {
		get {
			if (ShapeObject is not null)
				return string.IsNullOrEmpty (ShapeObject.Name) ? ShapeTypeName (ShapeObject.ShapeType) : ShapeObject.Name;
			if (TextObject is not null)
				return Translations.GetString ("Text");
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

	public bool Visible => !UserLayer?.Hidden ?? false;

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
	private Gtk.Image rasterized_icon;

	public static LayersListViewItemWidget New ()
		=> NewWithProperties ([]);

	[MemberNotNull (nameof (item_thumbnail))]
	[MemberNotNull (nameof (item_label))]
	[MemberNotNull (nameof (visible_button))]
	[MemberNotNull (nameof (rasterized_icon))]
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

		// Monochrome checkmark shown on rasterized (finalized) object rows. Hidden otherwise.
		Gtk.Image rasterizedIcon = Gtk.Image.NewFromIconName (Resources.StandardIcons.ObjectSelect);
		rasterizedIcon.Halign = Gtk.Align.End;
		rasterizedIcon.Visible = false;

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
		Append (itemLabel);
		Append (rasterizedIcon);
		Append (itemThumbnail);

		// --- References to keep

		item_thumbnail = itemThumbnail;
		item_label = itemLabel;
		visible_button = visibleButton;
		rasterized_icon = rasterizedIcon;
	}

	private void MenuGesture_OnPressed (
		Gtk.GestureClick _,
		Gtk.GestureClick.PressedSignalArgs args)
	{
		if (item is null || item.UserLayer is null || item.IsObjectRow || !PintaCore.Workspace.HasOpenDocuments)
			return;

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
		menu.AppendSection (null, propertiesSection);

		Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel (menu);
		popover.SetParent (this);
		popover.Popup ();
	}

	private Gdk.ContentProvider? DragSource_OnPrepare (
		Gtk.DragSource _,
		Gtk.DragSource.PrepareSignalArgs args)
	{
		if (item is null || item.UserLayer is null || item.IsObjectRow)
			return null;

		return Gdk.ContentProvider.NewForValue (new GObject.Value ((GObject.Object) item));
	}

	private bool DropTarget_OnDrop (
		Gtk.DropTarget _,
		Gtk.DropTarget.DropSignalArgs args)
	{
		if (item is null || item.UserLayer is null || item.IsObjectRow || !PintaCore.Workspace.HasOpenDocuments)
			return false;

		if (args.Value.GetObject () is not LayersListViewItem source || source.UserLayer is null || source.IsObjectRow)
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

		// Object rows (nested shapes/text) are read-only in this pass: no thumbnail, no visibility
		// checkbox, no tooltip. The TreeExpander supplies their indentation.
		bool isObject = item.IsObjectRow;
		visible_button.SetVisible (!isObject);
		item_thumbnail.SetVisible (!isObject);

		if (isObject) {
			// Rasterized shapes are baked into the layer's pixels and no longer editable; mark them
			// with a monochrome checkmark and explain in the tooltip. Editable object rows have no tooltip.
			bool rasterized = item.ShapeObject?.Rasterized == true;
			rasterized_icon.Visible = rasterized;
			SetTooltipText (rasterized
				? Translations.GetString ("Rasterized: baked into the layer's pixels. No longer editable as an object.")
				: null);
			return;
		}

		rasterized_icon.Visible = false;

		visible_button.SetActive (item.Visible);
		SetTooltipText (item.TooltipText);

		thumbnail_surface = null;
		item_thumbnail.QueueDraw ();
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
