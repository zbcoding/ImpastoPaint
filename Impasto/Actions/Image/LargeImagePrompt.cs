using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

/// <summary>
/// Gate for resizes: refuses sizes an image surface cannot hold, and asks for confirmation on
/// sizes big enough to bog the machine down, since every layer costs width * height * 4 bytes
/// and history snapshots multiply that.
/// </summary>
internal static class LargeImagePrompt
{
	// ponytail: flat pixel-count threshold, ~64 MB per layer. Scale by available RAM if it ever matters.
	private const long WarnPixelCount = 16_000_000;

	public static async Task<bool> ConfirmIfLarge (IChromeService chrome, Size newSize)
	{
		if (!CairoExtensions.IsSupportedImageSize (newSize)) {
			await GtkExtensions.RunInfoAsync (
				chrome.MainWindow,
				Translations.GetString ("This image size is too large"),
				// Translators: {0} and {1} are image dimensions; {2} and {3} are memory in megabytes.
				Translations.GetString (
					"{0} x {1} pixels would need {2} MB of memory per layer, more than the {3} MB an image can hold.",
					newSize.Width,
					newSize.Height,
					(long) newSize.Width * newSize.Height * 4 / (1024 * 1024),
					CairoExtensions.MaxImageBytes / (1024 * 1024)),
				Translations.GetString ("_OK"));
			return false;
		}

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
