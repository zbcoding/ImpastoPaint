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

	// Responsive folding thresholds (own allocated width). As the bar narrows past
	// each one, that section stops drawing in the bar - its content moves into the
	// color-wheel popover instead (see MainWindow's popover section builders).
	// Each has a separate, wider UNFOLD threshold so the section stays folded once
	// it's been folded (hysteresis): a tiny width wobble around the boundary (which
	// happens as other sections above it pack/unpack the same bar) can't make it
	// ping-pong back into the footer.
	private const int FOLD_QUICK_COLORS_WIDTH = 300;
	private const int FOLD_QUICK_COLORS_UNFOLD = 350;
	private const int FOLD_RECENT_COLORS_WIDTH = 220;
	private const int FOLD_RECENT_COLORS_UNFOLD = 270;
	private const int FOLD_SWATCHES_WIDTH = 130;
	private const int FOLD_SWATCHES_UNFOLD = 180;

	private IChromeService chrome = null!; // NRT - set by factory method
	private IPaletteService palette = null!;
	private ISystemService system = null!;

	public event EventHandler? ColorWheelClicked;
	public RectangleD ColorWheelButtonRect => color_wheel_icon_rect;
	public event EventHandler? FloatColorsClicked;
	// Right-clicking the float button asks for the "Reset window" popover.
	public event EventHandler? ResetColorWindowClicked;
	public RectangleD FloatColorsButtonRect => float_colors_icon_rect;

	// Fires whenever this widget's allocated width changes - other status bar
	// widgets (e.g. the cursor position / image size labels) use it as a cheap
	// proxy for "the footer is getting tight", since GTK Box doesn't expose a
	// resize signal of its own.
	public event EventHandler<int>? WidthChanged;
	public bool ActionButtonsAtRightEdge { get; private set; }

	// Fires when a section folds in/out of the bar, so the popover content can be
	// rebuilt to match.
	public event EventHandler? FoldStateChanged;
	private bool quick_colors_folded;
	private bool recent_colors_folded;
	private bool swatches_folded;
	public bool QuickColorsFolded => quick_colors_folded;
	public bool RecentColorsFolded => recent_colors_folded;
	public bool SwatchesFolded => swatches_folded;

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

		GetStyleContext ().GetColor (out Gdk.RGBA fg_color);
		Cairo.Color cairo_fg_color = fg_color.ToCairoColor ();

		if (!swatches_folded) {

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
			DrawSwapIcon (g, cairo_fg_color);

			// Draw the reset icon.
			double square_size = 0.6 * reset_rect.Width;
			g.DrawRectangle (new RectangleD (reset_rect.Location (), square_size, square_size), cairo_fg_color, 1);
			g.FillRectangle (new RectangleD (reset_rect.Right - square_size, reset_rect.Bottom - square_size, square_size, square_size), cairo_fg_color);
		}

		DrawSectionDecorations (g);

		// Draw recently used color swatches
		if (!recent_colors_folded) {
			var recent = palette.RecentlyUsedColors;

			// Only draw up to MaxRecentlyUsedColor, which is what the grid fits. The list
			// can transiently hold more if the extended-palette setting was just toggled.
			int recent_count = Math.Min (recent.Count, palette.MaxRecentlyUsedColor);

			for (int i = 0; i < recent_count; i++) {

				RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, recent_palette_rect, true);
				Color recentColor = recent.ElementAt (i);

				if (recentColor.A < 1) // Only draw checkered pattern if there is transparency
					g.FillRectangle (swatchBounds, checkeredPattern);

				g.FillRectangle (swatchBounds, recentColor);
			}
		}

		// Draw color swatches
		if (!quick_colors_folded) {
			var currentPalette = palette.CurrentPalette;

			for (int i = 0; i < currentPalette.Colors.Count; i++) {

				RectangleD swatchBounds = PaletteWidget.GetSwatchBounds (palette, i, palette_rect);
				Color paletteColor = currentPalette.Colors[i];

				if (paletteColor.A < 1) // Only draw checkered pattern if there is transparency
					g.FillRectangle (swatchBounds, checkeredPattern);

				g.FillRectangle (swatchBounds, paletteColor);
			}
		}

		// Draw the wheel/float action buttons last so they sit on top of the color
		// bars that slide underneath as the bar narrows (they'd otherwise be painted
		// over by the recent/quick swatch loops above).
		if (show_action_icons)
			DrawActionButtons (g);

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

		if (!recent_colors_folded) {
			g.DrawLine (new PointD (recent_separator_x, top), new PointD (recent_separator_x, bottom), separator, 1);
			DrawClockIcon (g, recent_icon_rect, fg);
		}

		if (!quick_colors_folded) {
			g.DrawLine (new PointD (palette_separator_x, top), new PointD (palette_separator_x, bottom), separator, 1);
			DrawPaletteIcon (g, palette_icon_rect, fg);
		}
	}

	// The color-wheel and float-colors buttons, drawn as a pair at the right edge.
	// They render on top of whatever color bar is sliding underneath (see Draw), and
	// each gets an opaque rounded background so the bar below can't be seen through
	// or clicked through while it slides past.
	private void DrawActionButtons (Context g)
	{
		GetStyleContext ().GetColor (out Gdk.RGBA fg_rgba);
		Color fg = fg_rgba.ToCairoColor ();

		RectangleD pill = new (
			color_wheel_icon_rect.X - 4,
			color_wheel_icon_rect.Y - 4,
			float_colors_icon_rect.Right - color_wheel_icon_rect.X + 8,
			color_wheel_icon_rect.Height + 8);
		g.FillRoundedRectangle (pill, 6, ResolveOpaqueBackground ());

		DrawButtonChrome (g, color_wheel_icon_rect, fg, hovered_element == WidgetElement.ColorWheel);
		DrawColorWheelIcon (g, color_wheel_icon_rect);

		DrawButtonChrome (g, float_colors_icon_rect, fg, hovered_element == WidgetElement.FloatColors);
		DrawFloatIcon (g, float_colors_icon_rect, fg, palette.PrimaryColor);
	}

	// The footer's own background grey, resolved from the theme. It's what the pill
	// needs to be opaque against so the color bar sliding underneath stays hidden.
	private Color ResolveOpaqueBackground ()
	{
		if (GetStyleContext ().LookupColor ("window_bg_color", out Gdk.RGBA bg))
			return bg.ToCairoColor ();

		// Fallback: a neutral grey derived from the foreground when the named color
		// isn't available, so the pill never has a transparent see-through fill.
		GetStyleContext ().GetColor (out Gdk.RGBA fg_rgba);
		Cairo.Color fg = fg_rgba.ToCairoColor ();
		double lum = 0.3 * fg.R + 0.59 * fg.G + 0.11 * fg.B;
		double v = lum > 0.5 ? 0.93 : 0.15;
		return new Color (v, v, v);
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
	{
		UpdateLayout (e.Width);
		WidthChanged?.Invoke (this, e.Width);
	}

	private void UpdateLayout (int width)
	{
		bool was_quick_folded = quick_colors_folded;
		bool was_recent_folded = recent_colors_folded;
		bool was_swatches_folded = swatches_folded;

		// A folded section only unfolds after its wider threshold is reached. Once
		// unfolded, it stays visible until the narrower fold threshold is reached;
		// do not apply the unfold threshold again while it is already visible.
		// Also wait until the action pair is no longer pinned to the right edge.
		// That edge state can be caused by a label reclaiming width while the
		// window is still narrowing, and must not bring palette sections back.
		bool may_unfold_sections = !ActionButtonsAtRightEdge;
		if (quick_colors_folded) {
			if (may_unfold_sections && width >= FOLD_QUICK_COLORS_UNFOLD)
				quick_colors_folded = false;
		} else if (width < FOLD_QUICK_COLORS_WIDTH) {
			quick_colors_folded = true;
		}

		if (recent_colors_folded) {
			if (may_unfold_sections && width >= FOLD_RECENT_COLORS_UNFOLD)
				recent_colors_folded = false;
		} else if (width < FOLD_RECENT_COLORS_WIDTH) {
			recent_colors_folded = true;
		}

		if (swatches_folded) {
			if (may_unfold_sections && width >= FOLD_SWATCHES_UNFOLD)
				swatches_folded = false;
		} else if (width < FOLD_SWATCHES_WIDTH) {
			swatches_folded = true;
		}

		int recent_cols = PaletteWidget.GetRecentColorColumns (palette.MaxRecentlyUsedColor);
		int swatch_height = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS;

		// The anchor where the next visible section starts. Normally that's right
		// after the swap/reset icons (x=47); once those fold too, start from the
		// left margin instead.
		double cursor_x = swatches_folded ? PaletteWidget.PALETTE_MARGIN : 47;

		// Recent-colors section: a separator, then a small clock icon column, then the
		// recent swatches. Folds out first as the bar gets tight (past FOLD_RECENT_COLORS_WIDTH).
		if (!recent_colors_folded) {
			recent_separator_x = cursor_x;
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
			cursor_x = recent_palette_rect.Right + SECTION_GAP;
		} else {
			recent_separator_x = -1;
			recent_icon_rect = RectangleD.Zero;
			recent_palette_rect = RectangleD.Zero;
		}

		// Palette section: a separator, then a small palette icon column, then the
		// rainbow swatches. Folds out before the recent-colors section (past
		// FOLD_QUICK_COLORS_WIDTH, a wider threshold), since it's usually the wider
		// of the two.
		if (!quick_colors_folded) {
			palette_separator_x = cursor_x;
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
			cursor_x = palette_rect.Right + SECTION_GAP;
		} else {
			palette_separator_x = -1;
			palette_icon_rect = RectangleD.Zero;
			palette_rect = RectangleD.Zero;
		}

		// The action icons sit after the last visible section, but never off the
		// right edge, and never at a negative position on a very narrow bar.
		double actions_width = 2 * ACTION_ICON_SIZE + SECTION_GAP;
		double actions_x = Math.Max (0, Math.Min (
			cursor_x,
			Math.Max (0, width - actions_width - PaletteWidget.PALETTE_MARGIN)));

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
		// When the action pair reaches this limit, its right edge is touching the
		// palette's trailing boundary, which is the cursor group's leading edge.
		ActionButtonsAtRightEdge = show_action_icons
			&& float_colors_icon_rect.Right >= width - PaletteWidget.PALETTE_MARGIN;

		if (quick_colors_folded != was_quick_folded || recent_colors_folded != was_recent_folded || swatches_folded != was_swatches_folded)
			FoldStateChanged?.Invoke (this, EventArgs.Empty);
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
		if (GetRealized ()) {
			// Toggling the extended-palette setting reloads the default palette; the
			// extra row changes PALETTE_ROWS and the widget height, so re-layout.
			if (HeightRequest != PaletteWidget.WIDGET_HEIGHT) {
				HeightRequest = PaletteWidget.WIDGET_HEIGHT;
				UpdateLayout (GetWidth ());
			}
			QueueDraw ();
		}
	}

	// Exposed so the color-wheel popover's folded-in primary/secondary mini section
	// (MainWindow) can reuse the same color picker dialog as the bar's swatches,
	// instead of duplicating the dialog setup.
	public Task<PaletteColors?> PickColorsAsync (bool primarySelected) => RunColorPicker (primarySelected);

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

		if (!recent_colors_folded && recent_icon_rect.ContainsPoint (point))
			return WidgetElement.RecentColorsIcon;

		if (!quick_colors_folded && palette_icon_rect.ContainsPoint (point))
			return WidgetElement.PaletteIcon;

		if (!quick_colors_folded && palette_rect.ContainsPoint (point))
			return WidgetElement.Palette;

		if (!recent_colors_folded && recent_palette_rect.ContainsPoint (point))
			return WidgetElement.RecentColorsPalette;

		if (!swatches_folded && primary_rect.ContainsPoint (point))
			return WidgetElement.PrimaryColor;

		if (!swatches_folded && secondary_rect.ContainsPoint (point))
			return WidgetElement.SecondaryColor;

		if (!swatches_folded && swap_rect.ContainsPoint (point))
			return WidgetElement.SwapColors;

		if (!swatches_folded && reset_rect.ContainsPoint (point))
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
