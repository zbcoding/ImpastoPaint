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

		string primary = Translations.GetString ("This image size may slow down your computer");
		string secondary = Translations.GetString (
			$"{newSize.Width} x {newSize.Height} pixels needs about {pixels * 4 / (1024 * 1024)} MB of memory per layer, plus more for undo history.");

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (chrome.MainWindow, primary, secondary);

		const string cancel_response = "cancel";
		const string continue_response = "continue";

		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		dialog.AddResponse (continue_response, Translations.GetString ("_Continue"));
		dialog.SetResponseAppearance (continue_response, Adw.ResponseAppearance.Destructive);
		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = cancel_response;

		return await dialog.RunAsync () == continue_response;
	}
}
