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
	private Gtk.CheckButton paste_external_images_to_new_layer_check_button;
	private Gtk.ToggleButton popover_hint_mode_all_button;
	private Gtk.ToggleButton popover_hint_mode_essential_button;
	private Gtk.ToggleButton popover_hint_mode_none_button;
	private bool canvas_surround_color_is_default;
	private Cairo.Color default_canvas_surround_color;

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
	public Cairo.Color? CanvasSurroundColor => canvas_surround_color_is_default ? null : canvas_surround_color_button.DisplayColor;
	public bool PasteExternalImagesToNewLayer => paste_external_images_to_new_layer_check_button.Active;
	public PopoverHintMode PopoverHintMode
		=> popover_hint_mode_all_button.Active ? PopoverHintMode.All
			: popover_hint_mode_essential_button.Active ? PopoverHintMode.Essential
			: PopoverHintMode.None;

	internal static PreferencesDialog New (ChromeManager chrome, int defaultCanvasWidth, int defaultCanvasHeight, Cairo.Color canvasSurroundColor, bool canvasSurroundColorIsDefault, Cairo.Color defaultCanvasSurroundColor, bool pasteExternalImagesToNewLayer, PopoverHintMode popoverHintMode)
	{
		PreferencesDialog dialog = NewWithProperties ([]);
		dialog.canvas_width_spinner.Value = defaultCanvasWidth;
		dialog.canvas_height_spinner.Value = defaultCanvasHeight;
		dialog.canvas_surround_color_button.DisplayColor = canvasSurroundColor;
		dialog.canvas_surround_color_is_default = canvasSurroundColorIsDefault;
		dialog.default_canvas_surround_color = defaultCanvasSurroundColor;
		dialog.paste_external_images_to_new_layer_check_button.Active = pasteExternalImagesToNewLayer;
		dialog.SetPopoverHintMode (popoverHintMode);
		dialog.TransientFor = chrome.MainWindow;
		return dialog;
	}

	[MemberNotNull (nameof (canvas_width_spinner))]
	[MemberNotNull (nameof (canvas_height_spinner))]
	[MemberNotNull (nameof (canvas_surround_color_button))]
	[MemberNotNull (nameof (paste_external_images_to_new_layer_check_button))]
	[MemberNotNull (nameof (popover_hint_mode_all_button))]
	[MemberNotNull (nameof (popover_hint_mode_essential_button))]
	[MemberNotNull (nameof (popover_hint_mode_none_button))]
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

		grid.Attach (CreateLabel (Translations.GetString ("Default canvas size (on application open):"), Gtk.Align.Start), 0, 0, 4, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Width:"), Gtk.Align.End), 0, 1, 1, 1);
		grid.Attach (widthSpinner, 1, 1, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 1, 1, 1);
		grid.Attach (CreateResetButton (ResetCanvasWidth), 3, 1, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Height:"), Gtk.Align.End), 0, 2, 1, 1);
		grid.Attach (heightSpinner, 1, 2, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 2, 1, 1);
		grid.Attach (CreateResetButton (ResetCanvasHeight), 3, 2, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Canvas surround color:"), Gtk.Align.End), 0, 3, 1, 1);
		grid.Attach (canvasSurroundColorButton, 1, 3, 2, 1);
		grid.Attach (CreateResetButton (ResetCanvasSurroundColor), 3, 3, 1, 1);

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);

		Gtk.Box canvasPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		canvasPage.SetAllMargins (12);
		canvasPage.Append (grid);

		Gtk.CheckButton pasteExternalImagesCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Paste external images onto a new layer by default"));
		Gtk.Box clipboardPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		clipboardPage.SetAllMargins (12);
		Gtk.Box clipboardRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		pasteExternalImagesCheckButton.Hexpand = true;
		clipboardRow.Append (pasteExternalImagesCheckButton);
		clipboardRow.Append (CreateResetButton (ResetClipboard));
		clipboardPage.Append (clipboardRow);

		Gtk.ToggleButton popoverHintModeAllButton = CreateHintModeButton (
			"All",
			"Show all UI popover hints.");
		Gtk.ToggleButton popoverHintModeEssentialButton = CreateHintModeButton (
			"Essential",
			"Show only essential tool hints.");
		Gtk.ToggleButton popoverHintModeNoneButton = CreateHintModeButton (
			"None",
			"Hide all UI popover hints.");
		popoverHintModeEssentialButton.SetGroup (popoverHintModeAllButton);
		popoverHintModeNoneButton.SetGroup (popoverHintModeAllButton);

		Gtk.Box popoverHintPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		popoverHintPage.SetAllMargins (12);
		popoverHintPage.Append (Gtk.Label.New (Translations.GetString ("Popover hints:")));
		Gtk.Box popoverHintRow = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		popoverHintModeAllButton.Hexpand = true;
		popoverHintModeEssentialButton.Hexpand = true;
		popoverHintModeNoneButton.Hexpand = true;
		popoverHintRow.Append (popoverHintModeAllButton);
		popoverHintRow.Append (popoverHintModeEssentialButton);
		popoverHintRow.Append (popoverHintModeNoneButton);
		popoverHintRow.Append (CreateResetButton (ResetPopoverHintMode));
		popoverHintPage.Append (popoverHintRow);

		Gtk.Notebook notebook = Gtk.Notebook.New ();
		notebook.AppendPage (canvasPage, Gtk.Label.New (Translations.GetString ("Canvas")));
		notebook.AppendPage (clipboardPage, Gtk.Label.New (Translations.GetString ("Clipboard")));
		notebook.AppendPage (popoverHintPage, Gtk.Label.New (Translations.GetString ("UI")));
		contentArea.Append (notebook);

		Title = Translations.GetString ("Settings");
		Modal = true;
		IconName = Resources.StandardIcons.KeyboardShortcuts;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		canvas_width_spinner = widthSpinner;
		canvas_height_spinner = heightSpinner;
		canvas_surround_color_button = canvasSurroundColorButton;
		paste_external_images_to_new_layer_check_button = pasteExternalImagesCheckButton;
		popover_hint_mode_all_button = popoverHintModeAllButton;
		popover_hint_mode_essential_button = popoverHintModeEssentialButton;
		popover_hint_mode_none_button = popoverHintModeNoneButton;
	}

	private void ResetCanvasWidth (Gtk.Button sender, EventArgs e)
		=> canvas_width_spinner.Value = 800;

	private void ResetCanvasHeight (Gtk.Button sender, EventArgs e)
		=> canvas_height_spinner.Value = 600;

	private void ResetCanvasSurroundColor (Gtk.Button sender, EventArgs e)
	{
		canvas_surround_color_button.DisplayColor = default_canvas_surround_color;
		canvas_surround_color_is_default = true;
	}

	private void ResetClipboard (Gtk.Button sender, EventArgs e)
		=> paste_external_images_to_new_layer_check_button.Active = false;

	private void ResetPopoverHintMode (Gtk.Button sender, EventArgs e)
		=> SetPopoverHintMode (PopoverHintMode.All);

	private void SetPopoverHintMode (PopoverHintMode mode)
	{
		popover_hint_mode_all_button.Active = mode == PopoverHintMode.All;
		popover_hint_mode_essential_button.Active = mode == PopoverHintMode.Essential;
		popover_hint_mode_none_button.Active = mode == PopoverHintMode.None;
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
				canvas_surround_color_is_default = false;
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

	private static Gtk.ToggleButton CreateHintModeButton (string label, string tooltip)
	{
		Gtk.ToggleButton result = Gtk.ToggleButton.NewWithLabel (Translations.GetString (label));
		result.TooltipText = Translations.GetString (tooltip);
		return result;
	}

	private static Gtk.Button CreateResetButton (GObject.SignalHandler<Gtk.Button> handler)
	{
		Gtk.Button result = Gtk.Button.New ();
		result.IconName = "edit-undo-symbolic";
		result.TooltipText = Translations.GetString ("Reset to default");
		result.OnClicked += handler;
		return result;
	}
}
