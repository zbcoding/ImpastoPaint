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
	private Gtk.DropDown popover_hint_mode_dropdown;
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
	public PopoverHintMode PopoverHintMode => (PopoverHintMode) popover_hint_mode_dropdown.Selected;

	internal static PreferencesDialog New (ChromeManager chrome, int defaultCanvasWidth, int defaultCanvasHeight, Cairo.Color canvasSurroundColor, bool canvasSurroundColorIsDefault, Cairo.Color defaultCanvasSurroundColor, bool pasteExternalImagesToNewLayer, PopoverHintMode popoverHintMode)
	{
		PreferencesDialog dialog = NewWithProperties ([]);
		dialog.canvas_width_spinner.Value = defaultCanvasWidth;
		dialog.canvas_height_spinner.Value = defaultCanvasHeight;
		dialog.canvas_surround_color_button.DisplayColor = canvasSurroundColor;
		dialog.canvas_surround_color_is_default = canvasSurroundColorIsDefault;
		dialog.default_canvas_surround_color = defaultCanvasSurroundColor;
		dialog.paste_external_images_to_new_layer_check_button.Active = pasteExternalImagesToNewLayer;
		dialog.popover_hint_mode_dropdown.Selected = (uint) popoverHintMode;
		dialog.TransientFor = chrome.MainWindow;
		return dialog;
	}

	[MemberNotNull (nameof (canvas_width_spinner))]
	[MemberNotNull (nameof (canvas_height_spinner))]
	[MemberNotNull (nameof (canvas_surround_color_button))]
	[MemberNotNull (nameof (paste_external_images_to_new_layer_check_button))]
	[MemberNotNull (nameof (popover_hint_mode_dropdown))]
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
		canvasPage.SetAllMargins (12);
		canvasPage.Append (grid);

		Gtk.Button resetButton = Gtk.Button.NewWithLabel (Translations.GetString ("Reset to Defaults"));
		resetButton.Halign = Gtk.Align.Center;
		resetButton.OnClicked += ConfirmCanvasReset;
		canvasPage.Append (resetButton);

		Gtk.CheckButton pasteExternalImagesCheckButton = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Paste external images onto a new layer by default"));
		Gtk.Box clipboardPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		clipboardPage.SetAllMargins (12);
		clipboardPage.Append (pasteExternalImagesCheckButton);

		Gtk.Button resetClipboardButton = Gtk.Button.NewWithLabel (Translations.GetString ("Reset to Defaults"));
		resetClipboardButton.Halign = Gtk.Align.Center;
		resetClipboardButton.OnClicked += ConfirmClipboardReset;
		clipboardPage.Append (resetClipboardButton);

		Gtk.StringList popoverHintModeModel = Gtk.StringList.New ([
			Translations.GetString ("All"),
			Translations.GetString ("Essential"),
			Translations.GetString ("None")
		]);
		string[] popoverHintModeTooltips = [
			Translations.GetString ("Show all UI popover hints."),
			Translations.GetString ("Show only essential tool hints."),
			Translations.GetString ("Hide all UI popover hints.")
		];
		Gtk.DropDown popoverHintModeDropdown = Gtk.DropDown.New (popoverHintModeModel, expression: null);
		Gtk.SignalListItemFactory popoverHintModeFactory = Gtk.SignalListItemFactory.New ();
		popoverHintModeFactory.OnSetup += (_, args) => ((Gtk.ListItem) args.Object).SetChild (Gtk.Label.New (null));
		popoverHintModeFactory.OnBind += (_, args) => {
			Gtk.ListItem item = (Gtk.ListItem) args.Object;
			Gtk.Label label = (Gtk.Label) item.GetChild ()!;
			int position = (int) item.Position;
			label.SetText (popoverHintModeModel.GetString (item.Position) ?? string.Empty);
			label.TooltipText = popoverHintModeTooltips[position];
		};
		popoverHintModeDropdown.SetFactory (popoverHintModeFactory);
		popoverHintModeDropdown.SetListFactory (popoverHintModeFactory);
		void UpdatePopoverHintModeTooltip ()
			=> popoverHintModeDropdown.TooltipText = popoverHintModeTooltips[(int) popoverHintModeDropdown.Selected];
		Gtk.DropDown.SelectedPropertyDefinition.Notify (popoverHintModeDropdown, (_, _) => UpdatePopoverHintModeTooltip ());
		UpdatePopoverHintModeTooltip ();

		Gtk.Label popoverHintDescription = Gtk.Label.New (Translations.GetString (
			"All: show all UI hints. Essential: show only specific tool hints. None: turn off UI popover hints."));
		popoverHintDescription.Wrap = true;
		popoverHintDescription.Xalign = 0;

		Gtk.Box popoverHintPage = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		popoverHintPage.SetAllMargins (12);
		popoverHintPage.Append (Gtk.Label.New (Translations.GetString ("Popover hints:")));
		popoverHintPage.Append (popoverHintModeDropdown);
		popoverHintPage.Append (popoverHintDescription);

		Gtk.Button resetPopoverHintButton = Gtk.Button.NewWithLabel (Translations.GetString ("Reset to Defaults"));
		resetPopoverHintButton.Halign = Gtk.Align.Center;
		resetPopoverHintButton.OnClicked += ConfirmPopoverHintReset;
		popoverHintPage.Append (resetPopoverHintButton);

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
		popover_hint_mode_dropdown = popoverHintModeDropdown;
	}

	private async void ConfirmCanvasReset (Gtk.Button sender, EventArgs e)
	{
		Gtk.AlertDialog confirmation = Gtk.AlertDialog.NewWithProperties ([]);
		confirmation.Message = Translations.GetString ("Reset Canvas Settings?");
		confirmation.Detail = Translations.GetString ("This resets the canvas size and surround color to their defaults.");
		confirmation.Buttons = [Translations.GetString ("Reset"), Translations.GetString ("Cancel")];
		confirmation.DefaultButton = 1;
		confirmation.CancelButton = 1;

		if (await confirmation.ChooseAsync (this) != 0)
			return;

		canvas_width_spinner.Value = 800;
		canvas_height_spinner.Value = 600;
		canvas_surround_color_button.DisplayColor = default_canvas_surround_color;
		canvas_surround_color_is_default = true;
	}

	private async void ConfirmClipboardReset (Gtk.Button sender, EventArgs e)
	{
		Gtk.AlertDialog confirmation = Gtk.AlertDialog.NewWithProperties ([]);
		confirmation.Message = Translations.GetString ("Reset Clipboard Settings?");
		confirmation.Detail = Translations.GetString ("This resets the clipboard settings to their defaults.");
		confirmation.Buttons = [Translations.GetString ("Reset"), Translations.GetString ("Cancel")];
		confirmation.DefaultButton = 1;
		confirmation.CancelButton = 1;

		if (await confirmation.ChooseAsync (this) == 0)
			paste_external_images_to_new_layer_check_button.Active = false;
	}

	private async void ConfirmPopoverHintReset (Gtk.Button sender, EventArgs e)
	{
		Gtk.AlertDialog confirmation = Gtk.AlertDialog.NewWithProperties ([]);
		confirmation.Message = Translations.GetString ("Reset UI Settings?");
		confirmation.Detail = Translations.GetString ("This resets popover hints to show all UI hints.");
		confirmation.Buttons = [Translations.GetString ("Reset"), Translations.GetString ("Cancel")];
		confirmation.DefaultButton = 1;
		confirmation.CancelButton = 1;

		if (await confirmation.ChooseAsync (this) == 0)
			popover_hint_mode_dropdown.Selected = (uint) PopoverHintMode.All;
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
}
