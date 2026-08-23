using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

/// <summary>
/// Confirmation for resizes big enough to bog the machine down. Cairo still accepts these
/// sizes, but every layer costs width * height * 4 bytes and history snapshots multiply that.
/// </summary>
internal static class LargeImagePrompt
{
	// ponytail: flat pixel-count threshold, ~64 MB per layer. Scale by available RAM if it ever matters.
	private const long WarnPixelCount = 16_000_000;

	public static async Task<bool> ConfirmIfLarge (IChromeService chrome, Size newSize)
	{
		long pixels = (long) newSize.Width * newSize.Height;
		if (pixels <= WarnPixelCount)
			return true;

		string primary = Translations.GetString ("This image size may slow down your computer");                // Translators: {0} and {1} are image dimensions; {2} is memory in megabytes.
		string secondary = Translations.GetString (
			"{0} x {1} pixels needs about {2} MB of memory per layer, plus more for undo history.",
			newSize.Width,
			newSize.Height,
			pixels * 4 / (1024 * 1024));

		return await GtkExtensions.RunConfirmAsync (
			chrome.MainWindow,
			primary,
			secondary,
			Translations.GetString ("_Continue"),
			destructive: true);
	}
}
