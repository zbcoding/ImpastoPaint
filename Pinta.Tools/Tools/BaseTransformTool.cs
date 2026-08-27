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
using System.Diagnostics;
using System.IO;
using System.Linq;
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

	// Impasto: snapping. The mouse position itself is snapped upstream (see
	// UseSnapping), which is what grip resizing needs; a plain drag additionally
	// aligns the content's own corner - or its center, while "c" is held - to the
	// grid instead of preserving the grab offset.
	private bool center_snap_held = false;
	public override IEnumerable<IToolHandle> Handles => [handle];

	public override bool UseSnapping => true;

	// Live orientation of the moved content (issue #4): maps the axis-aligned
	// reference rect (ref_rect, captured when the selection first attaches) onto
	// the current on-screen quad, so the resize grips stay glued to rotated
	// content. Only ever a rotation + ref-space axis scale + translation, so it
	// always maps ref_rect to a proper (non-sheared) oriented rectangle.
	private readonly Matrix live = CairoExtensions.CreateIdentityMatrix ();
	private RectangleD ref_rect;

	// Nudge hint when holding arrow key for >2s (Issue #1559 extension)
	private DateTime? nudge_start_time;
	private uint nudge_hint_timeout_id = 0;
	private bool nudge_hint_visible = false;

	// Canvas tooltip the nudge hint displaced (often the grip hint), restored on
	// hide. Ownership is tracked explicitly rather than matched against tooltip
	// contents, which would break under translation.
	private string? tooltip_before_nudge_hint;
	private readonly TransientHintPopover nudge_popover = new ();

	/// <summary>
	/// Initializes a new instance of the <see cref="BaseTransformTool"/> class.
	/// </summary>
	public BaseTransformTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
		tool_service = services.GetService<IToolService> ();
		handle = new (workspace) { InvertIfNegative = true };
		// A larger, high-contrast bidirectional rotate cursor, sized to match the
		// resize grips' cursors. Resource SVGs load at their natural size (not the
		// requested one), so center the hotspot on the actual texture — a fixed
		// hotspot past the edge trips a GDK assertion.
		// Use a non-symbolic icon (rotate-handle.svg) so GTK preserves the white
		// halo + dark stroke. Symbolic icons get recolored to a single color and
		// lose contrast, making the cursor invisible against some backgrounds.
		Gdk.Texture rotate_texture = LoadRotateTexture ();
		rotate_cursor = Gdk.Cursor.NewFromTexture (rotate_texture, rotate_texture.Width / 2, rotate_texture.Height / 2, null);

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
			// Grip scaling is computed in the reference frame (see ApplyRefScale).
			source_rect = ref_rect;
			return;
		}

		if (!document.Workspace.PointInCanvas (e.PointDouble))
			return;

		if (rotate_requested) {
			is_rotating = true;
			SetCursor (rotate_cursor);
		} else if (e.IsControlPressed)
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
			HandleGripDrag (document, e);
			return;
		}

		if (!IsActive || !using_mouse) {
			UpdateHoverCursor (e);
			return;
		}

		ApplyActiveTransform (document, e);
	}

	/// <summary>
	/// Scale the content to match a resize grip being dragged (<c>is_handle_scaling</c>).
	/// </summary>
	private void HandleGripDrag (Document document, ToolMouseEventArgs e)
	{
		HandlePoint? active = handle.ActiveHandlePoint;
		if (active is null)
			return;

		// Work in the reference frame: un-rotate the mouse through the live
		// orientation so a (possibly rotated) grip drag reduces to an
		// axis-aligned resize of ref_rect. The resize matrices built below
		// are then re-applied through `live` in ApplyRefScale, and the grips
		// are re-positioned in the oriented frame (never via handle.UpdateDrag,
		// which would corrupt the reference rectangle).
		Matrix liveInv = live.Clone ();
		liveInv.Invert ();
		PointD mouse = liveInv.TransformPoint (e.PointDouble);
		PointD srcCenter = source_rect.GetCenter ();
		bool keepAspect = e.IsShiftPressed;
		bool fromCenter = e.IsControlPressed;

		if (IsCorner (active)) {
			// --- Corner handles: allow flipping (mirroring) ---
			PointD opp = OppositeCorner (source_rect, active.Value);
			PointD srcCorner = GetCornerPoint (source_rect, active.Value);

			Matrix flipTransform = CairoExtensions.CreateIdentityMatrix ();

			if (fromCenter) {
				// Scale about center; flip occurs when mouse crosses center.
				double cdx0 = srcCorner.X - srcCenter.X;
				double cdy0 = srcCorner.Y - srcCenter.Y;
				double cdx1 = mouse.X - srcCenter.X;
				double cdy1 = mouse.Y - srcCenter.Y;

				if (keepAspect && source_rect.Width > 0 && source_rect.Height > 0) {
					double halfW = source_rect.Width / 2.0;
					double halfH = source_rect.Height / 2.0;
					if (halfW > 0 && halfH > 0) {
						double s = Math.Max (Math.Abs (cdx1) / halfW, Math.Abs (cdy1) / halfH);
						double signX = cdx1 < 0 ? -1 : 1;
						double signY = cdy1 < 0 ? -1 : 1;
						// When exactly zero, keep positive to avoid NaN.
						cdx1 = signX * halfW * s;
						cdy1 = signY * halfH * s;
					}
				}

				double sx = cdx0 != 0 ? cdx1 / cdx0 : 1;
				double sy = cdy0 != 0 ? cdy1 / cdy0 : 1;

				flipTransform.Translate (srcCenter.X, srcCenter.Y);
				flipTransform.Scale (sx, sy);
				flipTransform.Translate (-srcCenter.X, -srcCenter.Y);
			} else {
				// Scale about opposite corner; flip occurs when mouse crosses that corner.
				double dx0 = srcCorner.X - opp.X;
				double dy0 = srcCorner.Y - opp.Y;
				double dx1 = mouse.X - opp.X;
				double dy1 = mouse.Y - opp.Y;

				if (keepAspect && source_rect.Width > 0 && source_rect.Height > 0) {
					double s = Math.Max (Math.Abs (dx1) / source_rect.Width, Math.Abs (dy1) / source_rect.Height);
					double signX = dx1 < 0 ? -1 : 1;
					double signY = dy1 < 0 ? -1 : 1;
					dx1 = signX * source_rect.Width * s;
					dy1 = signY * source_rect.Height * s;
				}

				double sx = dx0 != 0 ? dx1 / dx0 : 1;
				double sy = dy0 != 0 ? dy1 / dy0 : 1;

				flipTransform.Translate (opp.X, opp.Y);
				flipTransform.Scale (sx, sy);
				flipTransform.Translate (-opp.X, -opp.Y);
			}

			// Grips are placed via the oriented frame, not a target rect.
			ApplyRefScale (document, flipTransform);
		} else {
			// Edge handles: allow flipping (mirroring) as well, so user can
			// mirror horizontally by dragging left/right past opposite edge,
			// and vertically by dragging up/down past opposite edge.
			(bool horizontal, bool nearIsMin) = active.Value switch {
				HandlePoint.Left => (true, true),
				HandlePoint.Right => (true, false),
				HandlePoint.Up => (false, true),
				HandlePoint.Down => (false, false),
				// Unreachable: the four corner handles are dispatched by IsCorner above,
				// and these four cases exhaust HandlePoint.
				_ => throw new UnreachableException (),
			};

			Matrix edgeTransform = ComputeEdgeScaleTransform (source_rect, srcCenter, mouse, horizontal, nearIsMin, fromCenter, keepAspect);

			// Grips are placed via the oriented frame, not a target rect.
			ApplyRefScale (document, edgeTransform);
		}
	}

	/// <summary>
	/// Update the cursor/hint while hovering (not dragging) over a resize grip.
	/// </summary>
	private void UpdateHoverCursor (ToolMouseEventArgs e)
	{
		if (using_mouse || !handle.Active)
			return;

		Gdk.Cursor? gripCursor = handle.GetCursorAtPoint (e.WindowPoint);
		// Alt rotates; show the rotate cursor. Otherwise show the grip's
		// resize cursor when hovering one.
		SetCursor (e.IsAltPressed ? rotate_cursor : gripCursor ?? DefaultCursor);
		// Hint the modifier keys when hovering a grip.
		UpdateHandleHint (gripCursor is not null);
	}

	/// <summary>
	/// Apply the in-progress drag/rotate/scale gesture (<c>using_mouse</c> is true) to the
	/// content's transform and refresh the grips.
	/// </summary>
	private void ApplyActiveTransform (Document document, ToolMouseEventArgs e)
	{
		// Keep rotate cursor visible while actively rotating.
		if (is_rotating)
			SetCursor (rotate_cursor);

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

			double sx = c1.X != 0 ? (c1.X + dx) / c1.X : 1;
			double sy = c1.Y != 0 ? (c1.Y + dy) / c1.Y : 1;

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
			(dx, dy) = SnapTranslation (dx, dy);
			transform.Translate (dx, dy);
		}

		OnUpdateTransform (document, transform);

		// Keep the grips glued to the moved/rotated content in the oriented frame.
		RefreshOrientedHandles (document);
	}

	protected override void OnMouseUp (
		Document document,
		ToolMouseEventArgs e)
	{
		if (!IsActive || !using_mouse)
			return;

		if (is_handle_scaling)
			handle.EndDrag ();

		// For handle scaling we already computed the (possibly flipped) transform
		// in OnMouseMove and stored it in `transform`. Using ComputeScaleTransform
		// from the positive bounding rect would lose the sign and thus the flip.
		Matrix final = transform;

		OnFinishTransform (document, final);
	}

	/// <summary>
	/// Aligns a drag so the moved content itself lands on the grid or on the
	/// canvas guides, rather than moving it by a snapped delta from wherever it
	/// was grabbed. Against the canvas guides the whole box is offered, so a
	/// drag settles into centered or edge-aligned on its own; on a grid or ruler
	/// the anchor is the box's top-left corner, or its center while "c" is held.
	/// </summary>
	private (double, double) SnapTranslation (double dx, double dy)
	{
		if (!PintaCore.CanvasGrid.SnapEnabled)
			return (dx, dy);

		RectangleD bounds = CurrentBounds ();

		PointD snapped = PintaCore.CanvasGrid.SnapRect (
			new (new PointD (bounds.X + dx, bounds.Y + dy), bounds.Width, bounds.Height),
			center_snap_held);

		return (snapped.X - bounds.X, snapped.Y - bounds.Y);
	}

	/// <summary>
	/// Where the content sits on the canvas right now, as an axis-aligned box:
	/// the reference rect placed through the live orientation. Rotated content
	/// snaps by that box, which is what the guides are drawn against.
	/// </summary>
	private RectangleD CurrentBounds ()
	{
		PointD[] corners = [
			live.TransformPoint (new (ref_rect.X, ref_rect.Y)),
			live.TransformPoint (new (ref_rect.X + ref_rect.Width, ref_rect.Y)),
			live.TransformPoint (new (ref_rect.X + ref_rect.Width, ref_rect.Y + ref_rect.Height)),
			live.TransformPoint (new (ref_rect.X, ref_rect.Y + ref_rect.Height)),
		];

		double minX = corners.Min (c => c.X);
		double minY = corners.Min (c => c.Y);

		return new (
			new PointD (minX, minY),
			corners.Max (c => c.X) - minX,
			corners.Max (c => c.Y) - minY);
	}

	protected override bool OnKeyDown (
		Document document,
		ToolKeyEventArgs e)
	{
		if (e.Key.ToUpper ().Value == Gdk.Constants.KEY_C)
			center_snap_held = true;

		if (using_mouse) // Don't handle the arrow keys while already interacting via the mouse.
			return base.OnKeyDown (document, e);

		if (GetNudgeBinding (e.Gesture) is not ToolBindingDescriptor nudgeBinding)
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

		int canvasW = document.ImageSize.Width;
		int canvasH = document.ImageSize.Height;
		int ctrl10X = Math.Max (10, (int) Math.Round (canvasW * 0.10));
		int ctrl10Y = Math.Max (10, (int) Math.Round (canvasH * 0.10));
		int ctrl20X = Math.Max (20, (int) Math.Round (canvasW * 0.20));
		int ctrl20Y = Math.Max (20, (int) Math.Round (canvasH * 0.20));

		(double dx, double dy) = GetNudgeDelta (nudgeBinding, ctrl10X, ctrl10Y, ctrl20X, ctrl20Y);

		if (!IsActive) {
			is_dragging = true;
			OnStartTransform (document);
		}

		transform.Translate (dx, dy);
		OnUpdateTransform (document, transform);

		// Keep handles glued to the nudged content in realtime (issue #1).
		RefreshOrientedHandles (document);

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
		if (e.Key.ToUpper ().Value == Gdk.Constants.KEY_C)
			center_snap_held = false;

		// Clear nudge hint state when arrow key is released.
		if (GetNudgeBinding (e.Gesture) is not null) {
			ClearNudgeState ();
		}

		if (IsActive && !using_mouse)
			OnFinishTransform (document, transform);

		return base.OnKeyUp (document, e);
	}

	private static ToolBindingDescriptor? GetNudgeBinding (KeyGesture gesture)
	{
		foreach (var binding in KeyboardShortcutManager.ToolBindings) {
			if (binding.Id.StartsWith ("TransformTool.Nudge", StringComparison.Ordinal) &&
				PintaCore.Shortcuts.GetToolBinding (binding) == gesture)
				return binding;
		}

		return null;
	}

	private static (double dx, double dy) GetNudgeDelta (
		ToolBindingDescriptor binding,
		int ctrl10X,
		int ctrl10Y,
		int ctrl20X,
		int ctrl20Y)
	{
		bool large = binding.Id.EndsWith ("Large", StringComparison.Ordinal);
		bool percent = binding.Id.Contains ("Pct", StringComparison.Ordinal);
		double stepX = percent ? (large ? ctrl20X : ctrl10X) : large ? 10 : 1;
		double stepY = percent ? (large ? ctrl20Y : ctrl10Y) : large ? 10 : 1;

		return binding.Id switch {
			"TransformTool.NudgeLeft" or "TransformTool.NudgeLeftLarge" or "TransformTool.NudgeLeftPct" or "TransformTool.NudgeLeftPctLarge" => (-stepX, 0),
			"TransformTool.NudgeRight" or "TransformTool.NudgeRightLarge" or "TransformTool.NudgeRightPct" or "TransformTool.NudgeRightPctLarge" => (stepX, 0),
			"TransformTool.NudgeUp" or "TransformTool.NudgeUpLarge" or "TransformTool.NudgeUpPct" or "TransformTool.NudgeUpPctLarge" => (0, -stepY),
			_ => (0, stepY),
		};
	}

	private static string FormatNudgeBinding (ToolBindingDescriptor binding)
	{
		string shortcut = PintaCore.Shortcuts.GetToolBinding (binding).ToAcceleratorName ();
		string label = GtkExtensions.TryParseAccelerator (shortcut, out uint key, out var mods)
			? Gtk.Functions.AcceleratorGetLabel (key, mods)
			: shortcut;
		string direction = binding.Id.Contains ("Left", StringComparison.Ordinal) ? "Left" :
			binding.Id.Contains ("Right", StringComparison.Ordinal) ? "Right" :
			binding.Id.Contains ("Up", StringComparison.Ordinal) ? "Up" : "Down";
		return $"{direction}: {label}";
	}

	private static string FormatCustomNudgeBinding (ToolBindingDescriptor binding)
	{
		string direction = binding.Id.Contains ("Left", StringComparison.Ordinal) ? "left" :
			binding.Id.Contains ("Right", StringComparison.Ordinal) ? "right" :
			binding.Id.Contains ("Up", StringComparison.Ordinal) ? "up" : "down";
		string label = FormatNudgeBinding (binding).Split (": ", 2)[1];
		string amount = binding.Id.Contains ("PctLarge", StringComparison.Ordinal) ? "20% of canvas" :
			binding.Id.Contains ("Pct", StringComparison.Ordinal) ? "10% of canvas" :
			binding.Id.EndsWith ("Large", StringComparison.Ordinal) ? "10px" : "1px";

		return amount == "1px"
			? Translations.GetString ("Nudge {0}: {1}", direction, label)
			: Translations.GetString ("Nudge {0} ({1}): {2}", amount, direction, label);
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

		// Commit this gesture into the live orientation and snap the grips onto
		// it, so the next gesture (and the drawn handles) stay in the oriented
		// frame instead of resetting to an axis-aligned box (issue #4).
		live.Multiply (transform);
		if (document.Selection.Visible)
			handle.SetOriented (ref_rect, live.Clone ());
		else
			handle.Active = false;
		document.Workspace.Invalidate ();

		// Restore cursor after a rotate/scale gesture
		SetCursor (DefaultCursor);
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

		// Fully detach popover to avoid holding parent reference.
		nudge_popover.Dispose ();
	}

	/// <summary>
	/// Position the resize grips on the current selection's bounding box, and
	/// only show them when there is a <em>visible</em> selection to transform.
	/// A fresh document carries an invisible full-canvas select-all
	/// (<c>ResetSelectionPaths</c>), so gating on <c>SelectionPolygons.Count</c>
	/// would wrongly show grips on the default layer; paste sets <c>Visible</c>.
	/// </summary>
	private void UpdateHandlesFromDocument (Document? document)
	{
		bool hasSelection = document is not null && document.Selection.Visible;

		if (!hasSelection) {
			handle.Active = false;
			return;
		}

		// Derive the grips from the selection's own polygon. It is part of
		// document.Selection, so history saves/restores it, and it outlines the
		// transformed content exactly (a rotate maps the polygon corners the same
		// way it maps the pixels). A rotated rectangular selection is a 4-corner
		// quad; map an axis-aligned reference rect onto it so the existing
		// draw/hit-test/scale code (which works in ref space) keeps functioning.
		// Non-rectangular selections fall back to the axis-aligned bounding box.
		if (TryGetOrientedQuad (document!, out RectangleD refRect, out Matrix orientation)) {
			ref_rect = refRect;
			live.InitMatrix (orientation);
		} else {
			ref_rect = GetSourceRectangle (document!);
			live.InitIdentity ();
		}
		handle.Active = true;
		handle.SetOriented (ref_rect, live.Clone ());
		document!.Workspace.Invalidate ();
	}

	/// <summary>
	/// Re-position the grips on the moved/rotated content during a non-grip
	/// gesture (body drag, rotate, nudge). The content transform for the whole
	/// gesture is <c>transform</c>; the grips follow at <c>live · transform</c>.
	/// </summary>
	private void RefreshOrientedHandles (Document document)
	{
		Matrix disp = live.Clone ();
		disp.Multiply (transform);
		handle.SetOriented (ref_rect, disp);
		document.Workspace.Invalidate ();
	}

	/// <summary>
	/// Apply a resize computed in the reference frame (<paramref name="s"/> maps
	/// ref_rect onto the resized rect) to the actual content, mapped through the
	/// live orientation so rotated content scales along its own axes:
	/// <c>g = live · s · live⁻¹</c>. The grips are drawn at the candidate
	/// post-drag orientation (committed on mouse-up in OnFinishTransform).
	/// </summary>
	private void ApplyRefScale (Document document, Matrix s)
	{
		Matrix liveInv = live.Clone ();
		liveInv.Invert ();

		Matrix g = liveInv;   // apply live⁻¹ first,
		g.Multiply (s);       // then the ref-space resize,
		g.Multiply (live);    // then re-apply live.

		transform.InitMatrix (g);
		OnUpdateTransform (document, transform);

		Matrix disp = live.Clone ();
		disp.Multiply (g);
		handle.SetOriented (ref_rect, disp);
		document.Workspace.Invalidate ();
	}

	/// <summary>
	/// Show/clear a canvas tooltip explaining the modifier keys while the
	/// cursor is over a resize grip.
	/// </summary>
	private void UpdateHandleHint (bool overGrip)
	{
		if (!workspace.HasOpenDocuments)
			return;

		// The nudge hint owns the tooltip while it is up; it restores whatever
		// was there (often this hint) when it hides.
		if (nudge_hint_visible)
			return;

		Gtk.Widget canvas = workspace.ActiveWorkspace.Canvas;
		if (!TransientHintPopover.ShouldShow) {
			if (canvas.TooltipText is not null)
				canvas.SetTooltipText (null);
			return;
		}

		string? hint = overGrip
			// Translators: hint shown when hovering a selection resize handle. Now lists shortcuts vertically.
			? BuildGripHint ()
			: null;

		if (canvas.TooltipText != hint)
			canvas.SetTooltipText (hint);
	}

	private static string BuildGripHint ()
	{
		string ctrl = PintaCore.System.CtrlLabel ();
		// Translators: hint shown when hovering a selection resize handle. Now lists shortcuts vertically.
		return Translations.GetString (
			$"Drag to resize\nShift: keep aspect ratio\n{ctrl}+drag: scale from center\nAlt-drag: rotate");
	}

	/// <summary>
	/// Show a hint about arrow-key nudging after holding for 2 seconds.
	/// Similar UI to the tool menu button's hint popovers (issue #1559).
	/// Anchored to the lower-right of the nudged area so it appears near the
	/// content even when the mouse is elsewhere (keyboard-only use).
	/// </summary>
	private void ShowNudgeHint (Document document)
	{
		if (!workspace.HasOpenDocuments || !TransientHintPopover.ShouldShow)
			return;

		ToolBindingDescriptor[] nudgeBindings = [
			KeyboardShortcutManager.TransformNudgeLeft,
			KeyboardShortcutManager.TransformNudgeRight,
			KeyboardShortcutManager.TransformNudgeUp,
			KeyboardShortcutManager.TransformNudgeDown,
			KeyboardShortcutManager.TransformNudgeLeftLarge,
			KeyboardShortcutManager.TransformNudgeRightLarge,
			KeyboardShortcutManager.TransformNudgeUpLarge,
			KeyboardShortcutManager.TransformNudgeDownLarge,
			KeyboardShortcutManager.TransformNudgeLeftPct,
			KeyboardShortcutManager.TransformNudgeRightPct,
			KeyboardShortcutManager.TransformNudgeUpPct,
			KeyboardShortcutManager.TransformNudgeDownPct,
			KeyboardShortcutManager.TransformNudgeLeftPctLarge,
			KeyboardShortcutManager.TransformNudgeRightPctLarge,
			KeyboardShortcutManager.TransformNudgeUpPctLarge,
			KeyboardShortcutManager.TransformNudgeDownPctLarge,
		];

		string ctrl = PintaCore.System.CtrlLabel ();
		string shift = Translations.GetString ("Shift");

		List<string> hintLines = [
			Translations.GetString ("Nudge: Arrow keys"),
			Translations.GetString ("Nudge 10px: {0}", shift),
			Translations.GetString ("Nudge 10% of canvas: {0}", ctrl),
			Translations.GetString ("Nudge 20% of canvas: {0}", $"{ctrl}+{shift}"),
		];
		hintLines.AddRange (nudgeBindings
			.Where (binding => PintaCore.Shortcuts.GetToolBinding (binding) != binding.DefaultGesture)
			.Select (FormatCustomNudgeBinding));
		string hint = string.Join ("\n", hintLines);

		var activeWs = workspace.ActiveWorkspace;
		Gtk.Widget canvas = activeWs.Canvas;

		// Determine lower-right of the nudged area in canvas coordinates.
		RectangleD rect;
		if (handle.Active) {
			rect = handle.Rectangle;
		} else {
			// Fallback to current selection bounds.
			try {
				rect = GetSourceRectangle (document);
			} catch {
				rect = document.Selection.GetBounds ();
			}
		}

		PointD lowerRightCanvas = new (rect.X + rect.Width, rect.Y + rect.Height);
		// Anchor to the oriented lower-right when the content is rotated (issue #4).
		if (handle.Orientation is not null)
			lowerRightCanvas = handle.Orientation.TransformPoint (lowerRightCanvas);
		PointD lowerRightView = activeWs.CanvasPointToView (lowerRightCanvas);

		// Anchor popover to lower-right of the nudge area.
		// If mouse is elsewhere, popover still appears near content (fixes issue #2).
		nudge_popover.Show (canvas, hint, lowerRightView);

		// Also set tooltip as fallback for accessibility / hover. Remember what
		// was there (often the grip hint) so it can be restored exactly, instead
		// of guessing from the text.
		tooltip_before_nudge_hint = canvas.TooltipText;
		canvas.SetTooltipText (hint);

		nudge_hint_visible = true;
	}

	private void HideNudgeHint ()
	{
		if (!nudge_hint_visible && !nudge_popover.Exists)
			return;

		if (workspace.HasOpenDocuments) {
			try {
				Gtk.Widget canvas = workspace.ActiveWorkspace.Canvas;

				// Restore whatever the hint displaced only if nothing has
				// overwritten it since; otherwise leave the newer tooltip alone.
				if (canvas.TooltipText == nudge_popover.LastText) {
					string? restore = nudge_hint_visible ? tooltip_before_nudge_hint : null;
					canvas.SetTooltipText (restore);
				}
			} catch {
				// Workspace may be disposed.
			}
		}

		nudge_popover.Hide ();

		nudge_hint_visible = false;
		tooltip_before_nudge_hint = null;
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

	/// <summary>
	/// If the selection is a rotated rectangle (a 4-corner quad), returns the
	/// axis-aligned reference rect <c>(0,0,w,h)</c> and the <paramref name="orientation"/>
	/// matrix that maps it onto that quad. The quad comes straight from the
	/// selection polygon, so it matches the on-screen content at any history step.
	/// Returns false for non-rectangular selections (caller uses the bbox).
	/// </summary>
	private static bool TryGetOrientedQuad (Document document, out RectangleD refRect, out Matrix orientation)
	{
		refRect = default;
		orientation = CairoExtensions.CreateIdentityMatrix ();

		var polys = document.Selection.SelectionPolygons;
		// A rectangle is 4 corners (some paths repeat the first point to close).
		if (polys.Count != 1 || (polys[0].Count != 4 && polys[0].Count != 5))
			return false;

		// p0 and its two adjacent corners p1 (width edge) and p3 (height edge).
		PointD p0 = new (polys[0][0].X, polys[0][0].Y);
		PointD p1 = new (polys[0][1].X, polys[0][1].Y);
		PointD p3 = new (polys[0][3].X, polys[0][3].Y);

		PointD widthVec = p1 - p0;
		PointD heightVec = p3 - p0;
		double w = Math.Sqrt (widthVec.X * widthVec.X + widthVec.Y * widthVec.Y);
		double h = Math.Sqrt (heightVec.X * heightVec.X + heightVec.Y * heightVec.Y);
		if (w < 1e-6 || h < 1e-6)
			return false;

		PointD ex = new (widthVec.X / w, widthVec.Y / w);
		PointD ey = new (heightVec.X / h, heightVec.Y / h);

		// Reject non-orthogonal quads (sheared/arbitrary polygons); we only model
		// rotated + flipped rectangles. SelectionPolygons stores integer points
		// (DocumentSelection.Transform truncates to IntPoint), so a genuinely
		// rotated rectangle carries up to ~1px error per corner — scale the
		// tolerance with edge length instead of demanding exact orthogonality.
		double dot = ex.X * ey.X + ex.Y * ey.Y;
		double tol = Math.Min (0.1, 4.0 * (1.0 / w + 1.0 / h));
		if (Math.Abs (dot) > tol)
			return false;

		double theta = Math.Atan2 (ex.Y, ex.X);
		double det = ex.X * ey.Y - ex.Y * ey.X; // ex × ey; <0 = mirrored

		Matrix m = CairoExtensions.CreateIdentityMatrix ();
		m.Translate (p0.X, p0.Y);
		m.Rotate (theta);
		if (det < 0)
			m.Scale (1, -1); // height edge is mirrored relative to a pure rotation

		refRect = new RectangleD (0, 0, w, h);
		orientation = m;
		return true;
	}

	private static bool IsCorner (HandlePoint? p)
		=> p is HandlePoint.UpperLeft or HandlePoint.UpperRight
			or HandlePoint.LowerLeft or HandlePoint.LowerRight;

	private static PointD OppositeCorner (RectangleD s, HandlePoint dragged) => dragged switch {
		HandlePoint.UpperLeft => new (s.Right, s.Bottom),
		HandlePoint.UpperRight => new (s.Left, s.Bottom),
		HandlePoint.LowerLeft => new (s.Right, s.Top),
		HandlePoint.LowerRight => new (s.Left, s.Top),
		_ => s.GetCenter (),
	};

	private static PointD GetCornerPoint (RectangleD s, HandlePoint dragged) => dragged switch {
		HandlePoint.UpperLeft => new (s.X, s.Y),
		HandlePoint.UpperRight => new (s.X + s.Width, s.Y),
		HandlePoint.LowerLeft => new (s.X, s.Y + s.Height),
		HandlePoint.LowerRight => new (s.X + s.Width, s.Y + s.Height),
		HandlePoint.Left => new (s.X, s.GetCenter ().Y),
		HandlePoint.Right => new (s.X + s.Width, s.GetCenter ().Y),
		HandlePoint.Up => new (s.GetCenter ().X, s.Y),
		HandlePoint.Down => new (s.GetCenter ().X, s.Y + s.Height),
		_ => s.GetCenter (),
	};

	/// <summary>
	/// Left/Right/Up/Down edge-handle drags all scale about an anchor on their own axis and,
	/// when <paramref name="keepAspect"/> is set, apply the same ratio to the other axis - one
	/// block transposed per axis/sign. <paramref name="horizontal"/> picks the dragged axis
	/// (Left/Right vs Up/Down); <paramref name="nearIsMin"/> picks which edge of
	/// <paramref name="sourceRect"/> is under the cursor (Left/Up = the min edge, Right/Down =
	/// the max edge).
	/// </summary>
	internal static Matrix ComputeEdgeScaleTransform (
		RectangleD sourceRect,
		PointD srcCenter,
		PointD mouse,
		bool horizontal,
		bool nearIsMin,
		bool fromCenter,
		bool keepAspect)
	{
		double near = horizontal
			? (nearIsMin ? sourceRect.X : sourceRect.X + sourceRect.Width)
			: (nearIsMin ? sourceRect.Y : sourceRect.Y + sourceRect.Height);
		double opposite = horizontal
			? (nearIsMin ? sourceRect.X + sourceRect.Width : sourceRect.X)
			: (nearIsMin ? sourceRect.Y + sourceRect.Height : sourceRect.Y);
		double center = horizontal ? srcCenter.X : srcCenter.Y;
		double mouseCoord = horizontal ? mouse.X : mouse.Y;

		double pivot = fromCenter ? center : opposite;
		double d0 = near - pivot;
		double d1 = mouseCoord - pivot;
		double primary = d0 != 0 ? d1 / d0 : 1;

		double axisExtent = horizontal ? sourceRect.Width : sourceRect.Height;
		double secondary = keepAspect && axisExtent > 0 ? Math.Abs (primary) : 1;

		PointD anchor = fromCenter
			? srcCenter
			: horizontal ? new PointD (opposite, srcCenter.Y) : new PointD (srcCenter.X, opposite);

		double sx = horizontal ? primary : secondary;
		double sy = horizontal ? secondary : primary;

		Matrix edgeTransform = CairoExtensions.CreateIdentityMatrix ();
		edgeTransform.Translate (anchor.X, anchor.Y);
		edgeTransform.Scale (sx, sy);
		edgeTransform.Translate (-anchor.X, -anchor.Y);
		return edgeTransform;
	}

	/// <summary>
	/// Load the rotate cursor texture with white halo + black outline preserved.
	/// Tries direct file load first (bypasses Gtk IconTheme recoloring), then
	/// Resources.GetIcon, then a manual Cairo fallback.
	/// </summary>
	internal static Gdk.Texture LoadRotateTexture ()
	{
		// Try direct file load from the installed icons directory — this preserves
		// the white halo + black stroke, unlike the symbolic IconTheme path.
		string data_dir = Pinta.Core.SystemManager.GetDataRootDirectory ();
		string[] candidates = [
			System.IO.Path.Combine (data_dir, "icons", "hicolor", "scalable", "actions", "rotate-handle.svg"),
			System.IO.Path.Combine (data_dir, "icons", "hicolor", "scalable", "actions", "rotate-handle-symbolic.svg"),
		];

		foreach (string path in candidates) {
			try {
				if (!File.Exists (path))
					continue;
				byte[] data = File.ReadAllBytes (path);
				GLib.Bytes bytes = GLib.Bytes.New (data);
				Gdk.Texture tex = Gdk.Texture.NewFromBytes (bytes);
				if (tex.Width > 0 && tex.Height > 0)
					return tex;
			} catch {
				// fall through
			}
		}

		// Fallback to themed icon (may be recolored but better than nothing).
		try {
			Gdk.Texture themed = Pinta.Resources.ResourceLoader.GetIcon (Pinta.Resources.Icons.RotateHandle, 28);
			if (themed.Width > 0)
				return themed;
		} catch {
		}

		// Final fallback: draw a high-contrast rotate icon via Cairo (white halo + black arc).
		return CreateFallbackRotateTexture (32);
	}

	private static Gdk.Texture CreateFallbackRotateTexture (int size)
	{
		using ImageSurface surf = new (Format.Argb32, size, size);
		using Context g = new (surf);
		g.Antialias = Antialias.Subpixel;
		g.LineCap = LineCap.Round;
		g.LineJoin = LineJoin.Round;

		// Clear transparent.
		g.Operator = Operator.Source;
		g.SetSourceRgba (0, 0, 0, 0);
		g.Paint ();
		g.Operator = Operator.Over;

		double cx = size / 2.0;
		double cy = size / 2.0;
		double radius = size * 0.35; // ~11 at 32px, similar to 8 at 24px
		double gap_deg = 35.0;
		double start_rad = gap_deg * Math.PI / 180.0;
		double end_rad = (360.0 - gap_deg) * Math.PI / 180.0;

		// Helper to stroke arc + arrowheads.
		void StrokeArc (double r, double width, double[] color)
		{
			g.LineWidth = width;
			g.SetSourceRgb (color[0], color[1], color[2]);
			// Arc (Cairo angles: 0 = +X, positive clockwise because Y down, but we use math)
			g.Arc (cx, cy, r, start_rad, end_rad);
			g.Stroke ();

			// Arrowheads — small V at each end, tangent to circle.
			// Compute end points
			double sx = cx + r * Math.Cos (start_rad);
			double sy = cy + r * Math.Sin (start_rad);
			double ex = cx + r * Math.Cos (end_rad);
			double ey = cy + r * Math.Sin (end_rad);

			// Tangent directions (perpendicular to radius). For a clockwise arc,
			// tangent at start is roughly upwards-ish, at end downwards-ish.
			// Approximate arrowhead by drawing two short lines.
			double arrow_len = size * 0.18;
			double arrow_angle = 25.0 * Math.PI / 180.0;

			// Start arrow — tangent roughly -90deg from radius
			double t1 = start_rad - Math.PI / 2;
			g.MoveTo (sx, sy);
			g.LineTo (sx + arrow_len * Math.Cos (t1 + arrow_angle), sy + arrow_len * Math.Sin (t1 + arrow_angle));
			g.MoveTo (sx, sy);
			g.LineTo (sx + arrow_len * Math.Cos (t1 - arrow_angle), sy + arrow_len * Math.Sin (t1 - arrow_angle));
			g.Stroke ();

			// End arrow — tangent +90deg
			double t2 = end_rad + Math.PI / 2;
			g.MoveTo (ex, ey);
			g.LineTo (ex + arrow_len * Math.Cos (t2 + arrow_angle), ey + arrow_len * Math.Sin (t2 + arrow_angle));
			g.MoveTo (ex, ey);
			g.LineTo (ex + arrow_len * Math.Cos (t2 - arrow_angle), ey + arrow_len * Math.Sin (t2 - arrow_angle));
			g.Stroke ();
		}

		// White halo
		StrokeArc (radius, size * 0.16, [1, 1, 1]);
		// Black arc
		StrokeArc (radius, size * 0.09, [0.1, 0.1, 0.1]);

		return Gdk.Texture.NewForPixbuf (Gdk.Functions.PixbufGetFromSurface (surf, 0, 0, surf.Width, surf.Height)!);
	}

	private bool IsActive
		=> is_dragging || is_rotating || is_scaling || is_handle_scaling;
}
