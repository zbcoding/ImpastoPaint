using System.Collections.Generic;
using System.IO;

namespace Pinta.Core;

/// <summary>
/// What to do with the items a drag-and-drop delivered to the main window: which to open, and
/// which to leave alone. Dropping a whole folder, or a mixed selection, is usually an accident;
/// opening every item as if it were an image floods the window with documents or with error
/// dialogs, one per item.
/// </summary>
public sealed class DroppedFilesPlan
{
	/// <summary>
	/// More openable files than this and the user is asked before they are all opened.
	/// </summary>
	public const int ConfirmAboveCount = 10;

	/// <summary>Files to open, in the order they were dropped.</summary>
	public IReadOnlyList<Gio.File> ToOpen { get; }

	/// <summary>Display names of dropped folders. Folders are never opened or expanded.</summary>
	public IReadOnlyList<string> Folders { get; }

	/// <summary>Display names of dropped files whose extension is not an image format Impasto reads.</summary>
	public IReadOnlyList<string> Unsupported { get; }

	public bool NeedsConfirmation => ToOpen.Count > ConfirmAboveCount;

	public bool SkipsAnything => Folders.Count > 0 || Unsupported.Count > 0;

	private DroppedFilesPlan (List<Gio.File> toOpen, List<string> folders, List<string> unsupported)
	{
		ToOpen = toOpen;
		Folders = folders;
		Unsupported = unsupported;
	}

	public static DroppedFilesPlan Create (IEnumerable<Gio.File> dropped, ImageConverterManager formats)
	{
		List<Gio.File> toOpen = [];
		List<string> folders = [];
		List<string> unsupported = [];

		foreach (Gio.File file in dropped) {
			string name = file.GetDisplayName ();

			if (IsFolder (file))
				folders.Add (name);
			else if (IsOpenable (name, formats))
				toOpen.Add (file);
			else
				unsupported.Add (name);
		}

		return new (toOpen, folders, unsupported);
	}

	public static bool IsFolder (Gio.File file)
		=> file.QueryFileType (Gio.FileQueryInfoFlags.None, null) == Gio.FileType.Directory;

	// A name with no extension is still tried against every loader, as Open does: a picture
	// dragged out of a browser often arrives without one. A name whose extension is not an image
	// format at all is not worth an attempt.
	private static bool IsOpenable (string displayName, ImageConverterManager formats)
	{
		string extension = Path.GetExtension (displayName);

		return extension.Length == 0 || formats.GetImporterByFile (displayName) is not null;
	}
}
