//
// ResizePaletteAction.cs
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
using System.Linq;
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class ResizePaletteAction : IActionHandler
{
	private readonly EditActions edit;
	private readonly ChromeManager chrome;
	private readonly PaletteManager palette;
	internal ResizePaletteAction (
		EditActions edit,
		ChromeManager chrome,
		PaletteManager palette)
	{
		this.edit = edit;
		this.chrome = chrome;
		this.palette = palette;
	}

	void IActionHandler.Initialize ()
	{
		edit.ResizePalette.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		edit.ResizePalette.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		(int paletteRows, int paletteSize, int recentColorCount)? response = await PromptResize ();
		if (!response.HasValue) return;

		bool extendedRows = response.Value.paletteRows == 3;
		bool wasExtendedRows = PintaCore.Settings.GetSetting (SettingNames.EXTENDED_PALETTE_ROWS, false);

		// The two default palettes order their columns differently, so a row change has to
		// reload the defaults for the columns to line up - which throws away edited swatches.
		if (extendedRows != wasExtendedRows && await ConfirmPaletteReset (wasExtendedRows)) {
			PintaCore.Settings.PutSetting (SettingNames.EXTENDED_PALETTE_ROWS, extendedRows);
			palette.CurrentPalette.LoadDefault (extendedRows);
		}

		palette.CurrentPalette.Resize (response.Value.paletteSize);

		if (response.Value.recentColorCount != palette.MaxRecentlyUsedColor)
			palette.SetRecentlyUsedColorCount (response.Value.recentColorCount);
	}

	/// <summary>
	/// Asks before a row change discards edited swatches. An untouched palette is still
	/// the defaults, so there is nothing to lose and nothing to ask about.
	/// </summary>
	private async Task<bool> ConfirmPaletteReset (bool wasExtendedRows)
	{
		if (palette.CurrentPalette.Colors.SequenceEqual (PaletteHelper.EnumerateDefaultColors (wasExtendedRows)))
			return true;

		string primary = Translations.GetString ("Changing the number of palette rows resets the palette");
		string secondary = Translations.GetString (
			"The colors you have edited will be replaced by the default palette for the new row count. "
			+ "Keep them instead and the palette stays at its current number of rows.");

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (chrome.MainWindow, primary, secondary);

		const string keep_response = "keep";
		const string reset_response = "reset";

		dialog.AddResponse (keep_response, Translations.GetString ("_Keep Colors"));
		dialog.AddResponse (reset_response, Translations.GetString ("_Reset Palette"));
		dialog.SetResponseAppearance (reset_response, Adw.ResponseAppearance.Destructive);
		dialog.CloseResponse = keep_response;
		dialog.DefaultResponse = keep_response;

		return await dialog.RunAsync () == reset_response;
	}

	private async Task<(int paletteRows, int paletteSize, int recentColorCount)?> PromptResize ()
	{
		int rows = PaletteHelper.GetPaletteRowCount ();

		Gtk.StringList rowCountModel = Gtk.StringList.New (["2", "3"]);
		Gtk.DropDown rowCountDropdown = Gtk.DropDown.New (rowCountModel, expression: null);
		rowCountDropdown.Selected = (uint) (rows - 2);
		rowCountDropdown.TooltipText = Translations.GetString ("Three rows adds a darker shade of each color beneath the standard palette.");

		// Both counts move in whole rows, so the quick colors and recent colors stay
		// aligned with each other in the palette bar.
		Gtk.SpinButton paletteSizeSpinner = Gtk.SpinButton.NewWithRange (rows, 96, rows);
		paletteSizeSpinner.SetActivatesDefaultImmediate (true);
		// Round down to the nearest full row so every column stays full - without
		// silently resizing the actual palette until the user confirms.
		paletteSizeSpinner.Value = PaletteHelper.RoundDownToRowMultiple (palette.CurrentPalette.Colors.Count, rows);

		Gtk.SpinButton recentCountSpinner = Gtk.SpinButton.NewWithRange (0, PaletteHelper.MAX_RECENT_COLOR_COUNT, rows);
		recentCountSpinner.SetActivatesDefaultImmediate (true);
		recentCountSpinner.Value = PaletteHelper.NormalizeRecentColorCount (palette.MaxRecentlyUsedColor, rows);

		// Changing the row count re-steps both size fields so they keep landing on
		// full columns.
		Gtk.DropDown.SelectedPropertyDefinition.Notify (rowCountDropdown, (_, _) => {
			int newRows = GetRowCount ();
			paletteSizeSpinner.Adjustment!.StepIncrement = newRows;
			paletteSizeSpinner.Adjustment!.Lower = newRows;
			paletteSizeSpinner.Value = PaletteHelper.RoundDownToRowMultiple (paletteSizeSpinner.GetValueAsInt (), newRows);
			recentCountSpinner.Adjustment!.StepIncrement = newRows;
			recentCountSpinner.Value = PaletteHelper.NormalizeRecentColorCount (recentCountSpinner.GetValueAsInt (), newRows);
		});

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = 6;
		grid.ColumnSpacing = 6;
		grid.Attach (CreateLabel (Translations.GetString ("Palette rows:")), 0, 0, 1, 1);
		grid.Attach (rowCountDropdown, 1, 0, 1, 1);
		grid.Attach (CreateResetButton (() => rowCountDropdown.Selected = 0), 2, 0, 1, 1);
		grid.Attach (CreateLabel (Translations.GetString ("New palette size:")), 0, 1, 1, 1);
		grid.Attach (paletteSizeSpinner, 1, 1, 1, 1);
		grid.Attach (CreateResetButton (() => paletteSizeSpinner.Value = DefaultPaletteSize (GetRowCount ())), 2, 1, 1, 1);
		grid.Attach (CreateLabel ($"{Translations.GetString ("Recently picked colors")} (0 = {Translations.GetString ("None")}):"), 0, 2, 1, 1);
		grid.Attach (recentCountSpinner, 1, 2, 1, 1);
		grid.Attach (CreateResetButton (() => recentCountSpinner.Value = PaletteHelper.GetDefaultRecentColorCount (GetRowCount () == 3)), 2, 2, 1, 1);

		using Gtk.Dialog dialog = Gtk.Dialog.New ();
		dialog.Title = Translations.GetString ("Resize Palette");
		dialog.TransientFor = chrome.MainWindow;
		dialog.Modal = true;
		dialog.AddCancelOkButtons ();
		dialog.SetDefaultResponse (Gtk.ResponseType.Ok);

		Gtk.Box content = dialog.GetContentAreaBox ();
		content.SetAllMargins (12);
		content.Append (grid);

		try {
			Gtk.ResponseType response = await dialog.RunAsync ();
			if (response != Gtk.ResponseType.Ok) return null;
			return (GetRowCount (), paletteSizeSpinner.GetValueAsInt (), recentCountSpinner.GetValueAsInt ());
		} finally {
			dialog.Destroy ();
		}

		int GetRowCount () => (int) rowCountDropdown.Selected + 2;

		static int DefaultPaletteSize (int rows)
			=> PaletteHelper.EnumerateDefaultColors (rows == 3).Count ();

		static Gtk.Label CreateLabel (string text)
		{
			Gtk.Label label = Gtk.Label.New (text);
			label.Halign = Gtk.Align.Start;
			label.Hexpand = true;
			return label;
		}

		static Gtk.Button CreateResetButton (Action reset)
		{
			Gtk.Button button = Gtk.Button.New ();
			button.IconName = "edit-undo-symbolic";
			button.TooltipText = Translations.GetString ("Reset to default");
			button.OnClicked += (_, _) => reset ();
			return button;
		}
	}
}
