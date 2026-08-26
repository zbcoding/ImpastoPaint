// PropertiesDialogGridBuilder.cs
//
// The name / visible / blend-mode / opacity control block shared by the layer and object
// properties dialogs. Builds the widgets and their grid layout only; callers wire their own
// event handlers, dialog chrome, and initial values.

using Pinta.Core;

namespace Pinta.Gui.Widgets;

public static class PropertiesDialogGridBuilder
{
	public const int Spacing = 6;

	public sealed record Widgets (
		Gtk.Grid Grid,
		Gtk.Entry NameEntry,
		Gtk.CheckButton VisibilityCheckbox,
		Gtk.ComboBoxText BlendComboBox,
		Gtk.SpinButton OpacitySpinner,
		Gtk.Scale OpacitySlider);

	public static Widgets Build ()
	{
		Gtk.Label nameLabel = Gtk.Label.New (Translations.GetString ("Name:"));
		nameLabel.Halign = Gtk.Align.End;

		Gtk.Entry nameEntry = Gtk.Entry.New ();
		nameEntry.Hexpand = true;
		nameEntry.Halign = Gtk.Align.Fill;
		nameEntry.SetActivatesDefault (true);

		Gtk.CheckButton visibilityCheckbox = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Visible"));

		Gtk.Label blendLabel = Gtk.Label.New (Translations.GetString ("Blend Mode") + ":");
		blendLabel.Halign = Gtk.Align.End;

		Gtk.ComboBoxText blendComboBox = Gtk.ComboBoxText.New ();
		foreach (string name in UserBlendOps.GetAllBlendModeNames ())
			blendComboBox.AppendText (name);
		blendComboBox.Hexpand = true;
		blendComboBox.Halign = Gtk.Align.Fill;

		Gtk.Label opacityLabel = Gtk.Label.New (Translations.GetString ("Opacity:"));
		opacityLabel.Halign = Gtk.Align.End;

		Gtk.SpinButton opacitySpinner = Gtk.SpinButton.NewWithRange (0, 100, 1);
		opacitySpinner.Adjustment!.PageIncrement = 10;
		opacitySpinner.ClimbRate = 1;
		opacitySpinner.SetActivatesDefaultImmediate (true);

		Gtk.Scale opacitySlider = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 100, 1);
		opacitySlider.Digits = 0;
		opacitySlider.Adjustment!.PageIncrement = 10;
		opacitySlider.Hexpand = true;
		opacitySlider.Halign = Gtk.Align.Fill;

		Gtk.Box opacityBox = Gtk.Box.New (Gtk.Orientation.Horizontal, Spacing);
		opacityBox.Append (opacitySpinner);
		opacityBox.Append (opacitySlider);

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = Spacing;
		grid.ColumnSpacing = Spacing;
		grid.ColumnHomogeneous = false;
		grid.Attach (nameLabel, 0, 0, 1, 1);
		grid.Attach (nameEntry, 1, 0, 1, 1);
		grid.Attach (visibilityCheckbox, 1, 1, 1, 1);
		grid.Attach (blendLabel, 0, 2, 1, 1);
		grid.Attach (blendComboBox, 1, 2, 1, 1);
		grid.Attach (opacityLabel, 0, 3, 1, 1);
		grid.Attach (opacityBox, 1, 3, 1, 1);

		return new (grid, nameEntry, visibilityCheckbox, blendComboBox, opacitySpinner, opacitySlider);
	}
}
