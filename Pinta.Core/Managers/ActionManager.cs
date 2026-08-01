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

		AddinActions addins = new ();
		AdjustmentsActions adjustments = new ();
		AppActions app = new ();
		EditActions edit = new (chrome, paletteFormats, palette, tools, workspace);
		EffectsActions effects = new (chrome);
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
		toolbar.Append (Edit.Deselect.CreateToolBarItem ());
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
		header.PackStart (Edit.Deselect.CreateToolBarItem ());
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
		footer_cursor_group?.SetVisible (visible && !cursor_group_hidden);
	}

	public void SetStatusBarImageSizeVisible (bool visible)
	{
		show_image_size = visible;
		image_size_icon?.SetVisible (visible);
		image_size_label?.SetVisible (visible);
		footer_image_group?.SetVisible (visible && !image_group_hidden);
	}

	// The labels collide with the fully expanded color section, not with the
	// action buttons' current position. Keep a small visual gap at the boundary
	// and enough hysteresis to avoid immediately showing a group after it reclaims
	// its own width.
	private const int FOOTER_COLLISION_GAP = 1;
	private const int FOOTER_RESHOW_GAP = 16;

	private Gtk.Widget? footer_cursor_group;
	private Gtk.Widget? footer_image_group;
	private int cursor_group_reclaimed_width;
	private int image_group_reclaimed_width;

	// Latched hide state so the labels don't flicker during the resize: once a
	// group is hidden it stays hidden until the palette regrows well past the hide
	// threshold (see the SHOW constants above).
	private bool cursor_group_hidden;
	private bool image_group_hidden;

	// Called by MainWindow after the palette has laid out its natural full-color
	// boundary and the footer has allocated the label groups.
	public void SetFooterGeometry (int availableWidth, double full_color_section_right)
	{
		bool image_was_hidden = image_group_hidden || !show_image_size;
		int cursor_group_width = footer_cursor_group?.GetWidth () ?? 0;
		if (cursor_group_width > 0)
			cursor_group_reclaimed_width = cursor_group_width + 4;

		int image_group_width = footer_image_group?.GetWidth () ?? 0;
		if (image_group_width > 0)
			image_group_reclaimed_width = image_group_width + 8;

		int image_collision_width = (int) Math.Floor (
			full_color_section_right - cursor_group_reclaimed_width - FOOTER_COLLISION_GAP);
		bool image_is_touching = availableWidth <= image_collision_width;
		bool cursor_is_touching = availableWidth <= full_color_section_right + FOOTER_COLLISION_GAP;

		if (footer_image_group is not null) {
			if (image_is_touching) {
				image_group_reclaimed_width = Math.Max (image_group_reclaimed_width, image_group_width + 8);
				image_group_hidden = true;
			} else if (image_group_hidden && availableWidth > image_collision_width + image_group_reclaimed_width + FOOTER_RESHOW_GAP) {
				image_group_hidden = false;
			}
			footer_image_group.SetVisible (show_image_size && !image_group_hidden);
		}
		if (footer_cursor_group is not null) {
			if (cursor_is_touching && image_was_hidden) {
				cursor_group_reclaimed_width = Math.Max (cursor_group_reclaimed_width, cursor_group_width + 4);
				cursor_group_hidden = true;
			} else if (cursor_group_hidden && availableWidth > full_color_section_right + cursor_group_reclaimed_width + FOOTER_RESHOW_GAP) {
				cursor_group_hidden = false;
			}
			footer_cursor_group.SetVisible (show_cursor_position && !cursor_group_hidden);
		}
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
		// Cursor position widget - left aligned with enough space to display coordinates up to tens of thousands (e.g. 10000, 10000).
		Gtk.Box cursor_group = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		cursor_group.MarginEnd = 4;
		cursor_position_icon = Gtk.Image.NewFromIconName (Resources.Icons.CursorPosition);
		cursor_group.Append (cursor_position_icon);
		var cursor = Gtk.Label.New ("0, 0");
		cursor.Xalign = 0.0f;
		cursor.Halign = Gtk.Align.Start;
		cursor.WidthChars = 11;
		cursor_group.Append (cursor);
		cursor_position_label = cursor;
		ApplyStatusBarChipStyle (cursor_group);
		statusbar.Append (cursor_group);
		footer_cursor_group = cursor_group;

		SetStatusBarCursorPositionVisible (PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOW_CURSOR_POSITION, true));

		chrome.LastCanvasCursorPointChanged += delegate {
			var pt = chrome.LastCanvasCursorPoint;
			cursor.SetText ($"{pt.X}, {pt.Y}");
		};

		// Selection widget - top-left coords + size (issue #2116). Hidden until a
		// selection is actually visible; upstream's full-canvas reset selection
		// otherwise made it look like a selection existed when it didn't (PR #2013).
		var selection_icon = Gtk.Image.NewFromIconName (Resources.Icons.ToolSelectRectangle);
		statusbar.Append (selection_icon);
		var selection_size = Gtk.Label.New ("");
		selection_size.Xalign = 0.0f;
		selection_size.Halign = Gtk.Align.Start;
		selection_size.WidthChars = 20;
		statusbar.Append (selection_size);

		selection_icon.SetVisible (false);
		selection_size.SetVisible (false);

		workspaceManager.SelectionChanged += delegate {
			if (!workspaceManager.HasOpenDocuments || !workspaceManager.ActiveDocument.Selection.Visible) {
				selection_icon.SetVisible (false);
				selection_size.SetVisible (false);
				return;
			}
			var bounds = workspaceManager.ActiveDocument.Selection.GetBounds ();
			selection_size.SetText ($"{(int) bounds.X}, {(int) bounds.Y} · {(int) bounds.Width} × {(int) bounds.Height}");
			selection_icon.SetVisible (true);
			selection_size.SetVisible (true);
		};

		// Image dimensions widget - "800 × 600 · 4:3" (PR #2013).
		Gtk.Box image_group = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		image_group.MarginStart = 4;
		image_size_icon = Gtk.Image.NewFromIconName (Resources.Icons.ImageResize);
		image_group.Append (image_size_icon);
		var image_size = Gtk.Label.New ("");
		image_size.Xalign = 0.0f;
		image_size.Halign = Gtk.Align.Start;
		image_size.WidthChars = 16;
		image_group.Append (image_size);
		image_size_label = image_size;
		ApplyStatusBarChipStyle (image_group);
		statusbar.Append (image_group);
		footer_image_group = image_group;

		SetStatusBarImageSizeVisible (PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOW_IMAGE_SIZE, true));

		void UpdateImageSizeLabel ()
		{
			if (!workspaceManager.HasOpenDocuments) {
				image_size.SetText ("");
				return;
			}
			var size = workspaceManager.ActiveDocument.ImageSize;
			string text = $"{size.Width} × {size.Height} · {GetAspectRatio (size.Width, size.Height)}";
			if (text.Length > image_size.WidthChars)
				image_size.WidthChars = text.Length;
			image_size.SetText (text);
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
