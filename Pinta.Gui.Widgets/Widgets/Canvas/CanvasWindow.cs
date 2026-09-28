//
// CanvasWindow.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2015 Jonathan Pobst
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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta;

[GObject.Subclass<Gtk.Grid>]
public sealed partial class CanvasWindow
{
	private Document document = null!; // NRT - set by factory method.
	private ChromeManager chrome = null!;
	private ToolManager tools = null!;

	private PintaCanvas canvas;
	private Ruler horizontal_ruler;
	private Ruler vertical_ruler;
	private Gtk.ScrolledWindow scrolled_window;
	private Gtk.CssProvider viewport_css_provider;
	private Gtk.CssProvider canvas_css_provider;
	private Gtk.StyleContext viewport_style_context;
	private Cairo.Color default_canvas_surround_color;
	private Gtk.Widget? horizontal_scrollbar;
	private Gtk.Widget? vertical_scrollbar;
	private Gtk.EventControllerMotion motion_controller;
	private Gtk.GestureDrag drag_controller;
	private Gtk.GestureZoom gesture_zoom;

	private PointD current_canvas_pos = PointD.Zero;
	private double cumulative_zoom_amount;
	private double cumulative_alt_scroll_amount;
	private double last_scale_delta;

	// Last pointer position relative to this widget. Zoom anchors on it rather than on the canvas
	// point last seen under it, since zooming or autoscrolling moves the image under a still pointer.
	private PointD pointer_root_point;

	// Drag autoscroll: while a tool drag holds the pointer at or past the viewport edge, the view
	// scrolls every frame and the tool gets a move event for the pointer's new canvas position.
	private uint autoscroll_tick_id;
	private Gdk.ModifierType drag_state;
	private MouseButton drag_button;

	private const double ZOOM_THRESHOLD_SCROLL = 1.25;
	private const double ZOOM_THRESHOLD_PINCH = 0.15;
	// Band inside the viewport edge that already counts as "past" it, so a maximized or
	// fullscreen canvas whose edge touches the screen edge can still scroll.
	private const double AUTOSCROLL_EDGE_BAND = 8;
	// Pixels scrolled per frame for each pixel the pointer is past the band start, and the cap.
	private const double AUTOSCROLL_SPEED = 0.3;
	private const int AUTOSCROLL_MAX_STEP = 60;

	public Gtk.Widget Canvas { get { return canvas; } }

	public Cairo.Color? CanvasSurroundColor {
		set {
			ApplyCanvasEdgeShadow (value ?? default_canvas_surround_color);

			if (value is null) {
				viewport_style_context.RemoveProvider (viewport_css_provider);
				return;
			}

			viewport_css_provider.LoadFromString ($".canvas-surround {{ background-color: #{value.Value.ToHex (addAlpha: false)}; }}");
			viewport_style_context.RemoveProvider (viewport_css_provider);
			viewport_style_context.AddProvider (viewport_css_provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		}
	}

	// The canvas edge glow is a shade of the surround color, darker on a light surround and
	// lighter on a dark one, blurred so it fades from near-opaque at the image edge to fully
	// transparent into the surround. It still outlines an image that matches the surround.
	private void ApplyCanvasEdgeShadow (Cairo.Color surround)
	{
		double luminance = 0.2126 * surround.R + 0.7152 * surround.G + 0.0722 * surround.B;
		(double target, double amount) = luminance > 0.5 ? (0.0, 0.45) : (1.0, 0.35);

		static int Channel (double c, double target, double amount)
			=> (int) Math.Round (255 * (c + (target - c) * amount));

		string rgba = string.Create (CultureInfo.InvariantCulture,
			$"rgba({Channel (surround.R, target, amount)},{Channel (surround.G, target, amount)},{Channel (surround.B, target, amount)},0.85)");
		canvas_css_provider.LoadFromString ($"#canvas {{ box-shadow: 0 0 10px 1px {rgba}; }}");
	}

	public Cairo.Color DefaultCanvasSurroundColor => default_canvas_surround_color;

	[MemberNotNull (nameof (canvas))]
	[MemberNotNull (nameof (viewport_css_provider), nameof (canvas_css_provider))]
	[MemberNotNull (nameof (viewport_style_context))]
	[MemberNotNull (nameof (horizontal_ruler), nameof (vertical_ruler))]
	[MemberNotNull (nameof (scrolled_window), nameof (horizontal_scrollbar), nameof (vertical_scrollbar))]
	[MemberNotNull (nameof (motion_controller), nameof (drag_controller), nameof (gesture_zoom))]
	partial void Initialize ()
	{
		Gtk.GestureZoom gestureZoom = Gtk.GestureZoom.New ();
		gestureZoom.SetPropagationPhase (Gtk.PropagationPhase.Bubble);
		gestureZoom.OnScaleChanged += HandleGestureZoomScaleChanged;
		gestureZoom.OnEnd += (_, _) => cumulative_zoom_amount = last_scale_delta = 0;
		gestureZoom.OnCancel += (_, _) => cumulative_zoom_amount = last_scale_delta = 0;

		Gtk.EventControllerScroll scrollController = Gtk.EventControllerScroll.New (Gtk.EventControllerScrollFlags.BothAxes); // Both axes must be captured so the zoom gesture can cancel them
		scrollController.OnScroll += HandleScrollEvent;
		scrollController.OnDecelerate += (_, _) => gestureZoom.IsActive (); // Cancel scroll deceleration when zooming

		PintaCanvas canvas = PintaCanvas.New ();
		// For CSS: add a drop shadow outline to the canvas to give it a clear border
		// when the image is close to the background color. The margin leaves a strip of surround
		// around it inside the scrolled area, so an edge the view is scrolled to stays visible.
		canvas.Name = "canvas";
		canvas.MarginStart = canvas.MarginEnd = canvas.MarginTop = canvas.MarginBottom = DocumentWorkspace.CanvasMargin;

		Gtk.Viewport viewPort = Gtk.Viewport.New (null, null);
		viewPort.AddCssClass ("canvas-surround");
		viewPort.AddController (scrollController);
		viewPort.Child = canvas;

		// Use the drag gesture to forward a sequence of mouse press -> move -> release events to the current tool.
		// This is more reliable than using just a click gesture in combination with the move controller (see bug #1456)
		// Note that we attach this to the root canvas widget, not the canvas, so that it can receive drags that start outside the canvas.
		Gtk.GestureDrag dragController = Gtk.GestureDrag.New ();
		dragController.SetButton (0); // Listen for all mouse buttons.
		dragController.OnDragBegin += OnDragBegin;
		dragController.OnDragUpdate += OnDragUpdate;
		dragController.OnDragEnd += OnDragEnd;

		Gtk.ScrolledWindow scrolledWindow = Gtk.ScrolledWindow.New ();
		scrolledWindow.Hexpand = true;
		scrolledWindow.Vexpand = true;
		scrolledWindow.AddCssClass ("canvas-scroller");
		scrolledWindow.Child = viewPort;

		Ruler horizontalRuler = Ruler.New (Gtk.Orientation.Horizontal);
		horizontalRuler.Metric = MetricType.Pixels;
		horizontalRuler.Visible = false;

		Ruler verticalRuler = Ruler.New (Gtk.Orientation.Vertical);
		verticalRuler.Metric = MetricType.Pixels;
		verticalRuler.Visible = false;

		Gtk.EventControllerMotion motionController = Gtk.EventControllerMotion.New ();
		motionController.OnMotion += HandleMotion;

		// --- Initialization (Gtk.Widget)

		// The mouse handler in PintaCanvas grabs focus away from toolbar widgets, along
		// with DocumentWorkpace.GrabFocusToCanvas()
		Focusable = true;

		AddController (gestureZoom);
		AddController (dragController);
		AddController (motionController);

		// --- Initialization (Gtk.Grid)

		ColumnHomogeneous = false;
		RowHomogeneous = false;

		Attach (horizontalRuler, 1, 0, 1, 1);
		Attach (verticalRuler, 0, 1, 1, 1);
		Attach (scrolledWindow, 1, 1, 1, 1);

		// --- References to keep

		this.canvas = canvas;

		viewport_css_provider = Gtk.CssProvider.New ();
		viewport_style_context = viewPort.GetStyleContext ();
		if (viewport_style_context.LookupColor ("view_bg_color", out Gdk.RGBA defaultColor))
			default_canvas_surround_color = defaultColor.ToCairoColor ();
		else
			default_canvas_surround_color = new Cairo.Color (0.2, 0.2, 0.2);

		canvas_css_provider = Gtk.CssProvider.New ();
		canvas.GetStyleContext ().AddProvider (canvas_css_provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		ApplyCanvasEdgeShadow (default_canvas_surround_color);

		scrolled_window = scrolledWindow;
		gesture_zoom = gestureZoom;
		horizontal_ruler = horizontalRuler;
		vertical_ruler = verticalRuler;
		motion_controller = motionController;
		drag_controller = dragController;
		horizontal_scrollbar = scrolledWindow.GetHscrollbar ();
		vertical_scrollbar = scrolledWindow.GetVscrollbar ();

		// --- Further initialization

		// Update the ruler when the horizontal or vertical size has changed.
		// This can happen either from the canvas size changing (e.g. zooming),
		// or when the window is resized and the scroll area's size changes.
		scrolledWindow.Hadjustment!.OnChanged += UpdateRulerRange;
		scrolledWindow.Vadjustment!.OnChanged += UpdateRulerRange;

		// Update the ruler when scrolling around.
		scrolledWindow.Hadjustment!.OnValueChanged += UpdateRulerRange;
		scrolledWindow.Vadjustment!.OnValueChanged += UpdateRulerRange;
	}

	private void Configure (
		ChromeManager chrome,
		ToolManager tools,
		Document document,
		ICanvasGridService canvasGrid,
		Cairo.Color? canvasSurroundColor)
	{
		canvas.Configure (tools, document, canvasGrid);
		CanvasSurroundColor = canvasSurroundColor;

		// Also update if the view size changed without affecting the size of
		// the canvas widget (e.g. when zoomed out and no scrollbars are required)
		document.Workspace.ViewSizeChanged += UpdateRulerRange;
		document.SelectionChanged += UpdateRulerSelection;

		this.chrome = chrome;
		this.tools = tools;
		this.document = document;
	}

	public static CanvasWindow New (
		ChromeManager chrome,
		ToolManager tools,
		Document document,
		ICanvasGridService canvasGrid,
		Cairo.Color? canvasSurroundColor)
	{
		CanvasWindow window = NewWithProperties ([]);
		window.Configure (chrome, tools, document, canvasGrid, canvasSurroundColor);
		return window;
	}

	private void UpdateRulerSelection (object? sender, EventArgs e)
	{
		if (document.Selection.Visible) {
			RectangleD bounds = document.Selection.GetBounds ();
			var horizontalBounds = NumberRange.Create (bounds.Left, bounds.Left + bounds.Width);
			var verticalBounds = NumberRange.Create (bounds.Top, bounds.Top + bounds.Height);
			horizontal_ruler.SelectionBounds = horizontalBounds;
			vertical_ruler.SelectionBounds = verticalBounds;
		} else {
			// If there's no selection, clear the highlight
			horizontal_ruler.SelectionBounds = null;
			vertical_ruler.SelectionBounds = null;
		}
	}

	private void HandleMotion (
		Gtk.EventControllerMotion controller,
		Gtk.EventControllerMotion.MotionSignalArgs args)
	{
		PointD rootPoint = new (args.X, args.Y);
		pointer_root_point = rootPoint;

		// These coordinates are relative to our grid widget, so transform into the child image
		// view's coordinates, and then to the canvas coordinates.
		this.TranslateCoordinates (Canvas, rootPoint, out PointD viewPos);

		current_canvas_pos = document.Workspace.ViewPointToCanvas (viewPos);
		horizontal_ruler.Position = current_canvas_pos.X;
		vertical_ruler.Position = current_canvas_pos.Y;
		UpdateScrollbarTargeting (viewPos);

		// Forward mouse move events to the current tool when not dragging.
		if (drag_controller.GetStartPoint (out _, out _))
			return;

		if (document.Workspace.PointInCanvas (current_canvas_pos))
			chrome.LastCanvasCursorPoint = current_canvas_pos.ToInt ();

		ToolMouseEventArgs tool_args = new () {
			State = controller.GetCurrentEventState (),
			MouseButton = MouseButton.None,
			PointDouble = current_canvas_pos,
			WindowPoint = viewPos,
			RootPoint = rootPoint,
		};

		tools.DoMouseMove (document, tool_args);
	}

	private PointD PointerInViewport ()
	{
		this.TranslateCoordinates (scrolled_window.Child!, pointer_root_point, out PointD point);
		return point;
	}

	private void HandleGestureZoomScaleChanged (object? sender, EventArgs e)
	{
		// Allow the user to zoom in/out by pinching the trackpad
		double pinchDelta = gesture_zoom.GetScaleDelta () - 1 - last_scale_delta;
		if (pinchDelta < 0) {
			if (cumulative_zoom_amount > 0)
				cumulative_zoom_amount = 0; // Reset the counter if the user changes direction so that changing direction doesn't take extra movement

			cumulative_zoom_amount += pinchDelta;
			if (cumulative_zoom_amount <= -ZOOM_THRESHOLD_PINCH) {
				document.Workspace.ZoomOutAroundViewportPoint (PointerInViewport ());
				cumulative_zoom_amount = 0;
			}
		} else {
			if (cumulative_zoom_amount < 0)
				cumulative_zoom_amount = 0;

			cumulative_zoom_amount += pinchDelta;
			if (cumulative_zoom_amount >= ZOOM_THRESHOLD_PINCH) {
				document.Workspace.ZoomInAroundViewportPoint (PointerInViewport ());
				cumulative_zoom_amount = 0;
			}
		}
		last_scale_delta = gesture_zoom.GetScaleDelta () - 1;
	}

	public bool IsMouseOnCanvas
		=> motion_controller.ContainsPointer;

	public bool RulersVisible {
		get => horizontal_ruler.Visible;
		set {
			if (horizontal_ruler.Visible == value) return;
			horizontal_ruler.Visible = value;
			vertical_ruler.Visible = value;
		}
	}

	public MetricType RulerMetric {
		get => horizontal_ruler.Metric;
		set {
			if (horizontal_ruler.Metric == value) return;
			horizontal_ruler.Metric = value;
			vertical_ruler.Metric = value;
		}
	}

	public void UpdateRulerRange (object? sender, EventArgs e)
	{
		if (scrolled_window.Hadjustment == null || scrolled_window.Vadjustment == null)
			return;

		DocumentWorkspace workspace = document.Workspace;

		// The image's top-left corner on screen; the rulers span the visible page from there.
		PointD imageOrigin = workspace.CanvasPointToViewport (PointD.Zero);
		PointD lower = new (-imageOrigin.X / workspace.Scale, -imageOrigin.Y / workspace.Scale);
		PointD upper = new (
			lower.X + scrolled_window.Hadjustment.PageSize / workspace.Scale,
			lower.Y + scrolled_window.Vadjustment.PageSize / workspace.Scale);

		horizontal_ruler.RulerRange = new (lower.X, upper.X);
		vertical_ruler.RulerRange = new (lower.Y, upper.Y);
	}

	private bool HandleScrollEvent (
		Gtk.EventControllerScroll controller,
		Gtk.EventControllerScroll.ScrollSignalArgs args)
	{
		if (gesture_zoom.IsActive ())
			return true;

		// Allow the current tool (e.g. brush size) to handle Alt-Mousewheel or Alt-two-finger-scroll
		if (controller.GetCurrentEventState ().IsAltPressed ())
			return HandleAltScroll (args.Dy);

		// Allow the user to zoom in/out with Ctrl-Mousewheel or Ctrl-two-finger-scroll
		if (!controller.GetCurrentEventState ().IsControlPressed ())
			return false;

		// "clicky" scroll wheels generate 1 or -1

		if (args.Dy == -1) {
			document.Workspace.ZoomInAroundViewportPoint (PointerInViewport ());
			return true;
		}

		if (args.Dy == 1) {
			document.Workspace.ZoomOutAroundViewportPoint (PointerInViewport ());
			return true;
		}

		// analog scroll wheels and scrolling on a touchpad generates a range of values constantly as the user scrolls
		// this might feel "backwards" on a touchpad to some people
		if (args.Dy < 0) {
			if (cumulative_zoom_amount > 0)
				cumulative_zoom_amount = 0;

			cumulative_zoom_amount += args.Dy;
			if (cumulative_zoom_amount <= -ZOOM_THRESHOLD_SCROLL) {
				document.Workspace.ZoomInAroundViewportPoint (PointerInViewport ());
				cumulative_zoom_amount = 0;
			}

		} else {
			if (cumulative_zoom_amount < 0)
				cumulative_zoom_amount = 0;

			cumulative_zoom_amount += args.Dy;
			if (cumulative_zoom_amount >= ZOOM_THRESHOLD_SCROLL) {
				document.Workspace.ZoomOutAroundViewportPoint (PointerInViewport ());
				cumulative_zoom_amount = 0;
			}

		}

		return true;
	}

	// Alt+scroll adjusts the current tool's brush size. Tools that don't support it (anything
	// other than brush-family tools) leave the event unhandled so it falls through as normal
	// window scrolling. "Clicky" scroll wheels generate 1 or -1 per tick; analog/touchpad
	// scrolling generates a continuous range of small values, accumulated the same way as
	// Ctrl+scroll zoom.
	private bool HandleAltScroll (double dy)
	{
		if (!tools.CurrentToolSupportsMouseScroll)
			return false;

		if (dy == -1)
			return tools.DoMouseScroll (document, increase: true);

		if (dy == 1)
			return tools.DoMouseScroll (document, increase: false);

		if (dy < 0) {
			if (cumulative_alt_scroll_amount > 0)
				cumulative_alt_scroll_amount = 0;

			cumulative_alt_scroll_amount += dy;
			if (cumulative_alt_scroll_amount <= -ZOOM_THRESHOLD_SCROLL) {
				cumulative_alt_scroll_amount = 0;
				tools.DoMouseScroll (document, increase: true);
			}
		} else {
			if (cumulative_alt_scroll_amount < 0)
				cumulative_alt_scroll_amount = 0;

			cumulative_alt_scroll_amount += dy;
			if (cumulative_alt_scroll_amount >= ZOOM_THRESHOLD_SCROLL) {
				cumulative_alt_scroll_amount = 0;
				tools.DoMouseScroll (document, increase: false);
			}
		}

		return true;
	}

	private void OnDragBegin (Gtk.GestureDrag gesture, Gtk.GestureDrag.DragBeginSignalArgs args)
	{
		// A mouse click on the canvas should grab focus away from any toolbar widgets, etc
		// Using the root canvas widget works best - if the drawing area is given focus, the scroll
		// widget jumps back to the origin.
		GrabFocus ();

		// Note: if we ever regain support for docking multiple canvas
		// widgets side by side (like Pinta 1.7 could), a mouse click should switch
		// the active document to this document.

		// Send the mouse press event to the current tool.
		// Translate coordinates to the canvas widget.
		PointD rootPoint = new (args.StartX, args.StartY);
		this.TranslateCoordinates (Canvas, rootPoint, out PointD viewPoint);
		PointD canvasPoint = document.Workspace.ViewPointToCanvas (viewPoint);

		ToolMouseEventArgs tool_args = new () {
			State = gesture.GetCurrentEventState (),
			MouseButton = gesture.GetCurrentMouseButton (),
			PointDouble = canvasPoint,
			WindowPoint = viewPoint,
			RootPoint = rootPoint,
		};

		tools.DoMouseDown (document, tool_args);
	}

	private void OnDragUpdate (Gtk.GestureDrag gesture, Gtk.GestureDrag.DragUpdateSignalArgs args)
	{
		gesture.GetStartPoint (out double startX, out double startY);
		PointD rootPoint = new (startX + args.OffsetX, startY + args.OffsetY);

		// Translate coordinates to the canvas widget.
		this.TranslateCoordinates (Canvas, rootPoint, out PointD viewPoint);

		pointer_root_point = rootPoint;
		drag_state = gesture.GetCurrentEventState ();
		drag_button = gesture.GetCurrentMouseButton ();

		SendDragMove (rootPoint, viewPoint);

		if (autoscroll_tick_id == 0 && AutoScrollStep (rootPoint) != PointI.Zero)
			autoscroll_tick_id = AddTickCallback (OnAutoScrollTick);
	}

	private void SendDragMove (PointD rootPoint, PointD viewPoint)
	{
		current_canvas_pos = document.Workspace.ViewPointToCanvas (viewPoint);
		if (document.Workspace.PointInCanvas (current_canvas_pos))
			chrome.LastCanvasCursorPoint = current_canvas_pos.ToInt ();

		// Send the mouse move event to the current tool.
		ToolMouseEventArgs tool_args = new () {
			State = drag_state,
			MouseButton = drag_button,
			PointDouble = current_canvas_pos,
			WindowPoint = viewPoint,
			RootPoint = rootPoint,
		};

		tools.DoMouseMove (document, tool_args);
	}

	/// <summary>
	/// How far to scroll this frame for a drag at <paramref name="rootPoint"/>, or zero when the
	/// pointer is inside the view or the drag should not scroll it (middle-button pan, or a tool
	/// that moves the view itself).
	/// </summary>
	private PointI AutoScrollStep (PointD rootPoint)
	{
		if (drag_button == MouseButton.Middle || !tools.CurrentToolAutoScrollsWhileDragging)
			return PointI.Zero;

		Gtk.Widget viewport = scrolled_window.Child!;
		this.TranslateCoordinates (viewport, rootPoint, out PointD p);

		return new (
			StepForAxis (p.X, viewport.GetWidth ()),
			StepForAxis (p.Y, viewport.GetHeight ()));

		static int StepForAxis (double position, double length)
		{
			double depth =
				position < AUTOSCROLL_EDGE_BAND ? position - AUTOSCROLL_EDGE_BAND
				: position > length - AUTOSCROLL_EDGE_BAND ? position - (length - AUTOSCROLL_EDGE_BAND)
				: 0;

			if (depth == 0)
				return 0;

			int step = Math.Clamp ((int) Math.Ceiling (Math.Abs (depth) * AUTOSCROLL_SPEED), 1, AUTOSCROLL_MAX_STEP);
			return Math.Sign (depth) * step;
		}
	}

	private bool OnAutoScrollTick (Gtk.Widget widget, Gdk.FrameClock clock)
	{
		PointI step = drag_controller.GetStartPoint (out _, out _)
			? AutoScrollStep (pointer_root_point)
			: PointI.Zero;

		Gtk.Adjustment h_adjust = scrolled_window.Hadjustment!;
		Gtk.Adjustment v_adjust = scrolled_window.Vadjustment!;
		double old_x = h_adjust.Value;
		double old_y = v_adjust.Value;

		if (step != PointI.Zero)
			document.Workspace.ScrollCanvas (step);

		PointD scrolled = new (h_adjust.Value - old_x, v_adjust.Value - old_y);

		// Stop once the pointer is back inside or the view can't scroll further that way; the
		// next drag update past the edge starts it again.
		if (scrolled == PointD.Zero) {
			autoscroll_tick_id = 0;
			return false;
		}

		// Layout from the previous frame already reflects earlier scrolls; this frame's scroll has
		// not been laid out yet, so shift the pointer's view position by it directly.
		this.TranslateCoordinates (Canvas, pointer_root_point, out PointD viewPoint);
		SendDragMove (pointer_root_point, viewPoint + scrolled);
		return true;
	}

	private void StopAutoScroll ()
	{
		if (autoscroll_tick_id == 0)
			return;

		RemoveTickCallback (autoscroll_tick_id);
		autoscroll_tick_id = 0;
	}

	private void OnDragEnd (Gtk.GestureDrag gesture, Gtk.GestureDrag.DragEndSignalArgs args)
	{
		StopAutoScroll ();

		gesture.GetStartPoint (out double startX, out double startY);
		PointD rootPoint = new (startX + args.OffsetX, startY + args.OffsetY);

		// Translate coordinates to the canvas widget.
		this.TranslateCoordinates (Canvas, rootPoint, out PointD viewPoint);
		PointD canvasPoint = document.Workspace.ViewPointToCanvas (viewPoint);

		// Send the mouse release event to the current tool.
		ToolMouseEventArgs tool_args = new () {
			State = gesture.GetCurrentEventState (),
			MouseButton = gesture.GetCurrentMouseButton (),
			PointDouble = canvasPoint,
			WindowPoint = viewPoint,
			RootPoint = rootPoint,
		};

		tools.DoMouseUp (document, tool_args);
	}

	public bool DoKeyPressEvent (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyPressedSignalArgs args)
	{
		// Give the current tool a chance to handle the key press
		ToolKeyEventArgs tool_args = new () {
			Event = controller.GetCurrentEvent (),
			Key = args.GetKey (),
			State = args.State,
		};

		return tools.DoKeyDown (document, tool_args);
	}

	public bool DoKeyReleaseEvent (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyReleasedSignalArgs args)
	{
		ToolKeyEventArgs tool_args = new () {
			Event = controller.GetCurrentEvent (),
			Key = args.GetKey (),
			State = args.State,
		};

		return tools.DoKeyUp (document, tool_args);
	}

	private void UpdateScrollbarTargeting (PointD? viewPos = null)
	{
		bool canTarget = viewPos is null
			|| tools.CurrentTool?.Handles.Any (h => h.Active && h.ContainsPoint (viewPos.Value)) != true;

		if (horizontal_scrollbar is not null)
			horizontal_scrollbar.CanTarget = canTarget;

		if (vertical_scrollbar is not null)
			vertical_scrollbar.CanTarget = canTarget;
	}
}
