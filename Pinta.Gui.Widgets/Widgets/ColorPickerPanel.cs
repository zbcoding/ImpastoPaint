// Impasto: the floating Colors window's content — a persistent, always-live
// version of ColorPickerDialog's picker (hue/sat wheel or sat/val square, sliders,
// hex entry, primary/secondary swap display) plus the docked bar's recent/quick
// swatch rows, each preceded by the same small section icon the bar uses.
//
// Deliberately a fresh widget rather than an extraction from ColorPickerDialog:
// that dialog is 1,000+ lines of OK/Cancel/small-mode/reset chrome tangled with the
// picker logic, and the worst possible rebase target against upstream (see
// ColorWheelWidget's note for the same call). What's cheap to share - the swatch
// geometry (PaletteWidget), the slider control (ColorPickerSlider), and the section
// icons (StatusBarColorPaletteWidget.DrawClockIcon/DrawPaletteIcon) - is reused
// directly; the picker-surface math is small enough to duplicate rather than prise
// out of the dialog.
//
// ponytail: live-apply only, no OK/Cancel/Reset - clicks and drags write straight
// to the palette, same as ColorWheelWidget/ColorSlidersWidget already do.

using System;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.Box>]
public sealed partial class ColorPickerPanel
{
	enum SurfaceType
	{
		HueAndSat,
		SatAndVal,
	}

	const int SURFACE_RADIUS = 100;
	const int SURFACE_PADDING = 10;
	const int SLIDER_WIDTH = 150;
	const int SPACING = 6;
	const int SWATCH_ICON_SIZE = 14;
	// Wide enough that the swap and eyedropper buttons stretch to the swatch width
	// rather than the other way round, matching the picker dialog's proportions.
	const int DISPLAY_SIZE = 40;
	// Both swatch groups follow the configured 2- or 3-row palette. A 17-column
	// cap keeps either default quick palette on one band while still wrapping
	// larger user palettes.
	const int MAX_SWATCH_COLUMNS = 17;

	public event EventHandler? EyedropperClicked;

	private IPaletteService palette = null!; // NRT - set by factory method
	private IChromeService chrome = null!;
	private ISystemService system = null!;
	private bool color_picker_active;
	private bool primary_selected = true;
	private SurfaceType surface_type = SurfaceType.HueAndSat;
	private bool updating;
	private bool dragging_surface;

	private Gtk.DrawingArea primary_display = null!;
	private Gtk.DrawingArea secondary_display = null!;
	private Gtk.CheckButton show_value_check = null!;
	private Gtk.CheckButton show_alpha_check = null!;
	private Gtk.DrawingArea surface = null!;
	private Gtk.DrawingArea surface_cursor = null!;
	private Gtk.Entry code_entry = null!;
	private CssColorFormat code_format = CssColorFormat.Hex;
	private ColorPickerSlider[] sliders = null!;
	private Gtk.DrawingArea swatch_recent = null!;
	private Gtk.Box recent_swatch_row = null!;
	private Gtk.DrawingArea swatch_palette = null!;

	private Color CurrentColor {
		get => primary_selected ? palette.PrimaryColor : palette.SecondaryColor;
		set => ApplyColor (value, addToRecent: false);
	}

	[System.Diagnostics.CodeAnalysis.MemberNotNull (
		nameof (primary_display), nameof (secondary_display), nameof (show_value_check),
		nameof (show_alpha_check),
		nameof (surface), nameof (surface_cursor), nameof (code_entry), nameof (sliders),
		nameof (swatch_recent), nameof (swatch_palette), nameof (recent_swatch_row))]
	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Vertical);
		Spacing = SPACING;

		Gtk.Box displayBox = BuildColorDisplay ();
		Gtk.Box surfaceBox = BuildPickerSurface ();
		Gtk.Box slidersBox = BuildSliders ();

		Gtk.Box topBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		topBox.Append (displayBox);
		topBox.Append (surfaceBox);
		topBox.Append (slidersBox);

		Gtk.Box recentRow = BuildSwatchRow (
			icon: StatusBarColorPaletteWidget.DrawClockIcon,
			tooltip: Translations.GetString ("Recently picked colors"),
			swatchArea: out swatch_recent);
		recent_swatch_row = recentRow;
		swatch_recent.SetDrawFunc ((_, g, _, _) => DrawRecentSwatches (g));
		ConfigureSwatchClick (swatch_recent, recent: true);
		ConfigureSwatchTooltip (swatch_recent, recent: true);

		Gtk.Box paletteRow = BuildSwatchRow (
			icon: StatusBarColorPaletteWidget.DrawPaletteIcon,
			tooltip: Translations.GetString ("Quick colors"),
			swatchArea: out swatch_palette);
		swatch_palette.SetDrawFunc ((_, g, _, _) => DrawQuickSwatches (g));
		ConfigureSwatchClick (swatch_palette, recent: false);
		ConfigureSwatchTooltip (swatch_palette, recent: false);

		Gtk.FlowBox swatchRows = Gtk.FlowBox.New ();
		swatchRows.SetOrientation (Gtk.Orientation.Horizontal);
		swatchRows.MinChildrenPerLine = 1;
		swatchRows.MaxChildrenPerLine = 2;
		swatchRows.Homogeneous = false;
		swatchRows.SelectionMode = Gtk.SelectionMode.None;
		swatchRows.ColumnSpacing = SPACING;
		swatchRows.RowSpacing = SPACING;
		swatchRows.Insert (recentRow, -1);
		swatchRows.Insert (paletteRow, -1);

		Append (topBox);
		Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		Append (swatchRows);

		Gtk.GestureDrag dragGesture = Gtk.GestureDrag.New ();
		dragGesture.SetButton (0); // Listen for all mouse buttons.
		dragGesture.OnDragBegin += DragGesture_OnDragBegin;
		dragGesture.OnDragUpdate += DragGesture_OnDragUpdate;
		dragGesture.OnDragEnd += DragGesture_OnDragEnd;
		AddController (dragGesture);
	}

	[System.Diagnostics.CodeAnalysis.MemberNotNull (nameof (primary_display), nameof (secondary_display))]
	private Gtk.Box BuildColorDisplay ()
	{
		primary_display = Gtk.DrawingArea.New ();
		primary_display.SetSizeRequest (DISPLAY_SIZE, DISPLAY_SIZE);
		primary_display.TooltipText = Translations.GetString ("Click to select primary color.");
		primary_display.SetDrawFunc ((_, g, _, _) => DrawDisplay (g, palette.PrimaryColor, primary_selected));
		Gtk.GestureClick primaryClick = Gtk.GestureClick.New ();
		primaryClick.OnReleased += (_, _) => { primary_selected = true; RedrawAll (); };
		primary_display.AddController (primaryClick);

		secondary_display = Gtk.DrawingArea.New ();
		secondary_display.SetSizeRequest (DISPLAY_SIZE, DISPLAY_SIZE);
		secondary_display.TooltipText = Translations.GetString ("Click to select secondary color.");
		secondary_display.SetDrawFunc ((_, g, _, _) => DrawDisplay (g, palette.SecondaryColor, !primary_selected));
		Gtk.GestureClick secondaryClick = Gtk.GestureClick.New ();
		secondaryClick.OnReleased += (_, _) => { primary_selected = false; RedrawAll (); };
		secondary_display.AddController (secondaryClick);

		string label = Translations.GetString ("Click to switch between primary and secondary color.");
		Gtk.Button swapButton = Gtk.Button.NewFromIconName (Resources.StandardIcons.EditSwap);
		swapButton.TooltipText = label;
		swapButton.FocusOnClick = false;
		swapButton.OnClicked += (_, _) => {
			Color temp = palette.PrimaryColor;
			palette.SetColor (true, palette.SecondaryColor, false);
			palette.SetColor (false, temp, false);
		};

		Gtk.Button eyedropperButton = Gtk.Button.NewFromIconName (Resources.Icons.ToolColorPicker);
		eyedropperButton.TooltipText = Translations.GetString ("Selects the color in view. Sample from the composited image, including all visible layers.");
		eyedropperButton.FocusOnClick = false;
		eyedropperButton.OnClicked += (_, _) => EyedropperClicked?.Invoke (this, EventArgs.Empty);

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		box.Append (primary_display);
		box.Append (swapButton);
		box.Append (secondary_display);
		box.Append (eyedropperButton);
		box.Valign = Gtk.Align.Start;
		return box;
	}

	private static void DrawDisplay (Context g, Color c, bool selected)
	{
		const int MARGIN = 3;
		double wh = DISPLAY_SIZE - MARGIN * 2;
		RectangleD rect = new (MARGIN, MARGIN, wh, wh);

		if (c.A < 1) {
			g.FillRectangle (rect, new Color (1, 1, 1));
			g.FillRectangle (new RectangleD (rect.X, rect.Y, rect.Width / 2, rect.Height / 2), new Color (.8, .8, .8));
			g.FillRectangle (new RectangleD (rect.X + rect.Width / 2, rect.Y + rect.Height / 2, rect.Width / 2, rect.Height / 2), new Color (.8, .8, .8));
		}

		g.FillRectangle (rect, c);
		g.DrawRectangle (rect, new Color (0, 0, 0), selected ? 3 : 1);
	}

	[System.Diagnostics.CodeAnalysis.MemberNotNull (nameof (show_value_check), nameof (show_alpha_check), nameof (surface), nameof (surface_cursor))]
	private Gtk.Box BuildPickerSurface ()
	{
		Gtk.ToggleButton hueSatToggle = Gtk.ToggleButton.NewWithLabel (Translations.GetString ("Hue & Sat"));
		hueSatToggle.FocusOnClick = false;
		hueSatToggle.Active = true;

		Gtk.ToggleButton satValToggle = Gtk.ToggleButton.NewWithLabel (Translations.GetString ("Sat & Value"));
		satValToggle.FocusOnClick = false;

		Gtk.Image brightnessIcon = Gtk.Image.NewFromIconName (Resources.Icons.AdjustmentsBrightnessContrast);
		brightnessIcon.AddCssClass ("dim-label");

		show_value_check = Gtk.CheckButton.New ();
		show_value_check.Active = true;
		show_value_check.FocusOnClick = false;
		show_value_check.Child = brightnessIcon;
		show_value_check.TooltipText = $"{Translations.GetString ("Show selection brightness in preview")}\n{Translations.GetString ("If enabled, the hue/saturation surface is drawn at your current selection's brightness; otherwise it is shown at full brightness.")}";

		Gtk.Image alphaIcon = Gtk.Image.NewFromIconName (Resources.Icons.ColorModeTransparency);
		alphaIcon.AddCssClass ("dim-label");

		show_alpha_check = Gtk.CheckButton.New ();
		show_alpha_check.FocusOnClick = false;
		show_alpha_check.Child = alphaIcon;
		show_alpha_check.TooltipText = $"{Translations.GetString ("Show selection opacity in preview")}\n{Translations.GetString ("If enabled, the hue/saturation surface is drawn at your current selection's opacity, letting the checkerboard behind it show through; otherwise it is shown fully opaque.")}";

		hueSatToggle.OnToggled += (_, _) => {
			if (!hueSatToggle.Active) return;
			surface_type = SurfaceType.HueAndSat;
			show_value_check.Visible = true;
			show_alpha_check.Visible = true;
			RedrawAll ();
		};
		satValToggle.OnToggled += (_, _) => {
			if (!satValToggle.Active) return;
			surface_type = SurfaceType.SatAndVal;
			show_value_check.Visible = false;
			show_alpha_check.Visible = false;
			RedrawAll ();
		};
		hueSatToggle.SetGroup (satValToggle);

		Gtk.Box toggleBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		toggleBox.Homogeneous = true;
		toggleBox.Append (hueSatToggle);
		toggleBox.Append (satValToggle);

		int drawSize = (SURFACE_RADIUS + SURFACE_PADDING) * 2;

		surface = Gtk.DrawingArea.New ();
		surface.SetSizeRequest (drawSize, drawSize);
		surface.SetDrawFunc ((_, g, _, _) => DrawSurface (g));

		surface_cursor = Gtk.DrawingArea.New ();
		surface_cursor.SetSizeRequest (drawSize, drawSize);
		surface_cursor.SetDrawFunc ((_, g, _, _) => DrawSurfaceCursor (g));

		show_value_check.OnToggled += (_, _) => surface.QueueDraw ();
		show_alpha_check.OnToggled += (_, _) => surface.QueueDraw ();

		Gtk.Overlay overlay = Gtk.Overlay.New ();
		overlay.AddOverlay (surface);
		overlay.AddOverlay (surface_cursor);
		overlay.SetSizeRequest (drawSize, drawSize);

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		box.WidthRequest = drawSize;
		box.Append (toggleBox);
		box.Append (overlay);

		Gtk.Box previewCheckBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		previewCheckBox.Halign = Gtk.Align.Center;
		previewCheckBox.Append (show_value_check);
		previewCheckBox.Append (show_alpha_check);
		box.Append (previewCheckBox);
		return box;
	}

	[System.Diagnostics.CodeAnalysis.MemberNotNull (nameof (code_entry), nameof (sliders))]
	private Gtk.Box BuildSliders ()
	{
		// Translators: This tooltip lists CSS color syntax. Keep the code examples unchanged.
		string codeTooltip = Translations.GetString ("CSS color formats:\nHEX — #ff5733 or ff5733\nRGB — rgb(255 87 51)\nRGB with alpha — rgb(255 87 51 / 50%)\nHSL — hsl(11 100% 60%)\nHSL with alpha — hsl(11 100% 60% / 50%)\nHWB — hwb(11 20% 0%)\nHWB with alpha — hwb(11 20% 0% / 50%)\nOKLCH — oklch(65% 0.2 35)\nNamed colors — any CSS color name, such as red, white, rebeccapurple, transparent, or currentColor\n\nCommas are optional: rgb(255, 87, 51) and rgb(255 87 51) are equivalent.\ncurrentColor uses the currently selected color.");

		code_entry = Gtk.Entry.New ();
		code_entry.Hexpand = true;
		code_entry.OnChanged += (sender, _) => {
			if (updating) return;

			// Clearing the box abandons whichever notation was being echoed back.
			if (sender.GetText ().Trim ().Length == 0) {
				code_format = CssColorFormat.Hex;
				return;
			}

			Color? parsed = Color.FromCssCode (sender.GetText (), CurrentColor, out CssColorFormat format);
			if (parsed is null) return;
			code_format = format;
			CurrentColor = parsed.Value;
		};
		code_entry.TooltipText = codeTooltip;

		Gtk.Label codeLabel = Gtk.Label.New (Translations.GetString ("Code"));
		codeLabel.WidthRequest = 50;
		codeLabel.TooltipText = codeTooltip;

		Gtk.Box codeBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		codeBox.Append (codeLabel);
		codeBox.Append (code_entry);

		sliders = [
			CreateSlider (ColorPickerSlider.Component.Hue),
			CreateSlider (ColorPickerSlider.Component.Saturation),
			CreateSlider (ColorPickerSlider.Component.Value),
			CreateSlider (ColorPickerSlider.Component.Red),
			CreateSlider (ColorPickerSlider.Component.Green),
			CreateSlider (ColorPickerSlider.Component.Blue),
			CreateSlider (ColorPickerSlider.Component.Alpha),
		];

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		box.Append (codeBox);
		box.Append (sliders[0]);
		box.Append (sliders[1]);
		box.Append (sliders[2]);
		box.Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		box.Append (sliders[3]);
		box.Append (sliders[4]);
		box.Append (sliders[5]);
		box.Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		box.Append (sliders[6]);
		return box;
	}

	private ColorPickerSlider CreateSlider (ColorPickerSlider.Component component)
	{
		ColorPickerSlider slider = ColorPickerSlider.New (component, SLIDER_WIDTH);
		slider.OnColorChanged += (sender, _) => {
			if (updating) return;
			CurrentColor = ((ColorPickerSlider) sender!).Color;
		};
		return slider;
	}

	private Gtk.Box BuildSwatchRow (Action<Context, RectangleD, Color> icon, string tooltip, out Gtk.DrawingArea swatchArea)
	{
		Gtk.DrawingArea iconArea = Gtk.DrawingArea.New ();
		iconArea.SetSizeRequest (SWATCH_ICON_SIZE, SWATCH_ICON_SIZE);
		iconArea.Valign = Gtk.Align.Center;
		iconArea.TooltipText = tooltip;
		iconArea.SetDrawFunc ((area, g, _, _) => {
			area.GetStyleContext ().GetColor (out Gdk.RGBA fg);
			icon (g, new RectangleD (0, 0, SWATCH_ICON_SIZE, SWATCH_ICON_SIZE), fg.ToCairoColor ());
		});

		Gtk.DrawingArea swatch = Gtk.DrawingArea.New ();
		swatch.HeightRequest = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS;
		swatch.Halign = Gtk.Align.Start;

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		row.Append (iconArea);
		row.Append (swatch);

		swatchArea = swatch;
		return row;
	}

	private void ConfigureSwatchClick (Gtk.DrawingArea swatch, bool recent)
	{
		Gtk.GestureClick click = Gtk.GestureClick.New ();
		click.SetButton (0);
		click.OnReleased += (_, e) =>
			HandleSwatchClick (
				recent,
				new PointD (e.X, e.Y),
				click.GetCurrentButton (),
				click.GetCurrentEventState ());
		swatch.AddController (click);
	}

	private void ConfigureSwatchTooltip (Gtk.DrawingArea swatch, bool recent)
	{
		Gtk.Label caption = Gtk.Label.New (string.Empty);
		caption.Halign = Gtk.Align.Start;
		caption.Justify = Gtk.Justification.Left;
		caption.Wrap = true;
		caption.MaxWidthChars = 55;

		Gtk.Popover popup = Gtk.Popover.New ();
		popup.Autohide = false;
		popup.CanTarget = false;
		popup.HasArrow = false;
		popup.Position = Gtk.PositionType.Bottom;
		popup.AddCssClass ("color-swatch-tooltip");
		popup.SetChild (caption);
		popup.SetParent (swatch);

		int visibleIndex = -1;
		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnMotion += (_, args) => {
			int index = GetSwatchIndex (recent, new PointD (args.X, args.Y));
			if (index < 0) {
				popup.Popdown ();
				visibleIndex = -1;
				return;
			}

			if (index == visibleIndex)
				return;

			Color color = recent
				? palette.RecentlyUsedColors[index]
				: palette.CurrentPalette.Colors[index];
			string instructions = recent
				? Translations.GetString ("Left click to set primary color. Right click to set secondary color.")
				: Translations.GetString (
					"Left click to set primary color. Right click to set secondary color. Middle click or press {0} and left click to choose palette color.",
					system.CtrlLabel ());

			caption.SetText (Translations.GetString ("Color") + $": #{color.ToHex ()}\n\n" + instructions);
			visibleIndex = index;
			popup.Popup ();
		};
		motion.OnLeave += (_, _) => {
			popup.Popdown ();
			visibleIndex = -1;
		};
		swatch.AddController (motion);
	}

	private int GetSwatchIndex (bool recent, PointD point)
	{
		int rowCount = PaletteWidget.PALETTE_ROWS;
		return PaletteWidget.GetWrappedSwatchAtLocation (
			palette,
			point,
			new RectangleD (),
			MAX_SWATCH_COLUMNS,
			rowCount,
			recent);
	}

	private void DrawRecentSwatches (Context g)
	{
		var recent = palette.RecentlyUsedColors;

		int count = Math.Min (recent.Count, palette.MaxRecentlyUsedColor);
		for (int i = 0; i < count; i++)
			g.FillRectangle (
				PaletteWidget.GetWrappedSwatchBounds (palette, i, new RectangleD (), MAX_SWATCH_COLUMNS, PaletteWidget.PALETTE_ROWS, recentColorPalette: true),
				recent.ElementAt (i));
	}

	private void DrawQuickSwatches (Context g)
	{
		Palette currentPalette = palette.CurrentPalette;
		for (int i = 0; i < currentPalette.Colors.Count; i++)
			g.FillRectangle (
				PaletteWidget.GetWrappedSwatchBounds (palette, i, new RectangleD (), MAX_SWATCH_COLUMNS, PaletteWidget.PALETTE_ROWS),
				currentPalette.Colors[i]);
	}

	// Recomputes the swatch areas' height to fit however many row-bands the current
	// color count wraps into at MAX_SWATCH_COLUMNS.
	private void UpdateSwatchSizes ()
	{
		recent_swatch_row.Visible = palette.MaxRecentlyUsedColor > 0;
		int recentRows = PaletteWidget.PALETTE_ROWS;
		int recentCols = (palette.MaxRecentlyUsedColor + recentRows - 1) / recentRows;
		int visibleRecentCols = Math.Min (Math.Max (1, recentCols), MAX_SWATCH_COLUMNS);
		int recentBands = PaletteWidget.GetWrappedBandCount (recentCols, MAX_SWATCH_COLUMNS);
		swatch_recent.WidthRequest = PaletteWidget.SWATCH_SIZE * visibleRecentCols;
		swatch_recent.HeightRequest = PaletteWidget.SWATCH_SIZE * recentRows * recentBands;

		int quickCols = (palette.CurrentPalette.Colors.Count + PaletteWidget.PALETTE_ROWS - 1) / PaletteWidget.PALETTE_ROWS;
		int visibleQuickCols = Math.Min (Math.Max (1, quickCols), MAX_SWATCH_COLUMNS);
		int quickBands = PaletteWidget.GetWrappedBandCount (quickCols, MAX_SWATCH_COLUMNS);
		swatch_palette.WidthRequest = PaletteWidget.SWATCH_SIZE * visibleQuickCols;
		swatch_palette.HeightRequest = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS * quickBands;
	}

	// Left click sets the primary color, right click the secondary - same semantics
	// as the docked bar's quick/recent swatches (StatusBarColorPaletteWidget).
	private async void HandleSwatchClick (bool recent, PointD relPoint, uint button, Gdk.ModifierType state)
	{
		int index = GetSwatchIndex (recent, relPoint);
		if (index < 0)
			return;

		bool editQuickColor = !recent && (
			button == GtkExtensions.MOUSE_MIDDLE_BUTTON ||
			(button == GtkExtensions.MOUSE_LEFT_BUTTON && state.IsControlPressed ()));

		if (editQuickColor) {
			await EditQuickColor (index);
			return;
		}

		Color color = recent
			? palette.RecentlyUsedColors.ElementAt (index)
			: palette.CurrentPalette.Colors[index];

		if (button == GtkExtensions.MOUSE_RIGHT_BUTTON)
			palette.SetColor (false, color, addToRecent: !recent);
		else if (button == GtkExtensions.MOUSE_LEFT_BUTTON)
			palette.SetColor (true, color, addToRecent: !recent);
	}

	private async Task EditQuickColor (int index)
	{
		if (color_picker_active)
			return;

		color_picker_active = true;
		using ColorPickerDialog dialog = ColorPickerDialog.New (
			chrome.MainWindow,
			palette,
			new SingleColor (palette.CurrentPalette.Colors[index]),
			primarySelected: true,
			livePalette: false,
			Translations.GetString ("Choose Palette Color"));

		try {
			if (await dialog.RunAsync () == Gtk.ResponseType.Ok)
				palette.CurrentPalette.SetColor (index, ((SingleColor) dialog.Colors).Color);
		} finally {
			dialog.Destroy ();
			color_picker_active = false;
		}
	}

	private void Configure (IPaletteService palette, IChromeService chrome, ISystemService system)
	{
		this.palette = palette;
		this.chrome = chrome;
		this.system = system;

		palette.PrimaryColorChanged += (_, _) => { if (!updating) RedrawAll (); };
		palette.SecondaryColorChanged += (_, _) => { if (!updating) RedrawAll (); };
		palette.RecentColorsChanged += (_, _) => { UpdateSwatchSizes (); swatch_recent.QueueDraw (); };
		palette.CurrentPalette.PaletteChanged += (_, _) => { UpdateSwatchSizes (); swatch_palette.QueueDraw (); };

		UpdateSwatchSizes ();
		updating = true;
		RedrawAll ();
		updating = false;
	}

	public static ColorPickerPanel New (IPaletteService palette, IChromeService chrome, ISystemService system)
	{
		ColorPickerPanel panel = NewWithProperties ([]);
		panel.Configure (palette, chrome, system);
		return panel;
	}

	private void ApplyColor (Color color, bool addToRecent)
	{
		updating = true;
		palette.SetColor (setPrimary: primary_selected, color, addToRecent);
		RedrawAll ();
		updating = false;
	}

	private void CommitRecent ()
		=> palette.SetColor (setPrimary: primary_selected, CurrentColor, addToRecent: true);

	private void RedrawAll ()
	{
		surface.QueueDraw ();
		surface_cursor.QueueDraw ();
		primary_display.QueueDraw ();
		secondary_display.QueueDraw ();

		Color current = CurrentColor;
		foreach (var slider in sliders)
			slider.Color = current;

		if (!code_entry.IsEditingText ())
			code_entry.SetText (current.ToCssCode (code_format));
	}

	private void DrawSurface (Context g)
	{
		const int radius = SURFACE_RADIUS;
		const int radiusSquared = radius * radius;
		const int diameter = 2 * radius;
		Size drawSize = new (diameter, diameter);

		using ImageSurface imgSurface = CairoExtensions.CreateImageSurface (
			Format.Argb32,
			drawSize.Width,
			drawSize.Height);

		Span<ColorBgra> data = imgSurface.GetPixelData ();

		switch (surface_type) {

			case SurfaceType.HueAndSat:

				PointI center = new (radius, radius);

				for (int y = 0; y < drawSize.Height; y++) {
					for (int x = 0; x < drawSize.Width; x++) {

						PointI pixel = new (x, y);
						PointI vector = pixel - center;

						int magnitudeSquared = vector.MagnitudeSquared ();
						if (magnitudeSquared > radiusSquared) continue;

						double magnitude = Math.Sqrt (magnitudeSquared);

						double h = (MathF.Atan2 (vector.Y, -vector.X) + MathF.PI) / (2f * MathF.PI) * 360f;
						double s = Math.Min (magnitude / radius, 1);
						double v = show_value_check.Active ? CurrentColor.ToHsv ().Val : 1;

						double d = radius - magnitude;
						// The outermost pixel fades out to antialias the circle's edge.
						double edgeAlpha = d < 1 ? d : 1;
						double a = edgeAlpha * (show_alpha_check.Active ? CurrentColor.A : 1);

						data[drawSize.Width * y + x] = Color.FromHsv (h, s, v, a).ToColorBgra ();
					}
				}

				break;

			case SurfaceType.SatAndVal:

				for (int y = 0; y < drawSize.Height; y++) {
					double s = 1.0 - (double) y / (drawSize.Height - 1);
					for (int x = 0; x < drawSize.Width; x++) {
						double v = (double) x / (drawSize.Width - 1);
						data[drawSize.Width * y + x] = Color.FromHsv (CurrentColor.ToHsv ().Hue, s, v).ToColorBgra ();
					}
				}

				break;
		}

		imgSurface.MarkDirty ();

		if (surface_type == SurfaceType.HueAndSat)
			DrawTransparentBackgroundCircle (g);

		g.SetSourceSurface (imgSurface, SURFACE_PADDING, SURFACE_PADDING);
		g.Paint ();
	}

	/// <summary>
	/// The checkerboard the wheel is drawn over, so a partly transparent selection reads
	/// as transparent rather than as a darker color.
	/// </summary>
	private static void DrawTransparentBackgroundCircle (Context g)
	{
		const int CHECKER_SIZE = 12;
		// Sit just inside the wheel and fade out before reaching it, so the wheel's own
		// antialiased rim stays the outermost edge and no hard circle shows through.
		const double INSET = 2;
		const double FADE = 10;

		double center = SURFACE_RADIUS + SURFACE_PADDING;
		double radius = SURFACE_RADIUS - INSET;

		using ImageSurface checkers = CairoExtensions.CreateTransparentBackgroundSurface (CHECKER_SIZE);
		using SurfacePattern pattern = new (checkers) { Extend = Extend.Repeat };

		using RadialGradient fade = new (center, center, 0, center, center, radius);
		fade.AddColorStop (0, new Color (0, 0, 0, 1));
		fade.AddColorStop (1 - FADE / radius, new Color (0, 0, 0, 1));
		fade.AddColorStop (1, new Color (0, 0, 0, 0));

		g.SetSource (pattern);
		g.Mask (fade);
	}

	private void DrawSurfaceCursor (Context g)
	{
		PointD locBase = HsvToSurfaceLocation (CurrentColor.ToHsv ());
		PointD loc = new (locBase.X + SURFACE_RADIUS + SURFACE_PADDING, locBase.Y + SURFACE_RADIUS + SURFACE_PADDING);

		g.Antialias = Antialias.None;

		g.FillRectangle (new RectangleD (loc.X - 5, loc.Y - 5, 10, 10), CurrentColor);
		g.DrawRectangle (new RectangleD (loc.X - 5, loc.Y - 5, 10, 10), new Color (0, 0, 0), 4);
		g.DrawRectangle (new RectangleD (loc.X - 5, loc.Y - 5, 10, 10), new Color (1, 1, 1), 1);
	}

	private PointD HsvToSurfaceLocation (HsvColor hsv)
	{
		switch (surface_type) {
			case SurfaceType.HueAndSat: {
					double rad = hsv.Hue * (Math.PI / 180.0);
					double mag = hsv.Sat * SURFACE_RADIUS;
					return new (Math.Cos (rad) * mag, -(Math.Sin (rad) * mag));
				}
			case SurfaceType.SatAndVal: {
					int size = SURFACE_RADIUS * 2;
					double x = hsv.Val * (size - 1);
					double y = size - hsv.Sat * (size - 1);
					return new (x - SURFACE_RADIUS, y - SURFACE_RADIUS);
				}
			default:
				throw new InvalidOperationException ($"{nameof (surface_type)} cannot have a value of {surface_type}");
		}
	}

	private void SetColorFromSurface (PointD point)
	{
		surface.TranslateCoordinates (this, SURFACE_PADDING, SURFACE_PADDING, out double x, out double y);

		PointI cursor = new (
			X: (int) (point.X - x),
			Y: (int) (point.Y - y));

		if (surface_type == SurfaceType.HueAndSat) {

			PointI centre = new (SURFACE_RADIUS, SURFACE_RADIUS);
			PointI vector = cursor - centre;

			double hue = (Math.Atan2 (vector.Y, -vector.X) + Math.PI) / (2f * Math.PI) * 360f;
			double sat = Math.Min (vector.Magnitude () / SURFACE_RADIUS, 1);

			CurrentColor = CurrentColor.CopyHsv (hue: hue, sat: sat);

		} else if (surface_type == SurfaceType.SatAndVal) {

			int size = SURFACE_RADIUS * 2;
			cursor = cursor with {
				X = Math.Clamp (cursor.X, 0, size - 1),
				Y = Math.Clamp (cursor.Y, 0, size - 1),
			};

			double s = 1f - (double) cursor.Y / (size - 1);
			double v = (double) cursor.X / (size - 1);

			CurrentColor = CurrentColor.CopyHsv (sat: s, value: v);
		}
	}

	private void DragGesture_OnDragBegin (
		Gtk.GestureDrag gesture,
		Gtk.GestureDrag.DragBeginSignalArgs e)
	{
		gesture.GetStartPoint (out double startX, out double startY);
		PointD absPos = new (startX, startY);

		if (surface.IsMouseInDrawingArea (this, absPos, out PointD _)) {
			dragging_surface = true;
			SetColorFromSurface (absPos);
			return;
		}

		dragging_surface = false;

	}

	private void DragGesture_OnDragUpdate (
		Gtk.GestureDrag gesture,
		Gtk.GestureDrag.DragUpdateSignalArgs e)
	{
		if (!dragging_surface) return;

		gesture.GetStartPoint (out double startX, out double startY);
		SetColorFromSurface (new PointD (startX + e.OffsetX, startY + e.OffsetY));
	}

	private void DragGesture_OnDragEnd (
		Gtk.GestureDrag gesture,
		Gtk.GestureDrag.DragEndSignalArgs e)
	{
		if (dragging_surface)
			CommitRecent ();
		dragging_surface = false;
	}
}
