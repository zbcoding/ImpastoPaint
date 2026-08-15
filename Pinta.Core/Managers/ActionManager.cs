//
// ActionManager.cs
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

namespace Pinta.Core;

public sealed class ActionManager
{
	public AppActions App { get; }
	public FileActions File { get; }
	public EditActions Edit { get; }
	public ViewActions View { get; }
	public ImageActions Image { get; }
	public LayerActions Layers { get; }
	public AdjustmentsActions Adjustments { get; }
	public EffectsActions Effects { get; }
	public WindowActions Window { get; }
	public HelpActions Help { get; }
	public AddinActions Addins { get; }

	private readonly SystemManager system;
	private readonly ChromeManager chrome;
	public ActionManager (
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		PaletteFormatManager paletteFormats,
		PaletteManager palette,
		RecentFileManager recentFiles,
		SystemManager system,
		ToolManager tools,
		WorkspaceManager workspace)
	{
		// --- Action handlers that don't depend on other handlers

		AddinActions addins = new (chrome);
		AdjustmentsActions adjustments = new ();
		AppActions app = new ();
		EditActions edit = new (chrome, paletteFormats, palette, tools, workspace);
		EffectsActions effects = new (addins);
		ViewActions view = new (chrome, workspace);
		WindowActions window = new (workspace);

		// --- Action handlers that depend on other handlers

		FileActions file = new (system, app);
		HelpActions help = new (system, app);
		ImageActions image = new (tools, workspace, view);
		LayerActions layers = new (chrome, imageFormats, recentFiles, tools, workspace, image);

		// --- References to keep

		App = app;
		File = file;
		Edit = edit;
		View = view;
		Image = image;
		Layers = layers;
		Adjustments = adjustments;
		Effects = effects;
		Window = window;
		Help = help;
		Addins = addins;

		this.system = system;
		this.chrome = chrome;
	}

	public void CreateToolBar (Gtk.Box toolbar, string? pasteAlternateDescription = null)
	{
		toolbar.Append (File.New.CreateToolBarItem ());
		toolbar.Append (File.Open.CreateToolBarItem ());
		toolbar.Append (File.Save.CreateToolBarItem ());
		toolbar.Append (File.SaveAs.CreateToolBarItem ());
		// Printing is disabled for now until it is fully functional.
#if false
		toolbar.AppendItem (File.Print.CreateToolBarItem ());
#endif
		toolbar.Append (GtkExtensions.CreateToolBarSeparator ());

		// Cut/Copy/Paste comes before Undo/Redo on Windows
		if (system.OperatingSystem == OS.Windows) {
			toolbar.Append (Edit.Cut.CreateToolBarItem ());
			toolbar.Append (Edit.Copy.CreateToolBarItem ());
			toolbar.Append (Edit.Paste.CreateToolBarItem (alternate: Edit.PasteAlternate, alternate_description: pasteAlternateDescription));
			toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
			toolbar.Append (Edit.Undo.CreateToolBarItem ());
			toolbar.Append (Edit.Redo.CreateToolBarItem ());
		} else {
			toolbar.Append (Edit.Undo.CreateToolBarItem ());
			toolbar.Append (Edit.Redo.CreateToolBarItem ());
			toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
			toolbar.Append (Edit.Cut.CreateToolBarItem ());
			toolbar.Append (Edit.Copy.CreateToolBarItem ());
			toolbar.Append (Edit.Paste.CreateToolBarItem (alternate: Edit.PasteAlternate, alternate_description: pasteAlternateDescription));
		}

		toolbar.Append (GtkExtensions.CreateToolBarSeparator ());
		toolbar.Append (Image.CropToSelection.CreateToolBarItem ());
		toolbar.Append (CreateDeselectToolBarItem ());
	}

	public void CreateHeaderToolBar (Adw.HeaderBar header, string? pasteAlternateDescription = null)
	{
		header.PackStart (File.New.CreateToolBarItem ());
		header.PackStart (File.Open.CreateToolBarItem ());
		header.PackStart (File.Save.CreateToolBarItem ());
		header.PackStart (File.SaveAs.CreateToolBarItem ());

		header.PackStart (GtkExtensions.CreateToolBarSeparator ());
		header.PackStart (Edit.Undo.CreateToolBarItem ());
		header.PackStart (Edit.Redo.CreateToolBarItem ());

		header.PackStart (GtkExtensions.CreateToolBarSeparator ());
		header.PackStart (Edit.Cut.CreateToolBarItem ());
		header.PackStart (Edit.Copy.CreateToolBarItem ());
		header.PackStart (Edit.Paste.CreateToolBarItem (alternate: Edit.PasteAlternate, alternate_description: pasteAlternateDescription));

		header.PackStart (GtkExtensions.CreateToolBarSeparator ());
		header.PackStart (Image.CropToSelection.CreateToolBarItem ());
		header.PackStart (CreateDeselectToolBarItem ());
	}

	// Impasto: the Deselect toolbar button is also reachable via a quick
	// double-tap of Escape (see EditActions.HandlePintaCoreActionsEditDeselectSelectionActivated),
	// which isn't a real accelerator on the command itself, so mention it manually.
	private Gtk.Button CreateDeselectToolBarItem ()
	{
		Gtk.Button button = Edit.Deselect.CreateToolBarItem ();
		button.TooltipText += "\n" + Translations.GetString ("Quick deselect: Esc (×2)");
		return button;
	}

	private Gtk.Widget? cursor_position_icon;
	private Gtk.Widget? cursor_position_label;
	private Gtk.Widget? image_size_icon;
	private Gtk.Widget? image_size_label;
	private bool show_cursor_position;
	private bool show_image_size;

	public void SetStatusBarCursorPositionVisible (bool visible)
	{
		show_cursor_position = visible;
		cursor_position_icon?.SetVisible (visible);
		cursor_position_label?.SetVisible (visible);
		cursor_slot?.SetVisible (visible);
		if (cursor_slot is not null)
			cursor_slot.RevealChild = visible && !cursor_group_hidden;
	}

	public void SetStatusBarImageSizeVisible (bool visible)
	{
		show_image_size = visible;
		image_size_icon?.SetVisible (visible);
		image_size_label?.SetVisible (visible);
		image_slot?.SetVisible (visible);
		if (image_slot is not null)
			image_slot.RevealChild = visible && !image_group_hidden;
	}

	private const uint FOOTER_SLIDE_MS = 150;

	private Gtk.Widget? footer_cursor_group;
	private Gtk.Widget? footer_image_group;
	private Gtk.Revealer? cursor_slot;
	private Gtk.Revealer? image_slot;
	private bool cursor_group_hidden;
	private bool image_group_hidden;

	// The chips don't stretch, so their natural width is what they occupy - and
	// unlike GetWidth() it stays valid while the chip is hidden or mid-slide.
	private static int NaturalWidth (Gtk.Widget? widget)
	{
		if (widget is null)
			return 0;
		widget.Measure (Gtk.Orientation.Horizontal, -1, out _, out int natural, out _, out _);
		return natural;
	}

	/// <summary>
	/// What the chips currently take out of the palette's row, and what each would
	/// take if shown. The palette adds its own allocation to the first number to get
	/// its budget: the palette and the chips divide one fixed region between them, so
	/// that sum stays put even mid-slide, while the toolbar's padding and the spacing
	/// between status bar children fall outside it and cancel. Reconstructing the
	/// budget from the status bar's total width instead silently dropped that padding
	/// and handed the palette a few pixels it didn't have, which is what let the
	/// action buttons run under the chip on its left.
	/// </summary>
	public (int occupiedByChips, int cursorWidth, int imageWidth, bool sliding) GetFooterChipRoom ()
	{
		// Only the collapsible chips count here. The selection chip never collapses, so it
		// belongs outside the shared region: the box has already taken its width out of the
		// palette's allocation, and adding it back handed the palette room it doesn't have -
		// the swatches and action icons then drew out underneath it.
		int occupied = (cursor_slot?.GetWidth () ?? 0) + (image_slot?.GetWidth () ?? 0);

		return (
			occupied,
			show_cursor_position ? NaturalWidth (footer_cursor_group) : 0,
			show_image_size ? NaturalWidth (footer_image_group) : 0,
			SlideInProgress (cursor_slot) || SlideInProgress (image_slot));
	}

	// A revealer reports its target and its settled state separately; they differ
	// only while the slide animation is running.
	private static bool SlideInProgress (Gtk.Revealer? slot) =>
		slot is not null && slot.RevealChild != slot.ChildRevealed;

	/// <summary>
	/// Applies the chip half of the palette's collapse cascade: a chip that lost its
	/// room slides out to the right, under the zoom controls.
	/// </summary>
	public void SetFooterChipsVisible (bool cursor, bool image)
	{
		cursor_group_hidden = !cursor;
		image_group_hidden = !image;

		if (cursor_slot is not null)
			cursor_slot.RevealChild = show_cursor_position && cursor;
		if (image_slot is not null)
			image_slot.RevealChild = show_image_size && image;
	}

	// Wraps a chip so it slides out to the right, under the zoom controls, instead
	// of blinking out of the bar.
	private static Gtk.Revealer CreateChipSlot (Gtk.Widget chip)
	{
		Gtk.Revealer slot = Gtk.Revealer.New ();
		slot.TransitionType = Gtk.RevealerTransitionType.SlideLeft;
		slot.TransitionDuration = FOOTER_SLIDE_MS;
		slot.RevealChild = true;
		slot.SetChild (chip);
		return slot;
	}

	// A subtle rounded chip background, matching the low-contrast hover chrome
	// used elsewhere in the docked color palette, so each footer label group
	// reads as a distinct pill instead of loose icon+text floating in the bar.
	private static void ApplyStatusBarChipStyle (Gtk.Widget group)
	{
		group.AddCssClass ("statusbar-chip");
	}

	public void CreateStatusBar (Gtk.Box statusbar, WorkspaceManager workspaceManager)
	{
		// Selection widget - top-left coords + size (issue #2116). Sits left of the
		// other chips and never collapses: it's only on screen while a selection is
		// live, and the box takes its width straight out of the palette's allocation,
		// so the cascade starts sooner without it joining in.
		Gtk.Box selection_group = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		selection_group.MarginEnd = 4;
		var selection_icon = Gtk.Image.NewFromIconName (Resources.Icons.ToolSelectRectangle);
		selection_group.Append (selection_icon);
		var selection_size = Gtk.Label.New ("");
		selection_size.Xalign = 0.5f;
		selection_size.Halign = Gtk.Align.Center;
		selection_group.Append (selection_size);
		selection_group.TooltipText = Translations.GetString ("Selection: top-left corner in pixels, then its width and height.");
		ApplyStatusBarChipStyle (selection_group);
		selection_group.SetVisible (false);
		statusbar.Append (selection_group);

		// Hidden until a selection is actually visible; upstream's full-canvas reset
		// selection otherwise made it look like a selection existed when it didn't (PR #2013).
		workspaceManager.SelectionChanged += delegate {
			if (!workspaceManager.HasOpenDocuments || !workspaceManager.ActiveDocument.Selection.Visible) {
				selection_group.SetVisible (false);
				return;
			}
			var bounds = workspaceManager.ActiveDocument.Selection.GetBounds ();
			selection_size.SetText ($"{(int) bounds.X}, {(int) bounds.Y} · {(int) bounds.Width} × {(int) bounds.Height}");
			selection_group.SetVisible (true);
		};

		// Cursor position widget - left aligned with enough space to display coordinates up to tens of thousands (e.g. 10000, 10000).
		Gtk.Box cursor_group = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		cursor_group.MarginEnd = 4;
		cursor_position_icon = Gtk.Image.NewFromIconName (Resources.Icons.CursorPosition);
		cursor_group.Append (cursor_position_icon);
		var cursor = Gtk.Label.New ("0, 0");
		cursor.Xalign = 0.5f;
		cursor.Halign = Gtk.Align.Center;
		cursor.WidthChars = 8;
		cursor_group.Append (cursor);
		cursor_position_label = cursor;
		cursor_group.TooltipText = Translations.GetString ("Pointer position on the canvas, in pixels from the top-left corner.");
		ApplyStatusBarChipStyle (cursor_group);
		cursor_slot = CreateChipSlot (cursor_group);
		statusbar.Append (cursor_slot);
		footer_cursor_group = cursor_group;

		SetStatusBarCursorPositionVisible (PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOW_CURSOR_POSITION, true));

		chrome.LastCanvasCursorPointChanged += delegate {
			var pt = chrome.LastCanvasCursorPoint;
			cursor.SetText ($"{pt.X}, {pt.Y}");
		};

		// Image dimensions widget - "800 × 600 · 4:3" (PR #2013).
		Gtk.Box image_group = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		image_group.MarginStart = 4;
		image_size_icon = Gtk.Image.NewFromIconName (Resources.Icons.ImageResize);
		image_group.Append (image_size_icon);
		var image_size = Gtk.Label.New ("");
		image_size.Xalign = 0.5f;
		image_size.Halign = Gtk.Align.Center;
		image_size.WidthChars = 14;
		image_group.Append (image_size);
		image_size_label = image_size;
		image_group.TooltipText = Translations.GetString ("Canvas size in pixels and its aspect ratio.\nDouble click to change the canvas size.");
		Gtk.GestureClick image_group_click = Gtk.GestureClick.New ();
		image_group_click.OnPressed += (_, args) => {
			if (args.NPress == 2 && Image.CanvasSize.Sensitive)
				Image.CanvasSize.Activate ();
		};
		image_group.AddController (image_group_click);
		ApplyStatusBarChipStyle (image_group);
		image_slot = CreateChipSlot (image_group);
		statusbar.Append (image_slot);
		footer_image_group = image_group;

		SetStatusBarImageSizeVisible (PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOW_IMAGE_SIZE, true));

		void UpdateImageSizeLabel ()
		{
			if (!workspaceManager.HasOpenDocuments) {
				image_size.SetText ("");
				return;
			}
			var size = workspaceManager.ActiveDocument.ImageSize;
			// The label grows past WidthChars on its own for oversized dimensions.
			image_size.SetText ($"{size.Width} × {size.Height} · {GetAspectRatio (size.Width, size.Height)}");
		}

		workspaceManager.ActiveDocumentChanged += delegate { UpdateImageSizeLabel (); };
		workspaceManager.DocumentActivated += (_, args) => {
			args.Document.ImageSizeChanged += delegate { UpdateImageSizeLabel (); };
		};

		// Document zoom widget
		View.CreateStatusBar (statusbar);
	}

	// Simplified aspect ratio, e.g. 800×600 -> "4:3".
	private static string GetAspectRatio (int w, int h)
	{
		if (w == 0 || h == 0) return "";
		int gcd = GCD (w, h);
		return $"{w / gcd}:{h / gcd}";
	}

	private static int GCD (int a, int b)
	{
		while (b != 0) {
			int temp = b;
			b = a % b;
			a = temp;
		}
		return a;
	}

	public void RegisterHandlers ()
	{
		File.RegisterHandlers ();
		Edit.RegisterHandlers ();
		Image.RegisterHandlers ();
		Layers.RegisterHandlers ();
		View.RegisterHandlers ();
		Help.RegisterHandlers ();
	}
}
