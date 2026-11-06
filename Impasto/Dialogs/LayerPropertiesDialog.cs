//
// LayerPropertiesDialog.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta;

[GObject.Subclass<Gtk.Dialog>]
public sealed partial class LayerPropertiesDialog
{
	private LayerProperties initial_properties = new (string.Empty, false, 0.0, BlendMode.Normal);

	private double current_layer_opacity;
	private bool current_layer_hidden;
	private string current_layer_name = string.Empty;
	private BlendMode current_layer_blend_mode;

	private Gtk.Entry layer_name_entry;
	private Gtk.CheckButton visibility_checkbox;
	private Gtk.SpinButton opacity_spinner;
	private Gtk.Scale opacity_slider;
	private Gtk.ComboBoxText blend_combo_box;

	private WorkspaceManager workspace = null!; // NRT - set by factory method
	private Document document = null!; // NRT - set by factory method
	private UserLayer layer = null!; // NRT - set by factory method

	[MemberNotNull (nameof (layer_name_entry))]
	[MemberNotNull (nameof (visibility_checkbox))]
	[MemberNotNull (nameof (opacity_slider))]
	[MemberNotNull (nameof (opacity_spinner))]
	[MemberNotNull (nameof (blend_combo_box))]
	partial void Initialize ()
	{
		PropertiesDialogGridBuilder.Widgets widgets = PropertiesDialogGridBuilder.Build ();

		widgets.NameEntry.OnChanged += OnLayerNameChanged;
		widgets.VisibilityCheckbox.OnToggled += OnVisibilityToggled;
		widgets.BlendComboBox.OnChanged += OnBlendModeChanged;
		widgets.OpacitySpinner.OnValueChanged += OnOpacitySpinnerChanged;
		widgets.OpacitySlider.OnValueChanged += OnOpacitySliderChanged;

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("Layer Properties");

		// Non-modal (like the effect dialogs) so the dialog floats over the canvas without dimming
		// the whole window; TransientFor keeps it stacked with its parent.
		Modal = false;
		DefaultWidth = 349;
		DefaultHeight = 224;
		IconName = Resources.Icons.LayerProperties;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		// --- Initialization

		var contentArea = this.GetContentAreaBox ();
		contentArea.Spacing = PropertiesDialogGridBuilder.Spacing;
		contentArea.SetAllMargins (10);
		contentArea.Append (widgets.Grid);

		// --- References to keep

		layer_name_entry = widgets.NameEntry;
		visibility_checkbox = widgets.VisibilityCheckbox;
		blend_combo_box = widgets.BlendComboBox;
		opacity_spinner = widgets.OpacitySpinner;
		opacity_slider = widgets.OpacitySlider;
	}

	/// <summary>
	/// Edits <paramref name="document"/>'s current layer. The document and layer are captured here,
	/// so switching documents or layers while the non-modal dialog is open does not retarget it.
	/// </summary>
	public static LayerPropertiesDialog New (IChromeService chrome, WorkspaceManager workspace, Document document)
	{
		LayerPropertiesDialog dialog = NewWithProperties ([]);
		dialog.Configure (chrome, workspace, document);
		return dialog;
	}

	private void Configure (IChromeService chrome, WorkspaceManager workspace, Document document)
	{
		this.workspace = workspace;
		this.document = document;
		layer = document.Layers.CurrentUserLayer;
		TransientFor = chrome.MainWindow;

		string currentLayerName = layer.Name;
		bool currentLayerHidden = layer.Hidden;
		double currentLayerOpacity = layer.Opacity;
		BlendMode currentLayerBlendMode = layer.BlendMode;

		LayerProperties initialProperties = new (
			currentLayerName,
			currentLayerHidden,
			currentLayerOpacity,
			currentLayerBlendMode);

		layer_name_entry.SetText (initialProperties.Name);
		visibility_checkbox.Active = !initialProperties.Hidden;
		opacity_spinner.Value = Math.Round (initialProperties.Opacity * 100);
		opacity_slider.SetValue (Math.Round (initialProperties.Opacity * 100));

		var allBlendmodes = UserBlendOps.GetAllBlendModeNames ().ToImmutableArray ();
		var index = allBlendmodes.IndexOf (UserBlendOps.GetBlendModeName (currentLayerBlendMode));
		blend_combo_box.Active = index;

		current_layer_name = currentLayerName;
		current_layer_hidden = currentLayerHidden;
		current_layer_opacity = currentLayerOpacity;
		current_layer_blend_mode = currentLayerBlendMode;

		initial_properties = initialProperties;
	}

	public Document Document => document;

	public UserLayer Layer => layer;

	/// <summary>
	/// The captured layer still belongs to an open document (it was not deleted, nor its document closed).
	/// </summary>
	public bool IsLayerAttached
		=> workspace.OpenDocuments.Contains (document) && document.Layers.IndexOf (layer) >= 0;

	public bool AreLayerPropertiesUpdated =>
		initial_properties.Opacity != current_layer_opacity
		|| initial_properties.Hidden != current_layer_hidden
		|| initial_properties.Name != current_layer_name
		|| initial_properties.BlendMode != current_layer_blend_mode;

	public LayerProperties InitialLayerProperties
		=> initial_properties;

	public LayerProperties UpdatedLayerProperties
		=> new (
			current_layer_name,
			current_layer_hidden,
			current_layer_opacity,
			current_layer_blend_mode);

	private void OnLayerNameChanged (object? sender, EventArgs e)
	{
		current_layer_name = layer_name_entry.GetText ();
		ApplyCurrentProperties ();
	}

	private void OnVisibilityToggled (object? sender, EventArgs e)
	{
		current_layer_hidden = !visibility_checkbox.Active;
		ApplyCurrentProperties ();
	}

	private void OnOpacitySliderChanged (object? sender, EventArgs e)
	{
		opacity_spinner.Value = opacity_slider.GetValue ();
		UpdateOpacity ();
	}

	private void OnOpacitySpinnerChanged (object? sender, EventArgs e)
	{
		opacity_slider.SetValue (opacity_spinner.Value);
		UpdateOpacity ();
	}

	private void UpdateOpacity ()
	{
		//TODO check redraws are being throttled.
		current_layer_opacity = opacity_spinner.Value / 100d;
		ApplyCurrentProperties ();
	}

	private void OnBlendModeChanged (object? sender, EventArgs e)
	{
		current_layer_blend_mode = UserBlendOps.GetBlendModeByName (blend_combo_box.GetActiveText ()!);
		ApplyCurrentProperties ();
	}

	/// <summary>
	/// Live-previews the edited properties on the captured layer while it is still attached.
	/// </summary>
	private void ApplyCurrentProperties ()
	{
		if (!IsLayerAttached)
			return;

		SetLayerProperties (UpdatedLayerProperties);
		document.Workspace.Invalidate ();
	}

	/// <summary>
	/// Sets <paramref name="properties"/> on the captured layer, and on the selection layer
	/// when the captured layer is attached and current (the selection layer is drawn with its properties).
	/// </summary>
	public void SetLayerProperties (LayerProperties properties)
	{
		properties.SetProperties (layer);

		if (IsLayerAttached && document.Layers.CurrentUserLayer == layer)
			properties.SetProperties (document.Layers.SelectionLayer);
	}
}

