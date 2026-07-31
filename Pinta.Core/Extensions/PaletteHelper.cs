using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;

namespace Pinta.Core;

public static class PaletteHelper
{
	public static Palette CreateDefault (bool includeDarkRow = false)
	{
		return new (EnumerateDefaultColors (includeDarkRow));
	}

	// The number of swatch rows the palette widget needs: two for the standard
	// palette, three when the extra darker row is enabled.
	public static int GetPaletteRowCount ()
		=> PintaCore.Settings.GetSetting (SettingNames.EXTENDED_PALETTE_ROWS, false) ? 3 : 2;

	public static void Save (this Palette palette, Gio.File file, IPaletteSaver saver)
	{
		saver.Save (palette.Colors, file);
	}

	public static void LoadDefault (this Palette palette, bool includeDarkRow = false)
	{
		palette.Load (EnumerateDefaultColors (includeDarkRow));
	}

	public static void Load (this Palette palette, PaletteFormatManager paletteFormats, Gio.File file)
	{
		List<Color> loadedColors = LoadColorsFromFile (paletteFormats, file);
		palette.Load (loadedColors);
	}

	private static List<Color> LoadColorsFromFile (PaletteFormatManager paletteFormats, Gio.File file)
	{
		var loader = paletteFormats.GetFormatByFilename (file.GetDisplayName ())?.Loader;

		if (loader != null)
			return loader.Load (file);

		StringBuilder errors = new ();

		// Not a recognized extension, so attempt all formats
		foreach (var format in paletteFormats.Formats.Where (f => !f.IsWriteOnly ())) {
			try {
				var loaded_colors = format.Loader.Load (file);
				if (loaded_colors != null)
					return loaded_colors;
			} catch (Exception e) {
				// Record errors in case none of the formats work.
				errors.AppendLine ($"Failed to load palette as {format.Filter.Name}:");
				errors.Append (e.ToString ());
				errors.AppendLine ();
			}
		}

		throw new PaletteLoadException (
			file.GetParseName (),
			errors.ToString ());
	}

	public static IEnumerable<Color> EnumerateDefaultColors (bool includeDarkRow = false)
	{
		if (includeDarkRow)
			return EnumerateExtendedPalette ();

		return EnumerateStandardPalette ();
	}

	// The standard palette: 34 colors laid out in two rows.
	// Each column holds a bright color and its light tint.
	private static IEnumerable<Color> EnumerateStandardPalette ()
	{
		yield return new (255 / 255f, 255 / 255f, 255 / 255f);
		yield return new (0 / 255f, 0 / 255f, 0 / 255f);

		yield return new (160 / 255f, 160 / 255f, 160 / 255f);
		yield return new (128 / 255f, 128 / 255f, 128 / 255f);

		yield return new (64 / 255f, 64 / 255f, 64 / 255f);
		yield return new (48 / 255f, 48 / 255f, 48 / 255f);

		yield return new (255 / 255f, 0 / 255f, 0 / 255f);
		yield return new (255 / 255f, 127 / 255f, 127 / 255f);

		yield return new (255 / 255f, 106 / 255f, 0 / 255f);
		yield return new (255 / 255f, 178 / 255f, 127 / 255f);

		yield return new (255 / 255f, 216 / 255f, 0 / 255f);
		yield return new (255 / 255f, 233 / 255f, 127 / 255f);

		yield return new (182 / 255f, 255 / 255f, 0 / 255f);
		yield return new (218 / 255f, 255 / 255f, 127 / 255f);

		yield return new (76 / 255f, 255 / 255f, 0 / 255f);
		yield return new (165 / 255f, 255 / 255f, 127 / 255f);

		yield return new (0 / 255f, 255 / 255f, 33 / 255f);
		yield return new (127 / 255f, 255 / 255f, 142 / 255f);

		yield return new (0 / 255f, 255 / 255f, 144 / 255f);
		yield return new (127 / 255f, 255 / 255f, 197 / 255f);

		yield return new (0 / 255f, 255 / 255f, 255 / 255f);
		yield return new (127 / 255f, 255 / 255f, 255 / 255f);

		yield return new (0 / 255f, 148 / 255f, 255 / 255f);
		yield return new (127 / 255f, 201 / 255f, 255 / 255f);

		yield return new (0 / 255f, 38 / 255f, 255 / 255f);
		yield return new (127 / 255f, 146 / 255f, 255 / 255f);

		yield return new (72 / 255f, 0 / 255f, 255 / 255f);
		yield return new (161 / 255f, 127 / 255f, 255 / 255f);

		yield return new (178 / 255f, 0 / 255f, 255 / 255f);
		yield return new (214 / 255f, 127 / 255f, 255 / 255f);

		yield return new (255 / 255f, 0 / 255f, 220 / 255f);
		yield return new (255 / 255f, 127 / 255f, 237 / 255f);

		yield return new (255 / 255f, 0 / 255f, 110 / 255f);
		yield return new (255 / 255f, 127 / 255f, 182 / 255f);
	}

	// Extended palette: 48 colors laid out in three rows. Restores the 14 dark
	// "127,x" hues that shipped with the status-bar palette in Pinta PR #154
	// (https://github.com/PintaProject/Pinta/pull/154), addressing issue #812
	// (https://github.com/PintaProject/Pinta/issues/812). Each column holds a light
	// tint, its bright color, and its dark shade, so the extra row reads as a darker
	// band beneath the standard two rows.
	private static IEnumerable<Color> EnumerateExtendedPalette ()
	{
		yield return new (255 / 255f, 255 / 255f, 255 / 255f);
		yield return new (0 / 255f, 0 / 255f, 0 / 255f);
		yield return new (64 / 255f, 64 / 255f, 64 / 255f);

		yield return new (160 / 255f, 160 / 255f, 160 / 255f);
		yield return new (128 / 255f, 128 / 255f, 128 / 255f);
		yield return new (48 / 255f, 48 / 255f, 48 / 255f);

		yield return new (255 / 255f, 127 / 255f, 127 / 255f);
		yield return new (255 / 255f, 0 / 255f, 0 / 255f);
		yield return new (127 / 255f, 0 / 255f, 0 / 255f);

		yield return new (255 / 255f, 178 / 255f, 127 / 255f);
		yield return new (255 / 255f, 106 / 255f, 0 / 255f);
		yield return new (127 / 255f, 51 / 255f, 0 / 255f);

		yield return new (255 / 255f, 233 / 255f, 127 / 255f);
		yield return new (255 / 255f, 216 / 255f, 0 / 255f);
		yield return new (127 / 255f, 106 / 255f, 0 / 255f);

		yield return new (218 / 255f, 255 / 255f, 127 / 255f);
		yield return new (182 / 255f, 255 / 255f, 0 / 255f);
		yield return new (91 / 255f, 127 / 255f, 0 / 255f);

		yield return new (165 / 255f, 255 / 255f, 127 / 255f);
		yield return new (76 / 255f, 255 / 255f, 0 / 255f);
		yield return new (38 / 255f, 127 / 255f, 0 / 255f);

		yield return new (127 / 255f, 255 / 255f, 142 / 255f);
		yield return new (0 / 255f, 255 / 255f, 33 / 255f);
		yield return new (0 / 255f, 127 / 255f, 14 / 255f);

		yield return new (127 / 255f, 255 / 255f, 197 / 255f);
		yield return new (0 / 255f, 255 / 255f, 144 / 255f);
		yield return new (0 / 255f, 127 / 255f, 70 / 255f);

		yield return new (127 / 255f, 255 / 255f, 255 / 255f);
		yield return new (0 / 255f, 255 / 255f, 255 / 255f);
		yield return new (0 / 255f, 127 / 255f, 127 / 255f);

		yield return new (127 / 255f, 201 / 255f, 255 / 255f);
		yield return new (0 / 255f, 148 / 255f, 255 / 255f);
		yield return new (0 / 255f, 74 / 255f, 127 / 255f);

		yield return new (127 / 255f, 146 / 255f, 255 / 255f);
		yield return new (0 / 255f, 38 / 255f, 255 / 255f);
		yield return new (0 / 255f, 19 / 255f, 127 / 255f);

		yield return new (161 / 255f, 127 / 255f, 255 / 255f);
		yield return new (72 / 255f, 0 / 255f, 255 / 255f);
		yield return new (33 / 255f, 0 / 255f, 127 / 255f);

		yield return new (214 / 255f, 127 / 255f, 255 / 255f);
		yield return new (178 / 255f, 0 / 255f, 255 / 255f);
		yield return new (87 / 255f, 0 / 255f, 127 / 255f);

		yield return new (255 / 255f, 127 / 255f, 237 / 255f);
		yield return new (255 / 255f, 0 / 255f, 220 / 255f);
		yield return new (127 / 255f, 0 / 255f, 110 / 255f);

		yield return new (255 / 255f, 127 / 255f, 182 / 255f);
		yield return new (255 / 255f, 0 / 255f, 110 / 255f);
		yield return new (127 / 255f, 0 / 255f, 55 / 255f);
	}
}
