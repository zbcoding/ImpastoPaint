//
// StatusBarColorPaletteWidget.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2020 Jonathan Pobst
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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Adw;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.DrawingArea>]
public sealed partial class StatusBarColorPaletteWidget
{
	private static bool color_picker_active = false;

	private readonly RectangleD primary_rect = new (4, 3, 24, 24);
	private readonly RectangleD secondary_rect = new (17, 16, 24, 24);
	private readonly RectangleD swap_rect = new (27, 2, 15, 15);
	private readonly RectangleD reset_rect = new (2, 27, 15, 15);

	private const int SECTION_GAP = 8;
	private const int ICON_SIZE = 12;
	private const int ACTION_ICON_SIZE = 28;

	private IChromeService chrome = null!; // NRT - set by factory method
	private IPaletteService palette = null!;
	private ISystemService system = null!;

	public event EventHandler? ColorWheelClicked;
	public RectangleD ColorWheelButtonRect => color_wheel_icon_rect;
	public event EventHandler? FloatColorsClicked;
	// Right-clicking the float button asks for the "Reset window" popover.
	public event EventHandler? ResetColorWindowClicked;
	public RectangleD FloatColorsButtonRect => float_colors_icon_rect;

	// Impasto: the wheel / float buttons only make sense while docked - the floating
	// window already shows the wheel, and clicking them there popped up an empty
	// popover or did nothing.
	private bool show_action_icons = true;
	public bool ShowActionIcons {
		get => show_action_icons;
		set {
			if (show_action_icons == value)
				return;
			show_action_icons = value;
			if (GetWidth () > 0)
				UpdateLayout (GetWidth ());
			QueueDraw ();
		}
	}

	private RectangleD palette_rect;
	private RectangleD recent_palette_rect;
	private double recent_separator_x;
	private double palette_separator_x;
	private RectangleD recent_icon_rect;
	private RectangleD palette_icon_rect;
	private RectangleD color_wheel_icon_rect;
	private RectangleD float_colors_icon_rect;

	partial void Initialize ()
	{
		HasTooltip = true;
		OnQueryTooltip += HandleQueryTooltip;

		HeightRequest = PaletteWidget.WIDGET_HEIGHT;

		OnResize += (_, e) => HandleSizeAllocated (e);
		SetDrawFunc ((area, context, width, height) => Draw (context));

		// Handle mouse clicks.
		Gtk.GestureClick click_gesture = Gtk.GestureClick.New ();
		click_gesture.SetButton (0); // Listen for all mouse buttons.
		click_gesture.OnReleased += (_, e) => {
			HandleClick (new PointD (e.X, e.Y), click_gesture.GetCurrentButton (), click_gesture.GetCurrentEventState ());
			click_gesture.SetState (Gtk.EventSequenceState.Claimed);
		};
		AddController (click_gesture);

		// Track which action icon the pointer is over, to draw a hover highlight.
		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnMotion += (_, e) => {
			WidgetElement previous = hovered_element;
			hovered_element = GetElementAtPoint (new PointD (e.X, e.Y));
			if (hovered_element != previous)
				QueueDraw ();
		};
		motion.OnLeave += (_, _) => {
			if (hovered_element != WidgetElement.Nothing) {
				hovered_element = WidgetElement.Nothing;
				QueueDraw ();
			}
		};
		AddController (motion);
	}

	private WidgetElement hovered_element = WidgetElement.Nothing;

	private void Configure (IChromeService chrome, IPaletteService palette, ISystemService system)
	{
		this.chrome = chrome;
		this.palette = palette;
		this.system = system;

		palette.PrimaryColorChanged += new EventHandler (Palette_ColorChanged);
		palette.SecondaryColorChanged += new EventHandler (Palette_ColorChanged);
		palette.RecentColorsChanged += new EventHandler (Palette_ColorChanged);
		palette.CurrentPalette.PaletteChanged += new EventHandler (Palette_ColorChanged);
	}

	public static StatusBarColorPaletteWidget New (IChromeService chrome, IPaletteService palette, ISystemService system)
	{
		StatusBarColorPaletteWidget widget = NewWithProperties ([]);
		widget.Configure (chrome, palette, system);
		return widget;
	}

	private async void HandleClick (PointD point, uint button, Gdk.ModifierType state)
	{
		var element = GetElementAtPoint (point);

		switch (element) {

			case WidgetElement.ColorWheel:
				if (button == GtkExtensions.MOUSE_LEFT_BUTTON)
					ColorWheelClicked?.Invoke (this, EventArgs.Empty);
				break;

			case WidgetElement.FloatColors:
				if (button == GtkExtensions.MOUSE_LEFT_BUTTON)
					FloatColorsClicked?.Invoke (this, EventArgs.Empty);
				else if (button == GtkExtensions.MOUSE_RIGHT_BUTTON)
					ResetColorWindowClicked?.Invoke (this, EventArgs.Empty);
				break;

			case WidgetElement.PrimaryColor:
			case WidgetElement.SecondaryColor:

				if (color_picker_active)
					break;

				color_picker_active = true;

				try {
					bool primarySelected = element switch {
						WidgetElement.PrimaryColor => true,
						WidgetElement.SecondaryColor => false,
						_ => throw new UnreachableException ()
					};

					PaletteColors? choices = await RunColorPicker (primarySelected);

					if (choices is null)
						break;

					if (palette.PrimaryColor != choices.Primary)
						palette.PrimaryColor = choices.Primary;

					if (palette.SecondaryColor != choices.Secondary)
						palette.SecondaryColor = choices.Secondary;
				} finally {
					color_picker_active = false;
				}

				break;

			case WidgetElement.SwapColors:

				Color temp = palette.PrimaryColor;

				// Swapping should not trigger adding colors to recently used palette
				palette.SetColor (true, palette.SecondaryColor, false);
				palette.SetColor (false, temp, false);

				break;

			case WidgetElement.ResetColors:

				palette.PrimaryColor = new Color (0, 0, 0);
				palette.SecondaryColor = new Color (1, 1, 1);

				break;

			case WidgetElement.Palette:

				int index = PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect);

				if (index < 0)
					break;

				bool isCtrlPressed = state.IsControlPressed ();
				if (button == GtkExtensions.MOUSE_RIGHT_BUTTON) {
					palette.SecondaryColor = palette.CurrentPalette.Colors[index];
				} else if (button == GtkExtensions.MOUSE_LEFT_BUTTON && !isCtrlPressed) {
					palette.PrimaryColor = palette.CurrentPalette.Colors[index];
				} else if (button == GtkExtensions.MOUSE_MIDDLE_BUTTON ||
					   (button == GtkExtensions.MOUSE_LEFT_BUTTON && isCtrlPressed)) {
					SingleColor pick = new (palette.CurrentPalette.Colors[index]);
					var colors = await GetUserChosenColor (
						pick,
						Translations.GetString ("Choose Palette Color"));

					if (colors != null)
						palette.CurrentPalette.SetColor (index, colors.Color);
				}

				break;

			case WidgetElement.RecentColorsPalette:

				int recent_index = PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true);

				if (recent_index < 0)
					break;

				Color recentColor = palette.RecentlyUsedColors.ElementAt (recent_index);

				if (button == GtkExtensions.MOUSE_RIGHT_BUTTON) {
					palette.SetColor (false, recentColor, false);
				} else if (button == GtkExtensions.MOUSE_LEFT_BUTTON) {
					palette.SetColor (true, recentColor, false);
				}

				break;
		}
	}

	private void Draw (Context g)
	{
		const int TILE_SIZE = 16;
		using Pattern checkeredPattern =
			CairoExtensions.CreateTransparentBackgroundPattern (TILE_SIZE);

		// Draw Secondary color swatch

		if (palette.SecondaryColor.A < 1)
			g.FillRectangle (secondary_rect, checkeredPattern);

		g.FillRectangle (secondary_rect, palette.SecondaryColor);
		g.DrawRectangle (new RectangleD (secondary_rect.X + 1, secondary_rect.Y + 1, secondary_rect.Width - 2, secondary_rect.Height - 2), new Color (1, 1, 1), 1);
		g.DrawRectangle (secondary_rect, new Color (0, 0, 0), 1);

		// Draw Primary color swatch

		if (palette.PrimaryColor.A < 1)
			g.FillRectangle (primary_rect, checkeredPattern);

		g.FillRectangle (primary_rect, palette.PrimaryColor);
		g.DrawRectangle (new RectangleD (primary_rect.X + 1, primary_rect.Y + 1, primary_rect.Width - 2, primary_rect.Height - 2), new Color (1, 1, 1), 1);
		g.DrawRectangle (primary_rect, new Color (0, 0, 0), 1);

		// Draw the swap icon.
		GetStyleContext ().GetColor (out Gdk.RGBA fg_color);
		Cairo.Color cairo_fg_color = fg_color.ToCairoColor ();
		DrawSwapIcon (g, cairo_fg_color);
		DrawSectionDecorations (g);

		// Draw the reset icon.
		double square_size = 0.6 * reset_rect.Width;
		g.DrawRectangle (new RectangleD (reset_rect.Location (), square_size, square_size), cairo_fg_color, 1);
		g.FillRectangle (new RectangleD (reset_rect.Right - square_size, reset_rect.Bottom - square_size, square_size, square_size), cairo_fg_color);

		// Draw recently used color swatches
		var recent = palette.RecentlyUsedColors;

		for (int i = 0; i < recent.Count; i++) {

			RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, recent_palette_rect, true);
			Color recentColor = recent.ElementAt (i);

			if (recentColor.A < 1) // Only draw checkered pattern if there is transparency
				g.FillRectangle (swatchBounds, checkeredPattern);

			g.FillRectangle (swatchBounds, recentColor);
		}

		// Draw color swatches
		var currentPalette = palette.CurrentPalette;

		for (int i = 0; i < currentPalette.Colors.Count; i++) {

			RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, palette_rect);
			Color paletteColor = currentPalette.Colors[i];

			if (paletteColor.A < 1) // Only draw checkered pattern if there is transparency
				g.FillRectangle (swatchBounds, checkeredPattern);

			g.FillRectangle (swatchBounds, paletteColor);
		}

		g.Dispose ();
	}

	private void DrawSwapIcon (Context g, Color color)
	{
		const double ARROW_SIZE = 4;

		g.Save ();
		g.LineWidth = 1.5;
		g.SetSourceColor (color);

		const double RADIUS = 11;
		const double OFFSET = 1;

		PointD p1 = new (
			X: swap_rect.Left + RADIUS,
			Y: swap_rect.Bottom - OFFSET);

		g.MoveTo (p1.X, p1.Y);

		g.CurveTo (
			p1.X,
			p1.Y - RADIUS - 2,
			p1.X,
			p1.Y - RADIUS + OFFSET,
			swap_rect.Left + OFFSET,
			swap_rect.Bottom - RADIUS);

		g.MoveTo (p1.X - ARROW_SIZE, p1.Y - ARROW_SIZE);

		g.LineTo (p1.X, p1.Y);
		g.LineTo (p1.X + ARROW_SIZE, p1.Y - ARROW_SIZE);

		PointD p2 = new (
			X: swap_rect.Left + OFFSET,
			Y: swap_rect.Bottom - RADIUS);

		g.MoveTo (p2.X + ARROW_SIZE, p2.Y - ARROW_SIZE);

		g.LineTo (p2.X, p2.Y);
		g.LineTo (p2.X + ARROW_SIZE, p2.Y + ARROW_SIZE);

		g.Stroke ();

		g.Restore ();
	}

	private void DrawSectionDecorations (Context g)
	{
		GetStyleContext ().GetColor (out Gdk.RGBA fg_rgba);
		Color fg = fg_rgba.ToCairoColor ();
		Color separator = new (fg.R, fg.G, fg.B, 0.25);

		double top = 3;
		double bottom = 3 + PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS;

		g.DrawLine (new PointD (recent_separator_x, top), new PointD (recent_separator_x, bottom), separator, 1);
		g.DrawLine (new PointD (palette_separator_x, top), new PointD (palette_separator_x, bottom), separator, 1);

		DrawClockIcon (g, recent_icon_rect, fg);
		DrawPaletteIcon (g, palette_icon_rect, fg);

		if (!show_action_icons)
			return;

		DrawButtonChrome (g, color_wheel_icon_rect, fg, hovered_element == WidgetElement.ColorWheel);
		DrawColorWheelIcon (g, color_wheel_icon_rect);

		DrawButtonChrome (g, float_colors_icon_rect, fg, hovered_element == WidgetElement.FloatColors);
		DrawFloatIcon (g, float_colors_icon_rect, fg, palette.PrimaryColor);
	}

	// A rounded background + border behind an action icon, shown on hover so it reads
	// as a clickable button (like the tool toolbar icons).
	private static void DrawButtonChrome (Context g, RectangleD icon, Color fg, bool hovered)
	{
		if (!hovered)
			return;

		RectangleD box = new (icon.X - 4, icon.Y - 4, icon.Width + 8, icon.Height + 8);
		g.FillRoundedRectangle (box, 5, new Color (fg.R, fg.G, fg.B, 0.14));

		g.Save ();
		RoundedRectanglePath (g, box, 5);
		g.SetSourceColor (new Color (fg.R, fg.G, fg.B, 0.45));
		g.LineWidth = 1;
		g.Stroke ();
		g.Restore ();
	}

	private static void RoundedRectanglePath (Context g, RectangleD r, double radius)
	{
		g.MoveTo (r.X + radius, r.Y);
		g.Arc (r.X + r.Width - radius, r.Y + radius, radius, -Math.PI / 2, 0);
		g.Arc (r.X + r.Width - radius, r.Y + r.Height - radius, radius, 0, Math.PI / 2);
		g.Arc (r.X + radius, r.Y + r.Height - radius, radius, Math.PI / 2, Math.PI);
		g.Arc (r.X + radius, r.Y + radius, radius, Math.PI, 3 * Math.PI / 2);
		g.ClosePath ();
	}

	private static void DrawClockIcon (Context g, RectangleD r, Color color)
	{
		Color faded = new (color.R, color.G, color.B, 0.55);
		g.DrawEllipse (r, faded, 1);

		double cx = r.X + r.Width / 2;
		double cy = r.Y + r.Height / 2;

		g.Save ();
		g.SetSourceColor (faded);
		g.LineWidth = 1;
		g.LineCap = LineCap.Round;
		g.MoveTo (cx, cy);
		g.LineTo (cx, cy - r.Height * 0.28);
		g.MoveTo (cx, cy);
		g.LineTo (cx + r.Width * 0.22, cy);
		g.Stroke ();
		g.Restore ();
	}

	private static void DrawPaletteIcon (Context g, RectangleD r, Color color)
	{
		Color faded = new (color.R, color.G, color.B, 0.55);
		g.DrawEllipse (r, faded, 1);

		double radius = r.Width * 0.12;
		(double, double, Color)[] dots = [
			(0.32, 0.32, new Color (0.85, 0.2, 0.2)),
			(0.68, 0.30, new Color (0.9, 0.7, 0.1)),
			(0.72, 0.62, new Color (0.2, 0.5, 0.85)),
			(0.38, 0.68, new Color (0.2, 0.7, 0.35)),
		];

		foreach ((double fx, double fy, Color c) in dots) {
			double dx = r.X + r.Width * fx;
			double dy = r.Y + r.Height * fy;
			g.FillEllipse (new RectangleD (dx - radius, dy - radius, radius * 2, radius * 2), c);
		}
	}

	private static void DrawColorWheelIcon (Context g, RectangleD r)
	{
		double cx = r.X + r.Width / 2;
		double cy = r.Y + r.Height / 2;
		double radius = r.Width / 2;

		g.Save ();
		const int segments = 12;
		for (int i = 0; i < segments; i++) {
			double a0 = i * 2 * Math.PI / segments;
			double a1 = (i + 1) * 2 * Math.PI / segments;
			double hue = i * 360.0 / segments;

			g.MoveTo (cx, cy);
			g.Arc (cx, cy, radius, a0, a1);
			g.ClosePath ();
			g.SetSourceColor (Color.FromHsv (hue, 0.85, 0.95));
			g.Fill ();
		}

		g.Arc (cx, cy, radius * 0.35, 0, 2 * Math.PI);
		g.SetSourceColor (new Color (1, 1, 1));
		g.Fill ();
		g.Restore ();
	}

	private static void DrawFloatIcon (Context g, RectangleD r, Color color, Color swatch)
	{
		Color faded = new (color.R, color.G, color.B, 0.7);
		g.DrawRectangle (r, faded, 1);
		g.DrawLine (
			new PointD (r.X, r.Y + 4),
			new PointD (r.Right, r.Y + 4),
			faded,
			1);

		// A color dot in the window body, so the button reads as "float the colors".
		double radius = r.Width * 0.22;
		double cx = r.X + r.Width / 2;
		double cy = r.Y + 4 + (r.Height - 4) / 2;
		g.FillEllipse (new RectangleD (cx - radius, cy - radius, radius * 2, radius * 2), swatch);
		g.DrawEllipse (new RectangleD (cx - radius, cy - radius, radius * 2, radius * 2), faded, 1);
	}

	private void HandleSizeAllocated (Gtk.DrawingArea.ResizeSignalArgs e)
		=> UpdateLayout (e.Width);

	private void UpdateLayout (int width)
	{
		int recent_cols = palette.MaxRecentlyUsedColor / PaletteWidget.PALETTE_ROWS;
		int swatch_height = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS;

		// Recent-colors section: a separator, then a small clock icon column, then the
		// recent swatches.
		recent_separator_x = 47;
		recent_icon_rect = new RectangleD (
			recent_separator_x + SECTION_GAP,
			2 + (swatch_height - ICON_SIZE) / 2.0,
			ICON_SIZE,
			ICON_SIZE);
		double recent_swatches_x = recent_icon_rect.Right + SECTION_GAP;

		recent_palette_rect = new RectangleD (
			recent_swatches_x,
			2,
			PaletteWidget.SWATCH_SIZE * recent_cols,
			swatch_height);

		// Palette section: a separator, then a small palette icon column, then the
		// rainbow swatches, and finally the color-wheel and float-colors action icons
		// on the right edge of the docked color picker.
		palette_separator_x = recent_palette_rect.Right + SECTION_GAP;
		palette_icon_rect = new RectangleD (
			palette_separator_x + SECTION_GAP,
			2 + (swatch_height - ICON_SIZE) / 2.0,
			ICON_SIZE,
			ICON_SIZE);
		double palette_swatches_x = palette_icon_rect.Right + SECTION_GAP;

		// The swatches are drawn for every palette color, so the clickable rect must
		// cover them all - the action icons are hit-tested first, so they stay safe
		// even if a long palette is drawn underneath them.
		int palette_columns = (palette.CurrentPalette.Colors.Count + PaletteWidget.PALETTE_ROWS - 1) / PaletteWidget.PALETTE_ROWS;

		palette_rect = new RectangleD (
			palette_swatches_x,
			2,
			PaletteWidget.SWATCH_SIZE * palette_columns,
			swatch_height);

		// The action icons sit after the swatches, but never off the right edge.
		double actions_width = 2 * ACTION_ICON_SIZE + SECTION_GAP;
		double actions_x = Math.Min (
			palette_rect.Right + SECTION_GAP,
			Math.Max (0, width - actions_width - PaletteWidget.PALETTE_MARGIN));

		color_wheel_icon_rect = new RectangleD (
			actions_x,
			2 + (swatch_height - ACTION_ICON_SIZE) / 2.0,
			ACTION_ICON_SIZE,
			ACTION_ICON_SIZE);
		float_colors_icon_rect = new RectangleD (
			color_wheel_icon_rect.Right + SECTION_GAP,
			color_wheel_icon_rect.Y,
			ACTION_ICON_SIZE,
			ACTION_ICON_SIZE);
	}

	/// <summary>
	/// Provide a custom tooltip based on the cursor location.
	/// </summary>
	private bool HandleQueryTooltip (object o, Gtk.Widget.QueryTooltipSignalArgs args)
	{
		string? text = null;
		PointD point = new (args.X, args.Y);

		static string BuildColorTooltip (Color color, string tooltip) => Translations.GetString ("Color") + $": #{color.ToHex ()}\n\n" + tooltip;

		switch (GetElementAtPoint (point)) {
			case WidgetElement.RecentColorsIcon:
				text = Translations.GetString ("Recently picked colors");
				break;
			case WidgetElement.PaletteIcon:
				text = Translations.GetString ("Quick colors");
				break;
			case WidgetElement.ColorWheel:
				text = Translations.GetString ("Show color wheel");
				break;
			case WidgetElement.FloatColors:
				text = Translations.GetString ("Float Colors");
				break;
			case WidgetElement.Palette:
				int paletteIndex = PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect);
				if (paletteIndex >= 0) {
					text = BuildColorTooltip (palette.CurrentPalette.Colors[paletteIndex],
					// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS.
					Translations.GetString ("Left click to set primary color. Right click to set secondary color. Middle click or press {0} and left click to choose palette color.",
						system.CtrlLabel ()));
				}
				break;
			case WidgetElement.RecentColorsPalette:
				int recentColorsIndex = PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true);
				if (recentColorsIndex >= 0) {
					text = BuildColorTooltip (palette.RecentlyUsedColors[recentColorsIndex],
					Translations.GetString ("Left click to set primary color. Right click to set secondary color."));
				}
				break;
			case WidgetElement.PrimaryColor:
				text = BuildColorTooltip (palette.PrimaryColor,
				Translations.GetString ("Click to select primary color."));
				break;
			case WidgetElement.SecondaryColor:
				text = BuildColorTooltip (palette.SecondaryColor,
				Translations.GetString ("Click to select secondary color."));
				break;
			case WidgetElement.SwapColors:
				string label = Translations.GetString ("Click to switch between primary and secondary color.");
				string shortcut_label = Translations.GetString ("Shortcut key");
				text = $"{label} {shortcut_label}: {"X"}";
				break;
			case WidgetElement.ResetColors:
				text = Translations.GetString ("Click to reset primary and secondary color.");
				break;
		}

		args.Tooltip.SetText (text);
		return text != null;
	}

	private void Palette_ColorChanged (object? sender, EventArgs e)
	{
		// Color change events may be received while the widget is minimized,
		// so we only call Invalidate() if the widget is shown.
		if (GetRealized ())
			QueueDraw ();
	}

	private async Task<PaletteColors?> RunColorPicker (bool primarySelected)
	{
		using ColorPickerDialog colorPicker = ColorPickerDialog.New (
			chrome.MainWindow,
			palette,
			new PaletteColors (palette.PrimaryColor, palette.SecondaryColor),
			primarySelected,
			true,
			Translations.GetString ("Choose Colors"));

		Gtk.ResponseType response = await colorPicker.RunAsync ();

		if (response != Gtk.ResponseType.Ok)
			return null;

		return (PaletteColors) colorPicker.Colors;
	}


	private async Task<SingleColor?> GetUserChosenColor (
		SingleColor colors,
		string title)
	{
		using ColorPickerDialog dialog = ColorPickerDialog.New (
			chrome.MainWindow,
			palette,
			colors,
			primarySelected: true,
			false,
			title);

		try {
			Gtk.ResponseType response = await dialog.RunAsync ();

			if (response != Gtk.ResponseType.Ok)
				return null;

			return (SingleColor) dialog.Colors;

		} finally {
			dialog.Destroy ();
		}
	}

	private WidgetElement GetElementAtPoint (PointD point)
	{
		if (show_action_icons && color_wheel_icon_rect.ContainsPoint (point))
			return WidgetElement.ColorWheel;

		if (show_action_icons && float_colors_icon_rect.ContainsPoint (point))
			return WidgetElement.FloatColors;

		if (recent_icon_rect.ContainsPoint (point))
			return WidgetElement.RecentColorsIcon;

		if (palette_icon_rect.ContainsPoint (point))
			return WidgetElement.PaletteIcon;

		if (palette_rect.ContainsPoint (point))
			return WidgetElement.Palette;

		if (recent_palette_rect.ContainsPoint (point))
			return WidgetElement.RecentColorsPalette;

		if (primary_rect.ContainsPoint (point))
			return WidgetElement.PrimaryColor;

		if (secondary_rect.ContainsPoint (point))
			return WidgetElement.SecondaryColor;

		if (swap_rect.ContainsPoint (point))
			return WidgetElement.SwapColors;

		if (reset_rect.ContainsPoint (point))
			return WidgetElement.ResetColors;

		return WidgetElement.Nothing;
	}

	private enum WidgetElement
	{
		Nothing,
		Palette,
		PaletteIcon,
		RecentColorsPalette,
		RecentColorsIcon,
		ColorWheel,
		FloatColors,
		PrimaryColor,
		SecondaryColor,
		SwapColors,
		ResetColors,
	}
}
