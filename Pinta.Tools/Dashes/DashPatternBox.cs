//
// DashPatternBox.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2013 Andrew Davis, GSoC 2013
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

using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class DashPatternBox
{
	private bool dash_change_setup = false;

	private Label? dash_pattern_label;
	private Separator? dash_pattern_sep;

	private Separator? dash_spacing_sep;
	private Label? dash_spacing_label;

	public ToolBarComboBox? ComboBox { get; private set; }

	public ToolBarComboBox? SpacingComboBox { get; private set; }

	/// <summary>
	/// Sets up the DashPatternBox in the Toolbar.
	///
	/// Note that the dash pattern change event response code must be created manually outside of the DashPatternBox
	/// (using the returned Gtk.ComboBox from the SetupToolbar method) so that each tool that uses it
	/// can react to the change in pattern according to its usage.
	///
	/// Returns null if the DashPatternBox has already been setup; otherwise, returns the DashPatternBox itself.
	/// </summary>
	/// <param name="tb">The Toolbar to add the DashPatternBox to.</param>
	/// <returns>null if the DashPatternBox has already been setup; otherwise, returns the DashPatternBox itself.</returns>
	public Gtk.ComboBoxText? SetupToolbar (Box tb)
	{
		dash_pattern_sep ??= GtkExtensions.CreateToolBarSeparator ();

		tb.Append (dash_pattern_sep);

		if (dash_pattern_label == null) {
			var dashString = Translations.GetString ("Dash");
			dash_pattern_label = Label.New ($" {dashString}: ");
		}

		tb.Append (dash_pattern_label);

		// Dash glyphs are very narrow - longest dash line is only half the old toolbar box.
		// Width matches selected option (was 90, then 38, now dynamic ~50-100, slightly wider than before).
		ComboBox ??= ToolBarComboBox.New (56, 0, true,
				"- (Solid)", " -", " --", " ---", "  -", "   -", " - --", " - - --------", " - - ---- - ----");
		ComboBox.ComboBox.AddCssClass (Resources.Styles.DashPatternCombo);
		// Keep focusable so single click on arrow opens popup, but not editable to avoid text-input look.
		ComboBox.ComboBox.CanFocus = true;
		var dashEntry = ComboBox.ComboBox.GetEntry ();
		dashEntry.SetEditable (false);
		dashEntry.CanFocus = false;
		dashEntry.SetWidthChars (6);
		dashEntry.SetMaxWidthChars (9);

		ComboBox.ComboBox.OnChanged += (o, _) => {
			// Immediately collapse selection so it doesn't look like highlighted text input.
			try { o.GetEntry ().SelectRegion (0, 0); } catch { }
			// Defer layout-changing work to idle so it doesn't interfere with popup close
			// and doesn't require an extra click to "wake up" the dropdown.
			string? active = ComboBox.ComboBox.GetActiveText ();
			GLib.Functions.IdleAdd (0, () => {
				UpdateDashComboWidth (active);
				UpdateSpacingSensitivity ();
				try { ComboBox.ComboBox.GetEntry ().SelectRegion (0, 0); } catch { }
				return false;
			});
		};

		tb.Append (ComboBox);

		dash_spacing_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (dash_spacing_sep);

		if (dash_spacing_label == null) {
			var spacingString = Translations.GetString ("Spacing");
			dash_spacing_label = Label.New ($" {spacingString}: ");
		}

		tb.Append (dash_spacing_label);

		// Spacing is single-digit multiplier, extra narrow. Shows "-" when solid.
		SpacingComboBox ??= ToolBarComboBox.New (36, 0, false,
				"-", "1", "2", "3", "4", "5", "6", "8", "10");

		tb.Append (SpacingComboBox);

		UpdateDashComboWidth (ComboBox.ComboBox.GetActiveText ());
		UpdateSpacingSensitivity ();

		if (dash_change_setup) {
			return null;
		} else {
			dash_change_setup = true;

			return ComboBox.ComboBox;
		}
	}

	private void UpdateDashComboWidth (string? activeText)
	{
		if (ComboBox == null) return;
		// Match selection box width to currently selected dash option, slightly wider than minimal.
		(int widthReq, int widthChars) = activeText switch {
			"- (Solid)" => (76, 8),
			" -" => (50, 3),
			" --" => (58, 4),
			" ---" => (64, 5),
			"  -" => (54, 4),
			"   -" => (58, 5),
			" - --" => (68, 6),
			" - - --------" => (92, 9),
			" - - ---- - ----" => (100, 10),
			_ => (58, 5),
		};

		try {
			ComboBox.ComboBox.WidthRequest = widthReq;
			var e = ComboBox.ComboBox.GetEntry ();
			e.SetWidthChars (widthChars);
			e.SetMaxWidthChars (widthChars + 1);
		} catch { }
	}

	// Spacing multiplies the gaps between dashes, so it does nothing for a solid line.
	// When solid, show "-" glyph in spacing combo to indicate disabled/no spacing.
	private void UpdateSpacingSensitivity ()
	{
		if (ComboBox == null) return;
		string? active = ComboBox.ComboBox.GetActiveText ();
		bool hasDashes = Pinta.Core.CairoExtensions.IsValidDashPattern (active ?? "");

		// Vary "(Solid)" size: smaller in toolbar selection, normal in popup options.
		try {
			var entry = ComboBox.ComboBox.GetEntry ();
			if (hasDashes) {
				entry.RemoveCssClass ("solid-selected");
			} else {
				entry.AddCssClass ("solid-selected");
			}
		} catch { }

		if (dash_spacing_label != null) dash_spacing_label.Sensitive = hasDashes;
		if (SpacingComboBox != null) {
			SpacingComboBox.Sensitive = hasDashes;
			// Show "-" when solid (disabled), otherwise ensure a numeric value is shown.
			if (!hasDashes) {
				if (SpacingComboBox.ComboBox.Active != 0)
					SpacingComboBox.ComboBox.Active = 0;
			} else {
				if (SpacingComboBox.ComboBox.Active == 0)
					SpacingComboBox.ComboBox.Active = 1;
			}
		}
	}

	public void SetVisible (bool visible)
	{
		if (dash_pattern_label == null || dash_pattern_sep == null || ComboBox == null) { return; }
		dash_pattern_label.Visible = dash_pattern_sep.Visible = ComboBox.Visible = visible;
		if (dash_spacing_sep != null) dash_spacing_sep.Visible = visible;
		if (dash_spacing_label != null) dash_spacing_label.Visible = visible;
		if (SpacingComboBox != null) SpacingComboBox.Visible = visible;
	}
}
