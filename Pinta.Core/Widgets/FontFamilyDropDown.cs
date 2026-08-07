using System;
using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Font-family picker used in place of <see cref="Gtk.FontDialogButton"/>. The GTK font
/// dialog (GtkFontChooserWidget) aborts the whole process when it hits a font whose face
/// Pango cannot describe ("gtk_font_chooser_widget_merge_font_desc: assertion failed").
/// This widget enumerates families ourselves from the Pango font map — skipping any with
/// no describable face — and shows each name rendered in its own font as a live preview.
/// Only the family is chosen here; size/weight/style/variant come from the other toolbar
/// controls, exactly as with the old button (which was set to <c>FontLevel.Family</c>).
/// </summary>
public sealed class FontFamilyDropDown
{
	public Gtk.DropDown Widget { get; }

	// Window width (px) below which popup rows show only the family name, no sample.
	private const int SampleWidthThreshold = 700;

	private readonly string[] families;
	private Pango.FontDescription font_desc;
	private bool suppress_notify;

	private string sample_text = "";

	public event EventHandler? FontChanged;

	/// <summary>
	/// A snippet of the current text. Its first 10 characters are shown after the family name
	/// on each popup row (rendered in that row's font) when the toolbar is wide enough; on a
	/// thin window the rows show only the name. The collapsed button always shows just the name
	/// so the toolbar control keeps a stable width.
	/// </summary>
	public string SampleText {
		set {
			string text = value ?? "";
			if (text == sample_text)
				return;
			sample_text = text;
			// Rebuild the popup rows so the sample refreshes next time the dropdown opens.
			// A fresh factory instance forces GtkListView to re-bind every row; re-setting
			// the same one is a no-op, which is why the sample looked stuck before.
			Widget.SetListFactory (BuildListFactory ());
		}
	}

	/// <summary>Working font description (family + size). Returns a copy; setting selects the family.</summary>
	public Pango.FontDescription FontDesc {
		get => font_desc.Copy ()!;
		set {
			font_desc = value.Copy ()!;
			string? family = font_desc.GetFamily ();
			int index = family is null
				? -1
				: Array.FindIndex (families, f => string.Equals (f, family, StringComparison.OrdinalIgnoreCase));
			if (index < 0)
				return;
			suppress_notify = true;
			Widget.SetSelected ((uint) index);
			suppress_notify = false;
		}
	}

	public FontFamilyDropDown (Pango.FontDescription initial)
	{
		font_desc = initial.Copy ()!;

		// Empty model first so the widget has a Pango context to read the font map from.
		Widget = Gtk.DropDown.New (Gtk.StringList.New ([]), expression: null);
		families = GetFamilies (Widget.GetPangoContext ());
		Widget.SetModel (Gtk.StringList.New (families));

		// Popup rows: family name, plus a sample of the current text when there's room.
		Widget.SetListFactory (BuildListFactory ());

		// Collapsed button: family name only, so the toolbar control stays a stable width.
		Gtk.SignalListItemFactory buttonFactory = Gtk.SignalListItemFactory.New ();
		buttonFactory.OnSetup += (_, args) => {
			Gtk.Label label = Gtk.Label.New (null);
			label.SetXalign (0);
			((Gtk.ListItem) args.Object).SetChild (label);
		};
		buttonFactory.OnBind += (_, args) => {
			var item = (Gtk.ListItem) args.Object;
			string family = ((Gtk.StringObject) item.GetItem ()!).GetString ();
			var label = (Gtk.Label) item.GetChild ()!;
			label.SetText (family);
			label.SetAttributes (FamilyAttributes (family));
		};
		Widget.SetFactory (buttonFactory);

		FontDesc = initial; // select the family

		Gtk.DropDown.SelectedPropertyDefinition.Notify (Widget, (_, _) => OnSelectionChanged ());
	}

	private Gtk.SignalListItemFactory BuildListFactory ()
	{
		Gtk.SignalListItemFactory factory = Gtk.SignalListItemFactory.New ();
		factory.OnSetup += (_, args) => {
			Gtk.Label label = Gtk.Label.New (null);
			label.SetXalign (0);
			((Gtk.ListItem) args.Object).SetChild (label);
		};
		factory.OnBind += OnBindRow;
		return factory;
	}

	private void OnBindRow (Gtk.SignalListItemFactory _, Gtk.SignalListItemFactory.BindSignalArgs args)
	{
		var item = (Gtk.ListItem) args.Object;
		string family = ((Gtk.StringObject) item.GetItem ()!).GetString ();
		var label = (Gtk.Label) item.GetChild ()!;
		label.SetText (family + SampleSuffix ());
		label.SetAttributes (FamilyAttributes (family));
	}

	// The " sample" appended to each row, or empty when there's no text or the window is thin.
	private string SampleSuffix ()
	{
		if (sample_text.Length == 0)
			return "";
		int width = Widget.GetAncestor (Gtk.Window.GetGType ())?.GetWidth () ?? 0;
		if (width > 0 && width < SampleWidthThreshold)
			return "";
		string sample = sample_text.ReplaceLineEndings (" ");
		if (sample.Length > 10)
			sample = sample[..10];
		return "   " + sample;
	}

	private static Pango.AttrList FamilyAttributes (string family)
	{
		Pango.FontDescription desc = Pango.FontDescription.New ();
		desc.SetFamily (family);
		Pango.AttrList attrs = Pango.AttrList.New ();
		attrs.Insert (Pango.AttrFontDesc.New (desc));
		return attrs;
	}

	private void OnSelectionChanged ()
	{
		if (suppress_notify)
			return;
		uint selected = Widget.GetSelected ();
		if (selected == Gtk.Constants.INVALID_LIST_POSITION || selected >= families.Length)
			return;
		font_desc.SetFamily (families[selected]);
		FontChanged?.Invoke (this, EventArgs.Empty);
	}

	private static string[] GetFamilies (Pango.Context context)
	{
		Pango.FontMap map = context.GetFontMap ()!;
		uint count = map.GetNItems ();
		SortedSet<string> names = new (StringComparer.CurrentCultureIgnoreCase);
		for (uint i = 0; i < count; i++) {
			var family = (Pango.FontFamily) map.GetObject (i)!;
			if (!HasDescribableFace (family))
				continue; // the exact fonts that crash the GTK dialog
			string? name = family.GetName ();
			if (!string.IsNullOrEmpty (name))
				names.Add (name);
		}
		string[] result = new string[names.Count];
		names.CopyTo (result);
		return result;
	}

	private static bool HasDescribableFace (Pango.FontFamily family)
	{
		uint faceCount = family.GetNItems ();
		for (uint i = 0; i < faceCount; i++) {
			try {
				var face = (Pango.FontFace) family.GetObject (i)!;
				if (face.Describe () is not null)
					return true;
			} catch {
				// Undescribable face — ignore and keep checking the family's others.
			}
		}
		return false;
	}
}
