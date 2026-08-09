//
// QualityDialog.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva
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
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta;

[GObject.Subclass<Gtk.Dialog>]
public sealed partial class QualityDialog
{
	/// <summary>
	/// Quality reported when the lossless toggle is active. Formats that offer
	/// lossless treat this value as "lossless" rather than "lossy at 100".
	/// </summary>
	public const int LosslessQuality = 100;

	private Gtk.Scale compression_level;
	private Gtk.CheckButton lossless_check;

	[MemberNotNull (nameof (compression_level), nameof (lossless_check))]
	partial void Initialize ()
	{
		Gtk.Label qualityLabel = Gtk.Label.New (Translations.GetString ("Quality: "));
		qualityLabel.Xalign = 0;

		Gtk.Scale levelScale = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 1, 100, 1);
		levelScale.DrawValue = true;

		Gtk.CheckButton losslessCheck = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Lossless"));
		losslessCheck.Visible = false;
		losslessCheck.OnToggled += (_, _) => {
			levelScale.Sensitive = !losslessCheck.Active;
			qualityLabel.Sensitive = !losslessCheck.Active;
		};

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("Image Quality");
		Modal = true;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		Gtk.Box contentBox = this.GetContentAreaBox ();
		contentBox.SetAllMargins (6);
		contentBox.Spacing = 3;
		contentBox.AppendMultiple ([
			losslessCheck,
			qualityLabel,
			levelScale]);

		// --- References to keep

		compression_level = levelScale;
		lossless_check = losslessCheck;
	}

	public static QualityDialog New (int defaultQuality, Gtk.Window parent, bool allowLossless = false)
	{
		QualityDialog dialog = NewWithProperties ([]);

		int maximumQuality = LosslessQuality;

		if (allowLossless) {
			// The lossy scale tops out at 99 so that 100 unambiguously means lossless.
			maximumQuality = LosslessQuality - 1;
			dialog.compression_level.SetRange (1, maximumQuality);
			dialog.lossless_check.Visible = true;
			dialog.lossless_check.Active = defaultQuality >= LosslessQuality;
		}

		dialog.compression_level.SetValue (Math.Min (defaultQuality, maximumQuality));
		dialog.TransientFor = parent;
		return dialog;
	}

	public int CompressionLevel
		=> lossless_check.Active
			? LosslessQuality
			: (int) compression_level.GetValue ();
}
