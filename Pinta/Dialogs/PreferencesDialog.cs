using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta;

// Impasto: application-wide user preferences. First setting is the default size of
// the blank canvas created on startup. Grows as more preferences are added.
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class PreferencesDialog
{
	private Gtk.SpinButton canvas_width_spinner;
	private Gtk.SpinButton canvas_height_spinner;

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

	internal static PreferencesDialog New (ChromeManager chrome, int defaultCanvasWidth, int defaultCanvasHeight)
	{
		PreferencesDialog dialog = NewWithProperties ([]);
		dialog.canvas_width_spinner.Value = defaultCanvasWidth;
		dialog.canvas_height_spinner.Value = defaultCanvasHeight;
		dialog.TransientFor = chrome.MainWindow;
		return dialog;
	}

	[MemberNotNull (nameof (canvas_width_spinner))]
	[MemberNotNull (nameof (canvas_height_spinner))]
	partial void Initialize ()
	{
		Gtk.SpinButton widthSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		widthSpinner.SetActivatesDefaultImmediate (true);

		Gtk.SpinButton heightSpinner = Gtk.SpinButton.NewWithRange (1, MAX_CANVAS_DIMENSION, 1);
		heightSpinner.SetActivatesDefaultImmediate (true);

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = SPACING;
		grid.ColumnSpacing = SPACING;
		grid.ColumnHomogeneous = false;

		grid.Attach (CreateLabel (Translations.GetString ("Default canvas size (new window):"), Gtk.Align.Start), 0, 0, 3, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Width:"), Gtk.Align.End), 0, 1, 1, 1);
		grid.Attach (widthSpinner, 1, 1, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 1, 1, 1);

		grid.Attach (CreateLabel (Translations.GetString ("Height:"), Gtk.Align.End), 0, 2, 1, 1);
		grid.Attach (heightSpinner, 1, 2, 1, 1);
		grid.Attach (Gtk.Label.New (Translations.GetString ("pixels")), 2, 2, 1, 1);

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);
		contentArea.Append (grid);

		Title = Translations.GetString ("Settings");
		Modal = true;
		IconName = Resources.StandardIcons.KeyboardShortcuts;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		canvas_width_spinner = widthSpinner;
		canvas_height_spinner = heightSpinner;
	}

	private static Gtk.Label CreateLabel (string text, Gtk.Align horizontalAlign)
	{
		Gtk.Label result = Gtk.Label.New (text);
		result.Halign = horizontalAlign;
		return result;
	}
}
