//
// MoveSelectedTool.cs
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
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MoveSelectedTool : BaseTransformTool
{
	private MovePixelsHistoryItem? hist;
	// Set when this drag lifted the pixels onto a new layer; recorded with hist as one undo step.
	private AddLayerHistoryItem? added_layer_hist;
	private DocumentSelection? original_selection;

	// Set when the user declined the rasterize prompt below. The gesture has already begun by then
	// (BaseTransformTool sets is_dragging before calling OnStartTransform), so the move is neutered
	// here rather than unwound there.
	private bool move_declined;

	// Whether a real selection was active before the drag started, captured in OnStartTransform.
	// When false, "no selection" was standing in for "move the whole layer" (see the fallback
	// below) and Selection/PreviousSelection must come out of the drag exactly as they went in -
	// otherwise the untouched, canvas-sized placeholder becomes a real, shifted selection that
	// silently clips every later paint tool to wherever the drag happened to end.
	private bool had_real_selection;
	private readonly Matrix original_transform = CairoExtensions.CreateIdentityMatrix ();

	// Skew, Perspective and 3D reshape the selection through their own grips; Move keeps the
	// inherited resize grips. Body drags, rotate and nudge behave the same in every mode.
	private TransformMode mode;
	private readonly SelectionWarp warp = new ();
	private readonly QuadHandle quad_handle;
	// The cursor last set while hovering the warp grips; null once something else set one.
	private Gdk.Cursor? hover_cursor;
	private Gdk.Cursor? move_cursor;
	private PointD last_hover_point;
	// ponytail: tracked from key presses, so a release while another window has focus leaves it
	// held until the key is pressed again. Upgrade: read the keyboard's state on each drag event.
	private bool depth_key_held;
	// A 3D turn or tilt from the keyboard is one undo step, from the first press to the release.
	private bool tilting_by_key;

	private const double DEFAULT_FIELD_OF_VIEW = 60;
	private const double MIN_FIELD_OF_VIEW = 10;
	private const double MAX_FIELD_OF_VIEW = 150;
	private const double KEY_TILT_DEGREES = 5;

	private static readonly (ToolBindingDescriptor Binding, double Yaw, double Pitch)[] key_tilts = [
		(KeyboardShortcutManager.Transform3DTurnLeft, 1, 0),
		(KeyboardShortcutManager.Transform3DTurnRight, -1, 0),
		(KeyboardShortcutManager.Transform3DTiltUp, 0, 1),
		(KeyboardShortcutManager.Transform3DTiltDown, 0, -1),
	];

	private readonly SystemManager system_manager;
	private readonly IWorkspaceService workspace;
	public MoveSelectedTool (IServiceProvider services) : base (services)
	{
		system_manager = services.GetService<SystemManager> ();
		workspace = services.GetService<IWorkspaceService> ();
		quad_handle = new QuadHandle (workspace);
		mode = ModeFromIndex (Settings.GetSetting (SettingNames.MOVE_SELECTED_MODE, 0));
		warp.FieldOfView = Math.Clamp (
			Settings.GetSetting (SettingNames.MOVE_SELECTED_FIELD_OF_VIEW, DEFAULT_FIELD_OF_VIEW),
			MIN_FIELD_OF_VIEW,
			MAX_FIELD_OF_VIEW);

		// Undo, redo, nudges and plain moves all replace the selection; a warp session built on
		// the old one would resample from stale pixels, and the grips must follow the new outline.
		workspace.SelectionChanged += (_, _) => {
			if (!warp.IsApplying)
				warp.Forget ();
			// Other tools change the selection on every mouse move; redrawing the canvas for grips
			// that are not shown made them recomposite the whole image each time.
			if (IsActiveTool ())
				RefreshQuadHandle ();
		};

		// The tilt grips sit a fixed number of screen pixels outside the edges.
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ())
				RefreshQuadHandle ();
		};
	}

	public override string Name => Translations.GetString ("Move Selected Pixels");
	public override string Icon => Pinta.Resources.Icons.ToolMove;
	// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
	public override string StatusBarText => Translations.GetString (
		"Left click and drag the selection to move selected content." +
		"\nHold {0} to scale instead of move." +
		"\nRight click and drag the selection to rotate selected content." +
		"\nHold Shift to rotate in steps." +
		"\nUse arrow keys to move selected content by a single pixel.",
		system_manager.CtrlLabel ())
		// Translators: Skew and Perspective are the Mode choices on this tool's toolbar.
		+ "\n" + Translations.GetString ("In Skew or Perspective mode, drag the grips on the selection's edges or corners.")
		+ "\n" + ThreeDHelp ();

	// Translators: {0} is the key held to push a corner in depth; {1} to {4} turn and tilt the selection in steps. 3D is a Mode choice on this tool's toolbar.
	private static string ThreeDHelp ()
		=> Translations.GetString (
			"In 3D mode, drag a corner to move it, or hold {0} and drag it up or down to push it away or pull it closer. Drag a grip outside an edge to turn or tilt the selection, holding Shift for 15° steps, or press {1}, {2}, {3} or {4}.",
			BindingLabel (KeyboardShortcutManager.Transform3DPushDepth),
			BindingLabel (KeyboardShortcutManager.Transform3DTurnLeft),
			BindingLabel (KeyboardShortcutManager.Transform3DTurnRight),
			BindingLabel (KeyboardShortcutManager.Transform3DTiltUp),
			BindingLabel (KeyboardShortcutManager.Transform3DTiltDown));

	private static string BindingLabel (ToolBindingDescriptor binding)
		=> PintaCore.Shortcuts.GetToolBinding (binding).ToLabel ();

	// Rendered at 2x the default 16px and with a centered hotspot so the enlarged
	// four-way cross still points at the pixel under the cursor.
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.ToolMoveCursor, 32), 16, 16, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_M);
	public override int Priority => 5;

	public override bool WritesToCurrentLayer
		=> true;

	// OnStartTransform below runs the very same rasterize offer over the whole selection it is about
	// to lift, which is a superset of the down point ToolManager's guard probes. Leaving the guard on
	// asked twice for one drag: once for whatever object sat under the cursor, then again for every
	// object the selection reaches.
	public override bool HandlesLiveObjectsItself
		=> true;

	protected override RectangleD GetSourceRectangle (Document document)
		=> document.Selection.GetBounds ();

	public override IEnumerable<IToolHandle> Handles
		=> mode == TransformMode.Move ? base.Handles : [quad_handle];

	protected override bool ResizeGripsEnabled
		=> mode == TransformMode.Move;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (ModeLabel);
		tb.Append (ModeDropDown);
		tb.Append (FieldOfViewLabel);
		tb.Append (FieldOfViewSpinButton);
		ShowFieldOfView ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) mode);
		settings.PutSetting (SettingNames.MOVE_SELECTED_FIELD_OF_VIEW, warp.FieldOfView);
	}

	private Gtk.Label? mode_label;
	private ToolBarDropDownButton? mode_button;
	private Gtk.Label? field_of_view_label;
	private Gtk.SpinButton? field_of_view;

	private Gtk.Label ModeLabel => mode_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Mode")));

	private ToolBarDropDownButton ModeDropDown {
		get {
			if (mode_button is not null)
				return mode_button;

			mode_button = ToolBarDropDownButton.New (true);
			mode_button.AddItem (Translations.GetString ("Move"), Pinta.Resources.Icons.ToolMove, TransformMode.Move,
				Translations.GetString ("Drag the grips to resize the selected pixels."));
			// Translators: Move Selected Pixels mode that slants the pixels by sliding one edge along itself.
			mode_button.AddItem (Translations.GetString ("Skew"), Pinta.Resources.Icons.TransformSkew, TransformMode.Skew,
				Translations.GetString ("Drag an edge's grip to slide that edge along itself, slanting the selected pixels."));
			// Translators: Move Selected Pixels mode where each corner moves on its own, to fit the pixels onto an angled surface.
			mode_button.AddItem (Translations.GetString ("Perspective"), Pinta.Resources.Icons.TransformPerspective, TransformMode.Perspective,
				Translations.GetString ("Drag any corner on its own to fit the selected pixels onto an angled surface."));
			// Translators: Move Selected Pixels mode that moves the pixels' corners in 3D space, viewed in perspective.
			mode_button.AddItem (Translations.GetString ("3D"), Pinta.Resources.Icons.Transform3D, TransformMode.ThreeD,
				Translations.GetString ("Move the corners, or turn and tilt the selected pixels, in 3D space."));
			// The button's own tooltip names the keys 3D mode uses, so it follows the user's bindings.
			mode_button.UseSelectedItemTooltip = false;
			mode_button.SelectedIndex = (int) mode;
			mode_button.SelectedItemChanged += (_, _) => {
				mode = (TransformMode) mode_button.SelectedItem.Tag!;
				hover_cursor = null;
				ShowFieldOfView ();
				UpdateModeTooltip ();
				RefreshQuadHandle ();
			};
			UpdateModeTooltip ();
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => UpdateModeTooltip ();

			return mode_button;
		}
	}

	private void UpdateModeTooltip ()
	{
		if (mode_button is null)
			return;

		ToolBarItem item = mode_button.SelectedItem;
		string help = mode == TransformMode.ThreeD ? ThreeDHelp () : item.Tooltip ?? string.Empty;
		mode_button.TooltipText = $"{item.Text}\n{help}";
	}

	private Gtk.Label FieldOfViewLabel
		=> field_of_view_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Field of view")));

	private Gtk.SpinButton FieldOfViewSpinButton {
		get {
			if (field_of_view is not null)
				return field_of_view;

			field_of_view = GtkExtensions.CreateToolBarSpinButton (MIN_FIELD_OF_VIEW, MAX_FIELD_OF_VIEW, 5, warp.FieldOfView);
			field_of_view.TooltipText = Translations.GetString (
				"How wide 3D mode's view is, in degrees. A wide view makes depth look stronger; a narrow one flattens it.");
			field_of_view.OnValueChanged += (_, _) => ChangeFieldOfView (field_of_view.Value);
			return field_of_view;
		}
	}

	private void ShowFieldOfView ()
	{
		bool shown = mode == TransformMode.ThreeD;
		FieldOfViewLabel.Visible = shown;
		FieldOfViewSpinButton.Visible = shown;
	}

	// The corners keep their places in 3D, so a new field of view reshapes what the camera shows
	// of them. That reshape is its own undo step.
	private void ChangeFieldOfView (double degrees)
	{
		warp.FieldOfView = degrees;
		if (!workspace.HasOpenDocuments)
			return;

		Document document = workspace.ActiveDocument;
		if (!warp.HasDepth (document) || !BeginWarpWithoutGrip (document))
			return;

		warp.Reproject (document);
		FinishWarp (document);
	}

	private static TransformMode ModeFromIndex (int index)
		=> Enum.IsDefined ((TransformMode) index) ? (TransformMode) index : TransformMode.Move;

	private void RefreshQuadHandle ()
	{
		PointD[]? quad = null;
		if (mode != TransformMode.Move && IsActiveTool () && workspace.HasOpenDocuments)
			quad = warp.CurrentQuad (workspace.ActiveDocument);

		quad_handle.Active = quad is not null;
		if (quad is not null)
			quad_handle.Update (quad, mode);

		if (workspace.HasOpenDocuments)
			workspace.Invalidate ();
	}

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);
		RefreshQuadHandle ();
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// A click lands a keyboard turn whose key release went astray (say, to a dialog the lift
		// opened), so the tool never stays stuck in it.
		if (tilting_by_key)
			FinishWarp (document);

		// A second button during a grip drag must not start another gesture.
		if (warp.IsDragging)
			return;

		if (!TryBeginWarp (document, e))
			base.OnMouseDown (document, e);
	}

	/// <summary>
	/// Starts a skew, perspective or 3D drag when a left click lands on one of their grips. Lifts
	/// the pixels first, exactly as a move would, so the drag reshapes floating content.
	/// </summary>
	private bool TryBeginWarp (Document document, ToolMouseEventArgs e)
	{
		if (mode == TransformMode.Move || IsActive)
			return false;
		if (e.MouseButton != MouseButton.Left || e.IsAltPressed)
			return false;

		int grip = quad_handle.GripAt (e.WindowPoint);
		if (grip < 0)
			return false;

		OnStartTransform (document);
		if (move_declined) {
			move_declined = false;
			return true;
		}

		warp.Begin (document, mode, grip, e.PointDouble);
		quad_handle.Select (grip);
		return true;
	}

	/// <summary>Starts a 3D change that no grip drives: a keyboard turn or a new field of view.</summary>
	private bool BeginWarpWithoutGrip (Document document)
	{
		if (IsActive || warp.IsDragging || !document.Selection.Visible)
			return false;

		OnStartTransform (document);
		if (move_declined) {
			move_declined = false;
			return false;
		}

		warp.Begin (document, TransformMode.ThreeD, -1, default);
		return true;
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (warp.IsDragging) {
			warp.Update (document, e.PointDouble, depth_key_held, e.IsShiftPressed);
			return;
		}

		base.OnMouseMove (document, e);
		UpdateGripCursor (e.WindowPoint, e.IsAltPressed);
	}

	// Only on a change: DefaultCursor builds a fresh texture every time it is read.
	private void UpdateGripCursor (PointD windowPoint, bool altPressed)
	{
		last_hover_point = windowPoint;
		if (mode == TransformMode.Move || IsActive || warp.IsDragging)
			return;

		Gdk.Cursor cursor = GripCursor (windowPoint, altPressed);
		if (cursor == hover_cursor)
			return;

		hover_cursor = cursor;
		SetCursor (cursor);
	}

	private Gdk.Cursor GripCursor (PointD windowPoint, bool altPressed)
	{
		// Alt turns any left press into a rotate, so while it is held the grip under the cursor is
		// not what a click would do.
		if (altPressed && quad_handle.Active)
			return RotateCursor;

		int grip = quad_handle.GripAt (windowPoint);
		if (grip < 0)
			return move_cursor ??= DefaultCursor;

		bool pushesDepth = depth_key_held && mode == TransformMode.ThreeD && grip < SelectionWarp.FIRST_TILT_GRIP;
		return pushesDepth ? QuadHandle.DepthCursor : quad_handle.CursorOf (grip);
	}

	private static bool IsDepthKey (Gdk.Key key)
	{
		KeyGesture binding = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.Transform3DPushDepth);
		return binding.IsValid && key.ToUpper ().Value == binding.Key.ToUpper ().Value;
	}

	private static (double Yaw, double Pitch)? KeyTilt (KeyGesture gesture)
	{
		double step = KEY_TILT_DEGREES * Math.PI / 180;
		foreach ((ToolBindingDescriptor binding, double yaw, double pitch) in key_tilts) {
			if (PintaCore.Shortcuts.GetToolBinding (binding) == gesture)
				return (yaw * step, pitch * step);
		}

		return null;
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (warp.IsDragging)
			FinishWarp (document);
		else
			base.OnMouseUp (document, e);
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (IsDepthKey (e.Key)) {
			depth_key_held = true;
			UpdateGripCursor (last_hover_point, e.IsAltPressed);
			if (mode == TransformMode.ThreeD)
				return true;
		}

		if (mode == TransformMode.ThreeD && KeyTilt (e.Gesture) is (double yaw, double pitch)) {
			TiltByKey (document, yaw, pitch);
			return true;
		}

		// A mid-drag key would nudge the pixels out from under the warp.
		return warp.IsDragging || base.OnKeyDown (document, e);
	}

	protected override bool OnKeyUp (Document document, ToolKeyEventArgs e)
	{
		if (IsDepthKey (e.Key)) {
			depth_key_held = false;
			UpdateGripCursor (last_hover_point, e.IsAltPressed);
		}

		// Any release ends a keyboard turn, as it ends a nudge: the modifier may be let go first.
		if (tilting_by_key) {
			FinishWarp (document);
			return true;
		}

		return base.OnKeyUp (document, e);
	}

	private void TiltByKey (Document document, double yaw, double pitch)
	{
		if (!tilting_by_key) {
			if (!BeginWarpWithoutGrip (document))
				return;
			tilting_by_key = true;
		}

		warp.Tilt (document, yaw, pitch);
	}

	private void FinishWarp (Document document)
	{
		string text = mode switch {
			TransformMode.Skew => Translations.GetString ("Skew"),
			TransformMode.Perspective => Translations.GetString ("Perspective"),
			_ => Translations.GetString ("3D"),
		};
		tilting_by_key = false;

		BaseHistoryItem? pixels = warp.End (document, Icon, text);
		quad_handle.Select (-1);

		if (hist is not null)
			document.History.PushNewItem (GroupWarp (text, hist, pixels));

		ClearGestureState ();
	}

	// One undo step for the whole drag: the lift (with any layer it added) and the resampled
	// pixels. A click that never moved a grip still lifted the pixels, so it records the lift alone.
	private BaseHistoryItem GroupWarp (string text, MovePixelsHistoryItem move, BaseHistoryItem? pixels)
	{
		if (pixels is null)
			return added_layer_hist is null ? move : GroupWithAddedLayer (move, added_layer_hist);

		CompoundHistoryItem group = new (Icon, text);
		if (added_layer_hist is not null)
			group.Push (added_layer_hist);
		group.Push (move);
		group.Push (pixels);
		return group;
	}

	protected override void OnStartTransform (Document document)
	{
		base.OnStartTransform (document);

		had_real_selection = document.Selection.Visible;

		// If there is no selection, select the whole image.
		if (document.Selection.SelectionPolygons.Count == 0) {
			RectangleD imageBounds = new (0, 0, document.ImageSize.Width, document.ImageSize.Height);
			document.Selection.CreateRectangleSelection (imageBounds);
		}

		// A floating selection (a paste, or pixels an earlier drag already lifted) moves on the
		// selection layer alone: the base raster and the objects over it are untouched until the
		// selection is finished, so nothing needs baking.
		bool lifts = !document.Layers.ShowSelectionLayer;

		// The lift below reads the layer's base raster and clears the moved region from it. Effect
		// nodes, shapes and text are not in that raster — the canvas shows them through the layer's
		// composite — so without this the drag carries un-effected pixels away, leaves the node
		// applying over the hole it left, and moves nothing at all when the selection covered a text
		// or shape object. Bake what the selection reaches, or send the pixels to a new layer, then lift.
		SelectionLiftChoice choice = lifts
			? ObjectRasterizer.PrepareForSelectionLift (
				document,
				PintaCore.Workspace,
				PintaCore.Chrome,
				document.Layers.CurrentUserLayer,
				document.Selection)
			: SelectionLiftChoice.SameLayer;

		move_declined = choice == SelectionLiftChoice.Cancel;
		if (move_declined)
			return;

		original_selection = document.Selection.Clone ();
		original_transform.InitMatrix (document.Layers.SelectionLayer.Transform);

		hist = new MovePixelsHistoryItem (Icon, Name, document);
		hist.TakeSnapshot (lifts);

		if (lifts) {
			UserLayer source = document.Layers.CurrentUserLayer;

			// Copy the selection to the temp layer
			document.Layers.CreateSelectionLayer ();
			document.Layers.ShowSelectionLayer = true;

			using Context selection_ctx = new (document.Layers.SelectionLayer.Surface);
			selection_ctx.AppendPath (document.Selection.SelectionPath);
			selection_ctx.FillRule = FillRule.EvenOdd;
			selection_ctx.SetSourceSurface (source.Surface, 0, 0);
			selection_ctx.Clip ();
			selection_ctx.Paint ();

			// Clears the lifted region from the raster and folds the hole into the layer's composite;
			// without the fold, a drag that missed every effect node (so nothing was baked above)
			// showed no movement until the composite was rebuilt on mouse release.
			ObjectOpacity.LiftSelectionFromRaster (
				PintaCore.Chrome,
				source,
				document.Selection);

			if (choice == SelectionLiftChoice.NewLayer)
				AddLayerForLiftedPixels (document, source);

			// Use same BlendMode, Opacity and Visibility for SelectionLayer
			document.Layers.SelectionLayer.BlendMode = document.Layers.CurrentUserLayer.BlendMode;
			document.Layers.SelectionLayer.Opacity = document.Layers.CurrentUserLayer.Opacity;
			document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;
		}

		document.Workspace.Invalidate ();
	}

	// Finishing a selection draws it into the current layer, so making the new layer current is
	// what lands the lifted pixels on it. It copies the source's blend mode and opacity so the
	// pixels look the same after the move as before it.
	private void AddLayerForLiftedPixels (Document document, UserLayer source)
	{
		UserLayer target = document.Layers.AddNewLayer (string.Empty);
		target.BlendMode = source.BlendMode;
		target.Opacity = source.Opacity;

		added_layer_hist = new AddLayerHistoryItem (
			Pinta.Resources.Icons.LayerNew,
			Translations.GetString ("Add New Layer"),
			document.Layers.IndexOf (target));
	}

	protected override void OnUpdateTransform (Document document, Matrix transform)
	{
		base.OnUpdateTransform (document, transform);

		if (move_declined)
			return;

		// Whole-layer fallback move: leave the canvas-sized placeholder selection exactly as it
		// was: moving without a real selection must not fabricate one.
		if (had_real_selection) {
			document.Selection = original_selection!.Transform (transform); // NRT - Set in OnStartTransform
			document.Selection.Visible = true;
		}

		document.Layers.SelectionLayer.Transform.InitMatrix (original_transform);
		document.Layers.SelectionLayer.Transform.Multiply (transform);

		document.Workspace.Invalidate ();
	}

	protected override void OnFinishTransform (Document document, Matrix transform)
	{
		base.OnFinishTransform (document, transform);
		// The base just restored the default cursor; the next hover must set the grip's again.
		hover_cursor = null;

		if (move_declined) {
			move_declined = false;
			return;
		}

		if (had_real_selection) {
			// Also transform the base selection used for the various select modes.
			var prev_selection = document.PreviousSelection;
			document.PreviousSelection = prev_selection.Transform (transform);
		}

		if (hist != null)
			document.History.PushNewItem (added_layer_hist is null ? hist : GroupWithAddedLayer (hist, added_layer_hist));

		ClearGestureState ();
	}

	private void ClearGestureState ()
	{
		hist = null;
		added_layer_hist = null;
		original_selection = null;
		original_transform.InitIdentity ();
	}

	// Redo re-adds the layer before the lift floats the pixels again: re-adding switches layers,
	// which commits the current tool, and committing a move while the pixels float would finish
	// them onto the source. Undo runs children in reverse, so the lift is put back on the source
	// layer (whose index the new layer, inserted above it, never shifted) and then the layer goes.
	private CompoundHistoryItem GroupWithAddedLayer (MovePixelsHistoryItem move, AddLayerHistoryItem addLayer)
	{
		CompoundHistoryItem group = new (Icon, Translations.GetString ("Move to New Layer"));
		group.Push (addLayer);
		group.Push (move);
		return group;
	}

	protected override void OnCommit (Document? document)
	{
		// Committing mid-warp lands the warp first, so its undo step comes before the commit's.
		if (warp.IsDragging && document is not null)
			FinishWarp (document);

		document?.FinishSelection ();
		warp.Forget ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		// Same reason as the base's mid-drag reset: a tool switch during a drag must still land it.
		if (warp.IsDragging && document is not null)
			FinishWarp (document);

		base.OnDeactivated (document, newTool);

		document?.FinishSelection ();
		warp.Forget ();
		quad_handle.Active = false;
		hover_cursor = null;
		depth_key_held = false;
	}
}
