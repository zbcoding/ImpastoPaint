// 
// BaseTransformTool.cs
//  
// Author:
//       Volodymyr <${AuthorEmail}>
// 
// Copyright (c) 2012 Volodymyr
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
using Pinta.Core;

namespace Pinta.Tools;

public abstract class BaseTransformTool : BaseTool
{
	private readonly int rotate_steps = 32;
	private readonly Matrix transform = CairoExtensions.CreateIdentityMatrix ();
	private RectangleD source_rect;
	private PointD original_point;
	private bool is_dragging = false;
	private bool is_rotating = false;
	private bool is_scaling = false;
	private bool using_mouse = false;

	// Drag-handle resizing of the moved/pasted content. Credit: issue #585,
	// marcovr's allow-handle-resize branch, and cameronwhite's draft PR #1515.
	// See docs/selection-transform-handles.md.
	private readonly IWorkspaceService workspace;
	private readonly IToolService tool_service;
	private readonly RectangleHandle handle;
	private readonly Gdk.Cursor rotate_cursor;
	private bool is_handle_scaling = false;
	public override IEnumerable<IToolHandle> Handles => [handle];

	// Nudge hint when holding arrow key for >2s (Issue #1559 extension)
	private DateTime? nudge_start_time;
	private uint nudge_hint_timeout_id = 0;
	private bool nudge_hint_visible = false;
	private string? last_nudge_hint;
	private Gtk.Popover? nudge_popover;
	private Gtk.Label? nudge_label;

	/// <summary>
	/// Initializes a new instance of the <see cref="BaseTransformTool"/> class.
	/// </summary>
	public BaseTransformTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
		tool_service = services.GetService<IToolService> ();
		handle = new (workspace) { InvertIfNegative = true };
		// A larger, high-contrast bidirectional rotate cursor (28px, hotspot centered),
		// sized to match the resize grips' cursors.
		rotate_cursor = Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.RotateHandle, 28), 14, 14, null);

		// The pasted/moved selection is often created *after* this tool is
		// activated (see PasteAction), so OnActivated is too early to show the
		// grips. Refresh them whenever the selection changes.
		workspace.SelectionChanged += (_, _) => {
			if (IsActive || tool_service.CurrentTool != this || !workspace.HasOpenDocuments)
				return;
			UpdateHandlesFromDocument (workspace.ActiveDocument);
		};
	}

	protected override void OnMouseDown (
		Document document,
		ToolMouseEventArgs e)
	{
		if (IsActive)
			return;

		original_point = e.PointDouble;

		// Alt (or right button) rotates, and takes priority over grabbing a grip.
		bool rotate_requested = e.MouseButton == MouseButton.Right || e.IsAltPressed;

		// If the mouse is on a resize grip, scale by dragging the handle.
		if (!rotate_requested && handle.Active && handle.BeginDrag (e.PointDouble, document.ImageSize)) {
			is_handle_scaling = true;
			using_mouse = true;
			OnStartTransform (document);
			return;
		}

		if (!document.Workspace.PointInCanvas (e.PointDouble))
			return;

		if (rotate_requested)
			is_rotating = true;
		else if (e.IsControlPressed)
			is_scaling = true;
		else
			is_dragging = true;

		using_mouse = true;

		OnStartTransform (document);
	}

	protected override void OnMouseMove (
		Document document,
		ToolMouseEventArgs e)
	{
		// While a grip is being dragged, scale the content to match the handle.
		if (is_handle_scaling) {
			HandlePoint? active = handle.ActiveHandlePoint;

			// Let RectangleHandle track the drag (updates its state + edge handles),
			// but never use its Shift=square constraint — we constrain to the
			// *pasted content's* aspect ratio ourselves below.
			handle.UpdateDrag (e.PointDouble, false);

			RectangleD to = IsCorner (active)
				? ComputeCornerRect (source_rect, ClampToImage (e.PointDouble, document), active!.Value, e.IsShiftPressed, e.IsControlPressed)
				: (e.IsControlPressed ? CenterAnchored (source_rect, handle.Rectangle) : handle.Rectangle);

			handle.Rectangle = to; // reflect the constrained rect back on the grips
			OnUpdateTransform (document, ComputeScaleTransform (source_rect, to));
			return;
		}

		if (!IsActive || !using_mouse) {
			if (!using_mouse && handle.Active) {
				Gdk.Cursor? gripCursor = handle.GetCursorAtPoint (e.WindowPoint);
				// Alt rotates; show the rotate cursor. Otherwise show the grip's
				// resize cursor when hovering one.
				SetCursor (e.IsAltPressed ? rotate_cursor : gripCursor ?? DefaultCursor);
				// Hint the modifier keys when hovering a grip.
				UpdateHandleHint (gripCursor is not null);
			}
			return;
		}

		bool constrain = e.IsShiftPressed;

		PointD center = source_rect.GetCenter ();

		// The cursor position can be a subpixel value. Round to an integer
		// so that we only translate by entire pixels.
		// (Otherwise, blurring / anti-aliasing may be introduced)

		double dx = Math.Floor (e.PointDouble.X - original_point.X);
		double dy = Math.Floor (e.PointDouble.Y - original_point.Y);

		PointD c1 = original_point - center;
		PointD c2 = e.PointDouble - center;

		RadiansAngle angle = new (Math.Atan2 (c1.Y, c1.X) - Math.Atan2 (c2.Y, c2.X));

		transform.InitIdentity ();

		if (is_scaling) {

			double sx = (c1.X + dx) / c1.X;
			double sy = (c1.Y + dy) / c1.Y;

			if (constrain) {

				double max_scale = Math.Max (Math.Abs (sx), Math.Abs (sy));

				sx = max_scale * Math.Sign (sx);
				sy = max_scale * Math.Sign (sy);
			}

			transform.Translate (center.X, center.Y);
			transform.Scale (sx, sy);
			transform.Translate (-center.X, -center.Y);
		} else if (is_rotating) {

			if (constrain)
				angle = Utility.GetNearestStepAngle (angle, rotate_steps);

			transform.Translate (center.X, center.Y);
			transform.Rotate (-angle.Radians);
			transform.Translate (-center.X, -center.Y);

		} else {
			transform.Translate (dx, dy);
		}

		OnUpdateTransform (document, transform);

		// Keep the grips on the (bounding box of the) moved content.
		UpdateHandlesFromDocument (document);
	}

	protected override void OnMouseUp (
		Document document,
		ToolMouseEventArgs e)
	{
		if (!IsActive || !using_mouse)
			return;

		if (is_handle_scaling)
			handle.EndDrag ();

		Matrix final = is_handle_scaling
			? ComputeScaleTransform (source_rect, handle.Rectangle)
			: transform;

		OnFinishTransform (document, final);
	}

	protected override bool OnKeyDown (
		Document document,
		ToolKeyEventArgs e)
	{
		if (using_mouse) // Don't handle the arrow keys while already interacting via the mouse.
			return base.OnKeyDown (document, e);

		// Determine if this is an arrow key for nudging.
		bool isArrow = e.Key.Value is Gdk.Constants.KEY_Left or Gdk.Constants.KEY_Right
			or Gdk.Constants.KEY_Up or Gdk.Constants.KEY_Down;

		if (!isArrow)
			return base.OnKeyDown (document, e);

		// Track nudge hold duration for hint (2 seconds).
		if (nudge_start_time is null) {
			nudge_start_time = DateTime.UtcNow;
			// Schedule a one-shot timeout to show hint after 2s even if no key repeat.
			if (nudge_hint_timeout_id == 0) {
				Document docForHint = document;
				nudge_hint_timeout_id = GLib.Functions.TimeoutAdd (0, 2000, () => {
					nudge_hint_timeout_id = 0;
					if (nudge_start_time is not null && IsActive && !using_mouse) {
						ShowNudgeHint (docForHint);
					}
					return false;
				});
			}
		} else {
			// If we've been holding for >2s, ensure hint is visible and updated.
			if ((DateTime.UtcNow - nudge_start_time.Value).TotalSeconds >= 2.0) {
				ShowNudgeHint (document);
			}
		}

		// Compute step amounts:
		// - 1px base
		// - Shift: 10px (Paint.NET parity, Issue #1559)
		// - Ctrl: 10% of canvas size (user request: based on canvas size)
		// - Ctrl+Shift: 20% of canvas size
		double dx = 0.0;
		double dy = 0.0;

		bool isCtrl = e.IsControlPressed;
		bool isShift = e.IsShiftPressed;

		int canvasW = document.ImageSize.Width;
		int canvasH = document.ImageSize.Height;

		// 10% and 20% of canvas, with sensible minimums so Ctrl is distinguishable.
		int ctrl10X = Math.Max (10, (int) Math.Round (canvasW * 0.10));
		int ctrl10Y = Math.Max (10, (int) Math.Round (canvasH * 0.10));
		int ctrl20X = Math.Max (20, (int) Math.Round (canvasW * 0.20));
		int ctrl20Y = Math.Max (20, (int) Math.Round (canvasH * 0.20));

		double stepX, stepY;

		if (isCtrl && isShift) {
			stepX = ctrl20X;
			stepY = ctrl20Y;
		} else if (isCtrl) {
			stepX = ctrl10X;
			stepY = ctrl10Y;
		} else if (isShift) {
			stepX = stepY = 10.0;
		} else {
			stepX = stepY = 1.0;
		}

		switch (e.Key.Value) {
			case Gdk.Constants.KEY_Left:
				dx = -stepX;
				break;
			case Gdk.Constants.KEY_Right:
				dx = stepX;
				break;
			case Gdk.Constants.KEY_Up:
				dy = -stepY;
				break;
			case Gdk.Constants.KEY_Down:
				dy = stepY;
				break;
		}

		if (!IsActive) {
			is_dragging = true;
			OnStartTransform (document);
		}

		transform.Translate (dx, dy);
		OnUpdateTransform (document, transform);

		// Keep handles glued to the nudged content in realtime (issue #1).
		UpdateHandlesFromDocument (document);

		// If hint is already visible, refresh its position/content (e.g., ctrl px changes).
		if (nudge_hint_visible) {
			ShowNudgeHint (document);
		}

		return true;
	}

	protected override bool OnKeyUp (
		Document document,
		ToolKeyEventArgs e)
	{
		// Clear nudge hint state when arrow key is released.
		if (e.Key.Value is Gdk.Constants.KEY_Left or Gdk.Constants.KEY_Right
			or Gdk.Constants.KEY_Up or Gdk.Constants.KEY_Down) {
			ClearNudgeState ();
		}

		if (IsActive && !using_mouse)
			OnFinishTransform (document, transform);

		return base.OnKeyUp (document, e);
	}

	protected abstract RectangleD GetSourceRectangle (Document document);

	protected virtual void OnStartTransform (Document document)
	{
		source_rect = GetSourceRectangle (document);
		transform.InitIdentity ();
	}

	protected virtual void OnUpdateTransform (
		Document document,
		Matrix transform)
	{ }

	protected virtual void OnFinishTransform (
		Document document,
		Matrix transform)
	{
		is_dragging = false;
		is_rotating = false;
		is_scaling = false;
		is_handle_scaling = false;
		using_mouse = false;

		// Clear any nudge hint when transform finishes.
		ClearNudgeState ();

		// Snap the grips onto the committed content.
		UpdateHandlesFromDocument (document);
	}

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);
		UpdateHandlesFromDocument (document);
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);
		handle.Active = false;
		UpdateHandleHint (false);
		ClearNudgeState ();

		if (nudge_popover is not null) {
			try {
				nudge_popover.Popdown ();
				nudge_popover.Unparent ();
			} catch { }
			nudge_popover = null;
			nudge_label = null;
		}
	}

	/// <summary>
	/// Position the resize grips on the current selection's bounding box, and
	/// only show them when there is a selection to transform. Gated on the
	/// selection's polygons (not <c>Visible</c>) because a freshly pasted
	/// selection isn't marked visible until after it is created.
	/// </summary>
	private void UpdateHandlesFromDocument (Document? document)
	{
		bool hasSelection = document is not null
			&& (document.Selection.Visible || document.Selection.SelectionPolygons.Count > 0);

		if (!hasSelection) {
			handle.Active = false;
			return;
		}

		handle.Active = true;
		handle.Rectangle = GetSourceRectangle (document!);
		document!.Workspace.Invalidate ();
	}

	/// <summary>
	/// Show/clear a canvas tooltip explaining the modifier keys while the
	/// cursor is over a resize grip.
	/// </summary>
	private void UpdateHandleHint (bool overGrip)
	{
		if (!workspace.HasOpenDocuments)
			return;

		// Don't override the nudge hint while it's visible.
		if (nudge_hint_visible)
			return;

		string? hint = overGrip
			// Translators: hint shown when hovering a selection resize handle.
			? Translations.GetString ("Drag to resize · Shift: keep aspect ratio · Ctrl+drag: scale from center · Alt-drag: rotate")
			: null;

		Gtk.Widget canvas = workspace.ActiveWorkspace.Canvas;
		if (canvas.TooltipText != hint)
			canvas.SetTooltipText (hint);
	}

	/// <summary>
	/// Show a hint about arrow-key nudging after holding for 2 seconds.
	/// Similar UI to the tool menu button's hint popovers (issue #1559).
	/// Anchored to the lower-right of the nudged area so it appears near the
	/// content even when the mouse is elsewhere (keyboard-only use).
	/// </summary>
	private void ShowNudgeHint (Document document)
	{
		if (!workspace.HasOpenDocuments)
			return;

		int w = document.ImageSize.Width;
		int h = document.ImageSize.Height;
		int ctrl10X = Math.Max (10, (int) Math.Round (w * 0.10));
		int ctrl10Y = Math.Max (10, (int) Math.Round (h * 0.10));
		int ctrl20X = Math.Max (20, (int) Math.Round (w * 0.20));
		int ctrl20Y = Math.Max (20, (int) Math.Round (h * 0.20));

		// Hint says "Ctrl+Arrow: 10% of canvas size" per user feedback, with example px.
		string template = Translations.GetString (
			"Arrow: 1px · Shift+Arrow: 10px · Ctrl+Arrow: 10% of canvas ({0}×{1}px) · Ctrl+Shift+Arrow: 20% of canvas ({2}×{3}px)");

		string hint;
		try {
			hint = string.Format (template, ctrl10X, ctrl10Y, ctrl20X, ctrl20Y);
		} catch {
			string fallback = Translations.GetString (
				"Arrow: 1px · Shift+Arrow: 10px · Ctrl+Arrow: 10% of canvas · Ctrl+Shift+Arrow: 20% of canvas");
			hint = fallback;
		}

		var activeWs = workspace.ActiveWorkspace;
		Gtk.Widget canvas = activeWs.Canvas;

		// Lower-right of the nudged area in canvas coords.
		RectangleD rect;
		if (handle.Active) {
			rect = handle.Rectangle;
		} else {
			try {
				rect = GetSourceRectangle (document);
			} catch {
				rect = document.Selection.GetBounds ();
			}
		}

		PointD lowerRightCanvas = new (rect.X + rect.Width, rect.Y + rect.Height);
		PointD lowerRightView = activeWs.CanvasPointToView (lowerRightCanvas);

		if (nudge_popover is null) {
			nudge_popover = Gtk.Popover.New ();
			nudge_popover.Autohide = false;
			nudge_popover.Position = Gtk.PositionType.Bottom;
			nudge_popover.SetParent (canvas);
			nudge_label = Gtk.Label.New (hint);
			nudge_label.Wrap = true;
			nudge_label.MaxWidthChars = 60;
			nudge_popover.SetChild (nudge_label);
		} else {
			if (nudge_label is not null)
				nudge_label.SetText (hint);
			if (nudge_popover.GetParent () != canvas) {
				nudge_popover.Unparent ();
				nudge_popover.SetParent (canvas);
			}
		}

		Gdk.Rectangle pointing = new () {
			X = (int) Math.Clamp (lowerRightView.X, 0, 10000),
			Y = (int) Math.Clamp (lowerRightView.Y, 0, 10000),
			Width = 1,
			Height = 1
		};
		nudge_popover.PointingTo = pointing;

		canvas.SetTooltipText (hint);

		nudge_popover.Popup ();
		last_nudge_hint = hint;
		nudge_hint_visible = true;
	}

	private void HideNudgeHint ()
	{
		if (!nudge_hint_visible && nudge_popover is null)
			return;

		if (workspace.HasOpenDocuments) {
			try {
				Gtk.Widget canvas = workspace.ActiveWorkspace.Canvas;
				if (last_nudge_hint is not null && canvas.TooltipText == last_nudge_hint) {
					canvas.SetTooltipText (null);
				} else if (canvas.TooltipText is not null && nudge_hint_visible) {
					if (canvas.TooltipText.Contains ("Arrow:") || canvas.TooltipText.Contains ("Shift+Arrow"))
						canvas.SetTooltipText (null);
				}
			} catch { }
		}

		if (nudge_popover is not null) {
			try {
				nudge_popover.Popdown ();
			} catch { }
		}

		nudge_hint_visible = false;
		last_nudge_hint = null;
	}

	private void ClearNudgeState ()
	{
		if (nudge_hint_timeout_id != 0) {
			GLib.Functions.SourceRemove (nudge_hint_timeout_id);
			nudge_hint_timeout_id = 0;
		}

		nudge_start_time = null;
		HideNudgeHint ();
	}

	/// <summary>
	/// Build the transform that maps the <paramref name="from"/> rectangle onto
	/// <paramref name="to"/>. Because the dragged handle keeps the opposite corner
	/// fixed, this scales relative to that corner rather than the center.
	/// </summary>
	private Matrix ComputeScaleTransform (RectangleD from, RectangleD to)
	{
		double sx = from.Width != 0 ? to.Width / from.Width : 1.0;
		double sy = from.Height != 0 ? to.Height / from.Height : 1.0;

		transform.InitIdentity ();
		transform.Translate (to.X, to.Y);
		transform.Scale (sx, sy);
		transform.Translate (-from.X, -from.Y);
		return transform;
	}

	private static bool IsCorner (HandlePoint? p)
		=> p is HandlePoint.UpperLeft or HandlePoint.UpperRight
			or HandlePoint.LowerLeft or HandlePoint.LowerRight;

	private static PointD ClampToImage (PointD p, Document document)
		=> new (
			Math.Round (Math.Clamp (p.X, 0, document.ImageSize.Width)),
			Math.Round (Math.Clamp (p.Y, 0, document.ImageSize.Height)));

	/// <summary>
	/// The corner of <paramref name="s"/> diagonally opposite the dragged one;
	/// this corner stays fixed while the dragged corner follows the mouse.
	/// </summary>
	private static PointD OppositeCorner (RectangleD s, HandlePoint dragged) => dragged switch {
		HandlePoint.UpperLeft => new (s.Right, s.Bottom),
		HandlePoint.UpperRight => new (s.Left, s.Bottom),
		HandlePoint.LowerLeft => new (s.Right, s.Top),
		HandlePoint.LowerRight => new (s.Left, s.Top),
		_ => s.GetCenter (),
	};

	/// <summary>
	/// Target rectangle for a corner drag. Shift keeps the pasted content's
	/// original width:height ratio (not a square); Ctrl anchors the scale to
	/// the center instead of the opposite corner.
	/// </summary>
	private static RectangleD ComputeCornerRect (RectangleD source, PointD mouse, HandlePoint dragged, bool keepAspect, bool fromCenter)
	{
		PointD anchor = OppositeCorner (source, dragged);
		double dx = mouse.X - anchor.X;
		double dy = mouse.Y - anchor.Y;

		if (keepAspect && source.Width > 0 && source.Height > 0) {
			// Scale both axes by the same factor, following whichever axis was dragged further.
			double s = Math.Max (Math.Abs (dx) / source.Width, Math.Abs (dy) / source.Height);
			dx = (dx < 0 ? -1 : 1) * source.Width * s;
			dy = (dy < 0 ? -1 : 1) * source.Height * s;
		}

		RectangleD rect = RectangleD.FromPoints (anchor, new PointD (anchor.X + dx, anchor.Y + dy), true);
		return fromCenter ? CenterAnchored (source, rect) : rect;
	}

	/// <summary>
	/// Re-centers <paramref name="rect"/> on the source's center, keeping its size,
	/// so scaling grows symmetrically about the center (Ctrl behavior).
	/// </summary>
	private static RectangleD CenterAnchored (RectangleD source, RectangleD rect)
	{
		PointD c = source.GetCenter ();
		return new RectangleD (new PointD (c.X - rect.Width / 2, c.Y - rect.Height / 2), rect.Width, rect.Height);
	}

	private bool IsActive
		=> is_dragging || is_rotating || is_scaling || is_handle_scaling;
}

