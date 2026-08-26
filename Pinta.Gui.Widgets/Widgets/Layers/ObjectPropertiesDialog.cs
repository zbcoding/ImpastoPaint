// ObjectPropertiesDialog.cs
//
// The dedicated settings window for one object sub-node (a shape or a text object), the sub-node
// counterpart of the Layer Properties dialog. The layers dock's right-click popover stays the quick
// path (blend + opacity); everything else about an object — its name above all — lives here, so the
// popover doesn't have to carry a text entry that steals keyboard focus from the canvas.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.Dialog>]
public sealed partial class ObjectPropertiesDialog
{
	private LayersListViewItem row = null!; // NRT - set by the factory method.

	// The values to restore on Cancel / to record as the "before" side of the history items. Captured
	// once, when the dialog is configured.
	private string initial_name = string.Empty;
	private bool initial_hidden;
	private double initial_opacity = 1.0;
	private BlendMode initial_blend = BlendMode.Normal;

	private Gtk.Entry name_entry;
	private Gtk.CheckButton visibility_checkbox;
	private Gtk.SpinButton opacity_spinner;
	private Gtk.Scale opacity_slider;
	private Gtk.ComboBoxText blend_combo_box;

	[MemberNotNull (nameof (name_entry))]
	[MemberNotNull (nameof (visibility_checkbox))]
	[MemberNotNull (nameof (opacity_slider))]
	[MemberNotNull (nameof (opacity_spinner))]
	[MemberNotNull (nameof (blend_combo_box))]
	partial void Initialize ()
	{
		PropertiesDialogGridBuilder.Widgets widgets = PropertiesDialogGridBuilder.Build ();

		widgets.NameEntry.OnChanged += OnNameChanged;
		widgets.VisibilityCheckbox.OnToggled += OnVisibilityToggled;
		widgets.BlendComboBox.OnChanged += OnBlendModeChanged;
		widgets.OpacitySpinner.OnValueChanged += OnOpacitySpinnerChanged;
		widgets.OpacitySlider.OnValueChanged += OnOpacitySliderChanged;

		// --- Initialization (Gtk.Window)

		// Non-modal, matching the layer properties dialog: it floats over the canvas (which keeps
		// showing the live result of every change) instead of dimming the window.
		Modal = false;
		DefaultWidth = 349;
		DefaultHeight = 224;
		IconName = Resources.Icons.LayerProperties;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
		OnResponse += OnDialogResponse;

		// --- Initialization

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.Spacing = PropertiesDialogGridBuilder.Spacing;
		contentArea.SetAllMargins (10);
		contentArea.Append (widgets.Grid);

		// --- References to keep

		name_entry = widgets.NameEntry;
		visibility_checkbox = widgets.VisibilityCheckbox;
		blend_combo_box = widgets.BlendComboBox;
		opacity_spinner = widgets.OpacitySpinner;
		opacity_slider = widgets.OpacitySlider;
	}

	/// <summary>
	/// Opens the settings window for the object <paramref name="row"/> stands for. Titled per kind
	/// ("Text Object Properties" / "Shape Object Properties") so the two kinds read as their own
	/// windows even though they share the settings every object has.
	/// </summary>
	public static void Show (Gtk.Window parent, LayersListViewItem row)
	{
		if (!row.IsObjectRow)
			return;

		ObjectPropertiesDialog dialog = NewWithProperties ([]);
		dialog.Configure (parent, row);
		dialog.Present ();
	}

	private void Configure (Gtk.Window parent, LayersListViewItem row)
	{
		this.row = row;
		TransientFor = parent;

		Title = row.TextObject is not null
			? Translations.GetString ("Text Object Properties")
			: Translations.GetString ("Shape Object Properties");

		initial_name = row.ObjectName;
		initial_hidden = row.ObjectHidden;
		initial_opacity = row.ObjectOpacity;
		initial_blend = row.ObjectBlendMode;

		// Set the widgets up before their handlers can fire against a half-configured dialog.
		name_entry.SetText (initial_name);
		name_entry.SetPlaceholderText (row.Label);
		visibility_checkbox.Active = !initial_hidden;
		opacity_spinner.Value = Math.Round (initial_opacity * 100);
		opacity_slider.SetValue (Math.Round (initial_opacity * 100));

		ImmutableArray<string> allBlendModes = [.. UserBlendOps.GetAllBlendModeNames ()];
		blend_combo_box.Active = allBlendModes.IndexOf (UserBlendOps.GetBlendModeName (initial_blend));
	}

	// Every control applies live (no history), so the canvas shows the real result while the window is
	// open; OK collapses the whole session into one undoable step per changed property, Cancel puts the
	// starting values back. Same bargain the popover makes on close.
	private void OnDialogResponse (Gtk.Dialog _, Gtk.Dialog.ResponseSignalArgs args)
	{
		if (args.ResponseId == (int) Gtk.ResponseType.Ok) {
			if (row.ObjectOpacity != initial_opacity)
				row.PushObjectOpacityHistory (initial_opacity);

			if (row.ObjectBlendMode != initial_blend)
				row.PushObjectBlendModeHistory (initial_blend);

			if (row.ObjectHidden != initial_hidden)
				row.PushObjectHiddenHistory (initial_hidden);

			// RenameObject is a no-op when the name is unchanged, and pushes its own history item.
			row.RenameObject (name_entry.GetText ());
		} else {
			row.SetObjectOpacity (initial_opacity);
			row.SetObjectBlendMode (initial_blend);
			row.SetObjectHiddenLive (initial_hidden);
		}

		this.Destroy ();
	}

	private void OnNameChanged (object? sender, EventArgs e)
	{
		// Renaming is committed on OK (one history item for the whole edit rather than one per
		// keystroke), so nothing to apply here.
	}

	private void OnVisibilityToggled (object? sender, EventArgs e)
		=> row.SetObjectHiddenLive (!visibility_checkbox.Active);

	private void OnOpacitySliderChanged (object? sender, EventArgs e)
	{
		opacity_spinner.Value = opacity_slider.GetValue ();
		row.SetObjectOpacity (opacity_spinner.Value / 100d);
	}

	private void OnOpacitySpinnerChanged (object? sender, EventArgs e)
	{
		opacity_slider.SetValue (opacity_spinner.Value);
		row.SetObjectOpacity (opacity_spinner.Value / 100d);
	}

	private void OnBlendModeChanged (object? sender, EventArgs e)
	{
		if (blend_combo_box.GetActiveText () is { } name)
			row.SetObjectBlendMode (UserBlendOps.GetBlendModeByName (name));
	}
}
