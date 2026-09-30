using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// What to do with the items a drag-and-drop delivered to the main window: which to open, and
/// which to leave alone. Dropping a whole folder is usually an accident, and a folder holds no
/// image to read. Every other file is tried, as Open does: a file's name is no reliable guide to
/// whether some loader can read it.
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

	public bool NeedsConfirmation => ToOpen.Count > ConfirmAboveCount;

	public bool SkipsAnything => Folders.Count > 0;

	private DroppedFilesPlan (List<Gio.File> toOpen, List<string> folders)
	{
		ToOpen = toOpen;
		Folders = folders;
	}

	public static DroppedFilesPlan Create (IEnumerable<Gio.File> dropped)
	{
		List<Gio.File> toOpen = [];
		List<string> folders = [];

		foreach (Gio.File file in dropped) {
			if (IsFolder (file))
				folders.Add (file.GetDisplayName ());
			else
				toOpen.Add (file);
		}

		return new (toOpen, folders);
	}

	public static bool IsFolder (Gio.File file)
		=> file.QueryFileType (Gio.FileQueryInfoFlags.None, null) == Gio.FileType.Directory;
}
