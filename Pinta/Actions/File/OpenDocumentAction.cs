// 
// OpenDocumentAction.cs
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
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class OpenDocumentAction : IActionHandler
{
	private readonly FileActions file;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly RecentFileManager recent_files;
	private readonly ImageConverterManager image_formats;
	internal OpenDocumentAction (
		FileActions file,
		ChromeManager chrome,
		WorkspaceManager workspace,
		RecentFileManager recentFiles,
		ImageConverterManager imageFormats)
	{
		this.file = file;
		this.chrome = chrome;
		this.workspace = workspace;
		recent_files = recentFiles;
		image_formats = imageFormats;
	}

	private Gio.SimpleAction? open_recent_action;

	void IActionHandler.Initialize ()
	{
		file.Open.Activated += Activated;

		// Parameterized action so each recent-file menu item can pass its own URI.
		open_recent_action = Gio.SimpleAction.New ("open-recent", GLib.VariantType.String);
		open_recent_action.OnActivate += OnOpenRecentActivated;
		chrome.Application.AddAction (open_recent_action);

		recent_files.RecentFilesChanged += (_, _) => RefreshRecentMenu ();
		RefreshRecentMenu ();
	}

	void IActionHandler.Uninitialize ()
	{
		file.Open.Activated -= Activated;
	}

	private void RefreshRecentMenu ()
	{
		file.OpenRecentMenu.RemoveAll ();

		foreach (var recent in recent_files.GetRecentFiles ()) {
			var item = Gio.MenuItem.New (recent.GetDisplayName (), null);
			item.SetActionAndTargetValue ("app.open-recent", GLib.Variant.NewString (recent.GetUri ()));
			file.OpenRecentMenu.AppendItem (item);
		}
	}

	private void OnOpenRecentActivated (Gio.SimpleAction sender, Gio.SimpleAction.ActivateSignalArgs args)
	{
		string? uri = args.Parameter?.GetString (out _);
		if (string.IsNullOrEmpty (uri))
			return;

		Gio.File recent = Gio.FileHelper.NewForUri (uri);

		if (!workspace.OpenFile (recent))
			return;

		recent_files.AddFile (recent);

		if (recent.GetParent () is Gio.File directory)
			recent_files.LastDialogDirectory = directory;
	}

	private async void Activated (object sender, EventArgs e)
	{
		using Gtk.FileFilter imagesFilter = CreateImagesFilter ();
		using Gtk.FileFilter catchAllFilter = CreateCatchAllFilter ();

		using Gio.ListStore filters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		filters.Append (imagesFilter);
		filters.Append (catchAllFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Open Image File"));
		fileDialog.SetFilters (filters);
		fileDialog.Modal = true;

		if (recent_files.GetDialogDirectory () is Gio.File dir && dir.QueryExists (null))
			fileDialog.SetInitialFolder (dir);

		var selection = await fileDialog.OpenFilesAsync (chrome.MainWindow);

		if (selection is null)
			return;

		foreach (var file in selection) {

			if (!workspace.OpenFile (file))
				continue;

			recent_files.AddFile (file);

			Gio.File? directory = file.GetParent ();

			if (directory is not null)
				recent_files.LastDialogDirectory = directory;
		}
	}

	private static Gtk.FileFilter CreateCatchAllFilter ()
	{
		Gtk.FileFilter result = Gtk.FileFilter.New ();
		result.Name = Translations.GetString ("All files");
		result.AddPattern ("*");
		return result;
	}

	private Gtk.FileFilter CreateImagesFilter ()
	{
		Gtk.FileFilter result = Gtk.FileFilter.New ();

		result.Name = Translations.GetString ("Image files");

		foreach (var format in image_formats.Formats) {

			if (!format.IsImportAvailable ())
				continue;

			foreach (var ext in format.Extensions)
				result.AddPattern ($"*.{ext}");

			// On Unix-like systems, file extensions are often considered optional.
			// Files can often also be identified by their MIME types.
			// Windows does not understand MIME types natively.
			// Adding a MIME filter on Windows would break the native file picker and force a GTK file picker instead.
			if (SystemManager.GetOperatingSystem () != OS.Windows) {
				foreach (var mime in format.Mimes)
					result.AddMimeType (mime);
			}
		}

		return result;
	}
}
