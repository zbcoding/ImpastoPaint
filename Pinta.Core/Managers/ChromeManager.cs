// 
// ChromeManager.cs
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
using System.Threading.Tasks;
using Mono.Addins.Localization;

namespace Pinta.Core;

public interface IChromeService
{
	Gtk.Window MainWindow { get; }

	/// <summary>
	/// Shows the progress cursor. Set around any work that blocks the main loop long enough for the
	/// window to look frozen — re-rendering a layer's effect stack, for one.
	/// </summary>
	bool MainWindowBusy { get; set; }

	Task<bool> LaunchSimpleEffectDialog (
		Gtk.Window parent,
		BaseEffect effect,
		IAddinLocalizer localizer,
		IWorkspaceService workspace);
}

/// <summary>
/// The menus that make up the main menu. Anything that extends one - an add-in, or the
/// effects registry - names it with this rather than being handed the model.
/// </summary>
public enum MainMenu
{
	File,
	Edit,
	View,
	Image,
	Adjustments,
	Effects,
	Addins,
	Window,
	Help,
}

public sealed class ChromeManager : IChromeService
{
	private PointI last_canvas_cursor_point;
	private bool main_window_busy;

	private readonly Dictionary<MainMenu, Gio.Menu> main_menus = [];

	// NRT - These are all initialized via the Initialize* functions
	// but it would be nice to rewrite it to provably non-null.
	public Gtk.Application Application { get; private set; } = null!;
	public Gtk.Window MainWindow { get; private set; } = null!;
	public Gtk.Widget Dock { get; private set; } = null!;
	public Gtk.Widget ImageTabsNotebook { get; private set; } = null!;
	private IProgressDialog progress_dialog = null!;
	private ErrorDialogHandler error_dialog_handler = null!;
	private MessageDialogHandler message_dialog_handler = null!;
	private SimpleEffectDialogHandler simple_effect_dialog_handler = null!;

	public Gtk.Box? MainToolBar { get; private set; }
	public Gtk.Box ToolToolBar { get; private set; } = null!;
	public Gtk.Widget ToolBox { get; private set; } = null!;
	public Gtk.Box StatusBar { get; private set; } = null!;

	public IProgressDialog ProgressDialog => progress_dialog;
	public Gio.Menu AdjustmentsMenu => GetMainMenu (MainMenu.Adjustments);
	public Gio.Menu EffectsMenu => GetMainMenu (MainMenu.Effects);

	public PointI LastCanvasCursorPoint {
		get => last_canvas_cursor_point;
		set {
			if (last_canvas_cursor_point != value) {
				last_canvas_cursor_point = value;
				OnLastCanvasCursorPointChanged ();
			}
		}
	}

	public bool MainWindowBusy {
		get => main_window_busy;
		set {
			main_window_busy = value;

			if (main_window_busy)
				MainWindow.Cursor = Gdk.Cursor.NewFromName (Pinta.Resources.StandardCursors.Progress, null);
			else
				MainWindow.Cursor = Gdk.Cursor.NewFromName (Pinta.Resources.StandardCursors.Default, null);
		}
	}

	public void InitializeApplication (Gtk.Application application)
	{
		Application = application;
	}

	public void InitializeWindowShell (Gtk.Window shell)
	{
		MainWindow = shell;
	}

	public void InitializeToolToolBar (Gtk.Box toolToolBar)
	{
		ToolToolBar = toolToolBar;
	}

	public void InitializeMainToolBar (Gtk.Box mainToolBar)
	{
		MainToolBar = mainToolBar;
	}

	public void InitializeStatusBar (Gtk.Box statusbar)
	{
		StatusBar = statusbar;
	}

	public void InitializeToolBox (Gtk.Widget toolbox)
	{
		ToolBox = toolbox;
	}

	public void InitializeDock (Gtk.Widget dock)
	{
		Dock = dock;
	}

	public void InitializeImageTabsNotebook (Gtk.Widget notebook)
	{
		ImageTabsNotebook = notebook;
	}

	/// <summary>
	/// Records the menus that make up the main menu, so anything that extends one can address
	/// it by name. In the header bar layout several of these are shown as toolbar menu buttons
	/// rather than menu bar entries, but they are the same models either way.
	/// </summary>
	public void InitializeMainMenu (IReadOnlyDictionary<MainMenu, Gio.Menu> menus)
	{
		main_menus.Clear ();

		foreach (var (id, menu) in menus)
			main_menus.Add (id, menu);
	}

	public Gio.Menu GetMainMenu (MainMenu id)
		=> main_menus.TryGetValue (id, out Gio.Menu? menu)
			? menu
			: throw new InvalidOperationException ($"The {id} menu has not been initialized");

	public void InitializeProgessDialog (IProgressDialog progressDialog)
	{
		progress_dialog = progressDialog;
	}

	public void InitializeErrorDialogHandler (ErrorDialogHandler handler)
	{
		error_dialog_handler = handler;
	}

	public void InitializeMessageDialog (MessageDialogHandler handler)
	{
		message_dialog_handler = handler;
	}

	public void InitializeSimpleEffectDialog (SimpleEffectDialogHandler handler)
	{
		simple_effect_dialog_handler = handler;
	}

	public async Task ShowErrorDialog (
		Gtk.Window parent,
		string message,
		string body,
		string details)
	{
		ErrorDialogResponse response = await error_dialog_handler (parent, message, body, details);
		switch (response) {
			case ErrorDialogResponse.Bug:
				PintaCore.Actions.Help.Bugs.Activate ();
				break;
		}
	}

	public Task ShowMessageDialog (Gtk.Window parent, string message, string body)
	{
		return message_dialog_handler (parent, message, body);
	}

	public void SetStatusBarText (string text)
	{
		OnStatusBarTextChanged (text);
	}

	public Task<bool> LaunchSimpleEffectDialog (
		Gtk.Window parent,
		BaseEffect effect,
		IAddinLocalizer localizer,
		IWorkspaceService workspace)
	{
		return simple_effect_dialog_handler (
			parent,
			effect,
			localizer,
			workspace);
	}

	private void OnLastCanvasCursorPointChanged ()
	{
		LastCanvasCursorPointChanged?.Invoke (this, EventArgs.Empty);
	}

	private void OnStatusBarTextChanged (string text)
	{
		StatusBarTextChanged?.Invoke (this, new TextChangedEventArgs (text));
	}

	public event EventHandler? LastCanvasCursorPointChanged;
	public event EventHandler<TextChangedEventArgs>? StatusBarTextChanged;
}

public interface IProgressDialog
{
	void Show ();
	void Hide ();
	string Title { get; set; }
	string Text { get; set; }
	double Progress { get; set; }
	event EventHandler Canceled;
}

public delegate Task<ErrorDialogResponse> ErrorDialogHandler (Gtk.Window parent, string message, string body, string details);
public delegate Task MessageDialogHandler (Gtk.Window parent, string message, string body);
public delegate Task<bool> SimpleEffectDialogHandler (Gtk.Window parent, BaseEffect effect, IAddinLocalizer localizer, IWorkspaceService workspace);
