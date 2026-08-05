//
// BaseEditEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
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
using System.Collections.ObjectModel;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

//The EditEngine was created for tools that wish to utilize any of the control point, line/curve, hover point (reacting to the mouse),
//and etc. code that was originally used in the LineCurveTool for editability. If a class wishes to use it, it should create and instantiate
//a protected instance of the EditEngine inside the class and then utilize it in a similar fashion to any of the editable tools.
public abstract class BaseEditEngine
{
	public enum ShapeTypes
	{
		OpenLineCurveSeries,
		ClosedLineCurveSeries,
		Ellipse,
		RoundedLineSeries,
		Triangle,
	}

	public static Dictionary<ShapeTypes, ShapeTool> CorrespondingTools { get; } = [];

	protected abstract string ShapeName { get; }

	// Base for a shape's default sub-row name; defaults to ShapeName but tools whose ShapeName is a
	// generic geometry label (e.g. the rectangle tool's "Closed Curve Shape") can override it.
	protected virtual string DefaultObjectName => ShapeName;

	// Per-session, per-type running counter for default shape names ("Ellipse 1", "Ellipse 2", ...).
	// ponytail: monotonic and never reused, so deleting a shape leaves a numbering gap; fine for a
	// default label the user can rename.
	private static readonly Dictionary<string, int> shape_name_counters = [];

	private static string NextDefaultShapeName (string baseName)
	{
		shape_name_counters.TryGetValue (baseName, out int n);
		shape_name_counters[baseName] = ++n;
		return $"{baseName} {n}";
	}

	protected readonly ShapeTool owner;

	protected bool is_drawing = false;

	protected RectangleD? last_dirty = null;

	protected PointD shape_origin;
	protected PointD current_point;
	protected bool triangle_switch_down;

	public static Color OutlineColor {
		get => PintaCore.Palette.PrimaryColor;
		set => PintaCore.Palette.PrimaryColor = value;
	}

	public static Color FillColor {
		get => PintaCore.Palette.SecondaryColor;
		set => PintaCore.Palette.SecondaryColor = value;
	}

	// NRT - Created by HandleBuildToolBar
	protected ToolBarDropDownButton shape_type_button = null!;
	protected Gtk.Label shape_type_label = null!;

	protected Gtk.Label fill_label = null!;
	protected ToolBarDropDownButton fill_button = null!;
	protected Gtk.Separator fill_sep = null!;

	protected Gtk.SpinButton outline_width = null!;
	protected Gtk.Label outline_width_label = null!;
	protected Gtk.Separator outline_width_sep = null!;

	protected DashPatternBox dash_pattern_box = new ();
	private string prev_dash_pattern = "-";
	private int prev_dash_spacing = 1;

	protected ToolBarDropDownButton curved_segments_button = null!;
	protected Gtk.Separator curved_segments_sep = null!;

	protected ToolBarDropDownButton rasterize_mode_button = null!;
	protected Gtk.Label rasterize_mode_label = null!;

	// Shared across all shape tools and remembered while the app is open.
	// When off, clicking a shape's line no longer inserts nodes for curved segments.
	private static bool curved_segments_enabled = true;

	// Object (false) keeps shapes live/editable; Rasterized (true) bakes them into the layer's
	// base raster on commit (Enter / tool switch), like classic paint tools. Shared across shape tools.
	private static bool rasterize_shapes = false;

	private bool prev_antialiasing = true;

	// Reads the current gap multiplier from the spacing dropdown (1 if unset).
	private int DashSpacingSetting =>
		int.TryParse (dash_pattern_box.SpacingComboBox?.ComboBox.GetActiveText (), out int s) && s > 0 ? s : 1;

	public int BrushWidth {
		get => outline_width?.GetValueAsInt () ?? BaseTool.DEFAULT_BRUSH_WIDTH;
		set {
			if (outline_width is not null)
				outline_width.Value = value;
		}
	}

	private void UpdateOutlineWidthTooltip ()
	{
		KeyGesture decrease = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.BrushDecreaseWidth);
		KeyGesture increase = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.BrushIncreaseWidth);

		outline_width.TooltipText = Translations.GetString ("Change outline width.") + "\n"
			+ "\n" + Translations.GetString ("Shortcut keys:")
			+ "\n" + Translations.GetString ("Press {0} to decrease outline width", decrease.ToLabel ())
			+ "\n" + Translations.GetString ("Press {0} to increase outline width", increase.ToLabel ());
	}

	private int prev_outline_width = BaseTool.DEFAULT_BRUSH_WIDTH;

	private bool StrokeShape {
		get {
			if (fill_button.SelectedItem?.Tag is int value)
				return value % 2 == 0;

			return true;
		}
	}

	private bool FillShape {
		get {
			if (fill_button.SelectedItem?.Tag is int value)
				return value >= 1;

			return false;
		}
	}

	private int CurrentFillStyle
		=> fill_button?.SelectedItem?.Tag is int value ? value : 0;

	private ShapeTypes ShapeType {
		get {
			if (shape_type_button.SelectedItem?.Tag is int value)
				return (ShapeTypes) value;

			return 0;
		}
	}

	public const double ShapeClickStartingRange = 10d;
	public const double DefaultEndPointTension = 0d;
	public const double DefaultMidPointTension = 1d / 3d;

	public int SelectedPointIndex;
	public int SelectedShapeIndex;

	protected int prev_selected_shape_index;

	/// <summary>
	/// The selected ControlPoint.
	/// </summary>
	public ControlPoint? SelectedPoint {
		get {
			ShapeEngine? selEngine = SelectedShapeEngine;

			if (selEngine != null && selEngine.ControlPoints.Count > SelectedPointIndex)
				return selEngine.ControlPoints[SelectedPointIndex];
			else
				return null;
		}
	}

	/// <summary>
	/// The active shape's ShapeEngine. A point does not have to be selected here, only a shape. This can be null.
	/// </summary>
	public ShapeEngine? ActiveShapeEngine {
		get {
			if (SelectedShapeIndex > -1 && SEngines.Count > SelectedShapeIndex)
				return SEngines[SelectedShapeIndex];
			else
				return null;
		}
	}

	/// <summary>
	/// The selected shape's ShapeEngine. This requires that a point in the shape be selected and should be used in most cases. This can be null.
	/// </summary>
	public ShapeEngine? SelectedShapeEngine => (SelectedPointIndex > -1) ? ActiveShapeEngine : null;

	/// <summary>
	/// Display the handles for all active shape engines' control points, along with the hover position
	/// </summary>
	public IEnumerable<IToolHandle> Handles =>
		SEngines.SelectMany (engine => engine.ControlPointHandles).Append (hover_handle);

	private readonly MoveHandle hover_handle;

	private readonly Gdk.Cursor grab_cursor = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Grab);
	private readonly Gdk.Cursor move_cursor = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Move);

	protected bool changing_tension = false;
	protected PointD last_mouse_pos = new (0d, 0d);

	private bool moving_whole_shape = false;
	private PointD last_shape_move_point;

	//Helps to keep track of the first modification on a shape after the mouse is clicked, to prevent unnecessary history items.
	protected bool clicked_without_modifying = false;

	//Stores the editable shape data.
	public static Collection<ShapeEngine> SEngines = [];

	static BaseEditEngine ()
	{
		// Fulfill "select this shape object" requests from the layers dock (Pinta.Gui.Widgets).
		LayerObjectSelection.ShapeSelectRequested += HandleShapeSelectRequested;
	}

	// Selects the shape at shapeIndex on the given layer and shows its control points, as if the
	// user had clicked into it on the canvas. Called via the Core bridge when an object sub-row is
	// clicked. Order matters: make the layer current, then activate the shape's tool (which
	// (re)populates SEngines from that layer), then select and redraw.
	private static void HandleShapeSelectRequested (UserLayer layer, int shapeIndex)
	{
		if (!PintaCore.Workspace.HasOpenDocuments)
			return;

		var layers = PintaCore.Workspace.ActiveDocument.Layers;
		int layerIndex = layers.IndexOf (layer);
		if (layerIndex < 0 || shapeIndex < 0 || shapeIndex >= layer.ShapeObjects.Count)
			return;

		ShapeObject target = layer.ShapeObjects[shapeIndex];
		if (target.Rasterized)
			return; // baked into the layer's pixels; there is no editable engine to select

		if (layers.CurrentUserLayerIndex != layerIndex)
			layers.SetCurrentUserLayer (layerIndex);

		// SEngines skips rasterized objects, so map the ShapeObjects index to the engine index.
		int engineIndex = 0;
		for (int i = 0; i < shapeIndex; ++i)
			if (!layer.ShapeObjects[i].Rasterized)
				++engineIndex;

		// ShapeObjectType and ShapeTypes share ordering (they are cast to each other elsewhere).
		ShapeTypes shapeType = (ShapeTypes) target.ShapeType;
		ActivateCorrespondingTool (shapeType, true);

		BaseEditEngine? engine = GetCorrespondingTool (shapeType)?.EditEngine;
		if (engine is null || engineIndex >= SEngines.Count)
			return;

		engine.SelectedShapeIndex = engineIndex;
		engine.SelectedPointIndex = 0; // a valid point index makes the shape "selected" and shows its control dots
		engine.DrawActiveShape (true, false, true, false, false);

		// Show control points only for the selected shape. Reset the flag first so the hide re-runs
		// even if another shape was already isolated (the guard would otherwise no-op).
		engine.other_shapes_points_hidden = false;
		engine.SetOtherShapesControlPointsHidden (true);
	}

	#region ToolbarEventHandlers

	protected virtual void BrushMinusButtonClickedEvent (object? o, EventArgs args)
	{
		BrushWidth--;

		//No need to store previous settings or redraw, as this is done in the Changed event handler.
	}

	protected virtual void BrushPlusButtonClickedEvent (object? o, EventArgs args)
	{
		BrushWidth++;

		//No need to store previous settings or redraw, as this is done in the Changed event handler.
	}

	protected void Palette_PrimaryColorChanged (object? sender, EventArgs e)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;
		if (activeEngine == null) return;
		activeEngine.OutlineColor = OutlineColor;
		DrawActiveShape (false, false, true, false, false);
	}

	protected void Palette_SecondaryColorChanged (object? sender, EventArgs e)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;
		if (activeEngine == null) return;
		activeEngine.FillColor = FillColor;
		DrawActiveShape (false, false, true, false, false);
	}

	private void OnFillStyleChanged (object? sender, EventArgs e)
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;
		if (activeEngine is not null)
			activeEngine.FillStyle = CurrentFillStyle;

		outline_width.Visible = outline_width_label.Visible = outline_width_sep.Visible = StrokeShape;
		dash_pattern_box.SetVisible (StrokeShape);
		DrawActiveShape (false, false, true, false, false);
	}

	#endregion ToolbarEventHandlers

	private readonly IToolService tools;
	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;
	private static UserLayer? runtime_layer;

	public BaseEditEngine (
		IServiceProvider services,
		ShapeTool passedOwner)
	{
		tools = services.GetService<IToolService> ();
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();

		owner = passedOwner;

		hover_handle = new (workspace);

		ResetShapes ();
	}

	public virtual void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		if (outline_width is not null)
			settings.PutSetting (SettingNames.BrushWidth (toolPrefix), (int) outline_width.Value);

		if (fill_button is not null)
			settings.PutSetting (SettingNames.FillStyle (toolPrefix), fill_button.SelectedIndex);

		if (shape_type_button is not null)
			settings.PutSetting (SettingNames.ShapeType (toolPrefix), shape_type_button.SelectedIndex);

		if (dash_pattern_box?.ComboBox is not null)
			settings.PutSetting (SettingNames.DashPattern (toolPrefix), dash_pattern_box.ComboBox.ComboBox.GetActiveText ()!);

		if (dash_pattern_box?.SpacingComboBox is not null)
			settings.PutSetting (SettingNames.DashSpacing (toolPrefix), DashSpacingSetting);
	}

	public void HandleBuildToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (shape_type_label == null) {
			string shapeTypeText = Translations.GetString ("Shape Type");
			shape_type_label = Gtk.Label.New ($" {shapeTypeText}: ");
		}

		tb.Append (shape_type_label);

		if (shape_type_button == null) {
			shape_type_button = ToolBarDropDownButton.New ();

			shape_type_button.AddItem (Translations.GetString ("Open Line/Curve Series"), Resources.Icons.ToolLine, 0, Translations.GetString ("Draws a line or curve with a start and an end point."));
			shape_type_button.AddItem (Translations.GetString ("Closed Line/Curve Series"), Resources.Icons.ToolRectangle, 1, Translations.GetString ("Automatically connects the last point back to the first, closing the shape (e.g. a rectangle)."));
			shape_type_button.AddItem (Translations.GetString ("Ellipse"), Resources.Icons.ToolEllipse, 2, Translations.GetString ("Draws an ellipse or circle."));
			shape_type_button.AddItem (Translations.GetString ("Rounded Line Series"), Resources.Icons.ToolRectangleRounded, 3, Translations.GetString ("Like Closed Line/Curve Series, but with rounded corners at each point."));
			shape_type_button.AddItem (Translations.GetString ("Triangle"), Resources.Icons.ToolTriangle, 4, Translations.GetString ("Draws a triangle."));

			shape_type_button.SelectedIndex = settings.GetSetting (
				SettingNames.ShapeType (toolPrefix),
				0);

			shape_type_button.SelectedItemChanged += (o, e) => {
				ShapeTypes newShapeType = ShapeType;
				ShapeEngine? selEngine = SelectedShapeEngine;

				//Verify that the tool needs to be switched.
				if (GetCorrespondingTool (newShapeType) == owner)
					return;

				if (selEngine == null) {
					ActivateCorrespondingTool (newShapeType, true);
					return;
				}

				//if shape is selected it will be converted to new shape and shape type will be changed, otherwise only shape type will be changed.

				//Create a new ShapesModifyHistoryItem so that the changing of the shape type can be undone.
				workspace.ActiveDocument.History.PushNewItem (new ShapesModifyHistoryItem (
					this, owner.Icon, Translations.GetString ("Changed Shape Type")));

				//Clone the old shape; it should be automatically garbage-collected. newShapeType already has the updated value.
				selEngine = selEngine.Convert (newShapeType, SelectedShapeIndex);

				int previousSSI = SelectedShapeIndex;
				ActivateCorrespondingTool (selEngine.ShapeType, true);
				SelectedShapeIndex = previousSSI;
				//Draw the updated shape with organized points generation (for mouse detection).
				DrawActiveShape (true, false, true, false, true);
			};
		}

		shape_type_button.SelectedItem = shape_type_button.Items[(int) owner.ShapeType];

		tb.Append (shape_type_button);

		if (rasterize_mode_label == null) {
			string modeText = Translations.GetString ("Mode");
			rasterize_mode_label = Gtk.Label.New ($" {modeText}: ");
		}

		tb.Append (rasterize_mode_label);

		if (rasterize_mode_button == null) {
			rasterize_mode_button = ToolBarDropDownButton.New ();

			rasterize_mode_button.AddItem (Translations.GetString ("Object — editable later"), Resources.Icons.LayerProperties, false,
				Translations.GetString ("Stays a live, re-editable shape. Cutting, erasing, or filtering across it will rasterize it first."));
			rasterize_mode_button.AddItem (Translations.GetString ("Raster — fuses to layer"), Resources.Icons.LayerMergeDown, true,
				Translations.GetString ("Painted into the layer's pixels on commit. Immediately cut/move/erase like any artwork, but not editable later."));

			rasterize_shapes = settings.GetSetting (SettingNames.SHAPE_RASTERIZE_MODE, false);
			rasterize_mode_button.SelectedIndex = rasterize_shapes ? 1 : 0;

			rasterize_mode_button.SelectedItemChanged += (o, e) => {
				rasterize_shapes = rasterize_mode_button.SelectedItem.GetTagOrDefault (false);
				settings.PutSetting (SettingNames.SHAPE_RASTERIZE_MODE, rasterize_shapes);
			};
		}

		rasterize_mode_button.SelectedIndex = rasterize_shapes ? 1 : 0;
		tb.Append (rasterize_mode_button);

		BuildTriangleTypeToolBar (tb, settings, toolPrefix);

		BuildShapeToolBar (tb, settings, toolPrefix);

		curved_segments_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (curved_segments_sep);

		if (curved_segments_button == null) {
			curved_segments_button = ToolBarDropDownButton.New ();

			curved_segments_button.AddItem (Translations.GetString ("Curved Segments On"), Resources.Icons.ToolLine, true, Translations.GetString ("Clicking a segment while editing inserts a control point to smoothly curve it."));
			curved_segments_button.AddItem (Translations.GetString ("Curved Segments Off"), Resources.Icons.ToolLine, false, Translations.GetString ("Segments stay straight; clicking a segment does not insert curve control points."));

			curved_segments_enabled = settings.GetSetting (SettingNames.SHAPE_CURVED_SEGMENTS, true);
			curved_segments_button.SelectedIndex = curved_segments_enabled ? 0 : 1;

			curved_segments_button.SelectedItemChanged += (o, e) => {
				curved_segments_enabled = curved_segments_button.SelectedItem.GetTagOrDefault (true);
				settings.PutSetting (SettingNames.SHAPE_CURVED_SEGMENTS, curved_segments_enabled);
			};
		}

		curved_segments_button.SelectedIndex = curved_segments_enabled ? 0 : 1;
		tb.Append (curved_segments_button);
	}

	protected virtual void BuildTriangleTypeToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
	}

	protected virtual void BuildShapeToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		fill_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (fill_sep);

		if (fill_label == null) {
			string fillStyleText = Translations.GetString ("Fill Style");
			fill_label = Gtk.Label.New ($" {fillStyleText}: ");
		}

		tb.Append (fill_label);

		if (fill_button == null) {
			fill_button = ToolBarDropDownButton.New ();

			fill_button.AddItem (Translations.GetString ("Outline Shape"), Resources.Icons.FillStyleOutline, 0, Translations.GetString ("Draw only the shape's outline, using the primary color."));
			fill_button.AddItem (Translations.GetString ("Fill Shape"), Resources.Icons.FillStyleFill, 1, Translations.GetString ("Fill the shape's interior with the secondary color, no outline."));
			fill_button.AddItem (Translations.GetString ("Fill and Outline Shape"), Resources.Icons.FillStyleOutlineFill, 2, Translations.GetString ("Fill the interior with the secondary color and outline it with the primary color."));

			fill_button.SelectedIndex = settings.GetSetting (
				SettingNames.FillStyle (toolPrefix),
				0);
			fill_button.SelectedItemChanged += OnFillStyleChanged;
		}

		tb.Append (fill_button);

		outline_width_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (outline_width_sep);

		if (outline_width_label == null) {
			string outlineWidthText = Translations.GetString ("Outline width");
			outline_width_label = Gtk.Label.New ($" {outlineWidthText}: ");
		}

		tb.Append (outline_width_label);

		if (outline_width == null) {

			outline_width = GtkExtensions.CreateToolBarSpinButton (
				1,
				1e5,
				1,
				settings.GetSetting (
					SettingNames.BrushWidth (toolPrefix),
					BaseTool.DEFAULT_BRUSH_WIDTH
				)
			);
			UpdateOutlineWidthTooltip ();
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => UpdateOutlineWidthTooltip ();

			outline_width.OnValueChanged += (o, e) => {

				ShapeEngine? selEngine = SelectedShapeEngine;
				if (selEngine == null) return;
				selEngine.BrushWidth = BrushWidth;
				StorePreviousSettings ();
				DrawActiveShape (false, false, true, false, false);
			};
		}

		tb.Append (outline_width);

		Gtk.ComboBoxText? dpbBox = dash_pattern_box.SetupToolbar (tb);

		outline_width.Visible = outline_width_label.Visible = outline_width_sep.Visible = StrokeShape;
		dash_pattern_box.SetVisible (StrokeShape);

		if (dpbBox == null)
			return;

		dpbBox.GetEntry ().SetText (
			settings.GetSetting (
				SettingNames.DashPattern (toolPrefix),
				"- (Solid)"
			)
		);

		dpbBox.OnChanged += (o, e) => {
			ShapeEngine? selEngine = SelectedShapeEngine;
			if (selEngine == null) return;
			selEngine.DashPattern = dpbBox.GetActiveText ()!;
			StorePreviousSettings ();
			DrawActiveShape (false, false, true, false, false);
		};

		if (dash_pattern_box.SpacingComboBox is not null) {
			int spacing = settings.GetSetting (SettingNames.DashSpacing (toolPrefix), 1);
			dash_pattern_box.SpacingComboBox.ComboBox.Active = SpacingToIndex (spacing);

			dash_pattern_box.SpacingComboBox.ComboBox.OnChanged += (o, e) => {
				ShapeEngine? selEngine = SelectedShapeEngine;
				if (selEngine == null) return;
				selEngine.DashSpacing = DashSpacingSetting;
				StorePreviousSettings ();
				DrawActiveShape (false, false, true, false, false);
			};
		}
	}

	// Maps a spacing multiplier back to its dropdown index (entries: "-,1-6,8,10").
	private static int SpacingToIndex (int spacing) => spacing switch {
		<= 1 => 1,
		<= 6 => spacing,
		<= 8 => 7,
		_ => 8,
	};

	public virtual void HandleActivated ()
	{
		EnsureShapesForCurrentLayer ();
		RecallPreviousSettings ();

		palette.PrimaryColorChanged += Palette_PrimaryColorChanged;
		palette.SecondaryColorChanged += Palette_SecondaryColorChanged;
		workspace.SelectedLayerChanged += HandleSelectedLayerChanged;
	}

	public virtual void HandleDeactivated (BaseTool? newTool)
	{
		SelectedPointIndex = -1;
		SelectedShapeIndex = -1;

		workspace.SelectedLayerChanged -= HandleSelectedLayerChanged;

		StorePreviousSettings ();

		if (workspace.HasOpenDocuments) {
			if (rasterize_shapes) {
				// Rasterized mode: switching away commits the shapes into the layer's base raster.
				FinalizeAllShapes ();
			} else {
				// Object mode: keep the raster overlay separate from UserLayer.Surface. Shape engines
				// stay re-editable even while a non-shape tool is active.
				// ToolManager has not assigned the new tool yet, so do not switch tools while
				// redrawing. A shape on another tool would otherwise re-enter deactivation.
				DrawAllShapes (preventSwitchBack: false, switchTools: false);
			}
			PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
		}

		palette.PrimaryColorChanged -= Palette_PrimaryColorChanged;
		palette.SecondaryColorChanged -= Palette_SecondaryColorChanged;
	}

	public virtual void HandleAfterSave ()
	{
		if (!workspace.HasOpenDocuments)
			return;

		DrawAllShapes (preventSwitchBack: false);
		PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
	}

	public virtual void HandleCommit ()
	{
		if (workspace.HasOpenDocuments)
			PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
	}

	public virtual bool HandleBeforeUndo ()
		=> false;

	public virtual bool HandleBeforeRedo ()
		=> false;

	public virtual void HandleAfterUndo ()
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;

		if (activeEngine != null)
			UpdateToolbarSettings (activeEngine);

		DrawActiveShape (true, false, true, false, false); // Draw the current state.
	}

	public virtual void HandleAfterRedo ()
	{
		ShapeEngine? activeEngine = ActiveShapeEngine;

		if (activeEngine != null)
			UpdateToolbarSettings (activeEngine);

		DrawActiveShape (true, false, true, false, false); // Draw the current state.
	}

	public virtual bool HandleKeyDown (Document document, ToolKeyEventArgs e)
	{
		bool IsBinding (ToolBindingDescriptor binding)
			=> PintaCore.Shortcuts.GetToolBinding (binding) == e.Gesture;
		bool IsDefault (ToolBindingDescriptor binding)
			=> PintaCore.Shortcuts.GetToolBinding (binding) == binding.DefaultGesture;

		if (IsBinding (KeyboardShortcutManager.ShapeDeletePoint)) {
			HandleDelete ();
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeFinalize)) {
			CommitShapeEditing ();
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeAddPointExact)) {
			HandleSpace (e, exact: true);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeAddPoint)) {
			HandleSpace (e);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeSelectPrevPoint)) {
			HandleLeft (e, selectPoint: true);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeSelectNextPoint)) {
			HandleRight (e, selectPoint: true);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeCreateNewAtPoint)) {
			HandleSpace (e, exact: true);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeMovePointLeft)) {
			HandleLeft (e);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeMovePointRight)) {
			HandleRight (e);
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeMovePointUp)) {
			HandleUp ();
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.ShapeMovePointDown)) {
			HandleDown ();
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.BrushDecreaseWidth)) {
			BrushWidth--;
			return true;
		}

		if (IsBinding (KeyboardShortcutManager.BrushIncreaseWidth)) {
			BrushWidth++;
			return true;
		}

		Gdk.Key keyPressed = e.Key;
		switch (keyPressed.Value) {
			case Gdk.Constants.KEY_Delete:
				if (!IsDefault (KeyboardShortcutManager.ShapeDeletePoint))
					return false;
				HandleDelete ();
				return true;
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
				if (!IsDefault (KeyboardShortcutManager.ShapeFinalize))
					return false;
				CommitShapeEditing ();
				return true;
			case Gdk.Constants.KEY_space:
				if (!IsDefault (KeyboardShortcutManager.ShapeAddPoint) ||
					(e.IsControlPressed && !IsDefault (KeyboardShortcutManager.ShapeAddPointExact)))
					return false;
				HandleSpace (e);
				return true;
			case Gdk.Constants.KEY_Up:
				if (!IsDefault (KeyboardShortcutManager.ShapeMovePointUp))
					return false;
				HandleUp ();
				return true;
			case Gdk.Constants.KEY_Down:
				if (!IsDefault (KeyboardShortcutManager.ShapeMovePointDown))
					return false;
				HandleDown ();
				return true;
			case Gdk.Constants.KEY_Left:
				if (!IsDefault (KeyboardShortcutManager.ShapeMovePointLeft))
					return false;
				HandleLeft (e);
				return true;
			case Gdk.Constants.KEY_Right:
				if (!IsDefault (KeyboardShortcutManager.ShapeMovePointRight))
					return false;
				HandleRight (e);
				return true;
			case Gdk.Constants.KEY_bracketleft:
				if (!IsDefault (KeyboardShortcutManager.BrushDecreaseWidth))
					return false;
				BrushWidth--;
				return true;
			case Gdk.Constants.KEY_bracketright:
				if (!IsDefault (KeyboardShortcutManager.BrushIncreaseWidth))
					return false;
				BrushWidth++;
				return true;
			default:
				if (keyPressed.IsControlKey ()) {
					// Redraw since the Ctrl key affects the hover cursor, etc
					DrawActiveShape (false, false, true, e.IsShiftPressed, false, true);
					return true;
				} else {
					return false;
				}
		}
	}

	private void HandleRight (ToolKeyEventArgs e, bool selectPoint = false)
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		if (selectPoint || e.IsControlPressed) {
			//Change the selected control point to be the following one.

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null) {
				++SelectedPointIndex;

				if (SelectedPointIndex > activeEngine.ControlPoints.Count - 1)
					SelectedPointIndex = 0;

			}
		} else {
			//Move the selected control point.
			PointD originalPosition = SelectedPoint!.Position; // NRT - Checked by SelectedPointIndex
			SelectedPoint.Position = originalPosition with { X = originalPosition.X + 1d };
		}

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleLeft (ToolKeyEventArgs e, bool selectPoint = false)
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		if (selectPoint || e.IsControlPressed) {
			//Change the selected control point to be the previous one.

			--SelectedPointIndex;

			if (SelectedPointIndex < 0) {
				ShapeEngine? activeEngine = ActiveShapeEngine;

				if (activeEngine != null)
					SelectedPointIndex = activeEngine.ControlPoints.Count - 1;

			}
		} else {
			//Move the selected control point.
			PointD originalPosition = SelectedPoint!.Position; // NRT - Checked by SelectedPointIndex
			SelectedPoint.Position = originalPosition with { X = originalPosition.X - 1d };
		}

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleDown ()
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		//Move the selected control point.
		PointD originalPosition = SelectedPoint!.Position; // NRT - Checked by SelectedPointIndex
		SelectedPoint.Position = originalPosition with { Y = originalPosition.Y + 1d };

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleUp ()
	{
		//Make sure a control point is selected.

		if (SelectedPointIndex < 0)
			return;

		//Move the selected control point.
		PointD originalPosition = SelectedPoint!.Position; // NRT - Checked by SelectedPointIndex
		SelectedPoint.Position = originalPosition with { Y = originalPosition.Y - 1d };

		DrawActiveShape (true, false, true, false, false);
	}

	private void HandleSpace (ToolKeyEventArgs e, bool exact = false)
	{
		ControlPoint? selPoint = SelectedPoint;

		if (selPoint == null)
			return;

		//This can be assumed not to be null since selPoint was not null.
		ShapeEngine selEngine = SelectedShapeEngine!; // NRT - ^^

		//Create a new ShapesModifyHistoryItem so that the adding of a control point can be undone.
		workspace.ActiveDocument.History.PushNewItem (
			new ShapesModifyHistoryItem (
				this,
				owner.Icon,
				ShapeName + " " + Translations.GetString ("Point Added")
			)
		);

		bool shiftKey = e.IsShiftPressed;
		bool ctrlKey = exact || e.IsControlPressed;

		PointD newPointPos;

		if (ctrlKey) {
			//Ctrl + space combo: same position as currently selected point.
			newPointPos = new PointD (selPoint.Position.X, selPoint.Position.Y);
		} else {
			shape_origin = new PointD (selPoint.Position.X, selPoint.Position.Y);

			if (shiftKey) {
				CalculateModifiedCurrentPoint ();
			}

			//Space only: position of mouse (after any potential shift alignment).
			newPointPos = new PointD (current_point.X, current_point.Y);
		}

		//Place the new point on the outside-most end, order-wise.
		if (SelectedPointIndex < selEngine.ControlPoints.Count / 2d) {
			selEngine.ControlPoints.Insert (SelectedPointIndex,
			    new ControlPoint (new PointD (newPointPos.X, newPointPos.Y), DefaultMidPointTension));
		} else {
			selEngine.ControlPoints.Insert (SelectedPointIndex + 1,
			    new ControlPoint (new PointD (newPointPos.X, newPointPos.Y), DefaultMidPointTension));

			++SelectedPointIndex;
		}

		DrawActiveShape (true, false, true, shiftKey, false, e.IsControlPressed);
	}

	private void HandleDelete ()
	{
		if (SelectedPointIndex < 0)
			return;

		List<ControlPoint> controlPoints = SelectedShapeEngine!.ControlPoints; // NRT - Code assumes this is not-null

		//Either delete a ControlPoint or an entire shape (if there's only 1 ControlPoint left).
		if (controlPoints.Count > 1) {
			//Create a new ShapesModifyHistoryItem so that the deletion of a control point can be undone.
			workspace.ActiveDocument.History.PushNewItem (
				new ShapesModifyHistoryItem (
					this,
					owner.Icon,
					ShapeName + " " + Translations.GetString ("Point Deleted")
				)
			);

			//Delete the selected point from the shape.
			controlPoints.RemoveAt (SelectedPointIndex);

			//Set the newly selected point to be the median-most point on the shape, order-wise.
			if (SelectedPointIndex > controlPoints.Count / 2)
				--SelectedPointIndex;

		} else {
			Document doc = workspace.ActiveDocument;

			//Create a new ShapesHistoryItem so that the deletion of a shape can be undone.
			doc.History.PushNewItem (
				new ShapesHistoryItem (
					this,
					owner.Icon,
					ShapeName + " " + Translations.GetString ("Deleted"),
					doc.Layers.CurrentUserLayer.Surface.Clone (),
					doc.Layers.CurrentUserLayer,
					SelectedPointIndex,
					SelectedShapeIndex,
					false
				)
			);


			//Delete the selected shape and drop its geometry from the shared ShapeLayer surface.
			SEngines.RemoveAt (SelectedShapeIndex);
			RedrawActiveLayerShapeSurface ();

			//Redraw the workspace.
			doc.Workspace.Invalidate ();

			SelectedPointIndex = -1;
			SelectedShapeIndex = -1;
		}

		DrawActiveShape (true, false, true, false, false);
	}

	public virtual bool HandleKeyUp (Document document, ToolKeyEventArgs e)
	{
		Gdk.Key keyReleased = e.Key;

		if (keyReleased.IsControlKey ())
			DrawActiveShape (false, false, true, e.IsShiftPressed, false, false);

		switch (keyReleased.Value) {
			case Gdk.Constants.KEY_Delete:
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
			case Gdk.Constants.KEY_space:
			case Gdk.Constants.KEY_Up:
			case Gdk.Constants.KEY_Down:
			case Gdk.Constants.KEY_Left:
			case Gdk.Constants.KEY_Right:
				return true;
			default:
				return false;
		}
	}

	public virtual void HandleMouseDown (Document document, ToolMouseEventArgs e)
	{
		EnsureShapesForCurrentLayer ();
		PointD unclamped_point = e.PointDouble;

		//If we are already drawing, ignore any additional mouse down events.
		if (is_drawing) return;

		//Redraw the previously (and possibly currently) active shape without any control points in case another shape is made active.
		DrawActiveShape (false, false, false, false, false);

		Document doc = workspace.ActiveDocument;

		shape_origin = doc.ClampToImageSize (unclamped_point);
		current_point = shape_origin;

		bool shiftKey = e.IsShiftPressed;

		if (shiftKey)
			CalculateModifiedCurrentPoint ();

		is_drawing = true;

		//Right clicking changes tension.
		changing_tension = e.MouseButton != MouseButton.Left;

		bool ctrlKey = e.IsControlPressed;

		SEngines.FindClosestControlPoint (
			unclamped_point,
			out int closestCPShapeIndex,
			out int closestCPIndex,
			out var closestControlPoint,
			out _);

		OrganizedPointCollection.FindClosestPoint (
			SEngines,
			unclamped_point,
			out int closestShapeIndex,
			out int closestPointIndex,
			out var closestPoint,
			out _);

		bool clicked_control_point = false;
		bool clicked_generated_point = false;

		PointD current_window_point = workspace.CanvasPointToView (unclamped_point);
		MoveHandle test_handle = new (workspace);

		// Check if the user is directly clicking on a control point.
		if (closestControlPoint != null) {
			test_handle.CanvasPosition = closestControlPoint.Position;
			clicked_control_point = test_handle.ContainsPoint (current_window_point);
			if (clicked_control_point) {
				SelectedPointIndex = closestCPIndex;
				SelectedShapeIndex = closestCPShapeIndex;
			}
		}

		// Otherwise, the user might have clicked on a generated point.
		if (!clicked_control_point && closestPoint.HasValue) {
			test_handle.CanvasPosition = closestPoint.Value;
			clicked_generated_point = test_handle.ContainsPoint (current_window_point);
		}

		clicked_without_modifying = clicked_control_point;

		if (!changing_tension && clicked_generated_point) {
			//Determine if the currently active tool matches the clicked on shape's corresponding tool, and if not, switch to it.
			if (ActivateCorrespondingTool (closestShapeIndex, true) != null) {
				//Pass on the event and its data to the newly activated tool.
				tools.DoMouseDown (document, e);

				//Don't do anything else here once the tool is switched and the event is passed on.
				return;
			}

			//The currently active tool matches the clicked on shape's corresponding tool.

			//Only add a node if the user isn't holding the control key down and curved segments are enabled.
			if (!ctrlKey && curved_segments_enabled) {
				//Create a new ShapesModifyHistoryItem so that the adding of a control point can be undone.
				doc.History.PushNewItem (new ShapesModifyHistoryItem (this, owner.Icon, ShapeName + " " + Translations.GetString ("Point Added")));

				ShapeEngine targetEngine = SEngines[closestShapeIndex];
				int insertedIdx = closestPointIndex;

				// Ellipse: keep elliptical shape when adding first extra node by converting
				// 4-corner perfect-rect to segmented closed curve that visually stays same.
				if (targetEngine is EllipseEngine ellipse && ellipse.ControlPoints.Count == 4) {
					// Check perfect rectangle still holds
					var cps = ellipse.ControlPoints;
					if (EllipseEngine.IsPerfectRectangle (cps[0].Position, cps[1].Position, cps[2].Position, cps[3].Position)) {
						insertedIdx = ellipse.ConvertToSegmentedEllipseAndInsert (
							new PointD (current_point.X, current_point.Y), DefaultMidPointTension);
						if (insertedIdx < 0)
							insertedIdx = closestPointIndex;
					} else {
						targetEngine.ControlPoints.Insert (closestPointIndex,
							new ControlPoint (new PointD (current_point.X, current_point.Y), DefaultMidPointTension));
					}
				} else {
					targetEngine.ControlPoints.Insert (closestPointIndex,
						new ControlPoint (new PointD (current_point.X, current_point.Y), DefaultMidPointTension));
				}

				//These should be set after creating the history item.
				SelectedPointIndex = insertedIdx;
				SelectedShapeIndex = closestShapeIndex;
			} else if (!ctrlKey && !curved_segments_enabled) {
				// Curved segments off: side click starts a whole-shape drag (no node add).
				SelectedShapeIndex = closestShapeIndex;
				SelectedPointIndex = 0;
				moving_whole_shape = true;
				last_shape_move_point = current_point;
				clicked_without_modifying = true;
			} else {
				SelectedPointIndex = closestPointIndex;
				SelectedShapeIndex = closestShapeIndex;
			}

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null)
				UpdateToolbarSettings (activeEngine);
		}

		//Create a new shape if the user control + clicks on a shape or if the user simply clicks outside of any shapes.
		if (!changing_tension && (ctrlKey || (!clicked_control_point && !clicked_generated_point))) {
			PointD prevSelPoint;

			//First, store the position of the currently selected point.
			if (SelectedPoint != null && ctrlKey) {
				prevSelPoint = new PointD (SelectedPoint.Position.X, SelectedPoint.Position.Y);
			} else {
				//This doesn't matter, other than the fact that it gets set to a value in order for the code to build.
				prevSelPoint = new PointD (0d, 0d);
			}

			//Create a new ShapesHistoryItem so that the creation of a new shape can be undone.
			doc.History.PushNewItem (new ShapesHistoryItem (this, owner.Icon, ShapeName + " " + Translations.GetString ("Added"),
				doc.Layers.CurrentUserLayer.Surface.Clone (), doc.Layers.CurrentUserLayer, SelectedPointIndex, SelectedShapeIndex, false));

			//Create the shape, add its starting points, and add it to SEngines.
			ShapeEngine newEngine = CreateShape (ctrlKey, clicked_control_point, prevSelPoint);
			newEngine.Name = NextDefaultShapeName (DefaultObjectName);
			SEngines.Add (newEngine);

			//Select the new shape.
			SelectedShapeIndex = SEngines.Count - 1;

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null) {
				//Set the AntiAliasing.
				activeEngine.AntiAliasing = owner.UseAntialiasing;
				activeEngine.FillStyle = CurrentFillStyle;
			}

			StorePreviousSettings ();
		} else if (clicked_control_point) {
			//Since the user is not creating a new shape or control point but rather modifying an existing control point, it should be determined
			//whether the currently active tool matches the clicked on shape's corresponding tool, and if not, switch to it.
			if (ActivateCorrespondingTool (SelectedShapeIndex, true) != null) {
				//Pass on the event and its data to the newly activated tool.
				tools.DoMouseDown (document, e);

				//Don't do anything else here once the tool is switched and the event is passed on.
				return;
			}

			//The currently active tool matches the clicked on shape's corresponding tool.

			ShapeEngine? activeEngine = ActiveShapeEngine;

			if (activeEngine != null)
				UpdateToolbarSettings (activeEngine);
		}

		//Determine if the user right clicks outside of any shapes (neither on their control points nor on their generated points).
		if ((!clicked_control_point && !clicked_generated_point) && changing_tension)
			clicked_without_modifying = true;

		DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
	}

	public virtual void HandleMouseUp (Document document, ToolMouseEventArgs e)
	{
		is_drawing = false;

		changing_tension = false;
		moving_whole_shape = false;

		DrawActiveShape (true, false, true, e.IsShiftPressed, false, e.IsControlPressed);
	}

	public virtual void HandleMouseMove (Document document, ToolMouseEventArgs e)
	{
		EnsureShapesForCurrentLayer ();
		current_point = e.PointDouble;
		bool shiftKey = e.IsShiftPressed;
		KeyGesture switchGesture = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.TriangleTypeSwitch);
		triangle_switch_down = IsSwitchGesturePressed (switchGesture, e.State);

		if (!is_drawing) {
			//Redraw the active shape to show a (temporary) highlighted control point (over any shape) when applicable.
			DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
			last_mouse_pos = current_point;
			return;
		}

		Document doc = document;

		current_point = document.ClampToImageSize (current_point);

		if (shiftKey)
			CalculateModifiedCurrentPoint ();

		if (moving_whole_shape && ActiveShapeEngine != null) {
			if (clicked_without_modifying) {
				doc.History.PushNewItem (
					new ShapesModifyHistoryItem (this, owner.Icon, ShapeName + " " + Translations.GetString ("Modified")));
				clicked_without_modifying = false;
			}

			double dx = current_point.X - last_shape_move_point.X;
			double dy = current_point.Y - last_shape_move_point.Y;

			if (dx != 0d || dy != 0d) {
				foreach (ControlPoint cp in ActiveShapeEngine.ControlPoints)
					cp.Position = new PointD (cp.Position.X + dx, cp.Position.Y + dy);

				last_shape_move_point = current_point;
			}

			DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
			last_mouse_pos = current_point;
			return;
		}

		ControlPoint? selPoint = SelectedPoint;

		//Make sure a control point is selected.
		if (selPoint == null) {
			last_mouse_pos = current_point;
			return;
		}

		if (clicked_without_modifying) {
			//Create a new ShapesModifyHistoryItem so that the modification of the shape can be undone.
			doc.History.PushNewItem (
							new ShapesModifyHistoryItem (this, owner.Icon, ShapeName + " " + Translations.GetString ("Modified")));

			clicked_without_modifying = false;
		}

		List<ControlPoint> controlPoints = SelectedShapeEngine!.ControlPoints; // NRT - Code assumes this is not-null

		if (!changing_tension) {
			//Moving a control point.

			//Make sure the control point was moved.
			if (current_point.X != selPoint.Position.X || current_point.Y != selPoint.Position.Y)
				MovePoint (controlPoints);

			DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);
			last_mouse_pos = current_point;
			return;
		}

		//Changing a control point's tension.

		//Unclamp the mouse position when changing tension.
		current_point = e.PointDouble;

		//Calculate the new tension based off of the movement of the mouse that's
		//perpendicular to the previous and following control points.

		PointD curPoint = selPoint.Position;
		PointD prevPoint, nextPoint;

		//Calculate the previous control point.
		if (SelectedPointIndex > 0) {
			prevPoint = controlPoints[SelectedPointIndex - 1].Position;
		} else {
			//There is none.
			prevPoint = curPoint;
		}

		//Calculate the following control point.
		if (SelectedPointIndex < controlPoints.Count - 1) {
			nextPoint = controlPoints[SelectedPointIndex + 1].Position;
		} else {
			//There is none.
			nextPoint = curPoint;
		}

		//The x and y differences are used as factors for the x and y change in the mouse position.
		double xDiff = prevPoint.X - nextPoint.X;
		double yDiff = prevPoint.Y - nextPoint.Y;
		double totalDiff = xDiff + yDiff;

		//Calculate the midpoint in between the previous and following points.
		PointD midPoint = new PointD ((prevPoint.X + nextPoint.X) / 2d, (prevPoint.Y + nextPoint.Y) / 2d);

		//Calculate the x change in the mouse position.
		double xChange =
			(curPoint.X <= midPoint.X)
			? current_point.X - last_mouse_pos.X
			: last_mouse_pos.X - current_point.X;

		//Calculate the y change in the mouse position.
		double yChange =
			(curPoint.Y <= midPoint.Y)
			? current_point.Y - last_mouse_pos.Y
			: last_mouse_pos.Y - current_point.Y;

		//Update the control point's tension.

		//Note: the difference factors are to be inverted for x and y change because this is perpendicular motion.
		controlPoints[SelectedPointIndex].Tension +=
			Math.Round (Math.Clamp ((xChange * yDiff + yChange * xDiff) / totalDiff, -1d, 1d)) / 50d;

		//Restrict the new tension to range from 0d to 1d.
		controlPoints[SelectedPointIndex].Tension = Math.Clamp (selPoint.Tension, 0d, 1d);

		DrawActiveShape (false, false, true, shiftKey, false, e.IsControlPressed);


		last_mouse_pos = current_point;
	}

	private static bool IsSwitchGesturePressed (KeyGesture gesture, Gdk.ModifierType state)
	{
		Gdk.ModifierType modifier = gesture.Key.Value switch {
			Gdk.Constants.KEY_Control_L or Gdk.Constants.KEY_Control_R => Gdk.ModifierType.ControlMask,
			Gdk.Constants.KEY_Shift_L or Gdk.Constants.KEY_Shift_R => Gdk.ModifierType.ShiftMask,
			Gdk.Constants.KEY_Alt_L or Gdk.Constants.KEY_Alt_R => Gdk.ModifierType.AltMask,
			Gdk.Constants.KEY_Meta_L or Gdk.Constants.KEY_Meta_R => Gdk.ModifierType.MetaMask,
			Gdk.Constants.KEY_Super_L or Gdk.Constants.KEY_Super_R => Gdk.ModifierType.SuperMask,
			Gdk.Constants.KEY_Hyper_L or Gdk.Constants.KEY_Hyper_R => Gdk.ModifierType.HyperMask,
			_ => default,
		};

		Gdk.ModifierType required = gesture.Modifiers | modifier;
		return required != default && (state & required) == required;
	}


	/// <summary>
	/// Draw the currently active shape.
	/// </summary>
	/// <param name="calculateOrganizedPoints">Whether to calculate the spatially organized
	/// points for mouse detection after drawing the shape.</param>
	/// <param name="finalize">Whether to finalize the drawing.</param>
	/// <param name="drawHoverSelection">Whether to draw any hover point or selected point.</param>
	/// <param name="shiftKey">Whether the shift key is being pressed. This is for width/height constraining/equalizing.</param>
	/// <param name="preventSwitchBack">Whether to prevent switching back to the old tool if a tool change is necessary.</param>
	public void DrawActiveShape (bool calculateOrganizedPoints, bool finalize, bool drawHoverSelection, bool shiftKey, bool preventSwitchBack, bool ctrl_key = false)
		=> DrawActiveShape (calculateOrganizedPoints, finalize, drawHoverSelection, shiftKey, preventSwitchBack, ctrl_key, skipToolSwitch: false);

	private void DrawActiveShape (
		bool calculateOrganizedPoints,
		bool finalize,
		bool drawHoverSelection,
		bool shiftKey,
		bool preventSwitchBack,
		bool ctrl_key,
		bool skipToolSwitch)
	{
		EnsureShapesForCurrentLayer ();
		ShapeTool? oldTool = skipToolSwitch
			? null
			: BaseEditEngine.ActivateCorrespondingTool (SelectedShapeIndex, calculateOrganizedPoints);

		//First, determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		if (oldTool != null) {
			//The tool has switched, so call DrawActiveShape again but inside that tool.
			if (tools.CurrentTool is ShapeTool tool)
				tool.EditEngine.DrawActiveShape (
				calculateOrganizedPoints, finalize, drawHoverSelection, shiftKey, preventSwitchBack);

			//Afterwards, switch back to the old tool, unless specified otherwise.
			if (!preventSwitchBack) {
				ActivateCorrespondingTool (oldTool.ShapeType, true);
			}

			return;
		}

		//The currently active tool should now match the shape's corresponding tool.

		BeforeDraw ();

		ShapeEngine? activeEngine = ActiveShapeEngine;

		if (activeEngine == null) {
			//No shape will be drawn; however, the hover point still needs to be drawn if drawHoverSelection is true.
			UpdateHoverHandle (drawHoverSelection, ctrl_key);
			PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
			return;
		}

		RectangleD dirty;

		//Determine if the drawing should be for finalizing the shape onto the image or drawing it temporarily.
		if (finalize)
			dirty = DrawFinalized (activeEngine, true, shiftKey);
		else
			dirty = DrawUnfinalized (activeEngine, drawHoverSelection, shiftKey, ctrl_key);

		//Determine if the organized (spatially hashed) points should be generated. This is for mouse interaction detection after drawing.
		if (calculateOrganizedPoints)
			OrganizePoints (activeEngine);

		InvalidateAfterDraw (dirty);
		PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	private void BeforeDraw ()
	{
		//Check to see if a new shape is selected.
		if (prev_selected_shape_index == SelectedShapeIndex)
			return;

		//A new shape is selected, so clear the previous dirty Rectangle.
		last_dirty = null;

		prev_selected_shape_index = SelectedShapeIndex;
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	/// <param name="dirty"></param>
	/// <param name="shiftKey"></param>
	private RectangleD DrawFinalized (ShapeEngine engine, bool createHistoryItem, bool shiftKey)
	{
		Document doc = workspace.ActiveDocument;

		//Finalize the shape onto the CurrentUserLayer.

		ImageSurface? undoSurface = null;

		if (createHistoryItem && engine.ControlPoints.Count > 0) //We only need to create a history item if there was a previous shape.
			undoSurface = doc.Layers.CurrentUserLayer.Surface.Clone ();

		//Draw the finalized shape into the layer's base raster.
		RectangleD dirty = DrawShapeGeometry (engine, doc.Layers.CurrentUserLayer.Surface);

		if (createHistoryItem && undoSurface != null) {

			//Create a new ShapesHistoryItem so that the finalization of the shape can be undone.

			doc.History.PushNewItem (
				new ShapesHistoryItem (
					this,
					owner.Icon,
					ShapeName + " " + Translations.GetString ("Finalized"),
					undoSurface,
					doc.Layers.CurrentUserLayer,
					SelectedPointIndex,
					SelectedShapeIndex,
					false
				)
			);
		}

		return dirty;
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	/// <param name="dirty"></param>
	/// <param name="drawHoverSelection"></param>
	/// <param name="shiftKey"></param>
	private RectangleD DrawUnfinalized (ShapeEngine engine, bool drawHoverSelection, bool shiftKey, bool ctrl_key)
	{
		RectangleD totalDirty = RedrawActiveLayerShapeSurface ();

		// Only the active shape refreshes its control-point handles; the other pending shapes keep
		// the handles they were last drawn with (matching the previous per-shape overlay behavior).
		DrawControlPoints (engine, true, drawHoverSelection, ctrl_key);

		return totalDirty;
	}

	/// <summary>
	/// Clears the active layer's shared ShapeLayer surface and re-renders every live shape's
	/// geometry into it. This is how the object-layer system composites the active layer's shapes
	/// now that per-shape overlays are retired. Returns the combined dirty rectangle.
	/// </summary>
	// ponytail: O(total control points) per redraw; fine for typical shape counts. If a layer ever
	// holds many complex shapes, redraw only the changed shape into a scratch + composite.
	private RectangleD RedrawActiveLayerShapeSurface ()
	{
		ImageSurface surface = workspace.ActiveDocument.Layers.CurrentUserLayer.ShapeLayer.Layer.Surface;
		surface.Clear ();

		RectangleD? totalDirty = null;
		foreach (ShapeEngine engine in SEngines) {
			RectangleD dirty = DrawShapeGeometry (engine, surface);
			totalDirty = totalDirty?.Union (dirty) ?? dirty;
		}

		return totalDirty ?? RectangleD.Zero;
	}

	/// <summary>
	/// Do not call. Use DrawActiveShape.
	/// </summary>
	/// <param name="engine"></param>
	private static void OrganizePoints (ShapeEngine engine)
	{
		//Organize the generated points for quick mouse interaction detection.

		//First, clear the previously organized points, if any.
		engine.OrganizedPoints.ClearCollection ();

		foreach (GeneratedPoint gp in engine.GeneratedPoints) {
			//For each generated point on the shape, calculate the spatial hashing for it and then store this information for later usage.
			engine.OrganizedPoints.StoreAndOrganizePoint (new OrganizedPoint (new PointD (gp.Position.X, gp.Position.Y), gp.ControlPointIndex));
		}
	}

	private void InvalidateAfterDraw (RectangleD dirty)
	{
		Document doc = workspace.ActiveDocument;

		// Increase the size of the dirty rect to account for antialiasing.
		if (owner.UseAntialiasing)
			dirty = dirty.Inflated (1, 1);

		//Combine, clamp, and invalidate the dirty Rectangle.
		if (last_dirty is not null)
			dirty = dirty.Union (last_dirty.Value);

		dirty = dirty.Clamped ();
		doc.Workspace.Invalidate (dirty.ToInt ());

		last_dirty = dirty;
	}


	/// <summary>
	/// Draws a single shape's geometry (fill/stroke/arrows), selection-clipped, into the given
	/// surface. Control-point handles are managed separately via <see cref="DrawControlPoints"/>,
	/// so this has no handle side effects and can be called for every shape when rebuilding the
	/// shared ShapeLayer surface.
	/// </summary>
	protected RectangleD DrawShapeGeometry (ShapeEngine engine, ImageSurface surface)
	{
		Document doc = workspace.ActiveDocument;

		using Context g = new (surface);

		g.AppendPath (doc.Selection.SelectionPath);
		g.FillRule = FillRule.EvenOdd;
		g.Clip ();

		g.Antialias = engine.AntiAliasing ? Antialias.Subpixel : Antialias.None;

		// Widen the gaps by the spacing multiplier by expanding each space in the pattern.
		string dashPattern = engine.DashSpacing > 1
			? engine.DashPattern.Replace (" ", new string (' ', engine.DashSpacing))
			: engine.DashPattern;
		bool isDashedLine = g.SetDashFromString (dashPattern, engine.BrushWidth, LineCap.Square);

		g.LineWidth = engine.BrushWidth;

		RectangleD? totalDirty = null;

		//Draw the shape.
		if (engine.ControlPoints.Count > 0) {
			//Generate the points that make up the shape.
			engine.GeneratePoints (engine.BrushWidth);

			var points = engine.GetActualPoints ();

			//Expand the invalidation rectangle as necessary.

			bool strokeShape = engine.FillStyle % 2 == 0;
			bool fillShape = engine.FillStyle >= 1;
			if (fillShape) {
				Color fillColor = strokeShape ? engine.FillColor : engine.OutlineColor;
				RectangleD dirty = g.FillPolygonal (points.AsSpan (), fillColor);
				totalDirty = totalDirty?.Union (dirty) ?? dirty;
			}

			if (strokeShape) {

				// dashpatterns cannot work with butt, so if we are using a dashpattern we default to square.
				LineCap lineCap =
					isDashedLine
					? LineCap.Square
					: engine.LineCap;

				RectangleD dirty = g.DrawPolygonal (points.AsSpan (), engine.OutlineColor, lineCap);
				totalDirty = totalDirty?.Union (dirty) ?? dirty;
			}
		}

		g.SetDash ([], 0.0);

		//Draw anything extra (that not every shape has), like arrows.
		DrawExtras (ref totalDirty, g, engine);

		return totalDirty ?? RectangleD.Zero;
	}

	private void DrawControlPoints (ShapeEngine shape, bool draw_controls, bool draw_selection, bool ctrl_key)
	{
		RectangleI dirty = MoveHandle.UnionInvalidateRects (shape.ControlPointHandles);
		shape.ControlPointHandles.Clear ();

		if (!draw_controls) {
			workspace.InvalidateWindowRect (dirty);
			return;
		}

		UpdateHoverHandle (draw_selection, ctrl_key);

		foreach (ControlPoint point in shape.ControlPoints) {

			//Skip drawing the control point if it is being hovered over.
			if (draw_selection && hover_handle.Active && hover_handle.CanvasPosition.DistanceSquared (point.Position) < 1d)
				continue;

			shape.ControlPointHandles.Add (
				new MoveHandle (workspace) {
					Active = true,
					CanvasPosition = point.Position,
					Selected = (point == SelectedPoint) && draw_selection
				}
			);
		}

		dirty = dirty.Union (MoveHandle.UnionInvalidateRects (shape.ControlPointHandles));

		workspace.InvalidateWindowRect (dirty);
	}

	/// <summary>
	/// Update the hover handle's position and redraw it.
	/// </summary>
	protected void UpdateHoverHandle (bool draw_selection, bool ctrl_key)
	{
		// SetOtherShapesControlPointsHidden() redraws other shapes via DrawShape(), which calls
		// back into this method for those shapes. Ignore those reentrant calls so they can't
		// clobber the hover state just computed for the actively hovered shape.
		if (updating_other_shapes_visibility)
			return;

		RectangleI dirty =
			hover_handle.Active
			? hover_handle.InvalidateRect
			: RectangleI.Zero;

		// Don't show the hover handle while the user is changing a control point's tension.
		hover_handle.Active = hover_handle.Selected = false;
		hover_handle.TooltipText = null;
		bool hovering_control_point = false;
		bool hovering_segment = false;

		if (!changing_tension && draw_selection) {

			PointD current_window_point = workspace.CanvasPointToView (current_point);

			SEngines.FindClosestControlPoint (
				current_point,
				out _,
				out _,
				out var closestControlPoint,
				out _);

			// Check if the user is directly hovering over a control point.
			if (closestControlPoint != null) {
				hover_handle.CanvasPosition = closestControlPoint.Position;
				hovering_control_point = hover_handle.ContainsPoint (current_window_point);
				if (hovering_control_point) {
					hover_handle.Active = hover_handle.Selected = true;
					hover_handle.TooltipText =
						$"{(int) Math.Round (closestControlPoint.Position.X)}, {(int) Math.Round (closestControlPoint.Position.Y)}\n"
						+ Translations.GetString ("Shift-drag to snap the adjacent segment to a 15° angle.");
				}
			}

			// Otherwise, the user may be hovering over a generated point (segment).
			// Only show the node-add preview when curved segments are on.
			if (!hovering_control_point) {

				OrganizedPointCollection.FindClosestPoint (
					SEngines,
					current_point,
					out _,
					out _,
					out var closestPoint,
					out _);

				if (closestPoint.HasValue) {
					hover_handle.CanvasPosition = closestPoint.Value;
					hovering_segment = hover_handle.ContainsPoint (current_window_point);
					if (hovering_segment && curved_segments_enabled)
						hover_handle.Active = true;
				}
			}

			if (hover_handle.Active)
				dirty = dirty.Union (hover_handle.InvalidateRect);
		}

		// Update the tool's cursor if we are hovering over a control point / generated point,
		// and Ctrl is not pressed (since Ctrl+click starts a new shape).
		// Otherwise, the normal cursor is shown to indicate that a shape can be drawn.
		var tool = tools.CurrentTool!;

		if (!is_drawing && !ctrl_key && (hovering_control_point || hovering_segment)) {
			// Grab on control points / for node-add; move when sides drag the whole shape.
			if (hovering_segment && !curved_segments_enabled)
				tool.SetCursor (move_cursor);
			else
				tool.SetCursor (grab_cursor);
		} else {
			tool.SetCursor (tool.DefaultCursor);
		}

		hovering_over_control_point = hovering_control_point;

		// While Ctrl is held over the canvas (about to start a fresh shape), hide other
		// pending shapes' control points so their handles don't visually clutter the spot
		// being clicked.
		SetOtherShapesControlPointsHidden (ctrl_key && !is_drawing && draw_selection);

		workspace.InvalidateWindowRect (dirty);
	}

	// True while the hover handle represents an actual control point (not a generated segment point).
	private bool hovering_over_control_point = false;

	/// <summary>
	/// The canvas position of the control point currently being hovered over, for tooltip display. Null if none.
	/// </summary>
	public PointD? HoveredControlPointPosition =>
		hovering_over_control_point ? hover_handle.CanvasPosition : null;

	private bool other_shapes_points_hidden = false;
	private bool updating_other_shapes_visibility = false;

	/// <summary>
	/// Shows/hides the control point handles of every pending shape other than the active one.
	/// </summary>
	private void SetOtherShapesControlPointsHidden (bool hidden)
	{
		if (other_shapes_points_hidden == hidden)
			return;

		other_shapes_points_hidden = hidden;

		updating_other_shapes_visibility = true;
		try {
			for (int i = 0; i < SEngines.Count; ++i) {

				if (i == SelectedShapeIndex)
					continue;

				ShapeEngine otherEngine = SEngines[i];

				if (otherEngine.ControlPoints.Count == 0)
					continue;

				// Geometry is shared in the ShapeLayer surface and unaffected here; only the
				// other shapes' control-point handles are shown/hidden.
				DrawControlPoints (otherEngine, draw_controls: !hidden, draw_selection: false, ctrl_key: false);
			}
		} finally {
			updating_other_shapes_visibility = false;
		}
	}

	/// <summary>
	/// Go through every editable shape and draw it.
	/// </summary>
	public void DrawAllShapes (bool preventSwitchBack = true, bool switchTools = true)
	{
		//Store the SelectedShapeIndex value for later restoration.
		int previousToolSI = SelectedShapeIndex;
		int previousToolPI = SelectedPointIndex;

		//Draw all of the shapes.
		for (SelectedShapeIndex = 0; SelectedShapeIndex < SEngines.Count; ++SelectedShapeIndex) {
			//Only draw the selected point for the selected shape.
			if (switchTools) {
				DrawActiveShape (true, false, previousToolSI == SelectedShapeIndex, false, preventSwitchBack);
				continue;
			}

			ShapeTool? correspondingTool = GetCorrespondingTool (SEngines[SelectedShapeIndex].ShapeType);
			BaseEditEngine drawingEngine = correspondingTool?.EditEngine ?? this;
			int previousShapeIndex = drawingEngine.SelectedShapeIndex;
			int previousPointIndex = drawingEngine.SelectedPointIndex;
			drawingEngine.SelectedShapeIndex = SelectedShapeIndex;
			drawingEngine.SelectedPointIndex = previousToolSI == SelectedShapeIndex ? previousToolPI : -1;
			drawingEngine.DrawActiveShape (true, false, previousToolSI == SelectedShapeIndex, false, preventSwitchBack, false, skipToolSwitch: true);
			drawingEngine.SelectedShapeIndex = previousShapeIndex;
			drawingEngine.SelectedPointIndex = previousPointIndex;
		}

		//Restore the previous SelectedShapeIndex value.
		SelectedShapeIndex = previousToolSI;

		//Determine if the currently active tool matches the shape's corresponding tool, and if not, switch to it.
		if (switchTools)
			BaseEditEngine.ActivateCorrespondingTool (SelectedShapeIndex, false);

		//The currently active tool should now match the shape's corresponding tool.
	}

	private void EnsureShapesForCurrentLayer ()
	{
		if (!workspace.HasOpenDocuments)
			return;

		UserLayer layer = workspace.ActiveDocument.Layers.CurrentUserLayer;
		if (runtime_layer == layer)
			return;

		if (runtime_layer is not null) {
			// Bake the layer we're leaving into its persistent ShapeLayer from its object list so
			// it keeps compositing once it's no longer the active layer (guards the white-rectangle
			// desync even if some path mutated objects without refreshing the surface).
			PersistShapeObjects (runtime_layer);
			RedrawShapeLayerSurface (runtime_layer);
		}

		SEngines.Clear ();
		runtime_layer = layer;

		// Rebuild the live editing engines for the now-active layer from its object list,
		// and render them into its ShapeLayer surface. The active layer now composites
		// solely through this shared surface (per-shape overlays are retired), so the
		// same geometry never renders twice. Rasterized objects are records only (baked into
		// the base raster), so they are not rebuilt as editable engines.
		foreach (ShapeObject source in layer.ShapeObjects)
			if (!source.Rasterized)
				SEngines.Add (ShapeEngineCollection.Create (layer, source));

		RedrawShapeLayerSurface (layer);
	}

	// Rebinds the live editing engines to the newly-selected layer. Without this, switching layers
	// while a shape tool is active leaves the previous layer's SEngines (and their control-point
	// handles / blue dots) in the draw loop even though the shapes no longer belong to the active
	// layer.
	private void HandleSelectedLayerChanged (object? sender, EventArgs e)
	{
		if (!workspace.HasOpenDocuments)
			return;

		DrawAllShapes (preventSwitchBack: false, switchTools: false);
		EnsureShapesForCurrentLayer ();
	}

	/// <summary>
	/// Rebuilds a layer's ShapeLayer surface from its ShapeObjects, maintaining the
	/// invariant that the object surface equals the render of the object list.
	/// </summary>
	public static void RedrawShapeLayerSurface (UserLayer layer)
		=> ShapeObjectRenderer.RenderAll (layer.ShapeLayer.Layer.Surface, layer);

	public static void PersistShapeObjects (UserLayer layer)
		=> ShapeEngineCollection.Store (layer, SEngines);

	/// <summary>
	/// Persists the live editing engines into <paramref name="layer"/>'s object list only when they
	/// actually belong to it (i.e. it is the active editing layer). Shape history items call this
	/// before snapshotting so a layer that is not currently being edited is never clobbered by
	/// another layer's engines.
	/// </summary>
	public static void PersistShapeObjectsIfLive (UserLayer layer)
	{
		if (runtime_layer == layer)
			PersistShapeObjects (layer);
	}

	/// <summary>
	/// Re-establishes the object surface and (if <paramref name="layer"/> is the active editing
	/// layer) the live editing engines for a layer after a shape history item swapped its
	/// <see cref="UserLayer.ShapeObjects"/>. This is the shape counterpart of the object-model
	/// history restore: the ShapeLayer surface is a pure function of the object list, and the
	/// live SEngines are rebuilt from it so editing reflects the restored state.
	/// </summary>
	public static void ReloadLayerShapes (UserLayer layer)
	{
		RedrawShapeLayerSurface (layer);

		bool isActive = PintaCore.Workspace.HasOpenDocuments
			&& PintaCore.Workspace.ActiveDocument.Layers.CurrentUserLayer == layer;

		if (!isActive) {
			// The live engines belong to a different layer; leave them untouched. Drop any
			// stale binding so the engines are rebuilt when the user returns to this layer.
			if (runtime_layer == layer)
				runtime_layer = null;
			return;
		}

		SEngines.Clear ();
		foreach (ShapeObject source in layer.ShapeObjects)
			if (!source.Rasterized)
				SEngines.Add (ShapeEngineCollection.Create (layer, source));
		runtime_layer = layer;
	}

	private void CommitShapeEditing ()
	{
		if (rasterize_shapes) {
			// Rasterized mode: bake the shapes into the layer's base raster and drop the objects.
			FinalizeAllShapes ();
			return;
		}

		SelectedPointIndex = -1;
		SelectedShapeIndex = -1;
		DrawAllShapes (preventSwitchBack: false);
		if (workspace.HasOpenDocuments)
			PersistShapeObjects (workspace.ActiveDocument.Layers.CurrentUserLayer);
	}

	/// <summary>
	/// Go through every editable shape not yet finalized and finalize it.
	/// </summary>
	protected void FinalizeAllShapes ()
	{
		//Finalize every editable shape not yet finalized.

		if (SEngines.Count == 0)
			return;

		Document doc = workspace.ActiveDocument;

		ImageSurface undoSurface = doc.Layers.CurrentUserLayer.Surface.Clone ();

		int previousSelectedPointIndex = SelectedPointIndex;

		RectangleD? totalDirty = null;

		//Finalize all of the shapes.
		for (SelectedShapeIndex = 0; SelectedShapeIndex < SEngines.Count; ++SelectedShapeIndex) {
			//Get a reference to each shape's corresponding tool.
			ShapeTool? correspondingTool = GetCorrespondingTool (SEngines[SelectedShapeIndex].ShapeType);

			if (correspondingTool == null)
				continue;

			//Finalize the now active shape using its corresponding tool's EditEngine.

			BaseEditEngine correspondingEngine = correspondingTool.EditEngine;

			correspondingEngine.SelectedShapeIndex = SelectedShapeIndex;

			correspondingEngine.BeforeDraw ();

			//Draw the current shape with the corresponding tool's EditEngine.
			RectangleD dirty = correspondingEngine.DrawFinalized (SEngines[SelectedShapeIndex], false, false);
			totalDirty = totalDirty?.Union (dirty) ?? dirty;
		}

		//Make sure that the undo surface isn't null.
		if (undoSurface != null) {
			//Create a new ShapesHistoryItem so that the finalization of the shapes can be undone.
			doc.History.PushNewItem (new ShapesHistoryItem (this, owner.Icon, Translations.GetString ("Finalized"),
				undoSurface, doc.Layers.CurrentUserLayer, previousSelectedPointIndex, prev_selected_shape_index, true));
		}

		// Rasterized shapes keep a non-editable record: persist the finalized shapes, mark them
		// Rasterized (their pixels now live in the base raster), and redraw the object surface — which
		// skips Rasterized objects, so the baked pixels aren't composited twice. They stay in the layers
		// dock as finalized records. The history item pushed above captured the pre-finalize editable
		// objects, so undo restores them and removes the baked pixels.
		UserLayer layer = doc.Layers.CurrentUserLayer;
		PersistShapeObjects (layer);
		foreach (ShapeObject obj in layer.ShapeObjects)
			obj.Rasterized = true;
		RedrawShapeLayerSurface (layer);

		if (totalDirty.HasValue) {
			InvalidateAfterDraw (totalDirty.Value);
		}

		//Clear out all of the data.
		ResetShapes ();
	}

	/// <summary>
	/// Constrain the current point to snap to fixed angles from the previous point, or to
	/// produce a square / circle when drawing those shape types.
	/// </summary>
	protected void CalculateModifiedCurrentPoint ()
	{
		ShapeEngine? selEngine = SelectedShapeEngine;

		//Don't bother calculating a modified point if there is no selected shape.
		if (selEngine == null)
			return;

		if (ShapeType != ShapeTypes.OpenLineCurveSeries && selEngine.ControlPoints.Count == 4) {

			// Constrain to a square / circle.

			PointD origin = selEngine.ControlPoints[(SelectedPointIndex + 2) % 4].Position;

			PointD d = current_point - origin;

			double length = Math.Max (Math.Abs (d.X), Math.Abs (d.Y));

			PointD offset = new (
				X: length * Math.Sign (d.X),
				Y: length * Math.Sign (d.Y));

			current_point = origin + offset;

		} else {
			// Calculate the modified position of currentPoint such that the angle between the adjacent point
			// (if any) and currentPoint is snapped to the closest angle out of a certain number of angles.
			ControlPoint adjacentPoint;

			if (SelectedPointIndex > 0) {
				//Previous point.
				adjacentPoint = selEngine.ControlPoints[SelectedPointIndex - 1];
			} else if (selEngine.ControlPoints.Count > 1) {
				//Previous point (looping around to the end) if there is more than 1 point.
				adjacentPoint = selEngine.ControlPoints[^1];
			} else {
				//Don't bother calculating a modified point because there is no reference point to align it with (there is only 1 point).
				return;
			}

			PointD dir = new (
				X: current_point.X - adjacentPoint.Position.X,
				Y: current_point.Y - adjacentPoint.Position.Y);

			RadiansAngle baseTheta = new (Math.Atan2 (dir.Y, dir.X));

			double length = Utility.Magnitude (dir);

			RadiansAngle theta = new (Math.Round (12 * baseTheta.Radians / Math.PI) * Math.PI / 12);

			current_point = new PointD (
				X: adjacentPoint.Position.X + length * Math.Cos (theta.Radians),
				Y: adjacentPoint.Position.Y + length * Math.Sin (theta.Radians));
		}
	}

	/// <summary>
	/// Resets the editable data.
	/// </summary>
	protected void ResetShapes ()
	{
		SEngines = [];

		//The fields are modified instead of the properties here because a redraw call is undesired (for speed/efficiency).
		SelectedPointIndex = -1;
		SelectedShapeIndex = -1;

		is_drawing = false;

		last_dirty = null;
	}

	/// <summary>
	/// Activates the corresponding tool to the given shapeIndex value if the tool is not already active, and then returns the previous tool
	/// if a tool switch has occurred or null otherwise. If a switch did occur and this was called in e.g. an event handler, it should most
	/// likely pass the event data on to the newly activated tool (accessing it using PintaCore.Tools.CurrentTool) and then return.
	/// </summary>
	/// <param name="shapeIndex">The index of the shape in SEngines to find the corresponding tool to and switch to.</param>
	/// <param name="permanentSwitch">Whether the tool switch is permanent or just temporary (for drawing).</param>
	/// <returns>The *previous* tool if a tool switch has occurred or null otherwise.</returns>
	public static ShapeTool? ActivateCorrespondingTool (int shapeIndex, bool permanentSwitch)
	{
		//First make sure that there is a validly selectable tool.
		if (shapeIndex > -1 && SEngines.Count > shapeIndex)
			return ActivateCorrespondingTool (SEngines[shapeIndex].ShapeType, permanentSwitch);

		//Let the caller know that the active tool has not been switched.
		return null;
	}

	/// <summary>
	/// Activates the corresponding tool to the given shapeType value if the tool is not already active, and then returns the previous tool
	/// if a tool switch has occurred or null otherwise. If a switch did occur and this was called in e.g. an event handler, it should most
	/// likely pass the event data on to the newly activated tool (accessing it using PintaCore.Tools.CurrentTool) and then return.
	/// </summary>
	/// <param name="shapeType">The index of the shape in SEngines to find the corresponding tool to and switch to.</param>
	/// <param name="permanentSwitch">Whether the tool switch is permanent or just temporary (for drawing).</param>
	/// <returns>The *previous* tool if a tool switch has occurred or null otherwise.</returns>
	public static ShapeTool? ActivateCorrespondingTool (ShapeTypes shapeType, bool permanentSwitch)
	{
		ShapeTool? correspondingTool = GetCorrespondingTool (shapeType);

		//Verify that the corresponding tool is valid and that it doesn't match the currently active tool.
		if (correspondingTool == null || PintaCore.Tools.CurrentTool == correspondingTool) {
			//Let the caller know that the active tool has not been switched.
			return null;
		}

		ShapeTool? oldTool = PintaCore.Tools.CurrentTool as ShapeTool;

		int oldToolSPI = -1;
		int oldToolSSI = -1;
		//SetCurrentTool sets oldTool's SelectedPointIndex and SelectedShapeIndex to -1 so their value has to be saved before this happens.
		if (oldTool != null && oldTool.IsEditableShapeTool && permanentSwitch) {
			oldToolSPI = oldTool.EditEngine.SelectedPointIndex;
			oldToolSSI = oldTool.EditEngine.SelectedShapeIndex;
		}

		//The active tool needs to be switched to the corresponding tool.
		PintaCore.Tools.SetCurrentTool (correspondingTool);
		var newTool = (ShapeTool?) PintaCore.Tools.CurrentTool;

		// This shouldn't be possible, but we need a null check.
		if (newTool is null)
			return null;

		//What happens next depends on whether the old tool was an editable ShapeTool.
		if (oldTool != null && oldTool.IsEditableShapeTool) {

			if (permanentSwitch) {
				//Set the new tool's active shape and point to the old shape and point.
				newTool.EditEngine.SelectedPointIndex = oldToolSPI;
				newTool.EditEngine.SelectedShapeIndex = oldToolSSI;

				//Make sure neither tool thinks it is drawing anything.
				newTool.EditEngine.is_drawing = false;
				oldTool.EditEngine.is_drawing = false;
			}

			ShapeEngine? activeEngine = newTool.EditEngine.ActiveShapeEngine;

			if (activeEngine != null)
				newTool.EditEngine.UpdateToolbarSettings (activeEngine);

		} else {
			if (permanentSwitch) {
				//Make sure that the new tool doesn't think it is drawing anything.
				newTool.EditEngine.is_drawing = false;
			}
		}

		//Let the caller know that the active tool has been switched.
		return oldTool;
	}

	/// <summary>
	/// Gets the corresponding tool to the given shape type and then returns that tool.
	/// </summary>
	/// <param name="ShapeType">The shape type to find the corresponding tool to.</param>
	/// <returns>The corresponding tool to the given shape type.</returns>
	public static ShapeTool? GetCorrespondingTool (ShapeTypes shapeType)
	{

		//Get the corresponding BaseTool reference to the shape type.
		CorrespondingTools.TryGetValue (shapeType, out var correspondingTool);

		return correspondingTool;
	}


	/// <summary>
	/// Copy the given shape's settings to the toolbar settings. Calls StorePreviousSettings.
	/// </summary>
	/// <param name="engine"></param>
	public virtual void UpdateToolbarSettings (ShapeEngine engine)
	{
		owner.UseAntialiasing = engine.AntiAliasing;

		//Update the DashPatternBox to represent the current shape's DashPattern.
		dash_pattern_box.ComboBox!.ComboBox.GetEntry ().SetText (engine.DashPattern); // NRT - Code assumes this is not-null
		if (dash_pattern_box.SpacingComboBox is not null)
			dash_pattern_box.SpacingComboBox.ComboBox.Active = SpacingToIndex (engine.DashSpacing);

		OutlineColor = engine.OutlineColor;
		FillColor = engine.FillColor;

		BrushWidth = engine.BrushWidth;
		if (fill_button is not null)
			fill_button.SelectedIndex = engine.FillStyle;

		StorePreviousSettings ();
	}

	/// <summary>
	/// Copy the previous settings to the toolbar settings.
	/// </summary>
	protected virtual void RecallPreviousSettings ()
	{
		dash_pattern_box.ComboBox?.ComboBox.GetEntry ().SetText (prev_dash_pattern);
		if (dash_pattern_box.SpacingComboBox is not null)
			dash_pattern_box.SpacingComboBox.ComboBox.Active = SpacingToIndex (prev_dash_spacing);

		owner.UseAntialiasing = prev_antialiasing;
		BrushWidth = prev_outline_width;
	}

	/// <summary>
	/// Copy the toolbar settings to the previous settings.
	/// </summary>
	protected virtual void StorePreviousSettings ()
	{
		if (dash_pattern_box.ComboBox != null)
			prev_dash_pattern = dash_pattern_box.ComboBox.ComboBox.GetEntry ().GetText ();

		if (dash_pattern_box.SpacingComboBox != null)
			prev_dash_spacing = DashSpacingSetting;

		prev_antialiasing = owner.UseAntialiasing;
		prev_outline_width = BrushWidth;
	}

	/// <summary>
	/// Creates a new shape, adds its starting points, and returns it.
	/// </summary>
	/// <param name="ctrlKey"></param>
	/// <param name="clickedOnControlPoint"></param>
	/// <param name="prevSelPoint"></param>
	protected abstract ShapeEngine CreateShape (bool ctrlKey, bool clickedOnControlPoint, PointD prevSelPoint);

	protected virtual void MovePoint (List<ControlPoint> controlPoints)
	{
		//Update the control point's position.
		controlPoints.ElementAt (SelectedPointIndex).Position = new PointD (current_point.X, current_point.Y);
	}

	protected virtual void DrawExtras (ref RectangleD? totalDirty, Context g, ShapeEngine engine)
	{

	}

	protected void AddLinePoints (bool ctrlKey, bool clickedOnControlPoint, ShapeEngine selEngine, PointD prevSelPoint)
	{
		PointD startingPoint;

		//Create the initial points of the shape. The second point will follow the mouse around until released.
		if (ctrlKey && clickedOnControlPoint) {
			startingPoint = prevSelPoint;

			clicked_without_modifying = false;
		} else {
			startingPoint = shape_origin;
		}


		selEngine.ControlPoints.Add (new ControlPoint (new PointD (startingPoint.X, startingPoint.Y), DefaultEndPointTension));
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X + .01d, startingPoint.Y + .01d), DefaultEndPointTension));


		SelectedPointIndex = 1;
		SelectedShapeIndex = SEngines.Count - 1;
	}

	protected void AddRectanglePoints (bool ctrlKey, bool clickedOnControlPoint, ShapeEngine selEngine, PointD prevSelPoint)
	{
		PointD startingPoint;

		//Create the initial points of the shape. The second point will follow the mouse around until released.
		if (ctrlKey && clickedOnControlPoint) {
			startingPoint = prevSelPoint;

			clicked_without_modifying = false;
		} else {
			startingPoint = shape_origin;
		}


		selEngine.ControlPoints.Add (new ControlPoint (new PointD (startingPoint.X, startingPoint.Y), 0.0));
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X, startingPoint.Y + .01d), 0.0));
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X + .01d, startingPoint.Y + .01d), 0.0));
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X + .01d, startingPoint.Y), 0.0));


		SelectedPointIndex = 2;
		SelectedShapeIndex = SEngines.Count - 1;
	}

	protected void MoveRectangularPoint (List<ControlPoint> controlPoints)
	{
		ShapeEngine? selEngine = SelectedShapeEngine;

		if (selEngine == null || !selEngine.Closed || controlPoints.Count != 4)
			return;

		//Figure out the indices of the surrounding points. The lowest point index should be 0 and the highest 3.

		int previousPointIndex = SelectedPointIndex - 1;
		int nextPointIndex = SelectedPointIndex + 1;
		int oppositePointIndex = SelectedPointIndex + 2;

		if (previousPointIndex < 0)
			previousPointIndex = controlPoints.Count - 1;

		if (nextPointIndex >= controlPoints.Count) {
			nextPointIndex = 0;
			oppositePointIndex = 1;
		} else if (oppositePointIndex >= controlPoints.Count) {
			oppositePointIndex = 0;
		}


		ControlPoint previousPoint = controlPoints.ElementAt (previousPointIndex);
		ControlPoint oppositePoint = controlPoints.ElementAt (oppositePointIndex);
		ControlPoint nextPoint = controlPoints.ElementAt (nextPointIndex);


		//Now that we know the indexed order of the points, we can align everything properly.
		if (SelectedPointIndex == 2 || SelectedPointIndex == 0) {
			//Control point visual order (counter-clockwise order always goes selectedPoint, previousPoint, oppositePoint, nextPoint,
			//where moving point == selectedPoint):
			//
			//static (opposite) point		horizontally aligned point
			//vertically aligned point		moving point
			//OR
			//moving point					vertically aligned point
			//horizontally aligned point	static (opposite) point


			//Update the previous control point's position.
			previousPoint.Position = new PointD (previousPoint.Position.X, current_point.Y);

			//Update the next control point's position.
			nextPoint.Position = new PointD (current_point.X, nextPoint.Position.Y);


			//Even though it's supposed to be static, just in case the points get out of order
			//(they do sometimes), update the opposite control point's position.
			oppositePoint.Position = new PointD (previousPoint.Position.X, nextPoint.Position.Y);
		} else {
			//Control point visual order (counter-clockwise order always goes selectedPoint, previousPoint, oppositePoint, nextPoint,
			//where moving point == selectedPoint):
			//
			//horizontally aligned point	static (opposite) point
			//moving point					vertically aligned point
			//OR
			//vertically aligned point		moving point
			//static (opposite) point		horizontally aligned point


			//Update the previous control point's position.
			previousPoint.Position = new PointD (current_point.X, previousPoint.Position.Y);

			//Update the next control point's position.
			nextPoint.Position = new PointD (nextPoint.Position.X, current_point.Y);


			//Even though it's supposed to be static, just in case the points get out of order
			//(they do sometimes), update the opposite control point's position.
			oppositePoint.Position = new PointD (nextPoint.Position.X, previousPoint.Position.Y);
		}
	}

	protected void AddTrianglePoints (bool ctrlKey, bool clickedOnControlPoint, ShapeEngine selEngine, PointD prevSelPoint)
	{
		PointD startingPoint;

		//Create the initial points of the shape. The third point (the moving base corner) will follow the mouse around until released.
		if (ctrlKey && clickedOnControlPoint) {
			startingPoint = prevSelPoint;

			clicked_without_modifying = false;
		} else {
			startingPoint = shape_origin;
		}

		//Apex.
		selEngine.ControlPoints.Add (new ControlPoint (new PointD (startingPoint.X, startingPoint.Y), 0.0));
		//Base, left.
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X, startingPoint.Y + .01d), 0.0));
		//Base, right (the moving point).
		selEngine.ControlPoints.Add (
			new ControlPoint (new PointD (startingPoint.X + .01d, startingPoint.Y + .01d), 0.0));

		SelectedPointIndex = 2;
		SelectedShapeIndex = SEngines.Count - 1;
	}

	//Keeps the triangle's base edge (points 1 and 2) level, the way MoveRectangularPoint keeps a rectangle's edges aligned.
	protected void MoveTriangularPoint (List<ControlPoint> controlPoints)
	{
		if (controlPoints.Count != 3)
			return;

		switch (SelectedPointIndex) {
			case 1:
				controlPoints[2].Position = new PointD (controlPoints[2].Position.X, current_point.Y);
				break;
			case 2:
				controlPoints[1].Position = new PointD (controlPoints[1].Position.X, current_point.Y);
				break;
		}
	}
}
