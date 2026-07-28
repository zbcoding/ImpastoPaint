// 
// RecentFileManager.cs
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
using System.Collections.Immutable;
using System.Linq;
using Gtk;

namespace Pinta.Core;

public sealed class RecentFileManager
{
	private const int MaxRecentFiles = 10;
	private const string RecentFilesSetting = "recent-files";

	private readonly ISettingsService settings;
	private Gio.File? last_dialog_directory;

	/// <summary>Raised when the list of recently-opened files changes.</summary>
	public event EventHandler? RecentFilesChanged;

	public RecentFileManager (ISettingsService settings)
	{
		this.settings = settings;
		last_dialog_directory = DefaultDialogDirectory;
	}

	public Gio.File? LastDialogDirectory {
		get => last_dialog_directory;
		set {
			// The file chooser dialog may return null for the current folder in certain cases,
			// such as the Recently Used pane in the Gnome file chooser.
			if (value != null)
				last_dialog_directory = value;
		}
	}

	public Gio.File? DefaultDialogDirectory {
		get {
			string path = System.Environment.GetFolderPath (Environment.SpecialFolder.MyPictures);
			return !string.IsNullOrEmpty (path) ? Gio.FileHelper.NewForPath (path) : null;
		}
	}

	/// <summary>
	/// Returns a directory for use in a dialog. The last dialog directory is
	/// returned if it exists, otherwise the default directory is used.
	/// </summary>
	public Gio.File? GetDialogDirectory ()
	{
		return (last_dialog_directory != null && last_dialog_directory.QueryExists (null)) ? last_dialog_directory : DefaultDialogDirectory;
	}

	/// <summary>
	/// Add a file to the list of recently-used files.
	/// </summary>
	public void AddFile (Gio.File file)
	{
		RecentManager.GetDefault ().AddItem (file.GetUri ());

		string uri = file.GetUri ();

		// Move to the front, drop any earlier occurrence, and cap the list.
		ImmutableArray<string> updated =
			GetRecentUris ()
			.Where (u => u != uri)
			.Prepend (uri)
			.Take (MaxRecentFiles)
			.ToImmutableArray ();

		settings.PutSetting (RecentFilesSetting, string.Join ('\n', updated));
		RecentFilesChanged?.Invoke (this, EventArgs.Empty);
	}

	private ImmutableArray<string> GetRecentUris ()
	{
		string stored = settings.GetSetting (RecentFilesSetting, string.Empty);
		return stored.Split ('\n', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray ();
	}

	/// <summary>
	/// The most recently opened files, newest first, skipping any that no longer exist.
	/// </summary>
	public IReadOnlyList<Gio.File> GetRecentFiles ()
		=> GetRecentUris ()
			.Select (Gio.FileHelper.NewForUri)
			.Where (f => f.QueryExists (null))
			.ToImmutableArray ();
}
