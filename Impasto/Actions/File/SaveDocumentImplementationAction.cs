//
// SaveDocumentImplmentationAction.cs
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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class SaveDocumentImplmentationAction : IActionHandler
{
	private readonly FileActions file;
	private readonly ImageActions image;
	private readonly ChromeManager chrome;
	private readonly ImageConverterManager image_formats;
	private readonly RecentFileManager recent_files;
	private readonly ToolManager tools;
	internal SaveDocumentImplmentationAction (
		FileActions file,
		ImageActions image,
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		RecentFileManager recentFiles,
		ToolManager tools)
	{
		this.file = file;
		this.image = image;
		this.chrome = chrome;
		image_formats = imageFormats;
		recent_files = recentFiles;
		this.tools = tools;
	}

	void IActionHandler.Initialize ()
	{
		file.SaveDocument += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		file.SaveDocument -= Activated;
	}

	private async Task<bool> Activated (FileActions sender, DocumentSaveEventArgs e)
	{
		// Prompt for a new filename for "Save As", or a document that hasn't been saved before
		if (e.SaveAs || !e.Document.HasFile) {
			return await SaveFileAs (e.Document);
		}

		// Document hasn't changed, don't re-save it
		if (!e.Document.IsDirty)
			return true;

		// If the document already has a filename, just re-save it
		return await SaveFile (e.Document, null, null, chrome.MainWindow);
	}

	// This is actually both for "Save As" and saving a file that never
	// been saved before.  Either way, we need to prompt for a filename.
	private async Task<bool> SaveFileAs (Document document)
	{
		var fcd = Gtk.FileChooserNative.New (
			Translations.GetString ("Save Image File"),
			chrome.MainWindow,
			Gtk.FileChooserAction.Save,
			Translations.GetString ("Save"),
			Translations.GetString ("Cancel"));

		if (document.HasFile)
			fcd.SetFile (document.File!);
		else {
			if (recent_files.GetDialogDirectory () is Gio.File dir && dir.QueryExists (null))
				fcd.SetCurrentFolder (dir);

			// Leave the name bare (no extension) on purpose: the save loop
			// below (SaveDocumentImplmentationAction.SaveFileAs) auto-appends
			// the extension of the format the user actually picks. Pre-filling
			// the *default* extension here makes portal-based pickers (KDE/GNOME)
			// key off the name's extension and revert the format dropdown to
			// match it, so a bare name is the only thing that keeps the user's
			// dropdown choice authoritative.
			fcd.SetCurrentName (document.DisplayName);
		}

		// Add all the formats we support to the save dialog
		Dictionary<Gtk.FileFilter, FormatDescriptor> filetypes = [];
		foreach (var format in image_formats.Formats) {

			if (!format.IsExportAvailable ())
				continue;

			fcd.AddFilter (format.Filter);
			filetypes.Add (format.Filter, format);

			// Set the filter to anything we found
			// We want to ensure that *something* is selected in the filetype
			fcd.Filter = format.Filter;
		}

		// If we already have a format, set it to the default.
		// If not, default to jpeg
		FormatDescriptor? format_desc = null;

		if (document.HasFile) {
			format_desc = image_formats.GetFormatByFile (document.DisplayName);
		}

		if (format_desc is null || !format_desc.IsExportAvailable ())
			format_desc = image_formats.GetDefaultSaveFormat ();

		fcd.Filter = format_desc.Filter;

		while (await fcd.RunAsync () == Gtk.ResponseType.Accept) {

			Gio.File file = fcd.GetFile ()!;

			// Note that we can't use file.GetDisplayName() because the file doesn't exist.
			string displayName = file.GetParent ()!.GetRelativePath (file)!;

			// Always follow the extension rather than the file type drop down
			// ie: if the user chooses to save a "jpeg" as "foo.png", we are going
			// to assume they just didn't update the dropdown and really want png
			FormatDescriptor? format = image_formats.GetFormatByFile (displayName);
			if (format is null) {
				// Fall back to the selected file filter, then to the default format.
				format = ImageConverterManager.ResolveSelectedFormat (fcd.Filter, filetypes)
					?? image_formats.GetDefaultSaveFormat ();
			}

			// Never rebuild a new Gio.File to paper over a missing extension: constructing
			// a different target after the dialog has already returned one breaks desktop
			// portals (upstream Pinta bug 1958670 - the write can silently land on a portal
			// staging file instead of the chosen name). Ask the user to add an extension
			// instead of quietly redirecting the write to a path the portal never granted.
			if (!HasExtension (displayName)) {

				await chrome.ShowMessageDialog (
					chrome.MainWindow,
					Translations.GetString ("Impasto does not save files without a file extension."),
					Translations.GetString ("Please enter a name with an extension, e.g. \"{0}.{1}\".", displayName, format.Extensions.First ()));

				fcd.SetCurrentName (displayName);
				continue;
			}

			if (!await ConfirmFlatten (document, format)) {
				continue;
			}

			Gio.File? directory = file.GetParent ();

			if (directory is not null)
				recent_files.LastDialogDirectory = directory;

			// If saving the file failed or was cancelled, let the user select
			// a different file type.
			if (!await SaveFile (document, file, format, chrome.MainWindow)) {
				// Re-set the current name and directory
				fcd.SetCurrentName (displayName);
				fcd.SetCurrentFolder (directory);
				continue;
			}

			//The user is saving the Document to a new file, so technically it
			//hasn't been saved to its associated file in this session.
			document.HasBeenSavedInSession = false;

			recent_files.AddFile (file);
			image_formats.SetDefaultFormat (format.Extensions.First ());

			document.File = file;
			document.FileType = format.Extensions.First ();
			return true;
		}

		return false;
	}

	// Path.GetExtension() treats a leading dot as part of the name rather than an extension
	// marker (e.g. ".bashrc" -> ".bashrc", not ""), so a blank or dotfile-style name would
	// otherwise look like it already has an extension.
	private static bool HasExtension (string fileName) => fileName.LastIndexOf ('.') > 0;

	private async Task<bool> SaveFile (Document document, Gio.File? file, FormatDescriptor? format, Gtk.Window parent)
	{
		file ??= document.File;

		if (file is null)
			throw new ArgumentException ("Attempted to save a document with no associated file", nameof (file));

		if (format is null) {

			if (string.IsNullOrEmpty (document.FileType))
				throw new ArgumentException ($"{nameof (document.FileType)} must contain value.", nameof (document));

			format = image_formats.GetFormatByExtension (document.FileType);
		}

		if (format is null || !format.IsExportAvailable ()) {

			await chrome.ShowMessageDialog (
				parent,
				Translations.GetString ("Impasto does not support saving images in this file format."),
				// Use this instead of file.GetDisplayName() in case file was not created.
				file.GetParent ()!.GetRelativePath (file)!);

			return false;
		}

		if (!await ConfirmFlatten (document, format)) {
			return false;
		}

		if (!await ConfirmBakeEffectNodes (document, format)) {
			return false;
		}

		// Commit any pending changes
		tools.Commit ();

		try {
			format.Exporter.Export (document, file, parent);

			// glycin rejects oversized ICOs with "...the image width must be `1..=256`, instead width 800 was provided";
			// older GdkPixbuf said "Image too large to be saved as ICO". Match both.
		} catch (GLib.GException e) when (e.Message == "Image too large to be saved as ICO"
			|| (e.Message.Contains ("the image width must be") || e.Message.Contains ("the image height must be"))) {

			string primary = Translations.GetString ("Image too large");
			string secondary = Translations.GetString ("ICO files can not be larger than 256 x 256 pixels.");

			// file.Replace() already truncated/created the target before the export
			// threw, leaving an empty/partial file on disk. Remove it.
			try { file.Delete (null); } catch (GLib.GException) { }

			await chrome.ShowMessageDialog (parent, primary, secondary);

			return false;

		} catch (GLib.GException e) when (e.Message.Contains ("Permission denied") && e.Message.Contains ("Failed to open")) {

			string primary = Translations.GetString ("Failed to save image");

			// Translators: {0} is the name of a file that the user does not have write permission for.
			string secondary = Translations.GetString ("You do not have access to modify '{0}'. The file or folder may be read-only.", file);

			await chrome.ShowMessageDialog (parent, primary, secondary);

			return false;

		} catch (OperationCanceledException) {

			return false;
		}

		document.File = file;
		document.FileType = format.Extensions.First ();

		tools.DoAfterSave (document);

		// Mark the document as clean following the tool's after-save handler, which might
		// adjust history (e.g. undo changes that were committed before saving).
		document.Workspace.History.SetClean ();

		//Now the Document has been saved to the file it's associated with in this session.
		document.HasBeenSavedInSession = true;

		return true;
	}

	private async Task<bool> ConfirmFlatten (Document document, FormatDescriptor format)
	{
		// If the format doesn't support layers but there is more than one layer, ask to flatten the image
		if (!format.SupportsLayers
			&& document.Layers.Count () > 1) {

			string heading = Translations.GetString ("This format does not support layers. Flatten image?");
			string body = Translations.GetString ("Flattening the image will merge all layers into a single layer.");

			bool confirmed = await GtkExtensions.RunConfirmAsync (
				chrome.MainWindow,
				heading,
				body,
				Translations.GetString ("Flatten"));

			if (!confirmed) {
				return false;
			}

			// Flatten the image
			tools.Commit ();
			image.Flatten.Activate ();
		}
		return true;
	}

	/// <summary>
	/// Impasto: warns before saving a document whose layer-effect nodes cannot be written as editable
	/// nodes, which is any effect that came from an add-in - nothing guarantees the add-in is present
	/// when the file is opened again. Only the written file is affected: the nodes stay live and
	/// editable in the open document either way.
	/// </summary>
	private async Task<bool> ConfirmBakeEffectNodes (Document document, FormatDescriptor format)
	{
		// Every other format is a flattened export already, so it has nothing extra to warn about.
		if (!format.Extensions.Contains ("ora"))
			return true;

		IReadOnlyList<string> baked = OraFormat.EffectNodesToBake (document);
		if (baked.Count == 0)
			return true;

		string heading = Translations.GetString ("Some effects cannot be saved as editable. Save anyway?");
		string body = Translations.GetString (
			"These effects come from add-ins and will be part of the saved image's pixels instead, so reopening the file will not let you change their settings. They stay editable in this window.")
			+ "\n\n"
			+ string.Join ("\n", baked.Distinct ());

		return await GtkExtensions.RunConfirmAsync (
			chrome.MainWindow,
			heading,
			body,
			Translations.GetString ("Save"));
	}
}
