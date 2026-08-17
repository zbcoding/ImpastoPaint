//
// CloneStampTool.cs
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
using System.Collections.Generic;
using Gdk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class CloneStampTool : BaseBrushTool
{
	private bool painting;
	private PointI? origin = null;
	private PointI? offset = null;
	private PointI? last_point = null;

	private readonly SystemManager system_manager;
	private readonly IWorkspaceService workspace;
	private readonly BrushHandle handle;
	private bool move_origin_handle = true;

	// Transient popover shown below the brush circle when the user tries to paint
	// without an origin set. Parented to the canvas, like the healing-brush hint.
	private Gtk.Popover? hint_popover;
	private uint hint_timeout_id;

	public CloneStampTool (IServiceProvider services) : base (services)
	{
		system_manager = services.GetService<SystemManager> ();
		workspace = services.GetService<IWorkspaceService> ();

		handle = new BrushHandle (workspace);

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};
	}

	public override string Name => Translations.GetString ("Clone Stamp");
	public override string Icon => Pinta.Resources.Icons.ToolCloneStamp;
	// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
	public override string StatusBarText => Translations.GetString ("{0} + left click to set origin, left click to paint.", system_manager.CtrlLabel ());
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_L);
	public override int Priority => 47;
	protected override bool ShowAntialiasingButton => true;
	public override IEnumerable<IToolHandle> Handles => [handle];

	public override Cursor DefaultCursor {
		get {
			double scale = workspace.GetScale ();
			var icon = GdkExtensions.CreateIconWithShape ("Cursor.CloneStamp.png",
							CursorShape.Ellipse, scale, BrushWidth, 16, 26,
							out var iconOffsetX, out var iconOffsetY);
			return Gdk.Cursor.NewFromTexture (icon, iconOffsetX, iconOffsetY, null);
		}
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// We only do stuff with the left mouse button
		if (e.MouseButton != MouseButton.Left)
			return;

		// Ctrl click is set origin, regular click is begin drawing
		if (!e.IsControlPressed) {
			if (!origin.HasValue) {
				ShowOriginHint (e.Point);
				return;
			}

			painting = true;

			if (!offset.HasValue)
				offset = new (e.Point.X - origin.Value.X, e.Point.Y - origin.Value.Y);

			document.Layers.ToolLayer.Clear ();
			document.Layers.ToolLayer.Hidden = false;

			surface_modified = false;
			undo_surface = document.Layers.CurrentUserLayer.Surface.Clone ();
		} else {
			origin = e.Point;
			offset = null;
			HideOriginHint ();
			UpdateOriginHandle (document, origin.Value.X, origin.Value.Y, false);
		}
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (!offset.HasValue)
			return;

		var x = e.Point.X;
		var y = e.Point.Y;

		UpdateOriginHandle (document, x - offset.Value.X, y - offset.Value.Y, true);

		if (!painting)
			return;

		if (!last_point.HasValue) {
			last_point = e.Point;
			return;
		}

		using Cairo.Context g = document.CreateClippedToolContext ();
		g.Antialias = UseAntialiasing ? Cairo.Antialias.Subpixel : Cairo.Antialias.None;

		g.MoveTo (last_point.Value.X, last_point.Value.Y);
		g.LineTo (x, y);

		g.SetSourceSurface (document.Layers.CurrentUserLayer.Surface, offset.Value.X, offset.Value.Y);
		g.LineWidth = BrushWidth;
		g.LineCap = Cairo.LineCap.Round;

		g.Stroke ();

		int dirtyPadding = BrushWidth + 2;
		RectangleI dirtyRect = RectangleI.FromPoints (last_point.Value, e.Point).Inflated (dirtyPadding, dirtyPadding);

		last_point = e.Point;
		surface_modified = true;
		document.Workspace.Invalidate (dirtyRect);

		// See FoldRasterIntoComposite: a layer with effect nodes is painted from its accumulated
		// surface, so a live stroke on the raster alone would not appear until it was committed.
		if (ObjectOpacity.FoldRasterIntoComposite (PintaCore.Chrome, document.Layers.CurrentUserLayer))
			document.Workspace.Invalidate ();
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		painting = false;

		if (e.IsControlPressed)
			handle.Active = true;

		using Cairo.Context g = new (document.Layers.CurrentUserLayer.Surface);
		g.SetSourceSurface (document.Layers.ToolLayer.Surface, 0, 0);
		g.Paint ();

		base.OnMouseUp (document, e);

		// Note: the offset persists until the clone source is reselected.
		last_point = null;

		document.Layers.ToolLayer.Clear ();
		document.Layers.ToolLayer.Hidden = true;
		document.Workspace.Invalidate ();
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		// Note that this WON'T work if user presses control key and THEN selects the tool!
		if (e.Key.IsControlKey ()) {
			move_origin_handle = false;
			SetCursor (Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.CloneStampSetSource.png"), 16, 26, null));
		}

		return false;
	}

	protected override bool OnKeyUp (Document document, ToolKeyEventArgs e)
	{
		if (e.Key.IsControlKey ()) {
			move_origin_handle = true;
			SetCursor (DefaultCursor);
		}

		return false;
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		origin = null;
		handle.Active = false;
		DisposeHint ();
	}

	private void UpdateOriginHandle (Document document, int x, int y, bool move_event)
	{
		if (move_origin_handle || (!move_event))
			handle.CanvasPosition = new (x, y);
		handle.BrushWidth = BrushWidth;
		document.Workspace.Invalidate (handle.InvalidateRect);
	}

	// Popover reminder shown just below the brush circle when the user clicks to paint
	// with no origin set. Names the modifier (Ctrl) used to set the origin. Auto-dismisses.
	private void ShowOriginHint (PointI canvasPoint)
	{
		if (!workspace.HasOpenDocuments || !ShouldShowPopoverHint ())
			return;

		var ws = workspace.ActiveWorkspace;
		Gtk.Widget canvas = ws.Canvas;

		double radius = Math.Max (1, BrushWidth / 2.0);
		PointD belowCircle = ws.CanvasPointToView (new PointD (canvasPoint.X, canvasPoint.Y + radius));

		if (hint_popover is null) {
			hint_popover = Gtk.Popover.New ();
			hint_popover.Autohide = false;
			hint_popover.Position = Gtk.PositionType.Bottom;
			hint_popover.SetParent (canvas);
			var label = Gtk.Label.New (Translations.GetString (
				"Hold {0} and click to set an origin point first.", system_manager.CtrlLabel ()));
			label.Wrap = true;
			label.MaxWidthChars = 40;
			label.MarginTop = label.MarginBottom = 4;
			label.MarginStart = label.MarginEnd = 8;
			hint_popover.SetChild (label);
		} else if (hint_popover.GetParent () != canvas) {
			hint_popover.Unparent ();
			hint_popover.SetParent (canvas);
		}

		hint_popover.PointingTo = new Gdk.Rectangle {
			X = (int) Math.Clamp (belowCircle.X, 0, 100000),
			Y = (int) Math.Clamp (belowCircle.Y, 0, 100000),
			Width = 1,
			Height = 1,
		};
		hint_popover.Popup ();

		if (hint_timeout_id != 0)
			GLib.Functions.SourceRemove (hint_timeout_id);
		hint_timeout_id = GLib.Functions.TimeoutAdd (0, 2500, () => {
			hint_timeout_id = 0;
			hint_popover?.Popdown ();
			return false;
		});
	}

	private static bool ShouldShowPopoverHint ()
		=> PintaCore.Settings.GetSetting (SettingNames.POPOVER_HINT_MODE, (int) PopoverHintMode.All) != (int) PopoverHintMode.None;

	private void HideOriginHint ()
	{
		if (hint_timeout_id != 0) {
			GLib.Functions.SourceRemove (hint_timeout_id);
			hint_timeout_id = 0;
		}
		hint_popover?.Popdown ();
	}

	private void DisposeHint ()
	{
		HideOriginHint ();
		if (hint_popover is not null) {
			hint_popover.Unparent ();
			hint_popover = null;
		}
	}
}
