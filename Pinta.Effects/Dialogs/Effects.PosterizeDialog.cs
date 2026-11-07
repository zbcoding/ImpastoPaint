//
// PosterizeDialog.cs
//
// Author:
//      Krzysztof Marecki <marecki.krzysztof@gmail.com>
//
// Copyright (c) 2010 Krzysztof Marecki
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
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.Effects;

[GObject.Subclass<Gtk.Dialog>]
public sealed partial class PosterizeDialog
{
	// NRT - Set in New, which knows the values to start from
	private HScaleSpinButtonWidget red_spinbox = null!;
	private HScaleSpinButtonWidget green_spinbox = null!;
	private HScaleSpinButtonWidget blue_spinbox = null!;
	private Gtk.CheckButton link_button = null!;

	public int Red => red_spinbox.ValueAsInt;
	public int Green => green_spinbox.ValueAsInt;
	public int Blue => blue_spinbox.ValueAsInt;

	public PosterizeData EffectData { get; private set; } = new ();

	partial void Initialize ()
	{
		DefaultWidth = 400;
		DefaultHeight = 300;

		Title = Translations.GetString ("Posterize");
		// Issue #1334 (reported by @prokoudine): do not dim the canvas while
		// adjusting a live effect preview.
		Modal = false;

		Resizable = false;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		Gtk.Box content_area = this.GetContentAreaBox ();
		content_area.WidthRequest = 400;
		content_area.SetAllMargins (6);
		content_area.Spacing = 6;
	}

	/// <summary>
	/// The controls start from <paramref name="effectData"/>'s values, and write to it only when the user changes them.
	/// </summary>
	public static PosterizeDialog New (IChromeService chrome, PosterizeData effectData)
	{
		PosterizeDialog dialog = NewWithProperties ([]);
		dialog.TransientFor = chrome.MainWindow;
		dialog.EffectData = effectData;

		dialog.red_spinbox = dialog.CreateChannelSpinBox (Translations.GetString ("Red"), effectData.Red);
		dialog.green_spinbox = dialog.CreateChannelSpinBox (Translations.GetString ("Green"), effectData.Green);
		dialog.blue_spinbox = dialog.CreateChannelSpinBox (Translations.GetString ("Blue"), effectData.Blue);
		dialog.link_button = CreateLinkButton (effectData.Red == effectData.Green && effectData.Green == effectData.Blue);

		dialog.GetContentAreaBox ().AppendMultiple ([
			dialog.red_spinbox,
			dialog.green_spinbox,
			dialog.blue_spinbox,
			dialog.link_button]);

		return dialog;
	}

	private static Gtk.CheckButton CreateLinkButton (bool linked)
	{
		var result = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Linked"));
		result.Active = linked;
		return result;
	}

	private HScaleSpinButtonWidget CreateChannelSpinBox (string label, int initialValue)
	{
		HScaleSpinButtonWidget spinner = HScaleSpinButtonWidget.New (initialValue);
		spinner.Label = label;
		spinner.MaximumValue = 64;
		spinner.MinimumValue = 2;
		// Set now as well as on realize, so a channel realized later never reads as its pre-realize value.
		spinner.Value = initialValue;
		spinner.ValueChanged += HandleValueChanged;
		return spinner;
	}

	private void HandleValueChanged (object? sender, EventArgs e)
	{
		if (sender is not HScaleSpinButtonWidget widget)
			return;

		if (link_button.Active)
			green_spinbox.Value = blue_spinbox.Value = red_spinbox.Value = widget.Value;

		UpdateEffectData ();
	}

	private void UpdateEffectData ()
	{
		EffectData.Red = red_spinbox.ValueAsInt;
		EffectData.Green = green_spinbox.ValueAsInt;
		EffectData.Blue = blue_spinbox.ValueAsInt;

		// Only fire event once, even if all properties have changed.
		EffectData.FirePropertyChanged ("_all_");
	}
}
