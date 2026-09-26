//
// DocumentWorkspace.cs
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

namespace Pinta.Core;

public sealed class DocumentWorkspace
{
	private readonly Document document;

	private readonly ActionManager actions;

	private enum ZoomType
	{
		ZoomIn,
		ZoomOut,
		ZoomManually,
	}

	internal DocumentWorkspace (
		ActionManager actions,
		Document document)
	{
		this.actions = actions;

		this.document = document;

		History = new DocumentHistory (document);
	}

	#region Public Events
	public event EventHandler<CanvasInvalidatedEventArgs>? CanvasInvalidated;
	public event EventHandler? ViewSizeChanged;
	#endregion

	#region Public Properties
	public Gtk.Widget Canvas { get; set; } = null!; // NRT - This is set soon after creation
	public Gtk.Widget CanvasWindow { get; set; } = null!; // NRT - This is set soon after creation

	/// <summary>
	/// Returns whether the zoomed image fits in the window without requiring scrolling.
	/// </summary>
	public bool ImageViewFitsInWindow {
		get {
			Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
			int window_x = view.GetAllocatedWidth ();
			int window_y = view.GetAllocatedHeight ();
			return ViewSize.Width <= window_x && ViewSize.Height <= window_y;
		}
	}

	/// <summary>
	/// Size of the zoomed image.
	/// </summary>
	public Size ViewSize {
		get => view_size;
		set {
			if (view_size == value) return;
			view_size = value;
			OnViewSizeChanged ();
		}
	}
	private Size view_size;

	public DocumentHistory History { get; }

	/// <summary>
	/// Returns whether the image (at 100% zoom) would fit in the window without requiring scrolling.
	/// </summary>
	public bool ImageFitsInWindow {
		get {
			Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
			int window_x = view.GetAllocatedWidth ();
			int window_y = view.GetAllocatedHeight ();
			return document.ImageSize.Width <= window_x && document.ImageSize.Height <= window_y;
		}
	}

	/// <summary>
	/// Scale factor for the zoomed image.
	/// </summary>
	public double Scale {
		get => ViewSize.Width / (double) document.ImageSize.Width;
		set {
			if (value == ViewSize.Width / (double) document.ImageSize.Width && value == ViewSize.Height / (double) document.ImageSize.Height)
				return;

			document.ImageSize = CoercedToPositive (document.ImageSize);
			ViewSize = GetNewViewSize (document.ImageSize, value);

			Invalidate ();
		}
	}

	/// <summary>
	/// Ensures that the size has a width and a height of at least 1
	/// </summary>
	private static Size CoercedToPositive (Size baseSize)
		=> new (
			Width: Math.Max (baseSize.Width, 1),
			Height: Math.Max (baseSize.Height, 1));

	private static Size GetNewViewSize (Size imageSize, double scale)
	{
		int new_x = Math.Max ((int) (imageSize.Width * scale), 1);
		int new_y = Math.Max ((int) ((long) new_x * imageSize.Height / imageSize.Width), 1);
		return new (new_x, new_y);
	}

	#endregion

	#region Public Methods
	public void Invalidate ()
	{
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs ());
	}

	/// <summary>
	/// Repaints a rectangle region on the canvas.
	/// </summary>
	/// <param name='canvasRect'>
	/// The rectangle region of the canvas requiring repainting
	/// </param>
	public void Invalidate (RectangleI canvasRect)
	{
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs (canvasRect));
	}

	/// <summary>
	/// Repaints a rectangle region in the window.
	/// Note that this overload uses window coordinates, whereas Invalidate() uses canvas coordinates.
	/// </summary>
	public void InvalidateWindowRect (RectangleI windowRect)
	{
		PointD windowTopLeft = new (windowRect.Left, windowRect.Top);
		PointD windowBtmRight = new (windowRect.Right + 1, windowRect.Bottom + 1);

		PointD canvasTopLeft = ViewPointToCanvas (windowTopLeft);
		PointD canvasBtmRight = ViewPointToCanvas (windowBtmRight);

		RectangleI canvasRect = RectangleD.FromPoints (canvasTopLeft, canvasBtmRight).ToInt ();
		OnCanvasInvalidated (new CanvasInvalidatedEventArgs (canvasRect));
	}

	/// <summary>
	/// Grabs focus to the canvas widget. This can be used to avoid leaving focus in
	/// toolbar widgets, for example.
	/// </summary>
	public void GrabFocusToCanvas ()
	{
		bool gained_focus = CanvasWindow.GrabFocus ();
		// Log a warning if something went wrong, e.g. there is a non-focusable widget
		// in the hierarchy.
		if (!gained_focus)
			Console.Error.WriteLine ("Failed to gain focus on the canvas widget!");
	}

	/// <summary>
	/// Determines whether the rectangle lies (at least partially) outside the canvas area.
	/// </summary>
	public bool IsPartiallyOffscreen (RectangleI rect)
		=> rect.IsEmpty || rect.Left < 0 || rect.Top < 0;

	public bool PointInCanvas (PointD point)
	{
		if (point.X < 0 || point.Y < 0)
			return false;

		if (point.X >= document.ImageSize.Width || point.Y >= document.ImageSize.Height)
			return false;

		return true;
	}

	/// <summary>
	/// Whether any part of the rectangle lies on the canvas. What a stroke painted, rather than
	/// where the pointer was, is what tells a paint tool that it changed pixels: a wide brush
	/// dragged along an edge paints while the pointer stays outside the canvas entirely.
	/// </summary>
	public bool RectangleIntersectsCanvas (RectangleI rect)
		=> !rect.Intersect (new RectangleI (PointI.Zero, document.ImageSize)).IsEmpty;

	public void RecenterView (PointD point)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;

		var h_adjust = view.GetHadjustment ()!;
		h_adjust.Value = Math.Clamp (point.X * Scale - h_adjust.PageSize / 2, h_adjust.Lower, h_adjust.Upper);

		var v_adjust = view.GetVadjustment ()!;
		v_adjust.Value = Math.Clamp (point.Y * Scale - v_adjust.PageSize / 2, v_adjust.Lower, v_adjust.Upper);
	}

	public void ScrollCanvas (PointI delta)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;

		var h_adjust = view.GetHadjustment ()!;
		h_adjust.Value = Math.Clamp (delta.X + h_adjust.Value, h_adjust.Lower, h_adjust.Upper - h_adjust.PageSize);

		var v_adjust = view.GetVadjustment ()!;
		v_adjust.Value = Math.Clamp (delta.Y + v_adjust.Value, v_adjust.Lower, v_adjust.Upper - v_adjust.PageSize);
	}

	/// <summary>
	/// Converts a point from image view coordinates to canvas coordinates
	/// </summary>
	/// <param name='x'>
	/// The X coordinate of the view point
	/// </param>
	/// <param name='y'>
	/// The Y coordinate of the view point
	/// </param>
	public PointD ViewPointToCanvas (PointD viewPoint)
	{
		Fraction<int> sf = ScaleFactor.CreateClamped (document.ImageSize.Width, ViewSize.Width);
		PointD pt = sf.ScalePoint (viewPoint);
		return new (pt.X, pt.Y);
	}

	/// <summary>
	/// Converts a point from canvas coordinates to view coordinates
	/// </summary>
	public PointD CanvasPointToView (PointD canvasPoint)
	{
		Fraction<int> sf = ScaleFactor.CreateClamped (document.ImageSize.Width, ViewSize.Width);
		return sf.UnscalePoint (canvasPoint);
	}

	public void ZoomIn ()
	{
		ZoomAroundCenter (ZoomType.ZoomIn);
	}

	public void ZoomOut ()
	{
		ZoomAroundCenter (ZoomType.ZoomOut);
	}

	public void ZoomInAroundCanvasPoint (in PointD canvas_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomIn, CanvasPointToViewport (canvas_point));
	}

	public void ZoomOutAroundCanvasPoint (in PointD canvas_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomOut, CanvasPointToViewport (canvas_point));
	}

	/// <summary>
	/// Zoom in around a pointer position given relative to the viewport's top-left corner.
	/// </summary>
	public void ZoomInAroundViewportPoint (in PointD viewport_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomIn, viewport_point);
	}

	/// <summary>
	/// Zoom out around a pointer position given relative to the viewport's top-left corner.
	/// </summary>
	public void ZoomOutAroundViewportPoint (in PointD viewport_point)
	{
		ZoomAndRecenterView (ZoomType.ZoomOut, viewport_point);
	}

	public void ZoomManually ()
	{
		ZoomAroundCenter (ZoomType.ZoomManually);
	}

	public void ZoomToCanvasRectangle (RectangleD rect)
	{
		double ratio =
			(document.ImageSize.Width / rect.Width <= document.ImageSize.Height / rect.Height)
			? document.ImageSize.Width / rect.Width
			: document.ImageSize.Height / rect.Height;

		actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (ratio));
		// Force update of scrollbar upper before recenter. An unrealized combo box (headless test
		// harness) has no display to pump, and on some platforms pumping the loop off the display's
		// own thread crashes outright.
		if (actions.View.ZoomComboBox.ComboBox.GetRealized ())
			GLib.MainContext.Default ().Iteration (false);

		PointD newPoint = new (
			X: rect.X + rect.Width / 2,
			Y: rect.Y + rect.Height / 2);

		RecenterView (newPoint);
	}
	#endregion

	#region Private Methods
	private void OnCanvasInvalidated (CanvasInvalidatedEventArgs e)
	{
		CanvasInvalidated?.Invoke (this, e);
	}

	public void OnViewSizeChanged ()
	{
		ViewSizeChanged?.Invoke (this, EventArgs.Empty);
	}

	/// <summary>
	/// Zoom in/out around the center of the screen.
	/// </summary>
	private void ZoomAroundCenter (ZoomType zoomType)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		PointD center = new (view.Hadjustment!.PageSize / 2.0, view.Vadjustment!.PageSize / 2.0);
		ZoomAndRecenterView (zoomType, center);
	}

	/// <summary>
	/// Where a canvas point currently sits on screen, relative to the viewport's top-left corner.
	/// </summary>
	private PointD CanvasPointToViewport (PointD canvasPoint)
	{
		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;
		PointD viewPoint = CanvasPointToView (canvasPoint);
		return new (
			CanvasOrigin (view.Hadjustment!.PageSize, ViewSize.Width) + viewPoint.X - view.Hadjustment.Value,
			CanvasOrigin (view.Vadjustment!.PageSize, ViewSize.Height) + viewPoint.Y - view.Vadjustment.Value);
	}

	/// <summary>
	/// Offset of the canvas widget inside the viewport's scrollable area on one axis: the widget
	/// is centered while it is smaller than the viewport, and fills it from the start otherwise.
	/// </summary>
	private static double CanvasOrigin (double page, double extent)
		=> Math.Max (0, (page - extent) / 2);

	// Outer share of the visible image, on each side, where a zoom pins that side of the view.
	private const double ZOOM_EDGE_SNAP = 0.1;
	// Up to this share from a side the anchor ramps from the pinned side back to the pointer,
	// so the anchor never jumps; the middle of the view zooms exactly around the pointer.
	private const double ZOOM_EDGE_BLEND = 0.25;

	/// <summary>
	/// Remaps a pointer position across the visible image (0 = one side, 1 = the other) to the
	/// position the zoom holds steady. Near a side the anchor moves onto that side, so zooming
	/// with the pointer toward an edge of the image keeps that edge in view instead of pushing it
	/// off screen.
	/// </summary>
	private static double FavorEdges (double t)
	{
		if (t > 0.5)
			return 1 - FavorEdges (1 - t);
		if (t <= ZOOM_EDGE_SNAP)
			return 0;
		if (t < ZOOM_EDGE_BLEND)
			return ZOOM_EDGE_BLEND * (t - ZOOM_EDGE_SNAP) / (ZOOM_EDGE_BLEND - ZOOM_EDGE_SNAP);
		return t;
	}

	/// <summary>
	/// Scroll value on one axis after the zoomed image changes from <paramref name="oldExtent"/>
	/// to <paramref name="newExtent"/> pixels, holding the anchor chosen by <see cref="FavorEdges"/>
	/// at the same place on screen.
	/// </summary>
	/// <param name="pointer">Pointer position relative to the viewport's start.</param>
	/// <param name="page">Visible size of the viewport.</param>
	/// <param name="scroll">Scroll value before the zoom.</param>
	internal static double ScrollAfterZoom (double pointer, double page, double scroll, double oldExtent, double newExtent)
	{
		double maxScroll = Math.Max (0, newExtent - page);
		double imageStart = CanvasOrigin (page, oldExtent) - scroll;
		double visibleStart = Math.Max (0, imageStart);
		double visibleEnd = Math.Min (page, imageStart + oldExtent);
		if (visibleEnd <= visibleStart || oldExtent <= 0)
			return Math.Clamp (scroll, 0, maxScroll);

		double t = Math.Clamp ((pointer - visibleStart) / (visibleEnd - visibleStart), 0, 1);
		double anchor = visibleStart + FavorEdges (t) * (visibleEnd - visibleStart);
		double imageFraction = (anchor - imageStart) / oldExtent;

		double newScroll = CanvasOrigin (page, newExtent) + imageFraction * newExtent - anchor;
		return Math.Clamp (newScroll, 0, maxScroll);
	}

	/// <summary>
	/// Zoom in/out around a specific point.
	/// </summary>
	/// <param name="pointer">Point to zoom around, relative to the viewport's top-left corner</param>
	private void ZoomAndRecenterView (ZoomType zoomType, PointD pointer)
	{
		if (zoomType == ZoomType.ZoomOut && (ViewSize.Width == 1 || ViewSize.Height == 1))
			return; //Can't zoom in past a 1x1 px canvas

		if (!ViewActions.TryParsePercent (actions.View.ZoomComboBox.ComboBox.GetActiveText ()!, out var zoom))
			zoom = Scale * 100;

		zoom = Math.Min (zoom, 3600);

		actions.View.SuspendZoomUpdate ();

		Gtk.Viewport view = (Gtk.Viewport) Canvas.Parent!;

		Size oldViewSize = ViewSize;
		double old_scroll_x = view.Hadjustment!.Value;
		double old_scroll_y = view.Vadjustment!.Value;

		if (zoomType == ZoomType.ZoomIn || zoomType == ZoomType.ZoomOut) {

			int i = 0;

			bool UpdateZoomLevel (string zoomInList)
			{
				if (!ViewActions.TryParsePercent (zoomInList, out var zoom_level))
					return false;

				switch (zoomType) {
					case ZoomType.ZoomIn:

						if (zoomInList == Translations.GetString ("Window") || zoom_level <= zoom) {
							actions.View.ZoomComboBox.ComboBox.Active = i - 1;
							return true;
						}

						break;

					case ZoomType.ZoomOut:

						if (zoomInList == Translations.GetString ("Window"))
							return true;

						if (zoom_level < zoom) {
							actions.View.ZoomComboBox.ComboBox.Active = i;
							return true;
						}

						break;
				}
				return false;
			}

			foreach (string item in actions.View.ZoomCollection) {

				if (UpdateZoomLevel (item))
					break;

				i++;
			}
		}

		actions.View.UpdateCanvasScale ();

		// Quick fix : need to manually update Upper limit because the value is not changing after updating the canvas scale.
		// TODO : I think there is an event need to be fired so that those values updated automatically.
		view.Hadjustment!.Upper = ViewSize.Width < view.Hadjustment.PageSize ? view.Hadjustment.PageSize : ViewSize.Width;
		view.Vadjustment!.Upper = ViewSize.Height < view.Vadjustment.PageSize ? view.Vadjustment.PageSize : ViewSize.Height;

		// The canvas widget might not have resized yet, so place the view from the new ViewSize
		// rather than from the widget's allocation.
		view.Hadjustment.Value = ScrollAfterZoom (pointer.X, view.Hadjustment.PageSize, old_scroll_x, oldViewSize.Width, ViewSize.Width);
		view.Vadjustment.Value = ScrollAfterZoom (pointer.Y, view.Vadjustment.PageSize, old_scroll_y, oldViewSize.Height, ViewSize.Height);

		actions.View.ResumeZoomUpdate ();
	}

	#endregion
}
