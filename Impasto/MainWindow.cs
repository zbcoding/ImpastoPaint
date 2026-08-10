//
// MainWindow.cs
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
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Mono.Addins;
using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;
using Pinta.Resources;

namespace Pinta;

internal sealed class MainWindow
{
	readonly Adw.Application app;
	// NRT - Created in OnActivated
	WindowShell window_shell = null!;
	Gtk.Window colors_window = null!; // Impasto: floating Colors palette.
	Gtk.Box colors_dock = null!;      // Impasto: its home in the status bar - the bar lives here always.
	Gtk.Box colors_contents = null!;  // Impasto: holds the live picker panel when floating.
	StatusBarColorPaletteWidget colors_palette = null!;
	ColorWheelWidget colors_wheel = null!; // Impasto: shown in the docked popover when bar sections fold.
	Gtk.Popover colors_wheel_popover = null!;
	Gtk.Box colors_popover_box = null!;    // Impasto: colors_wheel + folded-in mini sections, docked popover only.
	ColorPickerPanel colors_picker_panel = null!; // Impasto: live picker shown in the floating Colors window.
	ToolBoxWidget toolbox = null!; // Impasto: needed to persist pinned tools.
	Dock dock = null!;
	Gio.Menu menu_bar = null!;
	Gio.Menu image_menu = null!;
	Gio.Menu view_menu = null!;

	CanvasPad canvas_pad = null!;

	private int main_thread_id = -1;
	private Gtk.DropTarget drop_target = null!;

	public MainWindow (Adw.Application app)
	{
		this.app = app;

		// Set the human-readable application name, used by e.g. gtk_recent_manager_add_item().
		GLib.Functions.SetApplicationName (Translations.GetString ("Impasto"));
	}

	/// <summary>
	/// Performs any initialization that is not related to showing a new
	/// window (i.e. the Activate () method).
	/// In particular, the application menu must be initialized here in order to
	/// appear in the application window later (Gtk.Application.Menubar).
	/// </summary>
	public void Startup ()
	{
		CreateMainMenu ();
	}

	public void Activate ()
	{
		// Build our window
		CreateWindow ();

		// Initialize interface things
		_ = new ActionHandlers ();

		PintaCore.Chrome.InitializeProgessDialog (ProgressDialog.New (PintaCore.Chrome));
		PintaCore.Chrome.InitializeErrorDialogHandler (ErrorDialog.ShowError);
		PintaCore.Chrome.InitializeMessageDialog (ErrorDialog.ShowMessage);
		PintaCore.Chrome.InitializeSimpleEffectDialog (SimpleEffectDialog.Launch);

		PintaCore.Initialize ();

		// Initialize extensions
		string addins_dir = System.IO.Path.Combine (PintaCore.Settings.GetUserSettingsDirectory (), "addins");
		AddinManager.Initialize (addins_dir);

		AddinManager.Registry.Update ();
		var setupService = new AddinSetupService (AddinManager.Registry);
		if (!setupService.AreRepositoriesRegistered ())
			setupService.RegisterRepositories (true);

		// Look out for any changes in extensions
		main_thread_id = Environment.CurrentManagedThreadId;
		AddinManager.AddExtensionNodeHandler (typeof (IExtension), OnExtensionChanged);

		// Load the user's previous settings
		LoadUserSettings ();
		PintaCore.Actions.App.BeforeQuit += delegate { SaveUserSettings (); };

		// We support drag and drop for URIs, which are converted into a Gdk.FileList.
		drop_target = Gtk.DropTarget.New (Gdk.FileList.GetGType (), Gdk.DragAction.Copy);
		drop_target.OnDrop += HandleDrop;
		window_shell.Window.AddController (drop_target);

		// Handle a few main window specific actions
		window_shell.Window.OnCloseRequest += HandleCloseRequest;

		// Add custom key handling during the capture phase. For example, this ensures that the "-" key
		// can be typed into the dash pattern box instead of being handled as a shortcut
		var key_controller = Gtk.EventControllerKey.New ();
		key_controller.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		key_controller.OnKeyPressed += HandleGlobalKeyPress;
		key_controller.OnKeyReleased += HandleGlobalKeyRelease;
		window_shell.Window.AddController (key_controller);

		// TODO: These need to be [re]moved when we redo zoom support
		PintaCore.Actions.View.ZoomToWindow.Activated += ZoomToWindow_Activated;
		PintaCore.Actions.View.ZoomToSelection.Activated += ZoomToSelection_Activated;

		PintaCore.Workspace.ActiveDocumentChanged += ActiveDocumentChanged;

		PintaCore.Workspace.DocumentActivated += Workspace_DocumentCreated;
		PintaCore.Workspace.DocumentClosed += Workspace_DocumentClosed;

		DockNotebook notebook = canvas_pad.Notebook;
		notebook.TabClosed += DockNotebook_TabClosed;
		notebook.ActiveTabChanged += DockNotebook_ActiveTabChanged;
	}

	private void Workspace_DocumentClosed (object? sender, DocumentEventArgs e)
	{
		var tab = FindTabWithCanvas ((CanvasWindow) e.Document.Workspace.CanvasWindow);

		if (tab != null)
			canvas_pad.Notebook.RemoveTab (tab);
	}

	private void DockNotebook_TabClosed (object? sender, TabClosedEventArgs e)
	{
		var view = (DocumentViewContent) e.Item;

		int index = PintaCore.Workspace.OpenDocuments.IndexOf (view.Document);
		if (index < 0)
			return;

		PintaCore.Workspace.SetActiveDocument (index);
		PintaCore.Actions.File.Close.Activate ();

		if (PintaCore.Workspace.OpenDocuments.IndexOf (view.Document) < 0)
			return;

		// User must have canceled the close
		e.Cancel = true;
	}

	private void DockNotebook_ActiveTabChanged (object? sender, EventArgs e)
	{
		var item = canvas_pad.Notebook.ActiveItem;

		if (item == null)
			return;

		var view = (DocumentViewContent) item;

		int index = PintaCore.Workspace.OpenDocuments.IndexOf (view.Document);
		PintaCore.Workspace.SetActiveDocument (index);
		((CanvasWindow) view.Widget).Canvas.Cursor = PintaCore.Tools.CurrentTool?.CurrentCursor;
	}

	private static Cairo.Color? GetCanvasSurroundColor ()
		=> Cairo.Color.FromHex (
			PintaCore.Settings.GetSetting (SettingNames.CANVAS_SURROUND_COLOR, SettingNames.DEFAULT_CANVAS_SURROUND_COLOR));

	private void Workspace_DocumentCreated (object? sender, DocumentEventArgs e)
	{
		var doc = e.Document;

		var notebook = canvas_pad.Notebook;
		int selected_index = notebook.ActiveItemIndex;

		CanvasWindow canvas = CanvasWindow.New (
			PintaCore.Chrome,
			PintaCore.Tools,
			doc,
			PintaCore.CanvasGrid,
			GetCanvasSurroundColor ());
		canvas.RulersVisible = PintaCore.Actions.View.Rulers.Value;
		canvas.RulerMetric = GetCurrentRulerMetric ();
		PintaCore.CanvasGrid.RulerMetric = (int) GetCurrentRulerMetric ();
		doc.Workspace.CanvasWindow = canvas;
		doc.Workspace.Canvas = canvas.Canvas;

		DocumentViewContent my_content = new (doc, canvas);

		// Insert our tab to the right of the currently selected tab
		notebook.InsertTab (my_content, selected_index + 1);

		// Zoom to window only on first show (if we do it always, it will be called on every resize)
		// Note: this does seem to allow a small flicker where large images are shown at 100% zoom before
		// zooming out (Bug 1959673)
		// If the canvas is turned into a custom Gtk.Widget subclass in the future, this could
		// perhaps be done on the first measurement request

		bool canvasHasBeenShown = false;

		Gtk.Viewport view = (Gtk.Viewport) doc.Workspace.Canvas.Parent!;
		view.Hadjustment!.OnChanged += (o, e2) => {
			if (canvasHasBeenShown)
				return;

			GLib.Functions.IdleAdd (
				0,
				() => {
					ZoomToWindow_Activated (o, e);
					PintaCore.Workspace.Invalidate ();
					return false;
				}
			);

			canvasHasBeenShown = true;
		};

		// Impasto: while the zoom is "Window" the fit has to be recomputed as the window is
		// resized - upstream fit once and never again, so shrinking the window left the canvas
		// (and the scale-derived brush cursor) at its old size, with scrollbars appearing.
		//
		// The viewport's own size is what changed, and it shows up as the adjustments' page
		// size. ::changed is not reliable here (a re-fit moves the adjustment too, so it can't
		// be told apart from a resize); notify::page-size is the size itself.
		//
		// The re-fit runs on an idle below GTK's resize idle (GTK_PRIORITY_RESIZE, 110), since
		// it measures the viewport's allocation: measuring first sees the pre-resize width,
		// which oversizes the view and puts strokes left of the cursor.
		bool refitQueued = false;
		void QueueRefitToWindow ()
		{
			if (refitQueued || !canvasHasBeenShown || !PintaCore.Actions.View.ZoomToWindowActivated)
				return;

			refitQueued = true;
			GLib.Functions.IdleAdd (
				200,
				() => {
					refitQueued = false;
					ZoomToWindow_Activated (view, EventArgs.Empty);
					PintaCore.Workspace.Invalidate ();
					return false;
				}
			);
		}

		void WatchPageSize (Gtk.Adjustment adjustment)
			=> adjustment.OnNotify += (_, args) => {
				if (args.Pspec.GetName () == "page-size")
					QueueRefitToWindow ();
			};

		WatchPageSize (view.Hadjustment!);
		WatchPageSize (view.Vadjustment!);

		PintaCore.Actions.View.Rulers.Toggled += (active, _) => { canvas.RulersVisible = active; };
		PintaCore.Actions.View.RulerMetric.OnActivate += (o, args) => {
			PintaCore.Actions.View.RulerMetric.ChangeState (args.Parameter!);
			canvas.RulerMetric = GetCurrentRulerMetric ();
			PintaCore.CanvasGrid.RulerMetric = (int) GetCurrentRulerMetric ();
		};
	}

	private static MetricType GetCurrentRulerMetric ()
	{
		GLib.Variant state = PintaCore.Actions.View.RulerMetric.GetState () ??
			throw new InvalidOperationException ("action should not be stateless");

		return (MetricType) state.GetInt32 ();
	}

	private bool HandleGlobalKeyPress (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyPressedSignalArgs args)
	{
		// Give the widget that has focus a first shot at handling the event.
		// Otherwise, key presses may be intercepted by shortcuts for menu items.
		if (SendToFocusWidget (controller))
			return true;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		if (PintaCore.Workspace.HasOpenDocuments) {
			var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

			if ((canvas_window.HasFocus || canvas_window.IsMouseOnCanvas) &&
				 canvas_window.DoKeyPressEvent (controller, args)) {
				return true;
			}
		}

		// If the canvas/tool didn't consume it, see if its a toolbox shortcut
		if (PintaCore.Tools.SetCurrentTool (new KeyGesture (args.GetKey (), args.State)))
			return true;

		// Finally, see if the palette widget wants it.
		bool shouldSwapColors = new KeyGesture (args.GetKey (), args.State) ==
			PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.SwapColors);

		if (shouldSwapColors) {
			PintaCore.Palette.SwapColors ();
			return true;
		}

		// We return 'false' to indicate that nobody consumed it
		return false;
	}

	private void HandleGlobalKeyRelease (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyReleasedSignalArgs args)
	{
		if (SendToFocusWidget (controller) || !PintaCore.Workspace.HasOpenDocuments)
			return;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

		if (canvas_window.HasFocus || canvas_window.IsMouseOnCanvas)
			canvas_window.DoKeyReleaseEvent (controller, args);
	}

	private bool SendToFocusWidget (Gtk.EventControllerKey key_controller)
	{
		var widget = window_shell.Window.FocusWidget;
		if (widget != null && key_controller.Forward (widget)) return true;
		return false;
	}

	// Called when an extension node is added or removed
	// Note this may be called from any thread, not just the main UI thread!
	private void OnExtensionChanged (object s, ExtensionNodeEventArgs args)
	{
		// Run synchronously if invoked from the main thread, e.g. when loading
		// addins at startup we require them to be immediately loaded.
		if (Environment.CurrentManagedThreadId == main_thread_id)
			UpdateExtension (args);
		else {
			// Otherwise, schedule the addin to be loaded/unloaded from the main thread
			// in case it touches the UI, e.g. to update menu items.
			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
				UpdateExtension (args);
				return false;
			});
		}
	}

	private static void UpdateExtension (ExtensionNodeEventArgs args)
	{
		if (args.Change == ExtensionChange.Add) {
			try {
				IExtension extension = (IExtension) args.ExtensionObject;
				extension.Initialize ();
			} catch (Exception e) {
				// Translators: {0} is the name of an add-in.
				string body = Translations.GetString ("The '{0}' add-in may not be compatible with this version of Impasto", args.ExtensionNode.Addin.Id);
				_ = PintaCore.Chrome.ShowErrorDialog (
					PintaCore.Chrome.MainWindow,
					Translations.GetString ("Failed to initialize add-in"),
					body, e.ToString ());
			}
		} else {
			IExtension extension = (IExtension) args.ExtensionObject;
			extension.Uninitialize ();
		}
	}

	private void CreateWindow ()
	{
		// Check for stored window settings
		int width = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_WIDTH, 1100);
		int height = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_HEIGHT, 750);
		bool maximize = PintaCore.Settings.GetSetting (SettingNames.WINDOW_MAXIMIZED, false);

		ResourceLoader.LoadCssStyles ();

		window_shell = new WindowShell (
			app,
			"Impasto.GenericWindow",
			"Impasto",
			width,
			height,
			useMenuBar: IsUsingMenuBar (),
			maximize);

		CreateMainToolBar ();
		CreateToolToolBar ();

		CreatePanels ();
		CreateStatusBar ();
		CreateColorsWindow ();

		app.AddWindow (window_shell.Window);

		PintaCore.Chrome.InitializeApplication (app);
		PintaCore.Chrome.InitializeWindowShell (window_shell.Window);
	}

	private bool IsUsingMenuBar ()
	{
		// Impasto: a real menu bar is the default everywhere, matching Paint.NET.
		// (Upstream Pinta defaults to the hamburger/header bar except on macOS.)
		const bool use_menubar_default = true;

		return PintaCore.Settings.GetSetting (SettingNames.MENUBAR_SHOWN, use_menubar_default);
	}

	private void CreateMainMenu ()
	{
		bool usingMenuBar = IsUsingMenuBar ();
		bool isMac = PintaCore.System.OperatingSystem == OS.Mac;

		// When using a header bar, the View, Image, Effects, and Adjustments menus
		// are shown as menu buttons in the toolbar (see CreateMainToolBar ())

		Gio.Menu fileMenu = Gio.Menu.New ();
		Gio.Menu editMenu = Gio.Menu.New ();
		Gio.Menu imageMenu = Gio.Menu.New ();
		Gio.Menu adjustmentsMenu = Gio.Menu.New ();
		Gio.Menu effectsMenu = Gio.Menu.New ();
		Gio.Menu addinsMenu = Gio.Menu.New ();
		Gio.Menu windowMenu = Gio.Menu.New ();
		Gio.Menu helpMenu = Gio.Menu.New ();
		Gio.Menu padSection = Gio.Menu.New ();

		Gio.Menu viewMenu = Gio.Menu.New ();
		viewMenu.AppendSection (null, padSection);

		Gio.Menu menuBar = Gio.Menu.New ();
		menuBar.AppendSubmenu (Translations.GetString ("_File"), fileMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Edit"), editMenu);
		if (usingMenuBar) {
			menuBar.AppendSubmenu (Translations.GetString ("_View"), viewMenu);
			menuBar.AppendSubmenu (Translations.GetString ("_Image"), imageMenu);
			menuBar.AppendSubmenu (Translations.GetString ("_Adjustments"), adjustmentsMenu);
			menuBar.AppendSubmenu (Translations.GetString ("Effe_cts"), effectsMenu);
		}
		menuBar.AppendSubmenu (Translations.GetString ("A_dd-ins"), addinsMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Window"), windowMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Help"), helpMenu);

		// --- Global initializations

		if (usingMenuBar)
			app.Menubar = menuBar;

		if (isMac) {
			// Since GTK 4.14 there is an autogenerated Application menu, so we just need
			// to register actions with matching names for About, Quit, etc
			// https://gitlab.gnome.org/GNOME/gtk/-/issues/6762
			PintaCore.Actions.App.RegisterActions (app);
		}
		PintaCore.Actions.File.RegisterActions (app, fileMenu);
		PintaCore.Actions.Edit.RegisterActions (app, editMenu);
		PintaCore.Actions.View.RegisterActions (app, viewMenu);
		PintaCore.Actions.Image.RegisterActions (app, imageMenu);
		PintaCore.Actions.Layers.RegisterActions (app);
		PintaCore.Actions.Addins.RegisterActions (app, addinsMenu);
		PintaCore.Actions.Window.RegisterActions (app, windowMenu);
		PintaCore.Actions.Help.RegisterActions (app, helpMenu);

		PintaCore.Chrome.InitializeMainMenu (adjustmentsMenu, effectsMenu);

		// --- References to keep

		image_menu = imageMenu;
		menu_bar = menuBar;
		view_menu = viewMenu;
	}

	private void CreateMainToolBar ()
	{
		// Impasto: paste toolbar button also documents the secondary paste shortcut,
		// whose destination flips with the "paste external images to new layer" setting.
		string pasteAlternateDescription = PintaCore.Settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, false)
			? Translations.GetString ("Paste to Active Layer")
			: Translations.GetString ("Paste to New Layer");

		if (window_shell.HeaderBar is not null) {
			var headerBar = window_shell.HeaderBar;
			headerBar.PackEnd (GtkExtensions.CreateMenuButton (
				this.menu_bar,
				Resources.StandardIcons.OpenMenu,
				Translations.GetString ("Main Menu")));

			headerBar.PackEnd (GtkExtensions.CreateMenuButton (
				PintaCore.Chrome.EffectsMenu,
				Resources.Icons.EffectsDefault,
				Translations.GetString ("Effects")));

			headerBar.PackEnd (GtkExtensions.CreateMenuButton (
				PintaCore.Chrome.AdjustmentsMenu,
				Resources.Icons.AdjustmentsDefault,
				Translations.GetString ("Adjustments")));

			headerBar.PackEnd (GtkExtensions.CreateMenuButton (
				this.image_menu,
				Resources.StandardIcons.ImageGeneric,
				Translations.GetString ("Image")));

			headerBar.PackEnd (GtkExtensions.CreateMenuButton (
				this.view_menu,
				Resources.StandardIcons.ViewReveal,
				Translations.GetString ("View")));

			PintaCore.Actions.CreateHeaderToolBar (headerBar, pasteAlternateDescription);
		} else {
			var main_toolbar = window_shell.CreateToolBar ("main_toolbar");
			PintaCore.Chrome.InitializeMainToolBar (main_toolbar);
			// Impasto: let users hide the quick access icon toolbar (Preferences dialog).
			main_toolbar.Visible = PintaCore.Settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true);
			PintaCore.Actions.CreateToolBar (main_toolbar, pasteAlternateDescription);
		}
	}

	private void CreateToolToolBar ()
	{
		Gtk.Box tool_toolbar = window_shell.CreateToolBar ("tool_toolbar");
		tool_toolbar.HeightRequest = 42;

		PintaCore.Tools.WrapToolBarRows = PintaCore.Settings.GetSetting (SettingNames.TOOL_SETTINGS_WRAP_ROWS, true);

		PintaCore.Chrome.InitializeToolToolBar (tool_toolbar);
	}

	private void CreateStatusBar ()
	{
		Gtk.Box statusbar = window_shell.CreateStatusBar ("statusbar");

		colors_dock = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		colors_dock.Hexpand = true;
		colors_dock.Halign = Gtk.Align.Fill;
		statusbar.Append (colors_dock);

		PintaCore.Actions.CreateStatusBar (statusbar, PintaCore.Workspace);

		PintaCore.Chrome.InitializeStatusBar (statusbar);
	}

	private void CreateColorsWindow ()
	{
		colors_palette = StatusBarColorPaletteWidget.New (
			PintaCore.Chrome,
			PintaCore.Palette,
			PintaCore.System);
		colors_palette.Hexpand = true;
		colors_palette.Halign = Gtk.Align.Fill;
		// Impasto: the palette runs one collapse cascade for the whole footer - the
		// status bar supplies the budget and reports back which chips survived it.
		colors_palette.GetFooterMetrics = () => {
			var (occupied, cursor, image, sliding) = PintaCore.Actions.GetFooterChipRoom ();
			return new StatusBarColorPaletteWidget.FooterMetrics (occupied, cursor, image, sliding);
		};
		colors_palette.ChipVisibilityChanged += (_, chips) => PintaCore.Actions.SetFooterChipsVisible (chips.cursor, chips.image);

		// The bar lives in the dock permanently now - floating no longer reparents it,
		// the floating window gets its own live picker panel instead (below).
		colors_dock.Append (colors_palette);

		colors_wheel = ColorWheelWidget.New (PintaCore.Palette);
		colors_wheel.MarginStart = 6;
		colors_wheel.MarginEnd = 6;

		// Impasto: holds colors_wheel plus whichever bar sections have folded out
		// of the docked palette (quick colors, recent colors, primary/secondary) -
		// rebuilt on demand right before the popover opens (see RebuildColorPopoverSections).
		colors_popover_box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		colors_popover_box.Append (colors_wheel);

		// Impasto: the floating window's content - a persistent live picker replicating
		// ColorPickerDialog's "Choose Colors" UI (wheel/square surface, sliders, hex,
		// swap display) plus recent/quick swatch rows, with no OK/Cancel and no dialog
		// chrome; every change writes straight to the palette.
		colors_picker_panel = ColorPickerPanel.New (PintaCore.Palette);
		colors_picker_panel.MarginTop = 6;
		colors_picker_panel.MarginBottom = 6;
		colors_picker_panel.MarginStart = 6;
		colors_picker_panel.MarginEnd = 6;

		colors_contents = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		colors_contents.Append (colors_picker_panel);

		colors_wheel_popover = Gtk.Popover.New ();
		colors_wheel_popover.Autohide = true;
		colors_wheel_popover.Position = Gtk.PositionType.Top;
		colors_wheel_popover.SetParent (colors_palette);
		colors_wheel_popover.Child = colors_popover_box;
		colors_palette.ColorWheelClicked += (_, _) => {
			RebuildColorPopoverSections ();
			RectangleD r = colors_palette.ColorWheelButtonRect;
			colors_wheel_popover.PointingTo = new Gdk.Rectangle {
				X = (int) r.X,
				Y = (int) r.Y,
				Width = (int) r.Width,
				Height = (int) r.Height,
			};
			colors_wheel_popover.Popup ();
		};
		// Defer to idle: floating reparents colors_palette itself, which must not happen
		// while GTK is still dispatching the click on that same widget.
		colors_palette.FloatColorsClicked += (_, _) => GLib.Functions.IdleAdd (
			GLib.Constants.PRIORITY_DEFAULT_IDLE,
			() => {
				// Call directly - the Toggled event only fires on value *changes*,
				// so a stale value would otherwise leave the button dead.
				PintaCore.Actions.View.ColorsFloating.Value = true;
				SetColorsFloating (true);
				return false;
			});

		BuildColorsWindow ();

		// Right-click the float button for a "Reset window" popover, which re-centers the
		// floating window on the app at its default size.
		Gtk.Popover reset_popover = Gtk.Popover.New ();
		reset_popover.Autohide = true;
		reset_popover.Position = Gtk.PositionType.Top;
		reset_popover.SetParent (colors_palette);
		Gtk.Button reset_button = Gtk.Button.NewWithLabel (Translations.GetString ("Reset window"));
		reset_button.OnClicked += (_, _) => {
			reset_popover.Popdown ();
			ResetColorsWindow ();
		};
		reset_popover.SetChild (reset_button);
		colors_palette.ResetColorWindowClicked += (_, _) => {
			RectangleD r = colors_palette.FloatColorsButtonRect;
			reset_popover.PointingTo = new Gdk.Rectangle {
				X = (int) r.X,
				Y = (int) r.Y,
				Width = (int) r.Width,
				Height = (int) r.Height,
			};
			reset_popover.Popup ();
		};

		PintaCore.Actions.View.Colors.Toggled += (_, _) => UpdateColorsVisibility ();
		PintaCore.Actions.View.ColorsFloating.Toggled += (value, _) => SetColorsFloating (value);

		SetColorsFloating (PintaCore.Actions.View.ColorsFloating.Value);
	}

	// GTK4/Wayland forbids an app from reading or setting its own window position, so we can't
	// restore the exact spot the user dragged the floating window to (even onto another
	// monitor). The best we can do is give the window manager a fresh transient window each
	// time it opens, which the WM places centered on Impasto - a "best guess" back inside the
	// app. That is what BuildColorsWindow + RebuildColorsWindow provide.
	private void BuildColorsWindow ()
	{
		colors_window = Gtk.Window.New ();
		colors_window.Title = Translations.GetString ("Colors");
		colors_window.TransientFor = window_shell.Window;
		colors_window.DestroyWithParent = true;
		colors_window.Resizable = true;
		colors_window.DefaultWidth = 480;
		// Set the child once, like the original working floating window; the same
		// colors_contents (holding the live picker panel) is reused across rebuilds.
		colors_window.SetChild (colors_contents);

		// Only a close button - closing the floating window re-docks the colors.
		Gtk.HeaderBar colors_header = Gtk.HeaderBar.New ();
		colors_header.DecorationLayout = ":close";
		colors_window.Titlebar = colors_header;

		colors_window.OnCloseRequest += (_, _) => {
			PintaCore.Actions.View.ColorsFloating.Value = false;
			SetColorsFloating (false);
			return true;
		};
	}

	// Destroy and recreate the window so the WM re-places the fresh transient centered on
	// Impasto. Callers re-present it afterwards.
	private void RebuildColorsWindow ()
	{
		// Detach the shared contents first so destroying the window doesn't take them with it.
		if (colors_window.Child == colors_contents)
			colors_window.Child = null;
		colors_window.Destroy ();

		BuildColorsWindow ();
	}

	private void ResetColorsWindow ()
	{
		RebuildColorsWindow ();
		PintaCore.Actions.View.ColorsFloating.Value = true;
		SetColorsFloating (true);
	}

	// The bar (colors_palette) and its docked popover (colors_wheel) never leave the
	// dock any more - the floating window has its own live picker panel (colors_picker_panel,
	// inside colors_contents) instead, so there is nothing left to reparent here.
	private void SetColorsFloating (bool floating) => UpdateColorsVisibility ();

	private void UpdateColorsVisibility ()
	{
		bool shown = PintaCore.Actions.View.Colors.Value;
		bool floating = PintaCore.Actions.View.ColorsFloating.Value;

		if (!shown || floating)
			colors_wheel_popover.Popdown ();

		colors_dock.Visible = shown && !floating;
		colors_contents.Visible = shown && floating;

		if (shown && floating) {
			// Only rebuild when actually (re)opening - a fresh transient window is what makes
			// the WM place it back inside Impasto, since we can't restore its last position.
			if (!colors_window.Visible) {
				RebuildColorsWindow ();
				colors_window.Present ();
			}
		} else {
			colors_window.Hide ();
		}
	}

	// Impasto: mirrors whatever has folded out of the docked palette bar
	// (StatusBarColorPaletteWidget.QuickColorsFolded etc.) into the color-wheel
	// popover, in the same order it left the bar - quick colors, then recent
	// colors, then primary/secondary. Rebuilt fresh each time the popover is about
	// to open, so it's always in sync without needing to track every color-change
	// event separately.
	Gtk.Widget? popover_quick_section;
	Gtk.Widget? popover_recent_section;
	Gtk.Widget? popover_swatches_section;

	private void RebuildColorPopoverSections ()
	{
		if (popover_quick_section is not null) {
			colors_popover_box.Remove (popover_quick_section);
			popover_quick_section = null;
		}
		if (popover_recent_section is not null) {
			colors_popover_box.Remove (popover_recent_section);
			popover_recent_section = null;
		}
		if (popover_swatches_section is not null) {
			colors_popover_box.Remove (popover_swatches_section);
			popover_swatches_section = null;
		}

		if (colors_palette.QuickColorsFolded) {
			int rows = PaletteHelper.GetPaletteRowCount ();
			popover_quick_section = BuildMiniSwatchGrid (
				PintaCore.Palette.CurrentPalette.Colors,
				rows,
				recentOrder: false);
			colors_popover_box.Append (popover_quick_section);
		}

		if (colors_palette.RecentColorsFolded) {
			int rows = PaletteHelper.GetPaletteRowCount ();
			int count = Math.Min (PintaCore.Palette.RecentlyUsedColors.Count, PintaCore.Palette.MaxRecentlyUsedColor);
			popover_recent_section = BuildMiniSwatchGrid (
				PintaCore.Palette.RecentlyUsedColors.Take (count).ToList (),
				rows,
				recentOrder: true);
			colors_popover_box.Append (popover_recent_section);
		}

		if (colors_palette.SwatchesFolded) {
			popover_swatches_section = BuildPrimarySecondaryMiniSection ();
			colors_popover_box.Append (popover_swatches_section);
		}
	}

	// Left click sets the primary color, right click the secondary - same
	// semantics as the docked bar's quick/recent swatches (see
	// StatusBarColorPaletteWidget's WidgetElement.Palette / RecentColorsPalette
	// click handling).
	// The popover is tighter than the floating Colors panel, so wrap into extra
	// row-bands below once this many columns is reached, rather than growing wider.
	private const int MINI_SWATCH_MAX_COLUMNS = 8;

	private static Gtk.Widget BuildMiniSwatchGrid (IReadOnlyList<Cairo.Color> colors, int rows, bool recentOrder)
	{
		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = 1;
		grid.ColumnSpacing = 1;

		int naturalColumns = Math.Max (1, (colors.Count + rows - 1) / rows);
		int columns = Math.Min (naturalColumns, MINI_SWATCH_MAX_COLUMNS);

		for (int i = 0; i < colors.Count; i++) {
			Cairo.Color color = colors[i];

			int row, col;
			if (recentOrder) {
				// Row-major fill: capping the column count alone already wraps
				// correctly into extra rows below.
				row = i / columns;
				col = i % columns;
			} else {
				// Column-major fill: fold columns beyond the cap into extra
				// row-bands stacked below the first `rows` rows.
				int naturalRow = i % rows;
				int naturalCol = i / rows;
				int band = naturalCol / columns;
				row = naturalRow + band * rows;
				col = naturalCol % columns;
			}

			grid.Attach (
				BuildMiniSwatchButton (
					color,
					size: 14,
					onPrimary: () => PintaCore.Palette.SetColor (true, color, false),
					onSecondary: () => PintaCore.Palette.SetColor (false, color, false)),
				col, row, 1, 1);
		}

		return grid;
	}

	private Gtk.Widget BuildPrimarySecondaryMiniSection ()
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);

		box.Append (BuildMiniSwatchButton (
			PintaCore.Palette.PrimaryColor,
			size: 20,
			onPrimary: () => MiniPickColor (primarySelected: true),
			onSecondary: () => MiniPickColor (primarySelected: true)));
		box.Append (BuildMiniSwatchButton (
			PintaCore.Palette.SecondaryColor,
			size: 20,
			onPrimary: () => MiniPickColor (primarySelected: false),
			onSecondary: () => MiniPickColor (primarySelected: false)));

		Gtk.Button swap_button = Gtk.Button.NewWithLabel ("⇄");
		swap_button.TooltipText = Translations.GetString ("Click to switch between primary and secondary color.");
		swap_button.OnClicked += (_, _) => {
			Cairo.Color temp = PintaCore.Palette.PrimaryColor;
			PintaCore.Palette.SetColor (true, PintaCore.Palette.SecondaryColor, false);
			PintaCore.Palette.SetColor (false, temp, false);
		};
		box.Append (swap_button);

		Gtk.Button reset_button = Gtk.Button.NewWithLabel ("↺");
		reset_button.TooltipText = Translations.GetString ("Click to reset primary and secondary color.");
		reset_button.OnClicked += (_, _) => {
			PintaCore.Palette.PrimaryColor = new Cairo.Color (0, 0, 0);
			PintaCore.Palette.SecondaryColor = new Cairo.Color (1, 1, 1);
		};
		box.Append (reset_button);

		return box;
	}

	// Reuses the same color picker dialog as the bar's primary/secondary swatches
	// (StatusBarColorPaletteWidget.PickColorsAsync), rather than duplicating its setup.
	private async void MiniPickColor (bool primarySelected)
	{
		PaletteColors? choices = await colors_palette.PickColorsAsync (primarySelected);
		if (choices is null)
			return;
		if (PintaCore.Palette.PrimaryColor != choices.Primary)
			PintaCore.Palette.PrimaryColor = choices.Primary;
		if (PintaCore.Palette.SecondaryColor != choices.Secondary)
			PintaCore.Palette.SecondaryColor = choices.Secondary;
	}

	private static Gtk.Button BuildMiniSwatchButton (Cairo.Color color, int size, Action onPrimary, Action onSecondary)
	{
		Gtk.Button button = Gtk.Button.New ();
		button.WidthRequest = size;
		button.HeightRequest = size;

		Gtk.CssProvider provider = Gtk.CssProvider.New ();
		provider.LoadFromString ($"button {{ background-color: #{color.ToHex (addAlpha: false)}; min-width: {size}px; min-height: {size}px; padding: 0; }}");
		button.GetStyleContext ().AddProvider (provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);

		button.OnClicked += (_, _) => onPrimary ();

		Gtk.GestureClick right_click = Gtk.GestureClick.New ();
		right_click.SetButton (GtkExtensions.MOUSE_RIGHT_BUTTON);
		right_click.OnPressed += (_, _) => onSecondary ();
		button.AddController (right_click);

		return button;
	}

	private void CreatePanels ()
	{
		Gtk.Box panel_container = window_shell.CreateWorkspace ();
		CreateDockAndPads (panel_container);
	}

	private void CreateDockAndPads (Gtk.Box container)
	{
		ToolBoxWidget toolbox = ToolBoxWidget.New (PintaCore.Tools, PintaCore.Settings.GetSetting (SettingNames.TOOLBOX_CLASSIC_LAYOUT, false));
		this.toolbox = toolbox;

		Gtk.ScrolledWindow toolbox_scroll = Gtk.ScrolledWindow.New ();
		toolbox_scroll.Child = toolbox;
		toolbox_scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		// Impasto: a short window leaves tools below the fold - scroll them into reach instead
		// of clipping them. Never was set here for the sectioned layout, which made the scroller
		// demand the toolbox's full height; a window shorter than that squeezed the sections
		// down to slivers instead of letting them scroll.
		toolbox_scroll.VscrollbarPolicy = Gtk.PolicyType.Automatic;
		// Impasto: a border around the toolbox so its icons read as tools, not as loose
		// icons next to the canvas.
		toolbox_scroll.HasFrame = true;
		toolbox_scroll.MarginTop = 3;
		toolbox_scroll.MarginBottom = 3;
		toolbox_scroll.MarginStart = 3;
		toolbox_scroll.MarginEnd = 3;
		toolbox_scroll.OverlayScrolling = true;
		toolbox_scroll.WindowPlacement = Gtk.CornerType.BottomRight;

		container.Append (toolbox_scroll);
		PintaCore.Chrome.InitializeToolBox (toolbox);

		// Dock widget
		dock = Dock.New ();
		dock.Hexpand = true;
		dock.Halign = Gtk.Align.Fill;
		PintaCore.Chrome.InitializeDock (dock);

		// Canvas pad
		canvas_pad = new CanvasPad ();
		canvas_pad.Initialize (dock);
		PintaCore.Chrome.InitializeImageTabsNotebook (canvas_pad.Notebook);

		// Layer pad
		LayersPad layers_pad = new (PintaCore.Actions.Layers);
		layers_pad.Initialize (dock);

		// History pad
		HistoryPad history_pad = new (PintaCore.Actions.Edit);
		history_pad.Initialize (dock);

		container.Append (dock);
	}

	private void LoadUserSettings ()
	{
		dock.LoadSettings (PintaCore.Settings);

		// Set selected tool to last selected or default to the PaintBrush
		PintaCore.Tools.SetCurrentTool (PintaCore.Settings.GetSetting (SettingNames.LAST_SELECTED_TOOL, "PaintBrushTool"));

		PintaCore.Actions.View.Rulers.Value = PintaCore.Settings.GetSetting (SettingNames.RULER_SHOWN, false);
		PintaCore.Actions.View.ToolBar.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true);
		PintaCore.Actions.View.MenuBar.Value = IsUsingMenuBar ();
		PintaCore.Actions.View.StatusBar.Value = PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOWN, true);
		PintaCore.Actions.View.ToolBox.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBOX_SHOWN, true);
		PintaCore.Actions.View.Colors.Value = PintaCore.Settings.GetSetting (SettingNames.COLORS_SHOWN, true);
		// ponytail: floating state is intentionally not persisted - colors always start docked.
		toolbox.PinnedTools = PintaCore.Settings.GetSetting (SettingNames.TOOLBOX_PINNED, "");
		PintaCore.Actions.View.ImageTabs.Value = PintaCore.Settings.GetSetting (SettingNames.IMAGE_TABS_SHOWN, true);
		PintaCore.Actions.View.ToolWindows.Value = PintaCore.Settings.GetSetting (SettingNames.TOOL_WINDOWS_SHOWN, true);

		string dialog_uri = PintaCore.Settings.GetSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.DefaultDialogDirectory?.GetUri () ?? "");
		PintaCore.RecentFiles.LastDialogDirectory = Gio.FileHelper.NewForUri (dialog_uri);

		MetricType ruler_metric = (MetricType) PintaCore.Settings.GetSetting (SettingNames.RULER_METRIC, (int) MetricType.Pixels);
		PintaCore.Actions.View.RulerMetric.Activate (GLib.Variant.NewInt32 ((int) ruler_metric));

		int color_scheme = PintaCore.Settings.GetSetting (SettingNames.COLOR_SCHEME, 0);
		PintaCore.Actions.View.ColorScheme.Activate (GLib.Variant.NewInt32 (color_scheme));
	}

	private void SaveUserSettings ()
	{
		dock.SaveSettings (PintaCore.Settings);

		// Don't store the maximized height if the window is maximized
		if (!window_shell.Window.IsMaximized ()) {
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_WIDTH, window_shell.Window.GetWidth ());
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_HEIGHT, window_shell.Window.GetHeight ());
		}

		PintaCore.Settings.PutSetting (SettingNames.RULER_METRIC, (int) GetCurrentRulerMetric ());
		PintaCore.Settings.PutSetting (SettingNames.COLOR_SCHEME, PintaCore.Actions.View.ColorScheme.GetState ()!.GetInt32 ());
		PintaCore.Settings.PutSetting (SettingNames.WINDOW_MAXIMIZED, window_shell.Window.IsMaximized ());
		PintaCore.Settings.PutSetting (SettingNames.RULER_SHOWN, PintaCore.Actions.View.Rulers.Value);
		PintaCore.Settings.PutSetting (SettingNames.IMAGE_TABS_SHOWN, PintaCore.Actions.View.ImageTabs.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOL_WINDOWS_SHOWN, PintaCore.Actions.View.ToolWindows.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBAR_SHOWN, PintaCore.Actions.View.ToolBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.MENUBAR_SHOWN, PintaCore.Actions.View.MenuBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.STATUSBAR_SHOWN, PintaCore.Actions.View.StatusBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBOX_SHOWN, PintaCore.Actions.View.ToolBox.Value);
		PintaCore.Settings.PutSetting (SettingNames.COLORS_SHOWN, PintaCore.Actions.View.Colors.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBOX_PINNED, toolbox.PinnedTools);
		PintaCore.Settings.PutSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.LastDialogDirectory?.GetUri () ?? "");

		if (PintaCore.Tools.CurrentTool is BaseTool tool)
			PintaCore.Settings.PutSetting (SettingNames.LAST_SELECTED_TOOL, tool.GetType ().Name);

		PintaCore.Settings.DoSaveSettingsBeforeQuit ();
	}

	#region Action Handlers
	private bool HandleCloseRequest (object o, EventArgs args)
	{
		PintaCore.Actions.App.Exit.Activate ();

		// Stop the default handler from running so the user can cancel quitting.
		return true;
	}

	private bool HandleDrop (Gtk.DropTarget sender, Gtk.DropTarget.DropSignalArgs args)
	{
		if (args.Value.GetBoxed (Gdk.FileList.GetGType ()) is not Gdk.FileList file_list)
			return false;

		foreach (Gio.File file_dropped in file_list.GetFilesHelper ()) {
			Gio.File file = file_dropped;

			// On macOS, GTK4 pasteboard currently provides malformed URIs where the scheme is URL-encoded
			// (e.g., "file%3A///" instead of "file:///"). Because of this, GIO fails to recognize it as a local file.
			// This was fixed in GTK 4.23.1, so this workaround can be removed once Pinta requires GTK >= 4.23.1.
			string parseName = file_dropped.GetParseName ();
			if (parseName.StartsWith ("file%3A///", StringComparison.OrdinalIgnoreCase)) {
				string decodedUri = Uri.UnescapeDataString (parseName);
				file = Gio.FileHelper.NewForUri (decodedUri);
			}

			PintaCore.Workspace.OpenFile (file);

			if (file.GetUriScheme () is string scheme &&
			   (scheme.StartsWith ("http") || scheme.StartsWith ("ftp"))) {
				// If the file was likely dragged from a browser, mark as not having a file
				// so that the user must choose a new file to save to instead of hitting a permission error.
				PintaCore.Workspace.ActiveDocument.ClearFileReference ();
			}
		}

		return true;
	}

	private void ZoomToSelection_Activated (object sender, EventArgs e)
	{
		PintaCore.Workspace.ActiveWorkspace.ZoomToCanvasRectangle (PintaCore.Workspace.ActiveDocument.Selection.GetBounds ());
	}

	private void ZoomToWindow_Activated (object sender, EventArgs e)
	{
		// The image is small enough to fit in the window
		if (PintaCore.Workspace.ImageFitsInWindow) {
			PintaCore.Actions.View.ActualSize.Activate ();
		} else {
			int image_x = PintaCore.Workspace.ImageSize.Width;
			int image_y = PintaCore.Workspace.ImageSize.Height;

			var canvas_viewport = PintaCore.Workspace.ActiveWorkspace.Canvas.Parent!;

			int window_x = canvas_viewport.GetAllocatedWidth ();
			int window_y = canvas_viewport.GetAllocatedHeight ();

			double ratio =
				(image_x / (double) window_x >= image_y / (double) window_y)
				? (window_x - 20) / (double) image_x
				: (window_y - 20) / (double) image_y;

			// The image is more constrained by width than height

			PintaCore.Workspace.Scale = ratio;
			PintaCore.Actions.View.SuspendZoomUpdate ();
			PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
			PintaCore.Actions.View.ResumeZoomUpdate ();
		}

		PintaCore.Actions.View.ZoomToWindowActivated = true;
	}
	#endregion


	private void ActiveDocumentChanged (object? sender, EventArgs e)
	{
		if (!PintaCore.Workspace.HasOpenDocuments)
			return;

		PintaCore.Actions.View.SuspendZoomUpdate ();
		PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
		PintaCore.Actions.View.ResumeZoomUpdate ();

		var doc = PintaCore.Workspace.ActiveDocument;
		var tab = FindTabWithCanvas ((CanvasWindow) doc.Workspace.CanvasWindow);

		if (tab != null)
			canvas_pad.Notebook.ActiveItem = tab;

		doc.Workspace.GrabFocusToCanvas ();
	}

	private IDockNotebookItem? FindTabWithCanvas (CanvasWindow canvas_window) =>
		canvas_pad.Notebook.Items
		.Where (i => ((CanvasWindow) i.Widget) == canvas_window)
		.FirstOrDefault ();
}
