using System;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Tools;

/// <summary>
/// A window for editing the typography and rotation of a single text object. Changes
/// are applied to the object live as the user edits; OK keeps them, while closing
/// without OK (or Cancel) restores the object to its state when the window opened.
/// </summary>
internal sealed class TextPropertiesDialog : IDisposable
{
	private readonly TextObject text_object;
	private readonly Action redraw;
	private readonly Action finished;

	private readonly Gtk.Window window;

	//Snapshot of the object's state when the window opened, used to restore on cancel.
	private readonly Pango.FontDescription original_font;
	private readonly TextAlignment original_alignment;
	private readonly bool original_underline;
	private readonly double original_rotation;

	private bool finishing;

	#pragma warning disable CS8618 // NRT - initialized by BuildUi
	private Gtk.FontDialogButton font_button;
	private Gtk.SpinButton font_size;
	private ToolBarDropDownButton variant_btn;
	private ToolBarDropDownButton weight_btn;
	private Gtk.ToggleButton italic_btn;
	private Gtk.ToggleButton underscore_btn;
	private Gtk.ToggleButton left_alignment_btn;
	private Gtk.ToggleButton center_alignment_btn;
	private Gtk.ToggleButton right_alignment_btn;
	private Gtk.SpinButton rotation_spin;
	#pragma warning restore CS8618

	public TextPropertiesDialog (Gtk.Window parent, TextObject textObject, Action onRedraw, Action onFinished)
	{
		text_object = textObject;

		TextEngine engine = textObject.Engine;
		original_font = engine.Font.Copy ()!;
		original_alignment = engine.Alignment;
		original_underline = engine.Underline;
		original_rotation = textObject.Rotation;

		redraw = onRedraw;
		finished = onFinished;

		window = Gtk.Window.New ();
		window.SetTransientFor (parent);
		window.Modal = true;
		window.Title = Translations.GetString ("Text Properties");
		window.IconName = Icons.ToolText;

		BuildUi ();

		window.OnCloseRequest += (_, _) => { Finish (keepChanges: false); return true; };
	}

	public void Present () => window.Present ();

	private void Finish (bool keepChanges)
	{
		if (finishing)
			return;
		finishing = true;

		if (!keepChanges)
			RestoreOriginal ();

		window.Close ();
		finished ();
	}

	private void RestoreOriginal ()
	{
		text_object.Engine.SetFont (original_font.Copy ()!, original_alignment, original_underline);
		text_object.Rotation = original_rotation;
		redraw ();
	}

	private TextAlignment Alignment {
		get {
			if (right_alignment_btn.Active)
				return TextAlignment.Right;
			else if (center_alignment_btn.Active)
				return TextAlignment.Center;
			else
				return TextAlignment.Left;
		}
	}

	private Pango.FontDescription BuildFont ()
	{
		Pango.FontDescription font = font_button.FontDesc!.Copy ()!;
		font.SetSize (PangoExtensions.UnitsFromPixels (font_size.GetValueAsInt ()));
		font.SetVariant ((Pango.Variant) variant_btn.SelectedItem.GetTagOrDefault (Pango.Variant.Normal));
		font.SetWeight ((Pango.Weight) weight_btn.SelectedItem.GetTagOrDefault (Pango.Weight.Normal));
		font.SetStyle (italic_btn.Active ? Pango.Style.Italic : Pango.Style.Normal);
		return font;
	}

	private void Apply ()
	{
		text_object.Engine.SetFont (BuildFont (), Alignment, underscore_btn.Active);
		text_object.Rotation = NormalizeRotation (rotation_spin.Value);
		redraw ();
	}

	private static double NormalizeRotation (double degrees)
	{
		degrees %= 360;
		if (degrees < 0)
			degrees += 360;
		return degrees;
	}

	private void BuildUi ()
	{
		Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Vertical, 12);
		content.SetAllMargins (12);

		TextEngine engine = text_object.Engine;
		Pango.FontDescription font = engine.Font;

		// --- Font family ---
		Gtk.FontDialog fontDialog = Gtk.FontDialog.New ();
		fontDialog.Modal = true;

		font_button = Gtk.FontDialogButton.New (fontDialog);
		font_button.UseSize = false;
		font_button.UseFont = true;
		font_button.CanFocus = false;
		font_button.Level = Gtk.FontLevel.Family;
		font_button.FontDesc = font;
		Gtk.FontDialogButton.FontDescPropertyDefinition.Notify (font_button, (_, _) => Apply ());
		content.Append (font_button);

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);

		// --- Font size ---
		Gtk.Adjustment sizeAdj = Gtk.Adjustment.New (
			value: PangoExtensions.UnitsToPixels (font.GetSize ()),
			lower: 1, upper: 2000, stepIncrement: 1, pageIncrement: 0, pageSize: 0);
		font_size = Gtk.SpinButton.New (sizeAdj, climbRate: 0.0, digits: 0);
		font_size.OnValueChanged += (_, _) => Apply ();

		Gtk.Label sizeLabel = Gtk.Label.New (Translations.GetString ("Size: "));
		row.Append (sizeLabel);
		row.Append (font_size);

		// --- Rotation ---
		Gtk.Adjustment rotAdj = Gtk.Adjustment.New (
			value: NormalizeRotation (text_object.Rotation),
			lower: 0, upper: 360, stepIncrement: 1, pageIncrement: 15, pageSize: 0);
		rotation_spin = Gtk.SpinButton.New (rotAdj, climbRate: 1.0, digits: 1);
		rotation_spin.OnValueChanged += (_, _) => Apply ();

		Gtk.Label rotLabel = Gtk.Label.New (Translations.GetString ("Rotation: "));
		row.Append (rotLabel);
		row.Append (rotation_spin);

		content.Append (row);

		// --- Style buttons (weight, italic, underline) ---
		Gtk.Box styleRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);

		weight_btn = ToolBarDropDownButton.New ();
		weight_btn.AddItem ("Thin 100", Icons.TextExtraLight, Pango.Weight.Thin);
		weight_btn.AddItem ("Ultralight 200", Icons.TextExtraLight, Pango.Weight.Ultralight);
		weight_btn.AddItem ("Light 300", Icons.TextLight, Pango.Weight.Light);
		weight_btn.AddItem ("Semilight 350", Icons.TextLight, Pango.Weight.Semilight);
		weight_btn.AddItem ("Book 380", Icons.TextNormal, Pango.Weight.Book);
		weight_btn.AddItem ("Normal 400", Icons.TextNormal, Pango.Weight.Normal);
		weight_btn.AddItem ("Medium 500", Icons.TextNormal, Pango.Weight.Medium);
		weight_btn.AddItem ("Semibold 600", Icons.TextBold, Pango.Weight.Semibold);
		weight_btn.AddItem ("Bold 700", Icons.TextBold, Pango.Weight.Bold);
		weight_btn.AddItem ("Ultrabold 800", Icons.TextExtraBold, Pango.Weight.Ultrabold);
		weight_btn.AddItem ("Heavy 900", Icons.TextExtraBold, Pango.Weight.Heavy);
		weight_btn.AddItem ("Ultraheavy 1000", Icons.TextExtraBold, Pango.Weight.Ultraheavy);
		weight_btn.SelectedIndex = 0;
		for (int i = 0; i < weight_btn.Items.Count; i++)
			if (Equals (weight_btn.Items[i].Tag, font.GetWeight ()))
				weight_btn.SelectedIndex = i;
		weight_btn.SelectedItemChanged += (_, _) => Apply ();
		styleRow.Append (weight_btn);

		italic_btn = Gtk.ToggleButton.New ();
		italic_btn.IconName = StandardIcons.FormatTextItalic;
		italic_btn.TooltipText = Translations.GetString ("Italic");
		italic_btn.CanFocus = false;
		italic_btn.Active = font.GetStyle () == Pango.Style.Italic;
		italic_btn.OnToggled += (_, _) => Apply ();
		styleRow.Append (italic_btn);

		underscore_btn = Gtk.ToggleButton.New ();
		underscore_btn.IconName = StandardIcons.FormatTextUnderline;
		underscore_btn.TooltipText = Translations.GetString ("Underline");
		underscore_btn.CanFocus = false;
		underscore_btn.Active = engine.Underline;
		underscore_btn.OnToggled += (_, _) => Apply ();
		styleRow.Append (underscore_btn);

		content.Append (styleRow);

		// --- Alignment ---
		Gtk.Box alignRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);

		TextAlignment alignment = engine.Alignment;

		left_alignment_btn = Gtk.ToggleButton.New ();
		left_alignment_btn.IconName = StandardIcons.FormatJustifyLeft;
		left_alignment_btn.TooltipText = Translations.GetString ("Left Align");
		left_alignment_btn.CanFocus = false;
		left_alignment_btn.Active = alignment == TextAlignment.Left;
		left_alignment_btn.OnToggled += (_, _) => HandleAlignment ();
		alignRow.Append (left_alignment_btn);

		center_alignment_btn = Gtk.ToggleButton.New ();
		center_alignment_btn.IconName = StandardIcons.FormatJustifyCenter;
		center_alignment_btn.TooltipText = Translations.GetString ("Center Align");
		center_alignment_btn.CanFocus = false;
		center_alignment_btn.Active = alignment == TextAlignment.Center;
		center_alignment_btn.OnToggled += (_, _) => HandleAlignment ();
		alignRow.Append (center_alignment_btn);

		right_alignment_btn = Gtk.ToggleButton.New ();
		right_alignment_btn.IconName = StandardIcons.FormatJustifyRight;
		right_alignment_btn.TooltipText = Translations.GetString ("Right Align");
		right_alignment_btn.CanFocus = false;
		right_alignment_btn.Active = alignment == TextAlignment.Right;
		right_alignment_btn.OnToggled += (_, _) => HandleAlignment ();
		alignRow.Append (right_alignment_btn);

		content.Append (alignRow);

		// --- Variant ---
		variant_btn = ToolBarDropDownButton.New ();
		variant_btn.AddItem (Translations.GetString ("Normal"), Icons.TextVariantNormal, Pango.Variant.Normal);
		variant_btn.AddItem (Translations.GetString ("Small Caps"), Icons.TextVariantSmallCaps, Pango.Variant.SmallCaps);
		variant_btn.AddItem (Translations.GetString ("All Small Caps"), Icons.TextVariantAllSmallCaps, Pango.Variant.AllSmallCaps);
		variant_btn.AddItem (Translations.GetString ("Petite Caps"), Icons.TextVariantPetiteCaps, Pango.Variant.PetiteCaps);
		variant_btn.AddItem (Translations.GetString ("All Petite Caps"), Icons.TextVariantAllPetiteCaps, Pango.Variant.AllPetiteCaps);
		variant_btn.AddItem (Translations.GetString ("Unicase"), Icons.TextVariantUnicase, Pango.Variant.Unicase);
		variant_btn.AddItem (Translations.GetString ("Title Caps"), Icons.TextVariantTitleCaps, Pango.Variant.Normal);
		variant_btn.SelectedIndex = 0;
		for (int i = 0; i < variant_btn.Items.Count; i++)
			if (Equals (variant_btn.Items[i].Tag, font.GetVariant ()))
				variant_btn.SelectedIndex = i;
		variant_btn.SelectedItemChanged += (_, _) => Apply ();
		content.Append (variant_btn);

		// --- Bottom buttons ---
		Gtk.Box buttonRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		buttonRow.Halign = Gtk.Align.End;

		Gtk.Button cancelButton = Gtk.Button.NewWithLabel (Translations.GetString ("Cancel"));
		cancelButton.OnClicked += (_, _) => Finish (keepChanges: false);

		Gtk.Button okButton = Gtk.Button.NewWithLabel (Translations.GetString ("OK"));
		okButton.AddCssClass (AdwaitaStyles.SuggestedAction);
		okButton.OnClicked += (_, _) => Finish (keepChanges: true);

		buttonRow.Append (cancelButton);
		buttonRow.Append (okButton);
		content.Append (buttonRow);

		window.SetChild (content);
	}

	private void HandleAlignment ()
	{
		if (left_alignment_btn.Active)
			center_alignment_btn.Active = right_alignment_btn.Active = false;
		else if (center_alignment_btn.Active)
			left_alignment_btn.Active = right_alignment_btn.Active = false;
		else if (right_alignment_btn.Active)
			left_alignment_btn.Active = center_alignment_btn.Active = false;
		else
			left_alignment_btn.Active = true;

		Apply ();
	}

	public void Dispose ()
	{
		window.Destroy ();
	}
}
