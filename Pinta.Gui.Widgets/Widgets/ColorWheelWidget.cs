// Impasto: an inline HSV colour wheel for the Colors palette, so the wheel is reachable
// without opening the modal picker (Paint.NET shows it in the Colors window itself).
//
// Deliberately a fresh widget rather than an extraction from ColorPickerDialog: that
// dialog is 1,013 lines and the worst possible rebase target against upstream. The wheel
// maths here matches its HueAndSat surface so the two agree on which pixel is which
// colour. ponytail: hue/saturation plus a value slider, no alpha and no numeric entry —
// "More >>" already opens the full picker for those.

using System;
using Cairo;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.Box>]
public sealed partial class ColorWheelWidget
{
	private const int WHEEL_SIZE = 160;
	private const int RADIUS = WHEEL_SIZE / 2;
	private const int CURSOR_SIZE = 10;

	private IPaletteService palette = null!; // NRT - set by factory method
	private Gtk.DrawingArea wheel = null!;
	private Gtk.Scale value_slider = null!;

	// Guards the palette -> widget -> palette feedback loop.
	private bool updating;

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Vertical);
		Spacing = 3;

		wheel = Gtk.DrawingArea.New ();
		wheel.SetSizeRequest (WHEEL_SIZE, WHEEL_SIZE);
		wheel.Halign = Gtk.Align.Center;
		wheel.SetDrawFunc ((_, context, _, _) => Draw (context));

		double start_x = 0;
		double start_y = 0;

		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();

		drag.OnDragBegin += (_, e) => {
			start_x = e.StartX;
			start_y = e.StartY;
			PickAt (start_x, start_y);
		};

		drag.OnDragUpdate += (_, e) => PickAt (start_x + e.OffsetX, start_y + e.OffsetY);

		drag.OnDragEnd += (_, _) => CommitRecentColor ();

		wheel.AddController (drag);

		value_slider = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 1, 0.01);
		value_slider.DrawValue = false;
		value_slider.SetValue (1);
		value_slider.OnValueChanged += (_, _) => {
			if (updating) return;
			HsvColor hsv = palette.PrimaryColor.ToHsv ();
			SetColor (hsv.Hue, hsv.Sat, value_slider.GetValue ());
		};

		Gtk.GestureClick slider_click = Gtk.GestureClick.New ();
		slider_click.OnReleased += (_, _) => CommitRecentColor ();
		value_slider.AddController (slider_click);

		Gtk.Image brightness_icon = Gtk.Image.NewFromIconName (Resources.Icons.AdjustmentsBrightnessContrast);
		brightness_icon.TooltipText = Translations.GetString ("Brightness");
		brightness_icon.AddCssClass ("dim-label");

		Gtk.Box value_row = Gtk.Box.New (Gtk.Orientation.Horizontal, 3);
		value_row.Append (brightness_icon);
		value_row.Append (value_slider);
		value_slider.Hexpand = true;

		Append (wheel);
		Append (value_row);
	}

	private void Configure (IPaletteService palette)
	{
		this.palette = palette;

		palette.PrimaryColorChanged += SyncFromPalette;

		SyncFromPalette (this, EventArgs.Empty);
	}

	public static ColorWheelWidget New (IPaletteService palette)
	{
		ColorWheelWidget widget = NewWithProperties ([]);
		widget.Configure (palette);
		return widget;
	}

	private void SyncFromPalette (object? sender, EventArgs e)
	{
		if (updating) return;

		updating = true;
		value_slider.SetValue (palette.PrimaryColor.ToHsv ().Val);
		updating = false;

		wheel.QueueDraw ();
	}

	/// <summary>
	/// Angle is hue, distance from the centre is saturation. Matches
	/// ColorPickerDialog's HueAndSat surface.
	/// </summary>
	private void PickAt (double x, double y)
	{
		double dx = x - RADIUS;
		double dy = y - RADIUS;

		double hue = (Math.Atan2 (dy, -dx) + Math.PI) / (2 * Math.PI) * 360;
		double sat = Math.Min (Math.Sqrt (dx * dx + dy * dy) / RADIUS, 1);

		SetColor (hue, sat, value_slider.GetValue ());
	}

	private void SetColor (double hue, double sat, double val)
	{
		updating = true;
		palette.SetColor (
			setPrimary: true,
			palette.PrimaryColor.CopyHsv (hue: hue, sat: sat, value: val),
			addToRecent: false);
		updating = false;

		wheel.QueueDraw ();
	}

	private void CommitRecentColor ()
	{
		palette.SetColor (setPrimary: true, palette.PrimaryColor, addToRecent: true);
	}

	private void Draw (Context g)
	{
		double val = value_slider.GetValue ();

		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, WHEEL_SIZE, WHEEL_SIZE);

		Span<ColorBgra> data = surface.GetPixelData ();

		for (int y = 0; y < WHEEL_SIZE; y++) {
			for (int x = 0; x < WHEEL_SIZE; x++) {

				double dx = x - RADIUS;
				double dy = y - RADIUS;
				double magnitude = Math.Sqrt (dx * dx + dy * dy);

				if (magnitude > RADIUS) continue;

				double hue = (Math.Atan2 (dy, -dx) + Math.PI) / (2 * Math.PI) * 360;
				double sat = Math.Min (magnitude / RADIUS, 1);

				// Fade the outermost pixel to antialias the rim.
				double edge = RADIUS - magnitude;

				data[WHEEL_SIZE * y + x] = Color.FromHsv (hue, sat, val, edge < 1 ? edge : 1).ToColorBgra ();
			}
		}

		surface.MarkDirty ();
		g.SetSourceSurface (surface, 0, 0);
		g.Paint ();

		DrawCursor (g);
	}

	private void DrawCursor (Context g)
	{
		Color current = palette.PrimaryColor;
		HsvColor hsv = current.ToHsv ();

		// Inverse of the mapping in Draw ().
		double angle = hsv.Hue / 360 * 2 * Math.PI - Math.PI;
		double distance = hsv.Sat * RADIUS;

		double x = RADIUS - distance * Math.Cos (angle);
		double y = RADIUS + distance * Math.Sin (angle);

		RectangleD box = new (x - CURSOR_SIZE / 2, y - CURSOR_SIZE / 2, CURSOR_SIZE, CURSOR_SIZE);

		g.Antialias = Antialias.None;
		g.FillRectangle (box, current);
		g.DrawRectangle (box, new Color (0, 0, 0), 1);
	}
}
