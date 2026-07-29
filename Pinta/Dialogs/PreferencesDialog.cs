using System;
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta;

// Impasto: application-wide user preferences. First setting is the default size of
// the blank canvas created on startup. Grows as more preferences are added.
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class PreferencesDialog
{
	private Gtk.SpinButton canvas_width_spinner;
	private Gtk.SpinButton canvas_height_spinner;
	private PintaColorButton canvas_surround_color_button;

	private const int SPACING = 6;
	internal const int MAX_CANVAS_DIMENSION = 10000;
	private const long MAX_CANVAS_PIXELS = 100_000_000;

	internal static bool IsValidCanvasSize (int width, int height)
		=> width > 0 && height > 0
			&& width <= MAX_CANVAS_DIMENSION
			&& height <= MAX_CANVAS_DIMENSION
			&& (long) width * height <= MAX_CANVAS_PIXELS;

	public int DefaultCanvasWidth => canvas_width_spinner.GetValueAsInt ();
	public int DefaultCanvasHeight => canvas_height_spinner.GetValueAsInt ();
	public Cairo.Color CanvasSurroundColor => canvas_surround_color_button.DisplayColor;

	internal static PreferencesDialog New (ChromeManager chrome, int defaultCanvasWidth, int defaultCanvasHeight, Cairo.Color canvasSurroundColor)
	{
		PreferencesDialog dialog = NewWithProperties ([]);
		dialog.canvas_width_spinner.Value = defaultCanvasWidth;
		dialog.canvas_height_spinner.Value = defaultCanvasHeight;
		dialog.canvas_surround_color_button.DisplayColor = canvasSurroundColor;
		dialog.TransientFor = chrome.MainWindow;
		return dialog;
	}

	[MemberNotNull (nameof (canvas_width_spinner))]
	[MemberNotNull (nameof (canvas_height_spinner))]
	[MemberNotNull (nameof (canvas_surround_color_button))]
	partial void Initialize ()
	{
		Gtk.SpinButton widthSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		widthSpinner.SetActivatesDefaultImmediate (true);

		Gtk.SpinButton heightSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		heightSpinner.SetActivatesDefaultImmediate (true);

		PintaColorButton canvasSurroundColorButton = PintaColorButton.New ();
		canvasSurroundColorButton.TooltipText = Translations.GetString ("Choose color...");
		canvasSurroundColorButton.OnClicked += ChooseCanvasSurroundColor;

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = SPACING;
		grid.ColumnSpacing = SPACING;
		grid.ColumnHomogeneous = false;

		grid.Attach (CreateLabel (Translations.GetString ("Default canvas size (on application open):"), Gtk.Align.Start), 0, 0, 3, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Width:"), Gtk.Align.End), 0, 1, 1, 1);
		grid.Attach (widthSpinner, 1, 1, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 1, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Height:"), Gtk.Align.End), 0, 2, 1, 1);
		grid.Attach (heightSpinner, 1, 2, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 2, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Canvas surround color:"), Gtk.Align.End), 0, 3, 1, 1);
		grid.Attach (canvasSurroundColorButton, 1, 3, 2, 1);

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);

		Gtk.Box canvasPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		canvasPage.Append (grid);

		Gtk.Button resetButton = Gtk.Button.NewWithLabel (Translations.GetString ("Reset to Defaults"));
		resetButton.Halign = Gtk.Align.Start;
		resetButton.OnClicked += (_, _) => {
			widthSpinner.Value = 800;
			heightSpinner.Value = 600;
			canvasSurroundColorButton.DisplayColor = Cairo.Color.FromHex (SettingNames.DEFAULT_CANVAS_SURROUND_COLOR)!.Value;
		};
		canvasPage.Append (resetButton);

		Gtk.Notebook notebook = Gtk.Notebook.New ();
		notebook.AppendPage (canvasPage, Gtk.Label.New (Translations.GetString ("Canvas")));
		contentArea.Append (notebook);

		Title = Translations.GetString ("Settings");
		Modal = true;
		IconName = Resources.StandardIcons.KeyboardShortcuts;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		canvas_width_spinner = widthSpinner;
		canvas_height_spinner = heightSpinner;
		canvas_surround_color_button = canvasSurroundColorButton;
	}

	private async void ChooseCanvasSurroundColor (Gtk.Button sender, EventArgs e)
	{
		using ColorPickerDialog dialog = ColorPickerDialog.New (
			this,
			PintaCore.Palette,
			new SingleColor (canvas_surround_color_button.DisplayColor),
			primarySelected: true,
			livePalette: false,
			Translations.GetString ("Choose Color"));

		try {
			if (await dialog.RunAsync () == Gtk.ResponseType.Ok) {
				Cairo.Color color = ((SingleColor) dialog.Colors).Color;
				canvas_surround_color_button.DisplayColor = new Cairo.Color (color.R, color.G, color.B, 1);
			}
		} finally {
			dialog.Destroy ();
		}
	}

	private static Gtk.Label CreateLabel (string text, Gtk.Align horizontalAlign)
	{
		Gtk.Label result = Gtk.Label.New (text);
		result.Halign = horizontalAlign;
		return result;
	}
}
