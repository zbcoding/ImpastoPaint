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
		(int paletteSize, int recentColorCount)? response = await PromptResize ();
		if (!response.HasValue) return;

		palette.CurrentPalette.Resize (response.Value.paletteSize);

		if (response.Value.recentColorCount != palette.MaxRecentlyUsedColor)
			palette.SetRecentlyUsedColorCount (response.Value.recentColorCount);
	}

	private async Task<(int paletteSize, int recentColorCount)?> PromptResize ()
	{
		int rows = PaletteHelper.GetPaletteRowCount ();

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

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = 6;
		grid.ColumnSpacing = 6;
		grid.Attach (CreateLabel (Translations.GetString ("New palette size:")), 0, 0, 1, 1);
		grid.Attach (paletteSizeSpinner, 1, 0, 1, 1);
		grid.Attach (CreateLabel ($"{Translations.GetString ("Recently picked colors")} (0 = {Translations.GetString ("None")}):"), 0, 1, 1, 1);
		grid.Attach (recentCountSpinner, 1, 1, 1, 1);

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
			return (paletteSizeSpinner.GetValueAsInt (), recentCountSpinner.GetValueAsInt ());
		} finally {
			dialog.Destroy ();
		}

		static Gtk.Label CreateLabel (string text)
		{
			Gtk.Label label = Gtk.Label.New (text);
			label.Halign = Gtk.Align.Start;
			label.Hexpand = true;
			return label;
		}
	}
}
