// 
// ImageConverterManager.cs
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
using GdkPixbuf;

namespace Pinta.Core;

public sealed class ImageConverterManager
{
	private readonly SettingsManager settings_manager;
	public ImageConverterManager (SettingsManager settingsManager)
	{
		settings_manager = settingsManager;
	}

	private readonly List<FormatDescriptor> formats = [.. GetInitialFormats ()];

	private static IEnumerable<FormatDescriptor> GetInitialFormats ()
	{
		// All the formats supported by Gdk. Some systems register the same
		// format under more than one loader (e.g. glycin alongside the classic
		// gdk-pixbuf loaders), so dedupe by name to avoid duplicate filters
		// (bmp, tga...) in the file picker.
		HashSet<string> seen = new (StringComparer.OrdinalIgnoreCase);
		foreach (var format in GdkPixbufExtensions.GetFormats ()) {
			string? name = format.GetName ();

			// AVIF is handled by our own importer/exporter below, so that saving
			// works even though gdk-pixbuf's AVIF loader is read-only.
			if (name?.Equals ("avif", StringComparison.OrdinalIgnoreCase) == true)
				continue;

			if (name is not null && !seen.Add (name))
				continue;

			yield return CreateFormatDescriptor (format);
		}

		// Create all the formats we have our own importers/exporters for

		PdnFormat pdnHandler = new ();
		FormatDescriptor pdnFormatDescriptor = new (
			displayPrefix: "PDN",
			extensions: ["pdn", "PDN"],
			mimes: ["image/x-pdn", "image/x-paintnet"],
			importer: pdnHandler,
			exporter: null,
			supportsLayers: true);
		yield return pdnFormatDescriptor;

		OraFormat oraHandler = new ();
		FormatDescriptor oraFormatDescriptor = new (
			displayPrefix: "OpenRaster",
			extensions: ["ora", "ORA"],
			mimes: ["image/openraster"],
			importer: oraHandler,
			exporter: oraHandler,
			supportsLayers: true);
		yield return oraFormatDescriptor;

		NetpbmPortablePixmap netpbmPortablePixmap = new ();
		FormatDescriptor netpbmPortablePixmapDescriptor = new (
			displayPrefix: "Netpbm Portable Pixmap",
			extensions: ["ppm", "PPM"],
			mimes: ["image/x-portable-pixmap"], // Not official, but conventional
			importer: netpbmPortablePixmap,
			exporter: netpbmPortablePixmap,
			supportsLayers: false
		);
		yield return netpbmPortablePixmapDescriptor;

		AvifFormat avifHandler = new ();
		FormatDescriptor avifFormatDescriptor = new (
			displayPrefix: "AVIF",
			extensions: ["avif", "AVIF"],
			mimes: ["image/avif"],
			importer: avifHandler,
			exporter: AvifFormat.IsAvailable ? avifHandler : null,
			supportsLayers: false);
		yield return avifFormatDescriptor;
	}

	private static FormatDescriptor CreateFormatDescriptor (PixbufFormat format)
	{
		string formatName = format.GetName ()?.ToLowerInvariant () ??
				throw new ArgumentException ($"{nameof (format)} has an empty name");

		string formatNameUpperCase = formatName.ToUpperInvariant ();
		string[] extensions = formatName switch {
			"jpeg" => ["jpg", "jpeg", "JPG", "JPEG"],
			"tiff" => ["tif", "tiff", "TIF", "TIFF"],
			_ => [formatName, formatNameUpperCase],
		};
		GdkPixbufFormat importer = new (formatName);
		IImageExporter? exporter;
		if (formatName == "jpeg")
			exporter = importer = new JpegFormat ();
		else if (formatName == "webp")
			exporter = importer = new WebPFormat ();
		else if (formatName == "tga")
			exporter = new TgaExporter ();
		else if (format.IsWritable ())
			exporter = importer;
		else
			exporter = null;

		FormatDescriptor formatDescriptor = new (
			displayPrefix: formatNameUpperCase,
			extensions: extensions,
			mimes: format.GetMimeTypes () ?? [],
			importer: importer,
			exporter: exporter,
			supportsLayers: false);

		return formatDescriptor;
	}

	public IEnumerable<FormatDescriptor> Formats
		=> formats;

	/// <summary>
	/// Registers a new file format.
	/// </summary>
	public void RegisterFormat (FormatDescriptor fd)
	{
		formats.Add (fd);
	}

	/// <summary>
	/// Unregisters the file format for the given extension.
	/// </summary>
	public void UnregisterFormatByExtension (string extension)
	{
		string normalized = NormalizeExtension (extension);
		formats.RemoveAll (f => f.Extensions.Contains (normalized));
	}

	/// <summary>
	/// Returns the default format that should be used when saving a file.
	/// This is normally the last format that was chosen by the user.
	/// </summary>
	public FormatDescriptor GetDefaultSaveFormat ()
	{
		string extension = settings_manager.GetSetting<string> (SettingNames.DEFAULT_IMAGE_TYPE, "jpeg");

		FormatDescriptor? fd = GetFormatByExtension (extension);

		// We found the last one we used
		if (fd != null && fd.IsExportAvailable ())
			return fd;

		// Return any format we have
		foreach (var f in Formats)
			if (f.IsExportAvailable ())
				return f;

		// We don't have any formats
		throw new InvalidOperationException ("There are no image formats supported.");
	}

	/// <summary>
	/// Finds the correct exporter to use for opening the given file, or null
	/// if no exporter exists for the file.
	/// </summary>
	public IImageExporter? GetExporterByFile (string file)
	{
		string extension = Path.GetExtension (file);
		return GetExporterByExtension (extension);
	}

	/// <summary>
	/// Finds the file format for the given file name, or null
	/// if no file format exists for that file.
	/// </summary>
	public FormatDescriptor? GetFormatByFile (string file)
	{
		string extension = Path.GetExtension (file);
		return GetFormatByExtension (extension);
	}

	/// <summary>
	/// Finds the format for the file dialog filter the user selected, or null if
	/// none matches. Portal-based pickers (KDE/GNOME) hand back a different
	/// <see cref="Gtk.FileFilter"/> instance than the one we added, renamed to
	/// "&lt;our name&gt; (ext, ext...)", so we match by object identity first and
	/// then by name prefix.
	/// </summary>
	public static FormatDescriptor? ResolveSelectedFormat (
		Gtk.FileFilter? selected,
		IReadOnlyDictionary<Gtk.FileFilter, FormatDescriptor> filetypes)
	{
		if (selected is null)
			return null;

		if (filetypes.TryGetValue (selected, out FormatDescriptor? byIdentity))
			return byIdentity;

		return filetypes.Values.FirstOrDefault (
			f => selected.Name is not null && f.Filter.Name is not null
				&& selected.Name.StartsWith (f.Filter.Name, StringComparison.Ordinal));
	}

	/// <summary>
	/// Whether the file name has a real extension, e.g. for deciding whether to append one
	/// before saving. Path.GetExtension() treats a leading dot as part of the name rather
	/// than an extension marker (e.g. ".bashrc" -> ".bashrc", not ""), so a blank or
	/// dotfile-style name would otherwise look like it already has an extension.
	/// </summary>
	public static bool HasExtension (string fileName) => fileName.LastIndexOf ('.') > 0;

	/// <summary>
	/// Finds the correct importer to use for opening the given file, or null
	/// if no importer exists for the file.
	/// </summary>
	public IImageImporter? GetImporterByFile (string file)
	{
		string extension = Path.GetExtension (file);
		return extension == null ? null : GetImporterByExtension (extension);
	}

	/// <summary>
	/// Sets the default format used when saving files to the given extension.
	/// </summary>
	public void SetDefaultFormat (string extension)
	{
		string normalized = NormalizeExtension (extension);
		settings_manager.PutSetting (SettingNames.DEFAULT_IMAGE_TYPE, normalized);
	}

	/// <summary>
	/// Finds the correct exporter to use for the given file extension, or null
	/// if no exporter exists for that extension.
	/// </summary>
	private IImageExporter? GetExporterByExtension (string extension)
	{
		FormatDescriptor? format = GetFormatByExtension (extension);
		return format?.Exporter;
	}

	/// <summary>
	/// Finds the correct importer to use for the given file extension, or null
	/// if no importer exists for that extension.
	/// </summary>
	private IImageImporter? GetImporterByExtension (string extension)
	{
		FormatDescriptor? format = GetFormatByExtension (extension);
		return format?.Importer;
	}

	/// <summary>
	/// Finds the file format for the given file extension, or null
	/// if no file format exists for that extension.
	/// </summary>
	public FormatDescriptor? GetFormatByExtension (string extension)
	{
		string normalized = NormalizeExtension (extension);
		return Formats.Where (p => p.Extensions.Contains (normalized)).FirstOrDefault ();
	}

	/// <summary>
	/// Normalizes the extension.
	/// </summary>
	private static string NormalizeExtension (string extension)
		=> extension.ToLowerInvariant ().TrimStart ('.').Trim ();
}
