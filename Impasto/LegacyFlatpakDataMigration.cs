using System;
using System.IO;
using System.Linq;
using Pinta.Core;

namespace Pinta;

/// <summary>
/// The Flatpak keeps user data under ~/.var/app/&lt;app id&gt;, so changing the app id from
/// com.github.zbcoding.Impasto left settings, autosaves and recent files behind. On the first
/// start under the new id, copy them over. Native installs use ~/.config/Impasto for both ids.
/// </summary>
internal static class LegacyFlatpakDataMigration
{
	private const string LegacyApplicationId = "com.github.zbcoding.Impasto";
	private const string RecentFilesName = "recently-used.xbel";

	public static void Run ()
	{
		if (Environment.GetEnvironmentVariable ("FLATPAK_ID") != PintaCore.ApplicationId)
			return;

		string home = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
		string legacy_root = Path.Combine (home, ".var", "app", LegacyApplicationId);
		if (!Directory.Exists (legacy_root))
			return;

		try {
			CopyDirectoryIfAbsent (
				Path.Combine (legacy_root, "config", "Impasto"),
				Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "Impasto"));
			CopyFileIfAbsent (
				Path.Combine (legacy_root, "data", RecentFilesName),
				Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), RecentFilesName));
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine ($"Could not copy user data from {legacy_root}: {e.Message}");
		}
	}

	// Skips a target that already has content: the new id has been used before, or Flatpak
	// itself migrated the data during an end-of-life rebase.
	private static void CopyDirectoryIfAbsent (string source, string target)
	{
		if (!Directory.Exists (source))
			return;
		if (Directory.Exists (target) && Directory.EnumerateFileSystemEntries (target).Any ())
			return;

		foreach (string file in Directory.EnumerateFiles (source, "*", SearchOption.AllDirectories)) {
			string destination = Path.Combine (target, Path.GetRelativePath (source, file));
			Directory.CreateDirectory (Path.GetDirectoryName (destination)!);
			File.Copy (file, destination);
		}
	}

	private static void CopyFileIfAbsent (string source, string target)
	{
		if (!File.Exists (source) || File.Exists (target))
			return;

		Directory.CreateDirectory (Path.GetDirectoryName (target)!);
		File.Copy (source, target);
	}
}
