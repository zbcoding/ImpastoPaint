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

	private readonly RectangleD primary_rect = new (4, 3, 24, 24);
	private readonly RectangleD secondary_rect = new (17, 16, 24, 24);
	private readonly RectangleD swap_rect = new (27, 2, 15, 15);
	private readonly RectangleD reset_rect = new (2, 27, 15, 15);

	private const int SECTION_GAP = 8;
	private const int ICON_SIZE = 12;
	private const int ACTION_ICON_SIZE = 28;

	// The primary/secondary block occupies the bar's left edge up to this x.
	private const int SWATCHES_SECTION_RIGHT = 47;

	private IChromeService chrome = null!; // NRT - set by factory method
	private IPaletteService palette = null!;
	private ISystemService system = null!;

	public event EventHandler? ColorWheelClicked;
	public RectangleD ColorWheelButtonRect => color_wheel_icon_rect;
	public event EventHandler? FloatColorsClicked;
	/// <summary>
	/// Raised when a footer swatch is clicked, with true for primary and false for
	/// secondary. The floating Colors window handles this in place of a modal picker.
	/// </summary>
	public event EventHandler<bool>? EditColorRequested;
	// Right-clicking the float button asks for the "Reset window" popover.
	public event EventHandler? ResetColorWindowClicked;
	public RectangleD FloatColorsButtonRect => float_colors_icon_rect;

	// Fires whenever this widget's allocated width changes - other status bar
	// widgets (e.g. the cursor position / image size labels) use it as a cheap
	// proxy for "the footer is getting tight", since GTK Box doesn't expose a
	// resize signal of its own.
	public event EventHandler<int>? WidthChanged;

	/// <summary>
	/// What the collapsing chips take out of this widget's row right now, and what
	/// each would take if shown. Supplied by the status bar, which owns the chips.
	/// </summary>
	public readonly record struct FooterMetrics (int OccupiedByChips, int CursorChipWidth, int ImageChipWidth, bool Sliding);

	/// <summary>Queried at the start of every layout pass; see <see cref="FooterMetrics"/>.</summary>
	public Func<FooterMetrics>? GetFooterMetrics { get; set; }

	/// <summary>Which chips the collapse cascade left room for this pass.</summary>
	public event EventHandler<(bool cursor, bool image)>? ChipVisibilityChanged;

	// Tile size of the two swatch grids. Shrinks before either section folds, so the
	// colors stay reachable on a narrow window for as long as possible.
	private int swatch_size = PaletteWidget.SWATCH_SIZE;

	// Latest cascade verdict for each chip. Held across passes so a slide in flight
	// can't have its own outcome re-litigated on skewed measurements.
	private bool show_cursor_chip = true;
	private bool show_image_chip = true;

	// Fires when a section folds in/out of the bar, so the popover content can be
	// rebuilt to match.
	public event EventHandler? FoldStateChanged;
	private bool quick_colors_folded;
	private bool recent_colors_folded;
	private bool swatches_folded;
	public bool QuickColorsFolded => quick_colors_folded;
	public bool RecentColorsFolded => recent_colors_folded && palette.MaxRecentlyUsedColor > 0;
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
		palette.RecentColorsChanged += new EventHandler (Palette_LayoutChanged);
		palette.CurrentPalette.PaletteChanged += new EventHandler (Palette_LayoutChanged);
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

				EditColorRequested?.Invoke (this, element == WidgetElement.PrimaryColor);
				break;

			case WidgetElement.SwapColors:

				palette.SwapColors ();

				break;

			case WidgetElement.ResetColors:

				palette.ResetColors ();

				break;

			case WidgetElement.Palette:

				int index = PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect, false, swatch_size);

				if (index < 0)
					break;

				switch (PaletteWidget.ClassifySwatchClick (button, state.IsControlPressed (), recentColorPalette: false)) {
					case PaletteWidget.SwatchClickAction.SetSecondary:
						palette.SecondaryColor = palette.CurrentPalette.Colors[index];
						break;
					case PaletteWidget.SwatchClickAction.SetPrimary:
						palette.PrimaryColor = palette.CurrentPalette.Colors[index];
						break;
					case PaletteWidget.SwatchClickAction.EditColor:
						SingleColor pick = new (palette.CurrentPalette.Colors[index]);
						SingleColor? chosen = await PickSingleColor (
							pick,
							Translations.GetString ("Choose Palette Color"));

						if (chosen != null)
							palette.CurrentPalette.SetColor (index, chosen.Color);
						break;
				}

				break;

			case WidgetElement.RecentColorsPalette:

				int recent_index = PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true, swatch_size);

				if (recent_index < 0)
					break;

				Color recentColor = palette.RecentlyUsedColors.ElementAt (recent_index);

				switch (PaletteWidget.ClassifySwatchClick (button, state.IsControlPressed (), recentColorPalette: true)) {
					case PaletteWidget.SwatchClickAction.SetSecondary:
						palette.SetColor (false, recentColor, false);
						break;
					case PaletteWidget.SwatchClickAction.SetPrimary:
						palette.SetColor (true, recentColor, false);
						break;
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
			DrawSwatch (g, secondary_rect, palette.SecondaryColor, checkeredPattern);

			// Draw Primary color swatch
			DrawSwatch (g, primary_rect, palette.PrimaryColor, checkeredPattern);

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

			for (int i = 0; i < recent_count; i++)
				DrawSwatchFill (g, PaletteWidget.GetSwatchBounds (palette, i, recent_palette_rect, true, swatch_size), recent.ElementAt (i), checkeredPattern);
		}

		// Draw color swatches
		if (!quick_colors_folded) {
			var currentPalette = palette.CurrentPalette;

			for (int i = 0; i < currentPalette.Colors.Count; i++)
				DrawSwatchFill (g, PaletteWidget.GetSwatchBounds (palette, i, palette_rect, false, swatch_size), currentPalette.Colors[i], checkeredPattern);
		}

		// Draw the wheel/float action buttons last so they sit on top of the color
		// bars that slide underneath as the bar narrows (they'd otherwise be painted
		// over by the recent/quick swatch loops above).
		if (show_action_icons)
			DrawActionButtons (g);

		g.Dispose ();
	}

	//Fills a swatch (checkered first if it has transparency) and outlines it in white-then-black,
	//as the primary/secondary swatches at the left of the bar do.
	private static void DrawSwatch (Context g, RectangleD rect, Color color, Pattern checkeredPattern)
	{
		if (color.A < 1)
			g.FillRectangle (rect, checkeredPattern);

		g.FillRectangle (rect, color);
		g.DrawRectangle (new RectangleD (rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), new Color (1, 1, 1), 1);
		g.DrawRectangle (rect, new Color (0, 0, 0), 1);
	}

	//Fills a swatch (checkered first if it has transparency) with no outline, as the recent/quick
	//palette grids do.
	private static void DrawSwatchFill (Context g, RectangleD rect, Color color, Pattern checkeredPattern)
	{
		if (color.A < 1) // Only draw checkered pattern if there is transparency
			g.FillRectangle (rect, checkeredPattern);

		g.FillRectangle (rect, color);
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

	// Impasto: internal so the floating Colors window's live picker panel
	// (ColorPickerPanel) can draw the same section icons above its recent/quick
	// swatch rows.
	internal static void DrawClockIcon (Context g, RectangleD r, Color color)
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

	internal static void DrawPaletteIcon (Context g, RectangleD r, Color color)
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
	}

	private void UpdateLayout (int width, bool shrinkSwatches = true)
	{
		bool was_quick_folded = quick_colors_folded;
		bool was_recent_folded = recent_colors_folded;
		bool was_swatches_folded = swatches_folded;

		int recent_cols = PaletteWidget.GetRecentColorColumns (palette.MaxRecentlyUsedColor);
		int swatch_height = PaletteWidget.SWATCH_SIZE * PaletteWidget.PALETTE_ROWS;
		double actions_width = 2 * ACTION_ICON_SIZE + SECTION_GAP;

		// One budget drives the whole footer: this widget's row, shared with the status
		// bar chips. The two divide a fixed region, so the sum holds still - measuring
		// against this widget's allocation alone made each collapse hand its space
		// straight back to the palette, so a section folded, reappeared, and the chips
		// took turns flashing. Everything below is a plain function of the budget.
		//
		// The one thing that sum can't survive is a slide in flight: this widget's
		// allocation arrives fresh from the resize while the slot's is still the
		// previous pass's, and the skew is enough to flip a chip that's sitting on its
		// threshold back and forth every frame - the chips vibrated instead of sliding.
		// The cascade already settled that chip's fate, so leave the whole decision
		// alone until the slide lands, then re-decide on numbers that agree.
		FooterMetrics metrics = GetFooterMetrics?.Invoke () ?? default;
		double budget = width + metrics.OccupiedByChips;

		if (!metrics.Sliding) {
			show_cursor_chip = true;
			show_image_chip = true;
			swatch_size = PaletteWidget.SWATCH_SIZE;
			quick_colors_folded = false;
			recent_colors_folded = palette.MaxRecentlyUsedColor == 0;
			swatches_folded = false;

			// What the footer needs at the current state: the color content, the action
			// pair it must never overlap, and whichever chips are still shown.
			double Needed () =>
				(swatches_folded ? PaletteWidget.PALETTE_MARGIN : SWATCHES_SECTION_RIGHT)
				+ (recent_colors_folded ? 0 : SectionWidth (recent_cols, swatch_size))
				+ (quick_colors_folded ? 0 : SectionWidth (PaletteColumns, swatch_size))
				+ actions_width + PaletteWidget.PALETTE_MARGIN
				+ (show_image_chip ? metrics.ImageChipWidth : 0)
				+ (show_cursor_chip ? metrics.CursorChipWidth : 0);

			// Tiles give up size before a section gives up its place, but only down to
			// the floor - past that, folding the section is the better trade. Whatever
			// grid is left gets to start over from full size, so folding the quick
			// colors away leaves the recent colors legible again.
			void ShrinkTiles ()
			{
				swatch_size = PaletteWidget.SWATCH_SIZE;
				while (Needed () > budget && swatch_size > PaletteWidget.MIN_SWATCH_SIZE)
					swatch_size--;
			}

			// Collapse order, outermost first. Image size / aspect ratio goes, then
			// cursor position, then the swatch grids shrink during window resizing,
			// and only then do whole sections fold into the popover. Palette-content
			// changes skip shrinking: those changes must reposition the action buttons
			// without making the established footer swatches smaller.
			if (show_action_icons) {
				if (Needed () > budget) show_image_chip = false;
				if (Needed () > budget) show_cursor_chip = false;
				if (shrinkSwatches) ShrinkTiles ();
				if (Needed () > budget) {
					quick_colors_folded = true;
					if (shrinkSwatches) ShrinkTiles ();
				}
				if (Needed () > budget) {
					recent_colors_folded = true;
					if (shrinkSwatches) ShrinkTiles ();
				}
				if (Needed () > budget) swatches_folded = true;
			}
		}

		// Swatch grids keep their vertical centre as the tiles shrink.
		double swatch_y = 2 + (swatch_height - swatch_size * PaletteWidget.PALETTE_ROWS) / 2.0;

		// The anchor where the next visible section starts. Normally that's right
		// after the swap/reset icons (x=47); once those fold too, start from the
		// left margin instead.
		double cursor_x = swatches_folded ? PaletteWidget.PALETTE_MARGIN : SWATCHES_SECTION_RIGHT;

		// Lays out one "separator, small icon column, swatch grid" section starting at
		// cursorX, returning where the next section should start. Shared by the recent-colors
		// and quick-colors sections below, which differ only in column count and which fields
		// the result lands in.
		(double SeparatorX, RectangleD IconRect, RectangleD ContentRect, double NextCursorX) LayoutSection (double cursorX, int columns)
		{
			double separatorX = cursorX;
			RectangleD iconRect = new (
				separatorX + SECTION_GAP,
				2 + (swatch_height - ICON_SIZE) / 2.0,
				ICON_SIZE,
				ICON_SIZE);
			double swatchesX = iconRect.Right + SECTION_GAP;

			RectangleD contentRect = new (
				swatchesX,
				swatch_y,
				swatch_size * columns,
				swatch_size * PaletteWidget.PALETTE_ROWS);

			return (separatorX, iconRect, contentRect, contentRect.Right + SECTION_GAP);
		}

		// Recent-colors section: a separator, then a small clock icon column, then the
		// recent swatches. Folds out after the quick colors section.
		if (!recent_colors_folded) {
			(recent_separator_x, recent_icon_rect, recent_palette_rect, cursor_x) = LayoutSection (cursor_x, recent_cols);
		} else {
			recent_separator_x = -1;
			recent_icon_rect = RectangleD.Zero;
			recent_palette_rect = RectangleD.Zero;
		}

		// Palette section: a separator, then a small palette icon column, then the
		// rainbow swatches. Folds out before the recent-colors section, since it's
		// usually the wider of the two.
		//
		// The swatches are drawn for every palette color, so the clickable rect must cover
		// them all - the action icons are hit-tested first, so they stay safe even if a long
		// palette is drawn underneath them.
		if (!quick_colors_folded) {
			(palette_separator_x, palette_icon_rect, palette_rect, cursor_x) = LayoutSection (cursor_x, PaletteColumns);
		} else {
			palette_separator_x = -1;
			palette_icon_rect = RectangleD.Zero;
			palette_rect = RectangleD.Zero;
		}

		// The action icons sit immediately after the last visible section and nowhere
		// else. They used to be clamped to the bar's right edge as well, which pulled
		// them left across the swatches whenever this widget's allocation briefly
		// lagged the cascade - they'd slide over the colors and snap back. The
		// cascade already guarantees they fit, so the clamp only ever misplaced them.
		double actions_x = Math.Max (0, cursor_x);

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
		ChipVisibilityChanged?.Invoke (this, (show_cursor_chip, show_image_chip));
		WidthChanged?.Invoke (this, width);

		if (quick_colors_folded != was_quick_folded || recent_colors_folded != was_recent_folded || swatches_folded != was_swatches_folded)
			FoldStateChanged?.Invoke (this, EventArgs.Empty);
	}

	private int PaletteColumns =>
		(palette.CurrentPalette.Colors.Count + PaletteWidget.PALETTE_ROWS - 1) / PaletteWidget.PALETTE_ROWS;

	// A swatch section: separator gap, icon column, gap, the swatches, trailing gap.
	private static double SectionWidth (int columns, int swatchSize) =>
		SECTION_GAP + ICON_SIZE + SECTION_GAP + swatchSize * columns + SECTION_GAP;

	/// <summary>
	/// Provide a custom tooltip based on the cursor location.
	/// </summary>
	private bool HandleQueryTooltip (object o, Gtk.Widget.QueryTooltipSignalArgs args)
	{
		string? text = null;
		PointD point = new (args.X, args.Y);

		switch (GetElementAtPoint (point)) {
			case WidgetElement.RecentColorsIcon:
				text = PaletteWidget.RecentlyPickedColorsLabel;
				break;
			case WidgetElement.PaletteIcon:
				text = PaletteWidget.QuickColorsLabel;
				break;
			case WidgetElement.ColorWheel:
				text = Translations.GetString ("Show color wheel");
				break;
			case WidgetElement.FloatColors:
				text = Translations.GetString ("Float Colors");
				break;
			case WidgetElement.Palette:
				int paletteIndex = PaletteWidget.GetSwatchAtLocation (palette, point, palette_rect, false, swatch_size);
				if (paletteIndex >= 0)
					text = PaletteWidget.BuildSwatchTooltip (
						palette.CurrentPalette.Colors[paletteIndex],
						PaletteWidget.GetSwatchInstructions (recentColorPalette: false));
				break;
			case WidgetElement.RecentColorsPalette:
				int recentColorsIndex = PaletteWidget.GetSwatchAtLocation (palette, point, recent_palette_rect, true, swatch_size);
				if (recentColorsIndex >= 0)
					text = PaletteWidget.BuildSwatchTooltip (
						palette.RecentlyUsedColors[recentColorsIndex],
						PaletteWidget.GetSwatchInstructions (recentColorPalette: true));
				break;
			case WidgetElement.PrimaryColor:
				text = PaletteWidget.BuildSwatchTooltip (
					palette.PrimaryColor,
					Translations.GetString ("Click to select primary color."));
				break;
			case WidgetElement.SecondaryColor:
				text = PaletteWidget.BuildSwatchTooltip (
					palette.SecondaryColor,
					Translations.GetString ("Click to select secondary color."));
				break;
			case WidgetElement.SwapColors:
				string label = Translations.GetString ("Click to switch between primary and secondary color.");
				string shortcut_label = Translations.GetString ("Shortcut key");
				text = $"{label} {shortcut_label}: {PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.SwapColors).ToLabel ()}";
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
		if (GetRealized ())
			QueueDraw ();
	}

	private void Palette_LayoutChanged (object? sender, EventArgs e)
	{
		// Palette events may be received while the widget is minimized,
		// so only update the realized footer.
		if (!GetRealized ())
			return;

		// Palette size and row-count changes alter every section after the quick
		// colors, including the wheel and float buttons. Recompute even when the
		// overall widget height is unchanged.
		HeightRequest = PaletteWidget.WIDGET_HEIGHT;
		UpdateLayout (GetWidth (), shrinkSwatches: false);
		QueueDraw ();
	}

	// The modal single-color picker behind the quick-color edit flow. Exposed so the color-wheel
	// popover's folded-in primary/secondary mini section (MainWindow) can reuse the same dialog
	// setup as the bar's swatches.
	public Task<SingleColor?> PickSingleColor (
		SingleColor initial,
		string title)
		=> ColorPickerDialog.PickSingleColorAsync (
			chrome.MainWindow,
			palette,
			initial,
			title);

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
