/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Olivier Dufour <olivier.duff@gmail.com>                 //
//                     Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class TextTool : BaseTool
{
	public override bool UseSnapping => true;

	// Variables for dragging
	private PointD start_mouse_xy;
	private PointI start_click_point;
	private bool tracking;
	private readonly Gdk.Cursor cursor_move = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Move);
	private readonly Gdk.Cursor cursor_rotate = CreateRotateCursor ();

	private static Gdk.Cursor CreateRotateCursor ()
	{
		//Reuse the same rotation icon the image transform tools use.
		Gdk.Texture texture = BaseTransformTool.LoadRotateTexture ();
		return Gdk.Cursor.NewFromTexture (texture, texture.Width / 2, texture.Height / 2, null);
	}

	private PointI click_point;
	private bool is_editing;
	private RectangleI old_cursor_bounds = RectangleI.Zero;

	//The kind of canvas manipulation (move/rotate/resize) currently in progress,
	//invoked by dragging the dashed interaction rectangle around a text object.
	private enum TextManipulation { None, Move, Rotate, Resize }

	private TextManipulation manipulation = TextManipulation.None;
	//The object's bounding box when a move gesture started. Snapping aligns that
	//box - its edges and its centre lines - rather than the cursor alone.
	private RectangleD move_start_bounds;
	//The corner (0 TL, 1 TR, 2 BR, 3 BL) being dragged during a resize.
	private int resize_corner;
	//The object's rotation (degrees) and pointer angle (degrees) at gesture start.
	private double start_rotation_angle;
	private double start_pointer_angle;
	//The font size (pixels) and center-to-corner distance at gesture start.
	private double resize_start_fontsize;
	private double resize_start_corner_dist;
	//The area-text wrap width (pixels) at gesture start.
	private int resize_start_wrapwidth;

	//Area mode: while true, the current left-drag is defining a new flow box's width
	//(draw-the-box-first) rather than manipulating an existing object. new_box_start_x
	//is the drag's origin X in canvas space.
	private bool drawing_new_box;
	private int new_box_start_x;

	//Default wrap width for a newly created area (flow) text box, and the floor a
	//resize can shrink it to.
	private const int DefaultAreaWidth = 200;
	private const int MinAreaWidth = 20;
	//Prevents the toolbar's font-size spin handler from re-applying the toolbar font
	//while the font size is being set programmatically (e.g. live during a corner resize).
	private bool is_updating_font_size;

	//The text object currently being edited or moved, or null.
	private TextObject? current_text_object;
	//The layer current_text_object actually lives on. Tracked separately from
	//CurrentUserLayer because a layer-change event (add/remove/select) can
	//repoint CurrentUserLayer at a different layer before the in-progress edit
	//is committed.
	private UserLayer? editing_layer;

	//This is used to temporarily store the UserLayer's and TextLayer's previous ImageSurface states.
	private ImageSurface? text_undo_surface;
	private ImageSurface? user_undo_surface;
	private IReadOnlyList<ILayerObject>? undo_objects;
	// The last pre-editing string, if pre-editing is active.
	private string? preedit_string;
	// The selection from when editing started. This ensures that text doesn't suddenly disappear/appear
	// if the selection changes before the text is finalized.
	private DocumentSelection? selection;

	private readonly Gtk.IMMulticontext im_context;
	private readonly TextLayout layout;

	private UserLayer CurrentUserLayer
		=> workspace.ActiveDocument.Layers.CurrentUserLayer;

	private TextEngine CurrentTextEngine
		=> current_text_object?.Engine
			?? throw new InvalidOperationException ("Attempting to get CurrentTextEngine when there is no active text object");

	private TextLayout CurrentTextLayout {
		get {
			if (layout.Engine != current_text_object!.Engine)
				layout.Engine = current_text_object.Engine;
			return layout;
		}
	}

	// Re-edit hint popover, shown at the lower-right of a hovered text object
	// (mirrors the nudge hint in BaseTransformTool).
	private bool edit_hint_visible = false;
	private TextObject? edit_hint_target;
	private HitZone edit_hint_zone;
	private TextObject? hover_hint_target;
	private HitZone hover_hint_zone;
	private readonly TransientHintPopover edit_hint_popover = new ();
	//Delays showing the hover hint until the cursor has lingered for a moment.
	private uint hover_hint_timeout_id = 0;

	//While this is true, text will not be committed upon Surface.Clone calls.
	private bool ignore_clone_finalizations = false;

	//Whether or not either (or both) of the Ctrl keys are pressed.
	private bool ctrl_key = false;

	//Store the most recent mouse position.
	private PointI last_mouse_position = new (0, 0);

	public override bool UsesPaintColors => true;
	public override string Name
		=> Translations.GetString ("Text");

	public override string Icon
		=> Pinta.Resources.Icons.ToolText;

	public override Gdk.Key ShortcutKey
		=> new (Gdk.Constants.KEY_T);

	public override int Priority
		=> 35;

	public override string StatusBarText
		=> Translations.GetString ("Left click to place cursor, then type desired text. Text color is primary color. {0}+click to re-edit existing text.", PintaCore.System.CtrlLabel ());

	public override Gdk.Cursor DefaultCursor { get; }

	protected override bool ShowAntialiasingButton => true;

	private readonly IChromeService chrome;
	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;
	public TextTool (IServiceProvider services) : base (services)
	{
		IChromeService chromeService = services.GetService<IChromeService> ();

		chrome = chromeService;
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();

		im_context = Gtk.IMMulticontext.New ();
		im_context.OnCommit += OnIMCommit;
		im_context.OnPreeditStart += OnPreeditStart;
		im_context.OnPreeditChanged += OnPreeditChanged;
		im_context.OnPreeditEnd += OnPreeditEnd;

		layout = new TextLayout (chromeService);

		DefaultCursor = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Text);

		// Fulfill "select this text object" requests from the layers dock (clicking a text sub-row),
		// mirroring the shape tool's ShapeSelectRequested handling.
		LayerObjectSelection.TextSelectRequested += HandleTextSelectRequested;
	}

	// Activates the Text tool, makes the object's layer current, and starts editing it so its handles
	// show — as if the user had clicked into it on the canvas. Called via the Core bridge when a text
	// object sub-row is clicked in the layers dock.
	private void HandleTextSelectRequested (UserLayer layer, int textIndex)
	{
		if (!workspace.HasOpenDocuments)
			return;

		var layers = workspace.ActiveDocument.Layers;
		int layerIndex = layers.IndexOf (layer);
		if (layerIndex < 0 || textIndex < 0 || textIndex >= layer.TextObjects.Count)
			return;

		// Commit any in-progress edit (on whatever the current object is) before switching.
		if (is_editing)
			CommitCurrentText ();

		if (PintaCore.Tools.CurrentTool != this)
			PintaCore.Tools.SetCurrentTool (this);

		if (layers.CurrentUserLayerIndex != layerIndex)
			layers.SetCurrentUserLayer (layerIndex);

		// Re-validate: committing above may have dropped an empty object and shifted indices.
		if (textIndex >= layer.TextObjects.Count)
			return;

		StartEditing (layer.TextObjects[textIndex]);
		RedrawText (true);
	}

	#region ToolBar
	// NRT - Created by OnBuildToolBar
	private Gtk.Label font_label = null!;
	private FontFamilyDropDown font_button = null!;
	private ToolBarDropDownButton text_mode_btn = null!;
	private ToolBarDropDownButton rasterize_mode_btn = null!;
	private Gtk.Label rasterize_mode_label = null!;
	private ToolBarDropDownButton variant_btn = null!;
	private Gtk.SpinButton font_size = null!;
	private ToolBarDropDownButton weight_btn = null!;
	private Gtk.ToggleButton italic_btn = null!;
	private Gtk.ToggleButton underscore_btn = null!;
	private Gtk.ToggleButton left_alignment_btn = null!;
	private Gtk.ToggleButton center_alignment_btn = null!;
	private Gtk.ToggleButton right_alignment_btn = null!;
	private Gtk.ToggleButton justify_alignment_btn = null!;
	private Gtk.Label fill_label = null!;
	private ToolBarDropDownButton fill_button = null!;
	private Gtk.Separator fill_sep = null!;
	private Gtk.Separator outline_sep = null!;
	private Gtk.SpinButton outline_width = null!;
	private Gtk.Label outline_width_label = null!;
	private Gtk.Separator join_sep = null!;
	private ToolBarDropDownButton join_btn = null!;
	private Gtk.Separator confirm_sep = null!;
	private Gtk.Button confirm_btn = null!;
	private Gtk.Separator text_properties_sep = null!;
	private Gtk.Button text_properties_btn = null!;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		if (text_mode_btn == null) {
			text_mode_btn = ToolBarDropDownButton.New ();
			text_mode_btn.AddItem (Translations.GetString ("Point"), Pinta.Resources.Icons.ToolText, 0,
				Translations.GetString ("Point text: the text grows to its natural width and only wraps where you press Enter."));
			text_mode_btn.AddItem (Translations.GetString ("Area"), Pinta.Resources.Icons.ImageResizeCanvas, 1,
				Translations.GetString ("Area text: the text flows to fit a box. Drag a corner to resize the box and re-wrap the text."));

			text_mode_btn.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_MODE, 0);

			//For a brand-new object, Point/Area only takes effect at creation — AreaMode is read
			//there (same rule as the Object/Raster "Mode" toggle below). CanFocus=false keeps the
			//keyboard on the text input as a dropdown item is chosen, instead of letting Space
			//re-open the menu.
			text_mode_btn.CanFocus = false;

			// Justify only has an effect on area (flow) text, which has a fixed width to
			// fill. In point mode there's no box, so disable the Justify button.
			text_mode_btn.SelectedItemChanged += (_, _) => {
				if (justify_alignment_btn is not null)
					justify_alignment_btn.Sensitive = AreaMode;

				// Retroactively flip the object currently selected/being edited too, so the dropdown
				// also works as a convert-in-place control: Point -> Area boxes it at its current
				// width (no jump); Area -> Point drops the box and lets it grow free again.
				if (current_text_object is { } obj) {
					if (AreaMode && obj.Engine.WrapWidth == 0)
						obj.Engine.WrapWidth = Math.Max (MinAreaWidth, obj.TextBounds.Width);
					else if (!AreaMode && obj.Engine.WrapWidth != 0)
						obj.Engine.WrapWidth = 0;

					RedrawText (is_editing);
				}
			};
		}

		tb.Append (text_mode_btn);

		if (rasterize_mode_label == null) {
			string modeText = Translations.GetString ("Mode");
			rasterize_mode_label = Gtk.Label.New ($" {modeText}: ");
		}
		tb.Append (rasterize_mode_label);

		if (rasterize_mode_btn == null) {
			rasterize_mode_btn = ToolBarDropDownButton.New ();
			rasterize_mode_btn.AddItem (Translations.GetString ("Object — editable later"), Pinta.Resources.Icons.LayerProperties, false,
				Translations.GetString ("Stays a live, re-editable text object. Cutting, erasing, or filtering across it will rasterize it first."));
			rasterize_mode_btn.AddItem (Translations.GetString ("Raster — fuses to layer"), Pinta.Resources.Icons.LayerMergeDown, true,
				Translations.GetString ("Painted into the active layer's pixels on commit. Immediately cut/move/erase like any artwork, but not editable later."));

			// Don't let the dropdown hold keyboard focus (like every other widget on this toolbar): the
			// Text tool types with the keyboard, and a focused DropDown treats Space as "open the menu",
			// so typing a space after clicking this control would re-trigger it instead of inserting a
			// space. CanFocus=false keeps it click/pointer-usable while keeping the keyboard on the text.
			rasterize_mode_btn.CanFocus = false;

			rasterize_mode_btn.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_RASTERIZE_MODE, false) ? 1 : 0;
			//Stamps the mode for the next object created, and also retroactively re-stamps the object
			//currently selected/being edited (same convert-in-place rule as the Point/Area dropdown
			//above) - safe because RasterizeOnFinalize is only ever read at commit, so flipping it here
			//doesn't rasterize anything until the object is actually finalized. A rasterize-on-finalize
			//object gets no sub-row in the layers dock (it's transient), so switching to Raster must
			//drop its row and switching back to Object must bring it back - RaiseObjectsChanged is the
			//same seam the object's own creation uses to show up without a history push. RedrawText
			//refreshes the on-canvas "Obj." badge the same way, since it's skipped for Raster-mode text
			//(see DrawTextRectangles). CanFocus=false keeps the keyboard on the text input while toggling.
			rasterize_mode_btn.SelectedItemChanged += (_, _) => {
				Settings.PutSetting (SettingNames.TEXT_RASTERIZE_MODE, RasterizeText);

				if (current_text_object is { } obj) {
					obj.RasterizeOnFinalize = RasterizeText;
					LayerObjectSelection.RaiseObjectsChanged ();
					RedrawText (is_editing);
				}
			};
		}
		tb.Append (rasterize_mode_btn);

		tb.Append (GtkExtensions.CreateToolBarSeparator ());

		if (font_label == null) {
			string fontText = Translations.GetString ("Font");
			font_label = Gtk.Label.New ($" {fontText}: ");
		}

		tb.Append (font_label);

		if (font_button == null) {
			font_button = new FontFamilyDropDown (Pango.FontDescription.FromString (
				Settings.GetSetting (SettingNames.TEXT_FONT,
					Gtk.Settings.GetDefault ()!.GtkFontName!)));
			font_button.Widget.CanFocus = false;
			font_button.FontChanged += (_, _) => HandleFontChanged ();
		}

		tb.Append (font_button.Widget);

		tb.Append (GtkExtensions.CreateToolBarSeparator ());

		if (variant_btn == null) {
			variant_btn = ToolBarDropDownButton.New ();

			variant_btn.AddItem (
				// Translators: 'Normal' refers to the font-variant text property
				Translations.GetString ("Normal"),
				Pinta.Resources.Icons.TextVariantNormal,
				Pango.Variant.Normal,
				Translations.GetString ("Regular upper and lower case letters.")
			);
			variant_btn.AddItem (
				// Translators: 'Small Caps' refers to the font-variant text property
				Translations.GetString ("Small Caps"),
				Pinta.Resources.Icons.TextVariantSmallCaps,
				Pango.Variant.SmallCaps,
				Translations.GetString ("Lower case letters are replaced with smaller capital letters.")
			);
			variant_btn.AddItem (
				// Translators: 'All Small Caps' refers to the font-variant text property
				Translations.GetString ("All Small Caps"),
				Pinta.Resources.Icons.TextVariantAllSmallCaps,
				Pango.Variant.AllSmallCaps,
				Translations.GetString ("Both upper and lower case letters are replaced with smaller capital letters.")
			);
			variant_btn.AddItem (
				// Translators: 'Petite Caps' refers to the font-variant text property
				Translations.GetString ("Petite Caps"),
				Pinta.Resources.Icons.TextVariantPetiteCaps,
				Pango.Variant.PetiteCaps,
				Translations.GetString ("Like Small Caps, but the substituted capitals are slightly smaller.")
			);
			variant_btn.AddItem (
				// Translators: 'All Petite Caps' refers to the font-variant text property
				Translations.GetString ("All Petite Caps"),
				Pinta.Resources.Icons.TextVariantAllPetiteCaps,
				Pango.Variant.AllPetiteCaps,
				Translations.GetString ("Like All Small Caps, but the substituted capitals are slightly smaller.")
			);
			variant_btn.AddItem (
				// Translators: 'Unicase' refers to the font-variant text property
				Translations.GetString ("Unicase"),
				Pinta.Resources.Icons.TextVariantUnicase,
				Pango.Variant.Unicase,
				Translations.GetString ("Mixes small capital letters for upper case with normal lower case letters.")
			);
			variant_btn.AddItem (
				// Translators: 'Title Caps' refers to the font-variant text property
				Translations.GetString ("Title Caps"),
				Pinta.Resources.Icons.TextVariantTitleCaps,
				Pango.Variant.Normal,
				Translations.GetString ("Uses title-specific capital letter forms where available.")
			);

			variant_btn.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_VARIANT, 0);
			variant_btn.SelectedItemChanged += HandleVariantButtonChanged;
		}

		tb.Append (variant_btn);

		tb.Append (GtkExtensions.CreateToolBarSeparator ());

		if (font_size == null) {
			Gtk.Adjustment fontSizeAdjustment = Gtk.Adjustment.New (
				value: PangoExtensions.UnitsToPixels (font_button.FontDesc!.GetSize ()),
				lower: 1, upper: 2000, stepIncrement: 1, pageIncrement: 0, pageSize: 0);

			font_size = Gtk.SpinButton.New (fontSizeAdjustment, climbRate: 0.0, digits: 0);
			UpdateFontSizeTooltip ();
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => UpdateFontSizeTooltip ();
			font_size.OnValueChanged += HandleFontSizeChanged;
		}

		tb.Append (font_size);

		tb.Append (GtkExtensions.CreateToolBarSeparator ());

		if (weight_btn == null) {
			weight_btn = ToolBarDropDownButton.New ();

			weight_btn.AddItem (
				// Translators: 'Thin' (100) refers to the font-weight text property
				Translations.GetString ("Thin") + " 100",
				Pinta.Resources.Icons.TextExtraLight,
				Pango.Weight.Thin
			);
			weight_btn.AddItem (
				// Translators: 'Ultralight' (200) refers to the font-weight text property
				Translations.GetString ("Ultralight") + " 200",
				Pinta.Resources.Icons.TextExtraLight,
				Pango.Weight.Ultralight
			);
			weight_btn.AddItem (
				// Translators: 'Light' (300) refers to the font-weight text property
				Translations.GetString ("Light") + " 300",
				Pinta.Resources.Icons.TextLight,
				Pango.Weight.Light
			);
			weight_btn.AddItem (
				// Translators: 'Semilight' (350) refers to the font-weight text property
				Translations.GetString ("Semilight") + " 350",
				Pinta.Resources.Icons.TextLight,
				Pango.Weight.Semilight
			);
			weight_btn.AddItem (
				// Translators: 'Book' (380) refers to the font-weight text property
				Translations.GetString ("Book") + " 380",
				Pinta.Resources.Icons.TextNormal,
				Pango.Weight.Book
			);
			weight_btn.AddItem (
				// Translators: 'Normal' (400) refers to the font-weight text property
				Translations.GetString ("Normal") + " 400",
				Pinta.Resources.Icons.TextNormal,
				Pango.Weight.Normal
			);
			weight_btn.AddItem (
				// Translators: 'Medium' (500) refers to the font-weight text property
				Translations.GetString ("Medium") + " 500",
				Pinta.Resources.Icons.TextNormal,
				Pango.Weight.Medium
			);
			weight_btn.AddItem (
				// Translators: 'Semibold' (600) refers to the font-weight text property
				Translations.GetString ("Semibold") + " 600",
				Pinta.Resources.Icons.TextBold,
				Pango.Weight.Semibold
			);
			weight_btn.AddItem (
				// Translators: 'Bold' (700) refers to the font-weight text property
				Translations.GetString ("Bold") + " 700",
				Pinta.Resources.Icons.TextBold,
				Pango.Weight.Bold
			);
			weight_btn.AddItem (
				// Translators: 'Ultrabold' (800) refers to the font-weight text property
				Translations.GetString ("Ultrabold") + " 800",
				Pinta.Resources.Icons.TextExtraBold,
				Pango.Weight.Ultrabold
			);
			weight_btn.AddItem (
				// Translators: 'Heavy' (900) refers to the font-weight text property
				Translations.GetString ("Heavy") + " 900",
				Pinta.Resources.Icons.TextExtraBold,
				Pango.Weight.Heavy
			);
			weight_btn.AddItem (
				// Translators: 'Ultraheavy' (1000) refers to the font-weight text property
				Translations.GetString ("Ultraheavy") + " 1000",
				Pinta.Resources.Icons.TextExtraBold,
				Pango.Weight.Ultraheavy
			);

			weight_btn.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_WEIGHT, 5);
			weight_btn.SelectedItemChanged += HandleWeightButtonToggled;
		}

		tb.Append (weight_btn);

		if (italic_btn == null) {
			italic_btn = Gtk.ToggleButton.New ();
			italic_btn.IconName = Pinta.Resources.StandardIcons.FormatTextItalic;
			italic_btn.TooltipText = Translations.GetString ("Italic");
			italic_btn.CanFocus = false;
			italic_btn.Active = Settings.GetSetting (SettingNames.TEXT_ITALIC, false);
			italic_btn.OnToggled += HandleItalicButtonToggled;
		}

		tb.Append (italic_btn);

		if (underscore_btn == null) {
			underscore_btn = Gtk.ToggleButton.New ();
			underscore_btn.IconName = Pinta.Resources.StandardIcons.FormatTextUnderline;
			underscore_btn.TooltipText = Translations.GetString ("Underline");
			underscore_btn.CanFocus = false;
			underscore_btn.Active = Settings.GetSetting (SettingNames.TEXT_UNDERLINE, false);
			underscore_btn.OnToggled += HandleUnderscoreButtonToggled;
		}

		tb.Append (underscore_btn);

		tb.Append (GtkExtensions.CreateToolBarSeparator ());

		TextAlignment alignment = (TextAlignment) Settings.GetSetting (SettingNames.TEXT_ALIGNMENT, (int) TextAlignment.Left);

		if (left_alignment_btn == null) {
			left_alignment_btn = Gtk.ToggleButton.New ();
			left_alignment_btn.IconName = Pinta.Resources.StandardIcons.FormatJustifyLeft;
			left_alignment_btn.TooltipText = Translations.GetString ("Left Align");
			left_alignment_btn.CanFocus = false;
			left_alignment_btn.Active = alignment == TextAlignment.Left;
			left_alignment_btn.OnToggled += (_, _) => HandleAlignmentButtonToggled (left_alignment_btn);
		}

		tb.Append (left_alignment_btn);

		if (center_alignment_btn == null) {
			center_alignment_btn = Gtk.ToggleButton.New ();
			center_alignment_btn.IconName = Pinta.Resources.StandardIcons.FormatJustifyCenter;
			center_alignment_btn.TooltipText = Translations.GetString ("Center Align");
			center_alignment_btn.CanFocus = false;
			center_alignment_btn.Active = alignment == TextAlignment.Center;
			center_alignment_btn.OnToggled += (_, _) => HandleAlignmentButtonToggled (center_alignment_btn);
		}

		tb.Append (center_alignment_btn);

		if (right_alignment_btn == null) {
			right_alignment_btn = Gtk.ToggleButton.New ();
			right_alignment_btn.IconName = Pinta.Resources.StandardIcons.FormatJustifyRight;
			right_alignment_btn.TooltipText = Translations.GetString ("Right Align");
			right_alignment_btn.CanFocus = false;
			right_alignment_btn.Active = alignment == TextAlignment.Right;
			right_alignment_btn.OnToggled += (_, _) => HandleAlignmentButtonToggled (right_alignment_btn);
		}

		tb.Append (right_alignment_btn);

		if (justify_alignment_btn == null) {
			justify_alignment_btn = Gtk.ToggleButton.New ();
			justify_alignment_btn.IconName = Pinta.Resources.StandardIcons.FormatJustifyFill;
			justify_alignment_btn.TooltipText = Translations.GetString ("Justify");
			justify_alignment_btn.CanFocus = false;
			justify_alignment_btn.Active = alignment == TextAlignment.Justify;
			justify_alignment_btn.Sensitive = AreaMode;
			justify_alignment_btn.OnToggled += (_, _) => HandleAlignmentButtonToggled (justify_alignment_btn);
		}

		tb.Append (justify_alignment_btn);

		fill_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (fill_sep);

		if (fill_label == null) {
			string textStyleText = Translations.GetString ("Text Style");
			fill_label = Gtk.Label.New ($" {textStyleText}: ");
		}

		tb.Append (fill_label);

		if (fill_button == null) {
			fill_button = ToolBarDropDownButton.New ();

			fill_button.AddItem (Translations.GetString ("Normal"), Pinta.Resources.Icons.FillStyleFill, 0, Translations.GetString ("Fill the text with the primary color."));
			fill_button.AddItem (Translations.GetString ("Normal and Outline"), Pinta.Resources.Icons.FillStyleOutlineFill, 1, Translations.GetString ("Fill the text with the primary color and outline it with the secondary color."));
			fill_button.AddItem (Translations.GetString ("Outline"), Pinta.Resources.Icons.FillStyleOutline, 2, Translations.GetString ("Draw only the outline of the text, using the secondary color."));
			fill_button.AddItem (Translations.GetString ("Fill Background"), Pinta.Resources.Icons.FillStyleBackground, 3, Translations.GetString ("Fill the text's bounding box with the secondary color, behind the text."));

			fill_button.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_STYLE, 0);
			fill_button.SelectedItemChanged += HandleFillButtonToggled;
		}

		tb.Append (fill_button);

		outline_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (outline_sep);

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
				Settings.GetSetting (SettingNames.TEXT_OUTLINE_WIDTH, 2));
			outline_width.TooltipText = Translations.GetString ("Thickness of the text outline, in pixels.");
			outline_width.OnValueChanged += (_, __) => HandleFontChanged ();
		}

		tb.Append (outline_width);

		join_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (join_sep);

		if (join_btn == null) {
			join_btn = ToolBarDropDownButton.New ();

			join_btn.AddItem (
				// Translators: 'Miter Join' refers to the Cairo.LineJoin property
				Translations.GetString ("Miter Join"),
				Pinta.Resources.Icons.JoinMiter,
				Cairo.LineJoin.Miter,
				Translations.GetString ("Sharp, pointed corners on the outline.")
			);
			join_btn.AddItem (
				// Translators: 'Round Join' refers to the Cairo.LineJoin property
				Translations.GetString ("Round Join"),
				Pinta.Resources.Icons.JoinRound,
				Cairo.LineJoin.Round,
				Translations.GetString ("Rounded corners on the outline.")
			);
			join_btn.AddItem (
				// Translators: 'Bevel Join' refers to the Cairo.LineJoin property
				Translations.GetString ("Bevel Join"),
				Pinta.Resources.Icons.JoinBevel,
				Cairo.LineJoin.Bevel,
				Translations.GetString ("Flattened, squared-off corners on the outline.")
			);

			join_btn.SelectedIndex = Settings.GetSetting (SettingNames.TEXT_JOIN, 0);
			join_btn.SelectedItemChanged += HandleJoinButtonToggled;
		}

		tb.Append (join_btn);

		outline_width.Visible = outline_width_label.Visible = outline_sep.Visible = join_btn.Visible = join_sep.Visible = StrokeText;

		UpdateFont ();
	}

	protected override void OnBuildToolBarEnd (Gtk.Box tb)
	{
		base.OnBuildToolBarEnd (tb);

		text_properties_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (text_properties_sep);

		if (text_properties_btn == null) {
			text_properties_btn = Gtk.Button.New ();
			text_properties_btn.IconName = Pinta.Resources.Icons.LayerProperties;
			text_properties_btn.TooltipText = TextPropertiesTooltip ();
			text_properties_btn.CanFocus = false;
			text_properties_btn.OnClicked += (_, _) => {
				if (current_text_object is not null)
					OpenTextProperties (current_text_object);
			};
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => text_properties_btn.TooltipText = TextPropertiesTooltip ();
		}

		tb.Append (text_properties_btn);

		confirm_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (confirm_sep);

		if (confirm_btn == null) {
			confirm_btn = GtkExtensions.CreateConfirmToolBarButton (FinishTypingTooltip ());
			confirm_btn.OnClicked += (_, _) => CommitCurrentText ();
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => confirm_btn.TooltipText = FinishTypingTooltip ();
		}

		tb.Append (confirm_btn);

		UpdateConfirmButtonVisibility ();
	}

	// Both the properties button and the checkmark only make sense while text is
	// actively being typed/edited.
	private void UpdateConfirmButtonVisibility ()
	{
		if (confirm_btn is null || confirm_sep is null)
			return;

		confirm_btn.Visible = confirm_sep.Visible = is_editing;
		UpdateTextPropertiesButtonVisibility ();
	}

	private void UpdateTextPropertiesButtonVisibility ()
	{
		if (text_properties_btn is null || text_properties_sep is null)
			return;

		text_properties_btn.Visible = text_properties_sep.Visible = is_editing;
	}

	private static string TextPropertiesTooltip ()
		=> Translations.GetString ("Text properties ({0})",
			ClickBindingLabel (KeyboardShortcutManager.TextOpenProperties));

	private static string FinishTypingTooltip ()
		=> Translations.GetString ("Finish typing ({0})",
			PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.TextStopEditing).ToLabel ());

	private void UpdateFontSizeTooltip ()
	{
		KeyGesture decrease = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.TextDecreaseFontSize);
		KeyGesture increase = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.TextIncreaseFontSize);

		font_size.TooltipText = Translations.GetString ("Change font size.") + "\n"
			   + "\n" + Translations.GetString ("Shortcut keys:")
			   + "\n" + Translations.GetString ("Press {0} to decrease font size", decrease.ToLabel ())
			   + "\n" + Translations.GetString ("Press {0} to increase font size", increase.ToLabel ());
	}

	private void HandleFontSizeChanged (object? sender, EventArgs e)
	{
		//When the font size is being set programmatically (e.g. live while resizing by
		//dragging a corner), skip re-applying the toolbar font to the object.
		if (is_updating_font_size)
			return;

		var font = font_button.FontDesc!.Copy ()!;
		font.SetSize (PangoExtensions.UnitsFromPixels (font_size.GetValueAsInt ()));
		font_button.FontDesc = font;

		UpdateFont ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (font_button is not null)
			settings.PutSetting (SettingNames.TEXT_FONT, font_button.FontDesc!.ToString ()!);

		if (variant_btn is not null)
			settings.PutSetting (SettingNames.TEXT_VARIANT, variant_btn.SelectedIndex);

		if (weight_btn is not null)
			settings.PutSetting (SettingNames.TEXT_WEIGHT, weight_btn.SelectedIndex);

		if (italic_btn is not null)
			settings.PutSetting (SettingNames.TEXT_ITALIC, italic_btn.Active);

		if (underscore_btn is not null)
			settings.PutSetting (SettingNames.TEXT_UNDERLINE, underscore_btn.Active);

		if (left_alignment_btn is not null)
			settings.PutSetting (SettingNames.TEXT_ALIGNMENT, (int) Alignment);

		if (fill_button is not null)
			settings.PutSetting (SettingNames.TEXT_STYLE, fill_button.SelectedIndex);

		if (outline_width is not null)
			settings.PutSetting (SettingNames.TEXT_OUTLINE_WIDTH, outline_width.GetValueAsInt ());

		if (join_btn is not null)
			settings.PutSetting (SettingNames.TEXT_JOIN, join_btn.SelectedIndex);

		if (text_mode_btn is not null)
			settings.PutSetting (SettingNames.TEXT_MODE, text_mode_btn.SelectedIndex);
	}

	private void HandleFontChanged ()
	{
		var font = font_button.FontDesc!.Copy ()!;
		font.SetSize (PangoExtensions.UnitsFromPixels (font_size.GetValueAsInt ()));
		font_button.FontDesc = font;

		if (workspace.HasOpenDocuments)
			workspace.ActiveDocument.Workspace.GrabFocusToCanvas ();

		UpdateFont ();
	}

	//Whether the toolbar is set to create area (flow) text rather than point text.
	private bool AreaMode => text_mode_btn?.SelectedIndex == 1;

	//Whether new text fuses into the layer's raster on commit (Raster mode) rather than
	//staying a live, re-editable object (Object mode). Read at commit time.
	private bool RasterizeText => rasterize_mode_btn?.SelectedItem.GetTagOrDefault (false) ?? false;

	private void HandleVariantButtonChanged (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	private TextAlignment Alignment {
		get {
			if (right_alignment_btn.Active)
				return TextAlignment.Right;
			else if (center_alignment_btn.Active)
				return TextAlignment.Center;
			else if (justify_alignment_btn.Active)
				return TextAlignment.Justify;
			else
				return TextAlignment.Left;
		}
	}

	private void HandlePintaCorePalettePrimaryColorChanged (object? sender, EventArgs e)
	{
		UpdateTextEngineColor ();
		if (is_editing || current_text_object is not null)
			RedrawText (is_editing);
	}

	// The four alignment buttons act as a radio group with no GTK radio-group backing: activating
	// one deactivates the other three, and deactivating the only active one snaps it back active
	// (there is always exactly one alignment selected).
	private void HandleAlignmentButtonToggled (Gtk.ToggleButton toggled)
	{
		Gtk.ToggleButton[] all = [left_alignment_btn, center_alignment_btn, right_alignment_btn, justify_alignment_btn];

		if (toggled.Active) {
			foreach (Gtk.ToggleButton btn in all)
				if (btn != toggled)
					btn.Active = false;
		} else if (!all.Any (btn => btn != toggled && btn.Active)) {
			toggled.Active = true;
		}

		UpdateFont ();
	}

	private void HandleUnderscoreButtonToggled (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	private void HandleItalicButtonToggled (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	private void HandleWeightButtonToggled (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	private void HandleFillButtonToggled (object? sender, EventArgs e)
	{
		outline_width.Visible = outline_width_label.Visible = outline_sep.Visible = join_btn.Visible = join_sep.Visible = StrokeText;

		UpdateFont ();
	}

	private void HandleJoinButtonToggled (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	private void HandleSelectedLayerChanged (object? sender, EventArgs e)
	{
		//CurrentUserLayer may already point at the newly (de)selected layer by now, so
		//commit against editing_layer, the object's actual layer, which may since have
		//been removed from the document (still safe to commit to: the layer object and
		//its history item stay valid even if detached, e.g. an undo can bring it back).
		if (is_editing)
			CommitCurrentText (editing_layer);
		current_text_object = null;
		editing_layer = null;
		UpdateFont ();
		RedrawText (false);
	}

	// An undo/redo swaps the text objects + their TextLayer surface, but not the OverlayLayer overlay
	// (the dashed re-edit rects + blue handle dots). Rebuild the overlay from the current object list
	// so handles for a text object that doesn't exist at this history step no longer linger on canvas.
	private void HandleHistoryChanged (object? sender, EventArgs e)
	{
		if (!workspace.HasOpenDocuments)
			return;

		// The object being edited may have been removed by that step (e.g. "Rasterize All Objects"
		// bakes it into the raster and drops it). Leaving the editing session pointed at a detached
		// object keeps its handles and caret on canvas, so end the session first.
		if (current_text_object is not null && editing_layer is not null && !editing_layer.TextObjects.Contains (current_text_object))
			EndEditingSession ();

		DrawTextRectangles ();
	}

	protected override void OnAntialiasingChanged ()
	{
		UpdateFont ();
	}

	private void UpdateFont ()
	{
		if (workspace.HasOpenDocuments && current_text_object is not null) {

			var font = font_button.FontDesc!.Copy ()!; // NRT: Only nullable when nullptr is passed.
			font.SetVariant ((Pango.Variant) variant_btn.SelectedItem.GetTagOrDefault (Pango.Variant.Normal));
			font.SetWeight ((Pango.Weight) weight_btn.SelectedItem.GetTagOrDefault (Pango.Weight.Normal));
			font.SetStyle (italic_btn.Active ? Pango.Style.Italic : Pango.Style.Normal);

			current_text_object.Engine.SetFont (font, Alignment, underscore_btn.Active);

			// Style is stored per object (not read live from the toolbar at draw time),
			// so the object being created/edited picks up whatever the toolbar shows now.
			current_text_object.FillStyle = fill_button.SelectedItem.GetTagOrDefault (0);
			current_text_object.OutlineWidth = OutlineWidth;
			current_text_object.LineJoin = (Cairo.LineJoin) join_btn.SelectedIndex;
		}

		if (is_editing || current_text_object is not null)
			RedrawText (is_editing);
	}

	private void UpdateTextEngineColor ()
	{
		if (!workspace.HasOpenDocuments || current_text_object is null) return;
		current_text_object.Engine.PrimaryColor = palette.PrimaryColor;
		current_text_object.Engine.SecondaryColor = palette.SecondaryColor;
	}

	private int OutlineWidth
		=> outline_width.GetValueAsInt ();

	private bool StrokeText
		=> fill_button.SelectedItem.GetTagOrDefault (0) >= 1 && fill_button.SelectedItem.GetTagOrDefault (0) != 3;

	private bool FillText
		=> fill_button.SelectedItem.GetTagOrDefault (0) <= 1 || fill_button.SelectedItem.GetTagOrDefault (0) == 3;

	private bool BackgroundFill
		=> fill_button.SelectedItem.GetTagOrDefault (0) == 3;

	#endregion

	#region Activation/Deactivation
	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);

		// We may need to redraw our text when the color changes
		palette.PrimaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;

		workspace.LayerAdded += HandleSelectedLayerChanged;
		workspace.LayerRemoved += HandleSelectedLayerChanged;
		workspace.SelectedLayerChanged += HandleSelectedLayerChanged;

		// The re-edit overlay (dashed rects + blue handle dots) lives on the OverlayLayer, which history
		// undo/redo does NOT swap — so a step that removes a text object would leave its handles behind.
		// Refresh the overlay from the current object list on every undo/redo while we're the active tool.
		// HistoryItemAdded matters too: an op pushed from outside the tool (e.g. "Rasterize All
		// Objects" from the layers dock) removes text objects without going through RedrawText.
		if (document is not null) {
			document.History.ActionUndone += HandleHistoryChanged;
			document.History.ActionRedone += HandleHistoryChanged;
			document.History.HistoryItemAdded += HandleHistoryChanged;
		}

		// We always start off not in edit mode
		is_editing = false;
		UpdateConfirmButtonVisibility ();

		RedrawText (false);
	}

	protected override void OnCommit (Document? document)
	{
		im_context.FocusOut ();
		CommitCurrentText ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);

		// Stop listening for color change events
		palette.PrimaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;

		workspace.LayerAdded -= HandleSelectedLayerChanged;
		workspace.LayerRemoved -= HandleSelectedLayerChanged;
		workspace.SelectedLayerChanged -= HandleSelectedLayerChanged;

		if (document is not null) {
			document.History.ActionUndone -= HandleHistoryChanged;
			document.History.ActionRedone -= HandleHistoryChanged;
			document.History.HistoryItemAdded -= HandleHistoryChanged;
		}

		CommitCurrentText ();

		// Clear the re-edit rectangle overlay and the edit hint.
		if (document is not null && workspace.HasOpenDocuments) {
			try {
				document.Layers.OverlayLayer.Hidden = true;
				document.Layers.OverlayLayer.Clear ();
			} catch {
				// Workspace may be disposed.
			}
		}

		edit_hint_popover.Dispose ();
		if (hover_hint_timeout_id != 0) {
			GLib.Functions.SourceRemove (hover_hint_timeout_id);
			hover_hint_timeout_id = 0;
		}
		edit_hint_visible = false;
	}
	#endregion

	#region Mouse Handlers
	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		ctrl_key = e.IsControlPressed;
		im_context.FocusIn (); // Grab focus so we can get keystrokes
		selection = document.Selection.Clone ();
		HideEditHint ();

		switch (e.MouseButton) {
			case MouseButton.Right:
				HandleRightClick (document, e);
				break;
			case MouseButton.Left:
				HandleLeftClick (document, e);
				break;
		}
	}

	private void HandleLeftClick (Document document, ToolMouseEventArgs e)
	{
		//Store the mouse position.
		PointI pt = e.Point;

		// If we're editing this object and click inside of it, decide whether to place the
		// text cursor or to manipulate (move/rotate/resize) it. Manipulation can happen
		// without leaving text-entry mode, so an already-positioned object can still be
		// nudged around while the text input cursor is active.
		// Use the padded interaction zone (which includes the resize handles that sit outside the raw
		// text bounds) — not TextBounds — so clicking a corner handle manipulates the object being
		// edited instead of falling through to the commit path below (which, in Raster mode, would
		// bake the text before it could be resized).
		if (is_editing && current_text_object is not null) {
			TextObject editing = current_text_object;
			HitZone zone = GetHitZone (editing, e.PointDouble);

			//Rotate on the object while holding the rotate modifier.
			if (zone != HitZone.None && IsClickBindingPressed (KeyboardShortcutManager.TextRotate, e)) {
				BeginManipulation (document, editing, CurrentUserLayer, TextManipulation.Rotate, e.PointDouble);
				return;
			}

			//Corner clicks resize; border clicks move; interior clicks place the text cursor.
			if (zone == HitZone.Resize) {
				BeginManipulation (document, editing, CurrentUserLayer, TextManipulation.Resize, e.PointDouble, FindCorner (editing, e.PointDouble));
				return;
			}
			if (zone == HitZone.Move) {
				BeginManipulation (document, editing, CurrentUserLayer, TextManipulation.Move, e.UnsnappedPointDouble);
				return;
			}
			if (zone == HitZone.Interior) {
				TextPosition p = CurrentTextLayout.PointToTextPosition (pt);
				CurrentTextEngine.SetCursorPosition (p, true);

				//Redraw the text with the new cursor position.
				RedrawText (true);
				return;
			}

			// zone == None: the click is outside the object being edited — fall through to commit it
			// and start/select something else.
		}

		// Commit the previous edit (if any) before starting something new.
		if (is_editing)
			CommitCurrentText ();

		// Ctrl+Shift+Click opens the text properties window for the object under the cursor.
		if (IsClickBindingPressed (KeyboardShortcutManager.TextOpenProperties, e)) {
			(UserLayer? pl, TextObject? phit) = HitTest (pt, document, allLayers: true);
			if (phit is not null) {
				if (pl != CurrentUserLayer)
					document.Layers.SetCurrentUserLayer (pl!); // NRT - Non-null when phit is non-null.
				OpenTextProperties (phit);
			}
			return;
		}

		// Ctrl+click re-edits text (on any layer). A plain click on a text object on the
		// current layer also selects and edits it.
		(UserLayer? layer, TextObject? hit) = HitTest (pt, document, allLayers: ctrl_key || IsClickBindingPressed (KeyboardShortcutManager.TextReEdit, e));
		if (hit is not null) {

			//The mouse clicked on editable text. Switch to its layer if needed.
			if (layer != CurrentUserLayer)
				document.Layers.SetCurrentUserLayer (layer!); // NRT - Non-null when hit is non-null.

			// Holding the rotate modifier (default Alt) rotates the object about its center.
			if (IsClickBindingPressed (KeyboardShortcutManager.TextRotate, e)) {
				BeginManipulation (document, hit, layer!, TextManipulation.Rotate, e.PointDouble);
				return;
			}

			// Dragging a corner handle resizes (changes the font size proportionally).
			HitZone zone = GetHitZone (hit, e.PointDouble);
			if (zone == HitZone.Resize) {
				BeginManipulation (document, hit, layer!, TextManipulation.Resize, e.PointDouble, FindCorner (hit, e.PointDouble));
				return;
			}

			// Dragging the dashed border moves the object.
			if (zone == HitZone.Move) {
				BeginManipulation (document, hit, layer!, TextManipulation.Move, e.UnsnappedPointDouble);
				return;
			}

			//Inside the padded interaction rectangle but on the text: if we got here the
			//cursor is not on a corner or border yet was a reported hit, so just stop.
			if (zone == HitZone.None)
				return;

			//The mouse clicked inside the text. Start editing it.
			StartEditing (hit);

			//Set the cursor in the editable text where the mouse was clicked.
			TextPosition p = CurrentTextLayout.PointToTextPosition (pt);
			CurrentTextEngine.SetCursorPosition (p, true);

			//Redraw the editable text with the cursor.
			RedrawText (true);

			return;
		}

		if (ctrl_key)
			return;

		// Start editing at the cursor location as a brand new text object.
		TextObject newObject = new (new TextEngine ()) { RasterizeOnFinalize = RasterizeText };
		current_text_object = newObject;
		click_point = pt;
		UpdateFont ();
		click_point = click_point with { Y = click_point.Y - (CurrentTextLayout.FontHeight / 2) };
		newObject.Engine.Origin = click_point;
		// Snapshot the object list *before* the new object joins it: StartEditing's own capture runs
		// after the Add, so undoing the text creation would restore a list that still contains the
		// object — an empty text sub-node left behind on the layer at every earlier history step.
		IReadOnlyList<ILayerObject> objectsBeforeAdd = ObjectOpacity.CloneAll (CurrentUserLayer.Objects);
		CurrentUserLayer.AddText (newObject);
		// The object exists now but isn't pushed to history until commit, so tell the layers dock
		// directly — otherwise its sub-node row only appears one history step later.
		LayerObjectSelection.RaiseObjectsChanged ();
		StartEditing (newObject, isNewObject: true);
		undo_objects = objectsBeforeAdd;
		if (AreaMode) {
			//Draw-the-box-first: give it a provisional width and let the drag define the
			//real one (OnMouseMove). A click / tiny drag falls back to DefaultAreaWidth on
			//mouse up.
			newObject.Engine.WrapWidth = DefaultAreaWidth;
			drawing_new_box = true;
			new_box_start_x = pt.X;
			tracking = true;
			manipulation = TextManipulation.None;
		}
		RedrawText (true);
	}

	private void HandleRightClick (Document document, ToolMouseEventArgs e)
	{
		// A right click allows you to move a text object around.

		// If we're editing an object and clicked on it, move it in place without committing — the same
		// as a left-drag on its border. Committing first would (in Raster mode) bake the text and leave
		// an un-editable overlay, so the object being typed must be manipulated directly.
		if (is_editing && current_text_object is not null && GetHitZone (current_text_object, e.PointDouble) != HitZone.None) {
			BeginManipulation (document, current_text_object, CurrentUserLayer, TextManipulation.Move, e.UnsnappedPointDouble);
			return;
		}

		//Otherwise commit any active edit first, then pick up whatever object is under the cursor.
		if (is_editing)
			CommitCurrentText ();

		//Find the text object under the cursor to move.
		(UserLayer? layer, TextObject? hit) = HitTest (e.Point, document, allLayers: false);
		if (hit is null)
			return;

		if (layer != CurrentUserLayer)
			document.Layers.SetCurrentUserLayer (layer!); // NRT - Non-null when hit is non-null.

		BeginManipulation (document, hit, layer!, TextManipulation.Move, e.UnsnappedPointDouble);
	}

	/// <summary>
	/// Starts a move/rotate/resize gesture on the given text object.
	/// </summary>
	/// <summary>
	/// How much further than the raw drag to move the object, so that its
	/// bounding box lands on the grid or on the canvas guides.
	/// </summary>
	private PointD SnapMovedBounds (PointD delta)
	{
		if (!PintaCore.CanvasGrid.SnapEnabled || move_start_bounds.Width <= 0)
			return new (0, 0);

		PointD wanted = new (move_start_bounds.X + delta.X, move_start_bounds.Y + delta.Y);

		PointD snapped = PintaCore.CanvasGrid.SnapRect (
			new RectangleD (wanted, move_start_bounds.Width, move_start_bounds.Height),
			centerAnchor: false);

		return new (snapped.X - wanted.X, snapped.Y - wanted.Y);
	}

	private void BeginManipulation (Document document, TextObject obj, UserLayer layer, TextManipulation kind, PointD mouse, int corner = -1)
	{
		current_text_object = obj;
		editing_layer = layer;
		tracking = true;
		manipulation = kind;
		resize_corner = corner;
		start_mouse_xy = mouse;

		//When starting a manipulation during an active text edit, the editing session has
		//already captured the undo state, so don't clobber it. The final commit will fold
		//the move/rotate/resize into the edit's history item.
		if (!is_editing)
			CaptureUndoState ();

		switch (kind) {
			case TextManipulation.Move:
				start_click_point = obj.Engine.Origin;
				move_start_bounds = obj.TextBounds.ToDouble ();
				break;
			case TextManipulation.Rotate:
				start_rotation_angle = obj.Rotation;
				start_pointer_angle = AngleDeg (mouse, GetRotationPivot (obj));
				break;
			case TextManipulation.Resize: {
					layout.Engine = obj.Engine;
					resize_start_fontsize = PangoExtensions.UnitsToPixels (obj.Engine.Font.GetSize ());
					RectangleD pr = GetPaddedLocalRect (obj);
					PointD[] localCorners = GetLocalPaddedCorners (pr);
					resize_start_corner_dist = Math.Max (1, Distance (localCorners[corner], GetRotationPivot (obj)));
					resize_start_wrapwidth = obj.Engine.WrapWidth;
					break;
				}
		}

		//Change the cursor to indicate that the text is being manipulated.
		UpdateMouseCursor (document);
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		ctrl_key = e.IsControlPressed;

		last_mouse_position = e.Point;

		// If we're manipulating the text around, do that
		if (tracking) {

			TextObject obj = current_text_object!;

			// Area mode: defining a new flow box's width by dragging horizontally.
			if (drawing_new_box) {
				int width = Math.Abs (e.Point.X - new_box_start_x);
				if (width >= MinAreaWidth && width != obj.Engine.WrapWidth) {
					obj.Engine.WrapWidth = width;
					RedrawText (true);
				}
				return;
			}

			switch (manipulation) {
				case TextManipulation.Move: {
						//The raw cursor drives the drag: the cursor's own snap would
						//otherwise pull the object a second time, on top of its box's.
						PointD delta = new (
							e.UnsnappedPointDouble.X - start_mouse_xy.X,
							e.UnsnappedPointDouble.Y - start_mouse_xy.Y);

						PointD correction = SnapMovedBounds (delta);

						obj.Engine.Origin = new PointI (
							(int) (start_click_point.X + delta.X + correction.X),
							(int) (start_click_point.Y + delta.Y + correction.Y));
						break;
					}

				case TextManipulation.Rotate: {
						double curAngle = AngleDeg (e.PointDouble, GetRotationPivot (obj));
						obj.Rotation = NormalizeRotation (start_rotation_angle - (curAngle - start_pointer_angle));
						break;
					}

				case TextManipulation.Resize: {
						PointD pivot = GetRotationPivot (obj);
						PointD lp = RotatePoint (e.PointDouble, pivot, -RotationRadians (obj));
						double ratio = Distance (lp, pivot) / resize_start_corner_dist;

						// Area (flow) text: resize the box and re-wrap the text instead of
						// scaling the font. ponytail: reuses the corner-distance ratio, so the
						// box scales diagonally like the font handle rather than pure-horizontal.
						if (resize_start_wrapwidth > 0) {
							int newWidth = Math.Max (MinAreaWidth, (int) Math.Round (resize_start_wrapwidth * ratio));
							if (newWidth == obj.Engine.WrapWidth)
								return;
							obj.Engine.WrapWidth = newWidth;
							break;
						}

						int newSize = Math.Max (1, (int) Math.Round (resize_start_fontsize * ratio));

						// A full re-layout of every text object is expensive with lots of text.
						// Integer sizes mean many consecutive drag pixels map to the same size
						// (especially at small fonts), so skip the relayout when nothing changed.
						if (newSize == PangoExtensions.UnitsToPixels (obj.Engine.Font.GetSize ()))
							return;

						//Reflect the new size in the toolbar's font size control in realtime.
						if (font_size is not null) {
							is_updating_font_size = true;
							try {
								font_size.Adjustment!.Value = newSize;
							} finally {
								is_updating_font_size = false;
							}
						}

						Pango.FontDescription font = obj.Engine.Font.Copy ()!;
						font.SetSize (PangoExtensions.UnitsFromPixels (newSize));
						obj.Engine.SetFont (font, obj.Engine.Alignment, obj.Engine.Underline);

						// Keep the toolbar's font description in sync with the resized font, so a
						// later toolbar change (e.g. alignment) doesn't re-apply the stale
						// pre-resize size via UpdateFont().
						if (font_button is not null)
							font_button.FontDesc = font.Copy ()!;
						break;
					}
			}

			RedrawText (false);
		} else {
			UpdateMouseCursor (document, e);
			UpdateEditHint (document, e.Point);
		}
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		// If we were manipulating the text, finish that up
		if (!tracking)
			return;

		// Area mode: finish defining a freshly drawn flow box, then stay in edit mode so
		// the user can type into it. A click or too-small drag falls back to the default.
		if (drawing_new_box) {
			drawing_new_box = false;
			tracking = false;
			if (current_text_object is not null && Math.Abs (e.Point.X - new_box_start_x) < MinAreaWidth)
				current_text_object.Engine.WrapWidth = DefaultAreaWidth;
			RedrawText (true);
			UpdateMouseCursor (document);
			return;
		}

		if (current_text_object is not null) {
			bool changed = manipulation switch {
				TextManipulation.Move => current_text_object.Engine.Origin != start_click_point,
				TextManipulation.Rotate => current_text_object.Rotation != start_rotation_angle,
				TextManipulation.Resize => PangoExtensions.UnitsToPixels (current_text_object.Engine.Font.GetSize ()) != resize_start_fontsize
					|| current_text_object.Engine.WrapWidth != resize_start_wrapwidth,
				_ => false,
			};

			//While editing, the change is folded into the editing session's own history item,
			//so don't push a separate one here.
			if (changed && !is_editing)
				PushTextHistoryItem ();
		}

		//If we're still editing (manipulated without leaving text-entry mode), redraw with
		//the caret so the input cursor returns.
		RedrawText (is_editing);
		tracking = false;
		manipulation = TextManipulation.None;
		UpdateMouseCursor (document);
	}

	private void UpdateMouseCursor (Document document, ToolMouseEventArgs? e = null)
	{
		if (tracking) {
			Gdk.Cursor cursor = manipulation switch {
				TextManipulation.Rotate => cursor_rotate,
				TextManipulation.Resize => ResizeCursorForCorner (current_text_object!, resize_corner),
				_ => cursor_move,
			};
			if (CurrentCursor != cursor)
				SetCursor (cursor);
			return;
		}

		Gdk.Cursor? hoverCursor = GetHoverCursor (document, e);
		if (hoverCursor is not null) {
			if (CurrentCursor != hoverCursor)
				SetCursor (hoverCursor);
		} else if (CurrentCursor != DefaultCursor) {
			SetCursor (DefaultCursor);
		}
	}

	//Returns the resize cursor glyph for a corner (0 TL, 1 TR, 2 BR, 3 BL), accounting
	//for the object's rotation via the same octant logic the image transform tools use.
	private static Gdk.Cursor ResizeCursorForCorner (TextObject obj, int corner)
		=> ResizeCursors.ForCorner (corner, thetaDeg: -obj.Rotation);

	//The cursor to show while hovering, according to what is under the pointer.
	private Gdk.Cursor? GetHoverCursor (Document document, ToolMouseEventArgs? e)
	{
		if (!workspace.HasOpenDocuments)
			return null;

		(_, TextObject? hit) = HitTest (last_mouse_position, document, allLayers: true);
		if (hit is null || hit.IsEmpty)
			return null;

		//Holding the rotate modifier (default Alt) over an object rotates it.
		if (e is not null && IsClickBindingPressed (KeyboardShortcutManager.TextRotate, e))
			return cursor_rotate;

		HitZone zone = GetHitZone (hit, last_mouse_position.ToDouble ());
		if (zone == HitZone.Resize)
			return ResizeCursorForCorner (hit, FindCorner (hit, last_mouse_position.ToDouble ()));
		if (zone == HitZone.Move)
			return cursor_move;

		return null;
	}

	//Checks whether a mouse click matches a "click" tool binding (e.g. Ctrl+Shift+Click),
	//by matching the modifiers the user configured for that binding.
	private static bool IsClickBindingPressed (ToolBindingDescriptor binding, ToolMouseEventArgs e)
	{
		KeyGesture gesture = PintaCore.Shortcuts.GetToolBinding (binding);
		if (!gesture.IsValid)
			return false;

		return (e.State & KeyGesture.AcceleratorMask) == gesture.Modifiers;
	}

	//Renders a human-readable label for a "click" tool binding (e.g. "Ctrl+Shift+Click").
	private static string ClickBindingLabel (ToolBindingDescriptor binding)
		=> PintaCore.Shortcuts.GetToolBinding (binding).ClickBindingLabel ();
	#endregion

	#region Keyboard Handlers

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (!workspace.HasOpenDocuments)
			return false;

		// If we are dragging the text, we
		// aren't going to handle key presses
		if (tracking)
			return false;

		// Ignore anything with Alt pressed
		if (e.IsAltPressed)
			return false;

		ctrl_key = e.Key.IsControlKey ();
		UpdateMouseCursor (document);

		if (!is_editing) {
			foreach ((ToolBindingDescriptor binding, FontSizeCommand command) in font_size_bindings) {
				if (IsBinding (binding, e)) {
					ExecuteFontSizeCommand (command);
					return true;
				}
			}
		}

		bool keyHandled = false;
		if (is_editing) {
			if (preedit_string is not null && e.Event is not null) {
				// When pre-editing is active, the input method should consume all keystrokes first.
				// (e.g. Enter might be used to finish pre-editing)
				keyHandled = TryHandleChar (e.Event);
			}

			if (!keyHandled) {
				// Assume that we are going to handle the key
				keyHandled = true;

				if (TryHandleConfiguredBinding (document, e, out bool skipTrailingRedraw)) {
					if (skipTrailingRedraw)
						return true;
				} else {
					switch (e.Key.Value) {
						case Gdk.Constants.KEY_BackSpace:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextBackspace))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.Backspace, e);
							break;

						case Gdk.Constants.KEY_Delete:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextDelete))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.Delete, e);
							break;

						case Gdk.Constants.KEY_KP_Enter:
						case Gdk.Constants.KEY_Return:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextNewLine))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.NewLine, e);
							break;

						case Gdk.Constants.KEY_Left:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveLeft))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveLeft, e);
							break;

						case Gdk.Constants.KEY_Right:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveRight))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveRight, e);
							break;

						case Gdk.Constants.KEY_Up:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveUp))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveUp, e);
							break;

						case Gdk.Constants.KEY_Down:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveDown))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveDown, e);
							break;

						case Gdk.Constants.KEY_Home:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveHome))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveHome, e);
							break;

						case Gdk.Constants.KEY_End:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextMoveEnd))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.MoveEnd, e);
							break;

						case Gdk.Constants.KEY_Next:
						case Gdk.Constants.KEY_Prior:
							break;

						case Gdk.Constants.KEY_Escape:
							if (!IsDefaultBinding (KeyboardShortcutManager.TextStopEditing))
								return false;
							ExecuteTextKeyCommand (document, TextKeyCommand.StopEditing, e);
							return true;
						case Gdk.Constants.KEY_Insert:
							if ((e.IsShiftPressed && !IsDefaultBinding (KeyboardShortcutManager.TextPaste)) ||
								(e.IsControlPressed && !IsDefaultBinding (KeyboardShortcutManager.TextCopy)))
								return false;
							if (e.IsShiftPressed) {
								ExecuteTextKeyCommand (document, TextKeyCommand.Paste, e);
							} else if (e.IsControlPressed) {
								ExecuteTextKeyCommand (document, TextKeyCommand.Copy, e);
							}
							break;
						default:
							if (e.IsControlPressed) {
								if (e.Key.Value == Gdk.Constants.KEY_z) {
									if (!IsDefaultBinding (KeyboardShortcutManager.TextUndo))
										return false;
									//Ctrl + Z for undo while editing.
									ExecuteTextKeyCommand (document, TextKeyCommand.Undo, e);
								} else if (e.Key.Value == Gdk.Constants.KEY_i) {
									if (!IsDefaultBinding (KeyboardShortcutManager.TextItalic))
										return false;
									ExecuteTextKeyCommand (document, TextKeyCommand.Italic, e);
								} else if (e.Key.Value == Gdk.Constants.KEY_b) {
									if (!IsDefaultBinding (KeyboardShortcutManager.TextBold))
										return false;
									ExecuteTextKeyCommand (document, TextKeyCommand.Bold, e);
								} else if (e.Key.Value == Gdk.Constants.KEY_u) {
									if (!IsDefaultBinding (KeyboardShortcutManager.TextUnderline))
										return false;
									ExecuteTextKeyCommand (document, TextKeyCommand.Underline, e);
								} else if (e.Key.Value == Gdk.Constants.KEY_a) {
									if (!IsDefaultBinding (KeyboardShortcutManager.TextSelectAll))
										return false;
									ExecuteTextKeyCommand (document, TextKeyCommand.SelectAll, e);
								} else {
									//Ignore command shortcut.
									return false;
								}
							} else {
								if (e.Event is not null)
									keyHandled = TryHandleChar (e.Event);
							}

							break;
					}
				}
			}

			if (keyHandled)
				RedrawText (true);
		} else {
			switch (e.Key.Value) {
				case Gdk.Constants.KEY_bracketleft:
					if (!IsDefaultBinding (KeyboardShortcutManager.TextDecreaseFontSize))
						return false;
					ExecuteFontSizeCommand (FontSizeCommand.Decrease);
					return true;
				case Gdk.Constants.KEY_bracketright:
					if (!IsDefaultBinding (KeyboardShortcutManager.TextIncreaseFontSize))
						return false;
					ExecuteFontSizeCommand (FontSizeCommand.Increase);
					return true;
			}
		}

		// Keyguard: while editing text, never let a bare key fall through unhandled to the global key
		// handler, which would treat it as a toolbox shortcut and switch tools mid-type (e.g. typing "e"
		// jumping to the Ellipse tool). Modifier combos are already returned above — Alt exits early and
		// unrecognized Ctrl combos return false — so those legitimate global shortcuts still pass through.
		if (is_editing && !keyHandled)
			return true;

		return keyHandled;
	}

	private static bool IsBinding (ToolBindingDescriptor binding, ToolKeyEventArgs e)
		=> PintaCore.Shortcuts.GetToolBinding (binding) == e.Gesture;

	private static bool IsDefaultBinding (ToolBindingDescriptor binding)
		=> PintaCore.Shortcuts.GetToolBinding (binding) == binding.DefaultGesture;

	internal enum FontSizeCommand { Decrease, Increase }

	internal static readonly (ToolBindingDescriptor Binding, FontSizeCommand Command)[] font_size_bindings = [
		(KeyboardShortcutManager.TextDecreaseFontSize, FontSizeCommand.Decrease),
		(KeyboardShortcutManager.TextIncreaseFontSize, FontSizeCommand.Increase),
	];

	private void ExecuteFontSizeCommand (FontSizeCommand command)
	{
		switch (command) {
			case FontSizeCommand.Decrease: font_size.Adjustment!.Value--; break;
			case FontSizeCommand.Increase: font_size.Adjustment!.Value++; break;
			default: throw new ArgumentOutOfRangeException (nameof (command));
		}
	}

	// Shared by both the configured-binding loop (TryHandleConfiguredBinding) and the physical-key
	// fallback switch in OnKeyDown, so a command behaves identically regardless of which path
	// dispatched it. Only StopEditing skips OnKeyDown's trailing "if (keyHandled) RedrawText" -
	// CommitCurrentText already redraws internally, so both dispatch paths return true immediately
	// after it rather than redrawing twice.
	internal enum TextKeyCommand
	{
		StopEditing,
		NewLine,
		Backspace,
		Delete,
		MoveLeft,
		MoveRight,
		MoveUp,
		MoveDown,
		MoveHome,
		MoveEnd,
		Undo,
		Italic,
		Bold,
		Underline,
		SelectAll,
		Paste,
		Copy,
	}

	// Order matters: if two bindings were ever configured to the same gesture, the first match here
	// wins, exactly as the previous if-chain did.
	internal static readonly (ToolBindingDescriptor Binding, TextKeyCommand Command)[] text_key_bindings = [
		(KeyboardShortcutManager.TextStopEditing, TextKeyCommand.StopEditing),
		(KeyboardShortcutManager.TextNewLine, TextKeyCommand.NewLine),
		(KeyboardShortcutManager.TextBackspace, TextKeyCommand.Backspace),
		(KeyboardShortcutManager.TextDelete, TextKeyCommand.Delete),
		(KeyboardShortcutManager.TextMoveLeft, TextKeyCommand.MoveLeft),
		(KeyboardShortcutManager.TextMoveRight, TextKeyCommand.MoveRight),
		(KeyboardShortcutManager.TextMoveUp, TextKeyCommand.MoveUp),
		(KeyboardShortcutManager.TextMoveDown, TextKeyCommand.MoveDown),
		(KeyboardShortcutManager.TextMoveHome, TextKeyCommand.MoveHome),
		(KeyboardShortcutManager.TextMoveEnd, TextKeyCommand.MoveEnd),
		(KeyboardShortcutManager.TextUndo, TextKeyCommand.Undo),
		(KeyboardShortcutManager.TextItalic, TextKeyCommand.Italic),
		(KeyboardShortcutManager.TextBold, TextKeyCommand.Bold),
		(KeyboardShortcutManager.TextUnderline, TextKeyCommand.Underline),
		(KeyboardShortcutManager.TextSelectAll, TextKeyCommand.SelectAll),
		(KeyboardShortcutManager.TextPaste, TextKeyCommand.Paste),
		(KeyboardShortcutManager.TextCopy, TextKeyCommand.Copy),
	];

	private bool TryHandleConfiguredBinding (Document document, ToolKeyEventArgs e, out bool skipTrailingRedraw)
	{
		foreach ((ToolBindingDescriptor binding, TextKeyCommand command) in text_key_bindings) {
			if (!IsBinding (binding, e))
				continue;

			ExecuteTextKeyCommand (document, command, e);
			skipTrailingRedraw = command == TextKeyCommand.StopEditing;
			return true;
		}

		skipTrailingRedraw = false;
		return false;
	}

	private void ExecuteTextKeyCommand (Document document, TextKeyCommand command, ToolKeyEventArgs e)
	{
		switch (command) {
			case TextKeyCommand.StopEditing:
				CommitCurrentText ();
				break;
			case TextKeyCommand.NewLine:
				CurrentTextEngine.PerformEnter ();
				break;
			case TextKeyCommand.Backspace:
				CurrentTextEngine.PerformBackspace (e.IsControlPressed);
				break;
			case TextKeyCommand.Delete:
				CurrentTextEngine.PerformDelete ();
				break;
			case TextKeyCommand.MoveLeft:
				CurrentTextEngine.PerformLeft (e.IsControlPressed, e.IsShiftPressed);
				break;
			case TextKeyCommand.MoveRight:
				CurrentTextEngine.PerformRight (e.IsControlPressed, e.IsShiftPressed);
				break;
			case TextKeyCommand.MoveUp:
				CurrentTextEngine.PerformUp (e.IsShiftPressed);
				break;
			case TextKeyCommand.MoveDown:
				CurrentTextEngine.PerformDown (e.IsShiftPressed);
				break;
			case TextKeyCommand.MoveHome:
				CurrentTextEngine.PerformHome (e.IsControlPressed, e.IsShiftPressed);
				break;
			case TextKeyCommand.MoveEnd:
				CurrentTextEngine.PerformEnd (e.IsControlPressed, e.IsShiftPressed);
				break;
			case TextKeyCommand.Undo:
				OnHandleUndo (document);
				if (workspace.ActiveDocument.History.CanUndo)
					workspace.ActiveDocument.History.Undo ();
				break;
			case TextKeyCommand.Italic:
				italic_btn.Toggle ();
				UpdateFont ();
				break;
			case TextKeyCommand.Bold:
				weight_btn.SelectedIndex = weight_btn.SelectedIndex > 7 ? 5 : 8;
				UpdateFont ();
				break;
			case TextKeyCommand.Underline:
				underscore_btn.Toggle ();
				UpdateFont ();
				break;
			case TextKeyCommand.SelectAll:
				CurrentTextEngine.PerformHome (true, false);
				CurrentTextEngine.PerformEnd (true, true);
				break;
			case TextKeyCommand.Paste:
				PerformPasteAndRedraw ();
				break;
			case TextKeyCommand.Copy:
				CurrentTextEngine.PerformCopy (GdkExtensions.GetDefaultClipboard ());
				break;
			default:
				throw new ArgumentOutOfRangeException (nameof (command));
		}
	}

	protected override bool OnKeyUp (Document document, ToolKeyEventArgs e)
	{
		if (!e.Key.IsControlKey () && !e.IsControlPressed)
			return false;

		ctrl_key = false;

		UpdateMouseCursor (document);
		return false;
	}

	private bool TryHandleChar (Gdk.Event eventKey)
	{
		// Try to handle it as a character
		if (im_context.FilterKeypress (eventKey))
			return true;

		// We didn't handle the key
		return false;
	}

	private void OnIMCommit (object o, Gtk.IMContext.CommitSignalArgs args)
	{
		try {
			// Reset the pre-edit string. Depending on the platform there might still be
			// a preedit-changed signal (setting it to the empty string) after the commit, rather than before.
			UpdatePreeditString (string.Empty, redraw: false);

			CurrentTextEngine.InsertText (args.Str);
			RedrawText (true);
		} finally {
			im_context.Reset ();
		}
	}

	private void OnPreeditStart (object o, EventArgs args)
	{
		// Initialize to empty string (null means pre-editing is inactive).
		preedit_string = string.Empty;
	}

	private void OnPreeditEnd (object o, EventArgs args)
	{
		// Reset to indicate that pre-editing is done. There should have previously been
		// a preedit-changed signal to erase the last preedited string.
		preedit_string = null;
	}

	private void OnPreeditChanged (object o, EventArgs args)
	{
		// TODO - use the Pango.AttrList argument to better visualize the pre-edited text vs the regular text.
		im_context.GetPreeditString (out string updated_str, out _, out _);
		UpdatePreeditString (updated_str, redraw: true);
	}

	private void UpdatePreeditString (string updated, bool redraw)
	{
		// Remove the previous preedit string.
		for (int i = 0; i < preedit_string?.Length; ++i)
			CurrentTextEngine.PerformBackspace (false);

		// Insert the new string.
		preedit_string = updated;
		CurrentTextEngine.InsertText (preedit_string);

		RedrawText (true);
	}

	#endregion

	#region Start/Stop Editing

	// isNewObject stamps the current palette colors onto a just-created object, so it starts out
	// in whatever color the user has selected. Re-editing an existing object (a canvas click or a
	// layers-dock sub-row click) must NOT do this — it would recolor already-typed text to whatever
	// the palette happens to show, with no action from the user beyond selecting the object.
	private void StartEditing (TextObject obj, bool isNewObject = false)
	{
		if (!workspace.HasOpenDocuments)
			return;

		// Ensure we have an event handler to commit the current edit if the layer is cloned.
		workspace.ActiveDocument.LayerCloned -= HandleLayerCloned;
		workspace.ActiveDocument.LayerCloned += HandleLayerCloned;

		current_text_object = obj;
		editing_layer = CurrentUserLayer;
		is_editing = true;
		UpdateConfirmButtonVisibility ();

		im_context.SetClientWidget (workspace.ActiveWorkspace.Canvas);

		selection ??= workspace.ActiveDocument.Selection.Clone ();

		CaptureUndoState ();

		if (isNewObject)
			//Update Text Engine to use current colors of color palette
			UpdateTextEngineColor ();

		//Show this object's own font/style in the toolbar, rather than whatever the
		//toolbar last showed for a different object (or its defaults, for a brand new
		//one). UpdateFont() feeds any of these controls back onto `obj` as it applies
		//them, so this is a no-op for a freshly created object (whose font/style were
		//already set from the toolbar just before StartEditing was called).
		SyncToolbarFromObject (obj);
	}

	/// <summary>
	/// Sets the toolbar's font and style controls from the given object's own stored
	/// font, alignment, underline, fill style, outline width, and line join.
	/// </summary>
	private void SyncToolbarFromObject (TextObject obj)
	{
		TextEngine engine = obj.Engine;
		Pango.FontDescription font = engine.Font;

		font_button.FontDesc = font.Copy ()!;
		font_size.Adjustment!.Value = PangoExtensions.UnitsToPixels (font.GetSize ());
		variant_btn.SelectedIndex = IndexOfTag (variant_btn, font.GetVariant ());
		weight_btn.SelectedIndex = IndexOfTag (weight_btn, font.GetWeight ());
		italic_btn.Active = font.GetStyle () == Pango.Style.Italic;
		underscore_btn.Active = engine.Underline;

		left_alignment_btn.Active = engine.Alignment == TextAlignment.Left;
		center_alignment_btn.Active = engine.Alignment == TextAlignment.Center;
		right_alignment_btn.Active = engine.Alignment == TextAlignment.Right;
		justify_alignment_btn.Active = engine.Alignment == TextAlignment.Justify;

		fill_button.SelectedIndex = obj.FillStyle;
		outline_width.Adjustment!.Value = obj.OutlineWidth;
		join_btn.SelectedIndex = IndexOfTag (join_btn, obj.LineJoin);

		outline_width.Visible = outline_width_label.Visible = outline_sep.Visible = join_btn.Visible = join_sep.Visible = StrokeText;
	}

	/// <summary>
	/// Finds the index of the item in a ToolBarDropDownButton whose tag equals the
	/// given value, or 0 if none match.
	/// </summary>
	private static int IndexOfTag<T> (ToolBarDropDownButton button, T value)
	{
		for (int i = 0; i < button.Items.Count; i++)
			if (Equals (button.Items[i].Tag, value))
				return i;
		return 0;
	}

	/// <summary>
	/// Commits the current edit to the layer it actually belongs to. Normally that's
	/// CurrentUserLayer, but a layer-change event can repoint CurrentUserLayer at a
	/// different layer before the edit is committed, so callers reacting to such an
	/// event must pass the object's real layer explicitly (see HandleSelectedLayerChanged).
	/// </summary>
	private void CommitCurrentText (UserLayer? targetLayer = null)
	{
		if (!workspace.HasOpenDocuments || current_text_object is null)
			return;

		UserLayer layer = targetLayer ?? CurrentUserLayer;

		im_context.SetClientWidget (null);

		TextObject committed = current_text_object;

		// A fresh object that never received text is simply dropped.
		if (committed.IsEmpty) {
			layer.RemoveObject (committed);
			LayerObjectSelection.RaiseObjectsChanged ();
		}

		//Re-render the layer's TextLayer so the history item captures the committed state.
		//CurrentUserLayer already equals `layer` in the common case, where the usual
		//full redraw (invalidation, tool-layer overlay, cursor bounds) applies. Only the
		//layer-change-event path (HandleSelectedLayerChanged) commits a layer other than
		//the now-active one, where that full redraw would otherwise render onto the wrong
		//(new current) layer instead of finalizing the old one.
		if (layer == CurrentUserLayer)
			RedrawText (false);
		else
			RedrawTextLayerSurface (layer);

		PushTextHistoryItem (layer);

		// Capture the frozen editing selection before EndEditingSession clears it, so the
		// Raster-mode bake below clips to the same region the preview did.
		DocumentSelection? rasterClip = selection;

		EndEditingSession ();

		// Raster mode: fuse the just-committed text into the layer's base raster and drop it as an
		// object (mirrors the shape tool's Raster mode). Its own history step, right after the commit,
		// so one undo brings the editable text back and another removes it. Skipped for empty (dropped)
		// text. No confirmation prompt — the mode was chosen deliberately.
		if (committed.RasterizeOnFinalize && !committed.IsEmpty) {
			int index = IndexOfTextObject (layer, committed);
			if (index >= 0)
				ObjectRasterizer.RasterizeSubset (
					workspace.ActiveDocument, workspace, chrome, layer,
					shapeIndices: [], textIndices: [index], textClip: rasterClip);
		}
	}

	private void HandleLayerCloned ()
	{
		// Surface.Clone calls happen during our own undo capture and history pushes.
		if (ignore_clone_finalizations || !is_editing)
			return;

		CommitCurrentText ();
	}

	private void CaptureUndoState ()
	{
		ignore_clone_finalizations = true;

		//Store the previous state of the current UserLayer's and ObjectLayer's ImageSurfaces.
		user_undo_surface = CurrentUserLayer.Surface.Clone ();
		text_undo_surface = CurrentUserLayer.ObjectLayer.Layer.Surface.Clone ();

		ignore_clone_finalizations = false;

		//Store the previous state of the unified objects.
		undo_objects = ObjectOpacity.CloneAll (CurrentUserLayer.Objects);
	}

	private void PushTextHistoryItem (UserLayer? targetLayer = null)
	{
		if (!workspace.HasOpenDocuments || text_undo_surface is null || user_undo_surface is null || undo_objects is null)
			return;

		UserLayer layer = targetLayer ?? CurrentUserLayer;

		// Nothing actually changed (e.g. the user clicked an object without editing it).
		if (SurfaceDiff.Create (text_undo_surface, layer.ObjectLayer.Layer.Surface, force: true) == null)
			return;

		Document doc = workspace.ActiveDocument;

		//Start ignoring any Surface.Clone calls from this point on (so that it doesn't start to loop).
		ignore_clone_finalizations = true;

		//Create a new TextHistoryItem so that the committing of text can be undone.
		doc.History.PushNewItem (
			new TextHistoryItem (
				workspace,
				Icon,
				Name,
				text_undo_surface.Clone (),
				user_undo_surface.Clone (),
				undo_objects,
				layer
			)
		);

		//Stop ignoring any Surface.Clone calls from this point on.
		ignore_clone_finalizations = false;
	}

	// Index of a text object among the layer's text objects, or -1 if it is not present.
	private static int IndexOfTextObject (UserLayer layer, TextObject target)
	{
		IReadOnlyList<TextObject> texts = layer.TextObjects;
		for (int i = 0; i < texts.Count; ++i)
			if (ReferenceEquals (texts[i], target))
				return i;
		return -1;
	}

	private void EndEditingSession ()
	{
		is_editing = false;
		current_text_object = null;
		editing_layer = null;
		UpdateConfirmButtonVisibility ();

		undo_objects = null;
		text_undo_surface = null;
		user_undo_surface = null;
		selection = null;
		old_cursor_bounds = RectangleI.Zero;
	}

	#endregion

	#region Text Properties Window

	/// <summary>
	/// Opens the Text Properties window for the given text object. Changes are applied
	/// live; a single history item is pushed when the window closes (only if something
	/// actually changed).
	/// </summary>
	private void OpenTextProperties (TextObject obj)
	{
		if (!workspace.HasOpenDocuments)
			return;

		//Commit any in-progress edit so the object is in a stable, final state.
		if (is_editing)
			CommitCurrentText ();

		CaptureUndoState ();
		RedrawText (false);

		TextPropertiesDialog? dialog = null;
		dialog = new TextPropertiesDialog (
			chrome.MainWindow,
			obj,
			() => RedrawText (false),
			() => {
				RedrawText (false);
				PushTextHistoryItem ();
				dialog?.Dispose ();
			});

		dialog.Present ();
	}

	#endregion

	#region Text Drawing Methods

	/// <summary>
	/// Clears the entire TextLayer and redraws every text object.
	/// </summary>
	private void RedrawText (bool showCursor)
	{
		if (!workspace.HasOpenDocuments)
			return;

		UserLayer userLayer = CurrentUserLayer;

		// Invalidate the previous bounds of every object.
		foreach (TextObject obj in userLayer.TextObjects)
			InflateAndInvalidate (obj.PreviousTextBounds);

		// Clear the shared ObjectLayer surface and re-render every object on it in unified z-order, so
		// redrawing text neither wipes the shapes sharing the surface nor restacks them. Drawing all
		// shapes first and all text after would put every text above every shape, so merely clicking a
		// text row — which lands here — would recomposite the layer differently from every other render
		// path (ObjectOpacity.RenderLayerObjects) and change how a blended shape mixes with the text
		// beneath it.
		ImageSurface surface = userLayer.ObjectLayer.Layer.Surface;
		surface.Clear ();

		RectangleI allBounds = RectangleI.Zero;
		RectangleI cursorBounds = RectangleI.Zero;

		ObjectLayerRenderWalk.Walk (
			userLayer,
			renderShape: shape => {
				if (!shape.RasterizeOnFinalize)
					LayerObjectSelection.RenderShape (surface, userLayer, shape);
			},
			renderText: obj => {
				// Skip empty objects, but keep rendering the caret for the one being typed into.
				if (obj.IsEmpty && obj != current_text_object)
					return;

				RectangleI r = GetTextObjectBounds (obj);
				obj.PreviousTextBounds = obj.TextBounds;
				obj.TextBounds = r;
				allBounds = allBounds.Union (r);

				DrawTextObject (userLayer, obj);
			});

		if (is_editing && current_text_object is not null) {
			layout.Engine = current_text_object.Engine;
			cursorBounds = layout.GetCursorLocation ().Inflated (2, 10);
		}

		FoldObjectsIntoComposite (userLayer);

		InflateAndInvalidate (allBounds);
		workspace.Invalidate (old_cursor_bounds);
		workspace.Invalidate (cursorBounds);

		old_cursor_bounds = cursorBounds;

		//Keep the font-dropdown row preview showing a snippet of what's being typed. Only
		//update while there's an object with text; don't clear it when editing state drops
		//(e.g. focus moves to the dropdown), so the sample is still there when the popup opens.
		if (font_button is not null && current_text_object is not null) {
			string text = current_text_object.Engine.ToString ();
			if (text.Length > 0)
				font_button.SampleText = text;
		}

		//Draw the re-edit rectangles as a tool-layer overlay so they never get saved.
		DrawTextRectangles (showCursor);
	}

	/// <summary>
	/// Clears and redraws every text object on the given layer's TextLayer surface,
	/// without a cursor. Used instead of <see cref="RedrawText"/> when finalizing a
	/// layer other than CurrentUserLayer (see CommitCurrentText), since RedrawText
	/// always operates on CurrentUserLayer and would otherwise render onto the wrong
	/// layer's surface and overlay the wrong layer's re-edit rectangles.
	/// </summary>
	private void RedrawTextLayerSurface (UserLayer layer)
	{
		ImageSurface surface = layer.ObjectLayer.Layer.Surface;
		surface.Clear ();

		// Unified z-order, for the same reason as RedrawText.
		ObjectLayerRenderWalk.Walk (
			layer,
			renderShape: shape => {
				if (!shape.RasterizeOnFinalize)
					LayerObjectSelection.RenderShape (surface, layer, shape);
			},
			renderText: obj => {
				if (obj.IsEmpty)
					return;

				RectangleI r = GetTextObjectBounds (obj);
				obj.PreviousTextBounds = obj.TextBounds;
				obj.TextBounds = r;

				DrawTextObject (layer, obj);
			});

		FoldObjectsIntoComposite (layer);
	}

	/// <summary>
	/// A layer carrying modifier nodes is painted from <see cref="UserLayer.Composite"/> rather than
	/// from the object surface the redraws above write to (see UserLayer.GetLayersToPaint), so text
	/// drawn there would stay invisible until something else rebuilt the accumulator. Re-running the
	/// accumulator walk is enough: a text object's engine is its own store, so it already holds what
	/// was just typed or dragged. The caret is not part of this — it is drawn on the tool layer (see
	/// <see cref="DrawCursor"/>), which this walk does not touch.
	/// </summary>
	private void FoldObjectsIntoComposite (UserLayer layer)
		=> ObjectOpacity.FoldObjectSurfaceIntoComposite (workspace, PintaCore.Chrome, layer);

	/// <summary>
	/// Computes the on-canvas bounds of a text object, accounting for rotation.
	/// </summary>
	private RectangleI GetTextObjectBounds (TextObject obj)
		=> GetRotatedBounds (obj);

	/// <summary>
	/// Draws a single text object (using its own font, colors, and style) onto the given layer's TextLayer.
	/// </summary>
	/// <param name="layer">The layer to draw onto.</param>
	/// <param name="obj">The text object to draw.</param>
	private void DrawTextObject (UserLayer layer, TextObject obj)
		=> ObjectOpacity.Draw (
			layer.ObjectLayer.Layer.Surface,
			obj,
			target => DrawTextObjectOpaque (target, obj));

	private void DrawTextObjectOpaque (ImageSurface surf, TextObject obj)
	{
		//Clip Raster-mode text to the editing selection on every render — preview and the
		//final commit render alike — so the portion outside the selection is never drawn and
		//never bakes in (it stays invisible through finalize, in real time as you type/resize,
		//not one render behind). Keyed off the object's mode plus a live selection rather than
		//isActive: the commit path redraws with isActive:false but before EndEditingSession
		//clears `selection`. Object-mode text lives on its own sub-layer and is never clipped,
		//so it stays visible while editing and when re-selected from the dock even if it lies
		//outside the leftover selection.
		DocumentSelection? clip = obj.RasterizeOnFinalize ? selection : null;

		//The one draw routine, shared with every non-tool render path.
		TextObjectRenderer.RenderOpaque (surf, obj, layout, chrome, UseAntialiasing, clip);
	}

	/// <summary>
	/// Draws the highlight behind the selected text of the object being typed into, on the tool layer
	/// and for the same reason as <see cref="DrawCursor"/>: the accumulator rebuilds a modifier layer's
	/// composite from the objects alone, so a highlight drawn into the object surface was overwritten.
	/// It also keeps the highlight out of a raster-mode commit, which bakes the object surface.
	/// </summary>
	private void DrawSelectionHighlight (Context g, TextObject obj)
	{
		layout.Engine = obj.Engine;

		Color highlight = new (
			R: 0.7,
			G: 0.8,
			B: 0.9,
			A: 0.5);

		g.Save ();
		ApplyRotation (g, obj);
		foreach (RectangleI rect in layout.GetSelectionRectangles ())
			g.FillRectangle (rect.ToDouble (), highlight);
		g.Restore ();
	}

	/// <summary>
	/// Draws the caret for the object being typed into, on the tool layer. It belongs there rather than
	/// on the object surface: a layer carrying modifier nodes is painted from its composite, which the
	/// accumulator rebuilds from the objects alone, so a caret drawn into the object surface was
	/// overwritten and you typed blind.
	/// </summary>
	private void DrawCursor (Context g, TextObject obj)
	{
		layout.Engine = obj.Engine;
		RectangleI loc = layout.GetCursorLocation ();

		g.Save ();
		ApplyRotation (g, obj);
		g.DrawLine (
			new PointD (loc.X, loc.Y),
			new PointD (loc.X, loc.Y + loc.Height),
			obj.Engine.PrimaryColor,
			1);
		g.Restore ();
	}

	/// <summary>
	/// Draws the dashed re-edit rectangles for every text object on the current
	/// layer, on the overlay layer, so they are shown on the canvas but never saved. The caret rides
	/// along here for the same reason.
	/// </summary>
	private void DrawTextRectangles (bool showCursor = true)
	{
		if (!workspace.HasOpenDocuments)
			return;

		Document doc = workspace.ActiveDocument;
		Layer toolLayer = doc.Layers.OverlayLayer;
		toolLayer.Clear ();
		toolLayer.Hidden = false;

		using Context g = new (toolLayer.Surface);

		g.Save ();

		g.Translate (.5, .5);

		foreach (TextObject obj in CurrentUserLayer.TextObjects) {
			if (obj.IsEmpty)
				continue;

			//Draw the rotated text interaction rectangle (the dashed outline).
			PointD[] corners = GetInteractionCorners (obj);

			g.MoveTo (corners[3].X, corners[3].Y);
			foreach (PointD corner in corners)
				g.LineTo (corner.X, corner.Y);
			g.ClosePath ();

			g.LineWidth = 1;

			g.SetSourceColor (new Color (1, 1, 1));
			g.StrokePreserve ();

			g.SetDash ([2, 4], 0);
			g.SetSourceColor (new Color (1, .1, .2));

			g.Stroke ();

			//Draw the corner resize handles as blue dots, matching the look of the
			//selection / shape-handle grips used elsewhere in the app.
			g.Save ();
			g.SetDash ([], 0);
			const double HANDLE_RADIUS = 5;
			foreach (PointD corner in corners) {
				g.NewPath ();
				g.Arc (corner.X, corner.Y, HANDLE_RADIUS, 0, 2 * Math.PI);
				g.ClosePath ();
				g.SetSourceColor (new Color (0, 0, 1));
				g.FillPreserve ();
				g.LineWidth = 1.5;
				g.SetSourceColor (new Color (1, 1, 1, 0.85));
				g.Stroke ();
			}
			g.Restore ();

			// "Obj." editable-object badge at the field's lower-left corner (skipped for Raster-mode
			// text, which isn't a persistent object). Positioned just below the lowest-left corner.
			if (!obj.RasterizeOnFinalize) {
				double minX = corners[0].X, maxY = corners[0].Y;
				foreach (PointD c in corners) {
					minX = Math.Min (minX, c.X);
					maxY = Math.Max (maxY, c.Y);
				}
				EditableObjectBadge.Draw (g, new PointD (minX, maxY + 3), EditableObjectBadge.CanvasColor);
			}
		}

		// After the rectangles, so neither is hidden under a handle. Drawn even for an object
		// with no text yet — that empty field is exactly where the caret has to be visible.
		if (showCursor && is_editing && current_text_object is not null) {
			DrawSelectionHighlight (g, current_text_object);
			DrawCursor (g, current_text_object);
		}

		g.Restore ();

		doc.Workspace.Invalidate ();
	}

	private void InflateAndInvalidate (in RectangleI passedRectangle)
	{
		//Create a new instance to preserve the passed Rectangle.
		RectangleI r = new (
			passedRectangle.Location,
			passedRectangle.Size);

		r = r.Inflated (2, 2);

		workspace.Invalidate (r);
	}

	#endregion

	#region Text Manipulation Geometry

	private static double DegToRad (double deg) => deg * Math.PI / 180.0;
	private static double RadToDeg (double rad) => rad * 180.0 / Math.PI;

	//A positive Rotation (degrees) renders as counter-clockwise on screen.
	private static double RotationRadians (TextObject obj) => obj.RotationRadians;

	private static PointD RotatePoint (PointD p, PointD center, double angleRad)
	{
		double dx = p.X - center.X;
		double dy = p.Y - center.Y;
		double cos = Math.Cos (angleRad);
		double sin = Math.Sin (angleRad);
		return new PointD (
			center.X + dx * cos - dy * sin,
			center.Y + dx * sin + dy * cos);
	}

	private static double Distance (PointD a, PointD b)
	{
		double dx = a.X - b.X;
		double dy = a.Y - b.Y;
		return Math.Sqrt (dx * dx + dy * dy);
	}

	private static double AngleDeg (PointD p, PointD center)
		=> Math.Atan2 (p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI;

	private static double NormalizeRotation (double degrees)
	{
		degrees %= 360;
		if (degrees < 0)
			degrees += 360;
		return degrees;
	}

	//Rotates the given context about the text object's fixed top-left origin so the
	//whole object (fill, stroke, selection highlight, and caret) renders rotated.
	private static void ApplyRotation (Context g, TextObject obj)
		=> TextObjectRenderer.ApplyRotation (g, obj);

	private static PointD GetRotationPivot (TextObject obj) => obj.RotationPivot;

	//The unrotated, outline-padded rectangle that the dashed interaction box is based on.
	private RectangleD GetPaddedLocalRect (TextObject obj)
	{
		layout.Engine = obj.Engine;
		RectangleD local = layout.GetLayoutBounds ().ToDouble ();
		double pad = 10 + obj.OutlineWidth;
		return local.Inflated (pad, pad);
	}

	//The 4 local (unrotated) padded corners: 0 TL, 1 TR, 2 BR, 3 BL.
	private static PointD[] GetLocalPaddedCorners (RectangleD pr)
		=> [
			pr.Location (),
			new PointD (pr.Right + 1, pr.Top),
			new PointD (pr.Right + 1, pr.Bottom + 1),
			new PointD (pr.Left, pr.Bottom + 1),
		];

	//The 4 screen-space (rotated) corners of the dashed interaction rectangle.
	private PointD[] GetInteractionCorners (TextObject obj)
	{
		RectangleD pr = GetPaddedLocalRect (obj);
		PointD pivot = GetRotationPivot (obj);
		double a = RotationRadians (obj);
		PointD[] local = GetLocalPaddedCorners (pr);
		return [RotatePoint (local[0], pivot, a), RotatePoint (local[1], pivot, a), RotatePoint (local[2], pivot, a), RotatePoint (local[3], pivot, a)];
	}

	//The axis-aligned bounding box of the rotated interaction rectangle.
	private RectangleI GetRotatedBounds (TextObject obj)
	{
		PointD[] corners = GetInteractionCorners (obj);
		double minX = corners.Min (c => c.X), minY = corners.Min (c => c.Y);
		double maxX = corners.Max (c => c.X), maxY = corners.Max (c => c.Y);
		return new RectangleD (minX, minY, maxX - minX, maxY - minY).ToInt ();
	}

	private enum HitZone { None, Move, Resize, Interior }

	//Classifies where the cursor is relative to a text object's interaction rectangle.
	private HitZone GetHitZone (TextObject obj, PointD p)
	{
		if (obj.IsEmpty)
			return HitZone.None;

		RectangleD pr = GetPaddedLocalRect (obj);
		PointD pivot = GetRotationPivot (obj);
		PointD lp = RotatePoint (p, pivot, -RotationRadians (obj));

		const double HANDLE_R = 16;
		const double EDGE_R = 8;

		PointD[] corners = GetLocalPaddedCorners (pr);
		for (int i = 0; i < 4; i++)
			if (Distance (lp, corners[i]) <= HANDLE_R)
				return HitZone.Resize;

		if (!pr.ContainsPoint (lp))
			return HitZone.None;

		//Near an edge of the dashed rectangle → drag to move.
		double nearX = Math.Min (lp.X - pr.Left, pr.Right + 1 - lp.X);
		double nearY = Math.Min (lp.Y - pr.Top, pr.Bottom + 1 - lp.Y);
		if (Math.Min (nearX, nearY) <= EDGE_R)
			return HitZone.Move;

		return HitZone.Interior;
	}

	//Returns the local corner index (0 TL, 1 TR, 2 BR, 3 BL) nearest to the point.
	private int FindCorner (TextObject obj, PointD p)
	{
		PointD[] corners = GetInteractionCorners (obj);
		int best = 0;
		double bestDist = double.MaxValue;
		for (int i = 0; i < 4; i++) {
			double d = Distance (p, corners[i]);
			if (d < bestDist) {
				bestDist = d;
				best = i;
			}
		}
		return best;
	}

	#endregion

	#region Hit Testing & Edit Hint

	/// <summary>
	/// Returns the text object (and its layer) at the given canvas point, if any.
	/// </summary>
	/// <param name="allLayers">Whether to also search layers other than the current one.</param>
	private (UserLayer? layer, TextObject? text) HitTest (PointI point, Document document, bool allLayers)
	{
		//Search in reverse so the topmost (last-drawn) object wins when they overlap.
		//Bounds are inflated slightly so the resize grips (which stick out a bit past the
		//dashed rectangle's bounding box) are easy to grab.
		const int HIT_MARGIN = 10;
		IReadOnlyList<TextObject> currentObjects = CurrentUserLayer.TextObjects;
		for (int i = currentObjects.Count - 1; i >= 0; i--) {
			TextObject obj = currentObjects[i];
			if (!obj.IsEmpty && obj.TextBounds.Inflated (HIT_MARGIN, HIT_MARGIN).Contains (point))
				return (CurrentUserLayer, obj);
		}

		if (allLayers) {
			foreach (UserLayer ul in document.Layers.UserLayers) {
				if (ul == CurrentUserLayer)
					continue;

				IReadOnlyList<TextObject> objects = ul.TextObjects;
				for (int i = objects.Count - 1; i >= 0; i--) {
					TextObject obj = objects[i];
					if (!obj.IsEmpty && obj.TextBounds.Inflated (HIT_MARGIN, HIT_MARGIN).Contains (point))
						return (ul, obj);
				}
			}
		}

		return (null, null);
	}

	private void UpdateEditHint (Document document, PointI mousePosition)
	{
		if (!workspace.HasOpenDocuments || !TransientHintPopover.ShouldShow) {
			HideEditHint ();
			return;
		}

		//While actively editing, the active rectangle already communicates the state.
		if (is_editing) {
			HideEditHint ();
			return;
		}

		(_, TextObject? hit) = HitTest (mousePosition, document, allLayers: true);
		if (hit is null || hit.IsEmpty) {
			HideEditHint ();
			return;
		}

		HitZone zone = GetHitZone (hit, mousePosition.ToDouble ());

		//Already showing the right hint for this object/zone.
		if (edit_hint_visible && edit_hint_target == hit && edit_hint_zone == zone)
			return;

		//Schedule (or reschedule) the hint to appear after the cursor lingers briefly.
		if (hover_hint_timeout_id != 0) {
			GLib.Functions.SourceRemove (hover_hint_timeout_id);
			hover_hint_timeout_id = 0;
		}

		hover_hint_target = hit;
		hover_hint_zone = zone;
		hover_hint_timeout_id = GLib.Functions.TimeoutAdd (0, HoverHintDelayMs, () => {
			hover_hint_timeout_id = 0;
			if (edit_hint_visible && edit_hint_target == hit && edit_hint_zone == zone)
				return false;
			ShowHint (hit, zone);
			return false;
		});
	}

	//Show the hover hint (either the general one or the corner resize one) for an object.
	private void ShowHint (TextObject obj, HitZone zone)
	{
		if (!workspace.HasOpenDocuments)
			return;

		Gtk.Widget canvas = workspace.ActiveWorkspace.Canvas;

		string hint = zone == HitZone.Resize ? CornerHintText (obj.Engine.WrapWidth > 0) : EditHintText ();

		//Anchor the popover to the hovered corner for the resize hint. Otherwise,
		//spawn it slightly below the center of the word (not the lower-right corner,
		//which is an invisible corner when the object isn't focused/being edited).
		PointD anchor;
		if (zone == HitZone.Resize) {
			PointD[] corners = GetInteractionCorners (obj);
			anchor = corners[FindCorner (obj, last_mouse_position.ToDouble ())];
		} else {
			anchor = new PointD (
				(obj.TextBounds.Left + obj.TextBounds.Right) / 2.0,
				obj.TextBounds.Bottom);
		}

		PointD anchorView = workspace.ActiveWorkspace.CanvasPointToView (anchor);
		edit_hint_popover.Show (canvas, hint, anchorView);

		edit_hint_visible = true;
		edit_hint_target = obj;
		edit_hint_zone = zone;
	}

	private static string EditHintText ()
		// Translators: hints shown when hovering a text object with the text tool.
		=> string.Join ("\n",
			Translations.GetString ("{0} to edit", ClickBindingLabel (KeyboardShortcutManager.TextReEdit)),
			Translations.GetString ("{0} to open text properties", ClickBindingLabel (KeyboardShortcutManager.TextOpenProperties)),
			Translations.GetString ("Right click to move"));

	// Translators: hints shown when hovering a text object's resize corner.
	private static string CornerHintText (bool area)
		=> string.Join ("\n",
			area
				? Translations.GetString ("Drag corner to resize the text box")
				: Translations.GetString ("Drag corner to resize (changes font size)"),
			Translations.GetString ("{0} to rotate", ClickBindingLabel (KeyboardShortcutManager.TextRotate)));

	//How long (ms) the cursor must linger over an object before its hint appears.
	private const uint HoverHintDelayMs = 600;

	private void HideEditHint ()
	{
		if (hover_hint_timeout_id != 0) {
			GLib.Functions.SourceRemove (hover_hint_timeout_id);
			hover_hint_timeout_id = 0;
		}

		if (!edit_hint_visible && !edit_hint_popover.Exists)
			return;

		edit_hint_popover.Hide ();

		edit_hint_visible = false;
		edit_hint_target = null;
	}

	#endregion

	#region Undo/Redo

	protected override bool OnHandleUndo (Document document)
	{
		if (!is_editing)
			return false;

		// commit a history item to let the undo action undo text history item
		CommitCurrentText ();

		return false;
	}

	protected override bool OnHandleRedo (Document document)
	{
		//Rather than redoing something, if the text has been edited then simply commit and do not redo.
		if (!is_editing || CurrentTextEngine.State != TextMode.Uncommitted)
			return false;

		//Commit a new TextHistoryItem.
		CommitCurrentText ();

		return true;
	}

	#endregion

	#region Copy/Paste

	protected override async Task<bool> OnHandlePaste (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		if (!await CurrentTextEngine.PerformPaste (cb))
			return false;

		RedrawText (true);
		return true;
	}

	protected override bool OnHandleCopy (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		CurrentTextEngine.PerformCopy (cb);
		return true;
	}

	// ponytail: async void is the sanctioned fire-and-forget for event-thread paste.
	// Never .Wait() on the GTK thread — ReadTextAsync can deadlock. Redraw when the read lands.
	private async void PerformPasteAndRedraw ()
	{
		if (await CurrentTextEngine.PerformPaste (GdkExtensions.GetDefaultClipboard ()))
			RedrawText (true);
	}

	protected override bool OnHandleCut (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		CurrentTextEngine.PerformCut (cb);
		RedrawText (true);
		return true;
	}

	#endregion
}
