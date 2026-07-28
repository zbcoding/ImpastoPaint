// Impasto: the "advanced" section of the floating Colors window — hex entry plus
// HSV / RGB / alpha sliders, applied live to the palette's primary color. Reuses
// ColorPickerSlider from the modal picker so the two look and behave identically.
// ponytail: live-apply only, no OK/Cancel/Reset — the floating window edits the
// palette directly, so there is nothing to cancel back to.

using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.Box>]
public sealed partial class ColorSlidersWidget
{
	private const int SLIDER_WIDTH = 200;
	private const int SPACING = 6;

	private IPaletteService palette = null!; // NRT - set by factory method
	private Gtk.Entry hex_entry = null!;
	private ColorPickerSlider[] sliders = null!;

	// Guards the palette -> widget -> palette feedback loop.
	private bool updating;

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Vertical);
		Spacing = SPACING;

		hex_entry = Gtk.Entry.New ();
		hex_entry.MaxWidthChars = 10;
		hex_entry.OnChanged += (sender, _) => {
			if (updating) return;
			Color? parsed = Color.FromHex (sender.GetText ());
			if (parsed is not null)
				ApplyColor (parsed.Value);
		};

		Gtk.Label hexLabel = Gtk.Label.New (Translations.GetString ("Hex"));
		hexLabel.WidthRequest = 50;

		Gtk.Box hexBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		hexBox.Append (hexLabel);
		hexBox.Append (hex_entry);
		Append (hexBox);

		sliders = [
			CreateSlider (ColorPickerSlider.Component.Hue),
			CreateSlider (ColorPickerSlider.Component.Saturation),
			CreateSlider (ColorPickerSlider.Component.Value),
			CreateSlider (ColorPickerSlider.Component.Red),
			CreateSlider (ColorPickerSlider.Component.Green),
			CreateSlider (ColorPickerSlider.Component.Blue),
			CreateSlider (ColorPickerSlider.Component.Alpha),
		];

		Append (sliders[0]);
		Append (sliders[1]);
		Append (sliders[2]);
		Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		Append (sliders[3]);
		Append (sliders[4]);
		Append (sliders[5]);
		Append (Gtk.Separator.New (Gtk.Orientation.Horizontal));
		Append (sliders[6]);
	}

	private ColorPickerSlider CreateSlider (ColorPickerSlider.Component component)
	{
		ColorPickerSlider slider = ColorPickerSlider.New (component, SLIDER_WIDTH);
		slider.OnColorChanged += (sender, _) => {
			if (updating) return;
			ApplyColor (((ColorPickerSlider) sender!).Color);
		};
		return slider;
	}

	private void Configure (IPaletteService palette)
	{
		this.palette = palette;

		palette.PrimaryColorChanged += SyncFromPalette;

		SyncFromPalette (this, EventArgs.Empty);
	}

	public static ColorSlidersWidget New (IPaletteService palette)
	{
		ColorSlidersWidget widget = NewWithProperties ([]);
		widget.Configure (palette);
		return widget;
	}

	private void ApplyColor (Color color)
	{
		updating = true;
		// ponytail: sliders never add to recent colors — every drag tick would flood
		// the list; the wheel and swatches already feed recents.
		palette.SetColor (setPrimary: true, color, addToRecent: false);
		SyncControls (color);
		updating = false;
	}

	private void SyncFromPalette (object? sender, EventArgs e)
	{
		if (updating) return;

		updating = true;
		SyncControls (palette.PrimaryColor);
		updating = false;
	}

	private void SyncControls (Color color)
	{
		foreach (var slider in sliders)
			slider.Color = color;

		if (!hex_entry.IsEditingText ())
			hex_entry.SetText (color.ToHex ());
	}
}
