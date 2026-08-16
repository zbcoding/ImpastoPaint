using System;
using System.Globalization;
using System.Text;
using Pinta.Core;

namespace Cairo;

// TODO-GTK4 (bindings, unsubmitted) - should this be added to gir.core?
public readonly record struct Color (
	double R,
	double G,
	double B,
	double A)
:
	IInterpolableColor<Color>,
	IAlphaColor<Color>
{
	public static Color Black => new (0, 0, 0);
	public static Color Red => new (1, 0, 0);
	public static Color Green => new (0, 1, 0);
	public static Color Blue => new (0, 0, 1);
	public static Color Yellow => new (1, 1, 0);
	public static Color Magenta => new (1, 0, 1);
	public static Color Cyan => new (0, 1, 1);
	public static Color White => new (1, 1, 1);
	public static Color Transparent => new (0, 0, 0, 0);

	public Color (double r, double g, double b)
		: this (r, g, b, 1.0)
	{ }

	/// <summary>
	/// Returns the color value as a string in hex color format.
	/// </summary>
	/// <param name="addAlpha">If false, returns in format "RRGGBB" (Alpha will not affect result).<br/>
	/// Otherwise, returns in format "RRGGBBAA".</param>
	public string ToHex (bool addAlpha = true)
	{
		int r = Convert.ToInt32 (R * 255.0);
		int g = Convert.ToInt32 (G * 255.0);
		int b = Convert.ToInt32 (B * 255.0);
		int a = Convert.ToInt32 (A * 255.0);

		if (addAlpha)
			return $"{r:X2}{g:X2}{b:X2}{a:X2}";
		else
			return $"{r:X2}{g:X2}{b:X2}";
	}

	/// <summary>
	/// Returns a color from an RGBA hex color. Accepts the following formats:<br/>
	/// RRGGBBAA<br/>
	/// RRGGBB<br/>
	/// RGB (Expands to RRGGBB)<br/>
	/// RGBA (Expands to RRGGBBAA)<br/>
	/// Will accept leading #.
	/// </summary>
	/// <param name="hex">Hex color as a string.</param>
	/// <returns>Resulting color. If null, the method could not parse it.</returns>
	public static Color? FromHex (string hex)
	{
		string hashStripped =
			hex.StartsWith ('#')
			? hex[1..]
			: hex;

		// handle shorthand hex
		string expanded = ExpandColorHex (hashStripped);

		if (expanded.Length != 6 && expanded.Length != 8)
			return null;

		try {
			int r = int.Parse (expanded.Substring (0, 2), NumberStyles.HexNumber);
			int g = int.Parse (expanded.Substring (2, 2), NumberStyles.HexNumber);
			int b = int.Parse (expanded.Substring (4, 2), NumberStyles.HexNumber);
			int a =
				(expanded.Length > 6)
				? int.Parse (expanded.Substring (6, 2), NumberStyles.HexNumber)
				: 255;
			return new (r / 255.0, g / 255.0, b / 255.0, a / 255.0);
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Parses a CSS color, also accepting hexadecimal values without a leading hash.
	/// </summary>
	public static Color? FromCssCode (string code, Color currentColor)
	{
		string trimmedCode = code.Trim ();
		if (trimmedCode.Length == 0)
			return null;

		if (trimmedCode.Equals ("currentColor", StringComparison.OrdinalIgnoreCase))
			return currentColor;

		if (trimmedCode.Equals ("transparent", StringComparison.OrdinalIgnoreCase))
			return Transparent;

		Color? hexColor = FromHex (trimmedCode);
		if (hexColor is not null)
			return hexColor;

		if (TryParseOklch (trimmedCode, out Color oklchColor))
			return oklchColor;

		string parserCode =
			NormalizeModernColorFunction (trimmedCode, "rgb", "rgba", hueComponent: false)
			?? NormalizeModernColorFunction (trimmedCode, "hsl", "hsla", hueComponent: true)
			?? trimmedCode.ToLowerInvariant ();

		using Gdk.RGBA parsed = new ();
		if (!parsed.Parse (parserCode))
			return null;

		return new (parsed.Red, parsed.Green, parsed.Blue, parsed.Alpha);
	}

	private static string? NormalizeModernColorFunction (
		string code,
		string functionName,
		string alphaFunctionName,
		bool hueComponent)
	{
		if (!TrySplitModernFunction (code, functionName, out string[] components, out bool hasAlpha, out double alpha))
			return null;

		if (components.Length != 3)
			return null;

		if (hueComponent) {
			if (!TryParseCssAngle (components[0], out double hue))
				return null;
			components[0] = hue.ToString (CultureInfo.InvariantCulture);
		}

		string joinedComponents = string.Join (',', components);
		if (!hasAlpha)
			return $"{functionName}({joinedComponents})";

		return $"{alphaFunctionName}({joinedComponents},{alpha.ToString (CultureInfo.InvariantCulture)})";
	}

	private static bool TryParseOklch (string code, out Color color)
	{
		color = default;
		if (!TrySplitModernFunction (code, "oklch", out string[] components, out _, out double alpha))
			return false;

		if (components.Length != 3
			|| !TryParsePercentageOrNumber (components[0], 1, out double lightness)
			|| !TryParsePercentageOrNumber (components[1], 0.4, out double chroma)
			|| !TryParseCssAngle (components[2], out double hue)
			|| chroma < 0)
			return false;

		lightness = Math.Clamp (lightness, 0, 1);
		double hueRadians = hue * Math.PI / 180;
		double a = chroma * Math.Cos (hueRadians);
		double b = chroma * Math.Sin (hueRadians);

		double lRoot = lightness + 0.3963377774 * a + 0.2158037573 * b;
		double mRoot = lightness - 0.1055613458 * a - 0.0638541728 * b;
		double sRoot = lightness - 0.0894841775 * a - 1.2914855480 * b;
		double l = lRoot * lRoot * lRoot;
		double m = mRoot * mRoot * mRoot;
		double s = sRoot * sRoot * sRoot;

		double red = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
		double green = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
		double blue = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

		color = new (
			Math.Clamp (LinearToSrgb (red), 0, 1),
			Math.Clamp (LinearToSrgb (green), 0, 1),
			Math.Clamp (LinearToSrgb (blue), 0, 1),
			alpha);
		return true;
	}

	private static bool TrySplitModernFunction (
		string code,
		string functionName,
		out string[] components,
		out bool hasAlpha,
		out double alpha)
	{
		components = [];
		hasAlpha = false;
		alpha = 1;

		if (!code.StartsWith (functionName, StringComparison.OrdinalIgnoreCase)
			|| code.Length <= functionName.Length + 2
			|| code[functionName.Length] != '('
			|| code[^1] != ')')
			return false;

		string body = code[(functionName.Length + 1)..^1];
		if (body.Contains (','))
			return false;

		string[] colorAndAlpha = body.Split ('/', 2, StringSplitOptions.TrimEntries);
		if (colorAndAlpha.Length == 2) {
			hasAlpha = true;
			if (!TryParsePercentageOrNumber (colorAndAlpha[1], 1, out alpha))
				return false;
			alpha = Math.Clamp (alpha, 0, 1);
		}

		components = colorAndAlpha[0].Split ((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
		return true;
	}

	private static bool TryParsePercentageOrNumber (string text, double percentageScale, out double value)
	{
		bool percentage = text.EndsWith ('%');
		ReadOnlySpan<char> number = percentage ? text.AsSpan (0, text.Length - 1) : text.AsSpan ();
		if (!double.TryParse (number, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
			|| !double.IsFinite (value))
			return false;

		if (percentage)
			value = value / 100 * percentageScale;

		return true;
	}

	private static bool TryParseCssAngle (string text, out double degrees)
	{
		double scale = 1;
		int unitLength = 0;
		if (text.EndsWith ("grad", StringComparison.OrdinalIgnoreCase)) {
			scale = 0.9;
			unitLength = 4;
		} else if (text.EndsWith ("turn", StringComparison.OrdinalIgnoreCase)) {
			scale = 360;
			unitLength = 4;
		} else if (text.EndsWith ("rad", StringComparison.OrdinalIgnoreCase)) {
			scale = 180 / Math.PI;
			unitLength = 3;
		} else if (text.EndsWith ("deg", StringComparison.OrdinalIgnoreCase)) {
			unitLength = 3;
		}

		ReadOnlySpan<char> number = text.AsSpan (0, text.Length - unitLength);
		if (!double.TryParse (number, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees)
			|| !double.IsFinite (degrees))
			return false;

		degrees = (degrees * scale % 360 + 360) % 360;
		return true;
	}

	private static double LinearToSrgb (double value) =>
		value <= 0.0031308
			? 12.92 * value
			: 1.055 * Math.Pow (value, 1 / 2.4) - 0.055;

	/// <param name="hex">
	/// Hexadecimal color representation without the hash symbol
	/// </param>
	static string ExpandColorHex (string hex)
	{
		switch (hex.Length) {
			case 3:
			case 4:
				StringBuilder expanded = new (hex.Length * 2);
				for (int i = 0; i < hex.Length; i++)
					expanded.Append (hex[i], 2);
				return expanded.ToString ();

			default:
				return hex;
		}
	}

	/// <summary>
	/// Parses the color from a hex string in the byte order of the ColorBgra struct,
	/// for backwards compatibility with existing settings.
	/// </summary>
	/// <remarks>
	/// This should only be used for backwards compatibility with existing settings.
	/// New code should use FromHex()
	/// </remarks>
	[Obsolete ("New code uses R-G-B-A arrangement")]
	internal static Color? ParseBgraHexString (string hex)
	{
		Color? result = FromHex (hex);

		if (result is null)
			return null;

		// Inverse of the reordering in ToBgraHexString().
		return new (
			result.Value.G,
			result.Value.B,
			result.Value.A,
			result.Value.R);
	}

	/// <summary>
	/// Converts the color to a hex string in the byte order of the ColorBgra struct,
	/// for backwards compatibility with existing settings.
	/// </summary>
	/// /// <remarks>
	/// This should only be used for backwards compatibility with existing settings.
	/// New code should use ToHex()
	/// </remarks>
	[Obsolete ("New code uses R-G-B-A arrangement")]
	internal static string ToBgraHexString (Color color)
	{
		Color bgra = new (color.A, color.R, color.G, color.B);
		return bgra.ToHex (addAlpha: true);
	}

	/// <summary>
	/// Copied from RgbColor.ToHsv<br/>
	/// Returns the Cairo color in HSV value.
	/// </summary>
	/// <returns>HSV struct.
	/// Hue varies from 0 - 360.<br/>
	/// Saturation and value varies from 0 - 1.
	/// </returns>
	public HsvColor ToHsv ()
	{
		// In this function, R, G, and B values must be scaled
		// to be between 0 and 1.
		// HsvColor.Hue will be a value between 0 and 360, and
		// HsvColor.Saturation and value are between 0 and 1.

		double min = Math.Min (Math.Min (R, G), B);
		double max = Math.Max (Math.Max (R, G), B);

		double delta = max - min;

		if (max == 0 || delta == 0) {
			// R, G, and B must be 0, or all the same.
			// In this case, S is 0, and H is undefined.
			// Using H = 0 is as good as any...
			return new HsvColor (hue: 0, sat: 0, val: max);
		}

		double h;
		if (R == max) // Between Yellow and Magenta
			h = (G - B) / delta;
		else if (G == max) // Between Cyan and Yellow
			h = 2 + (B - R) / delta;
		else // Between Magenta and Cyan
			h = 4 + (R - G) / delta;

		// Scale h to be between 0 and 360.
		// This may require adding 360, if the value
		// is negative.
		h *= 60;
		if (h < 0)
			h += 360;

		double s = delta / max;

		double v = max;

		// Scale to the requirements of this
		// application. All values are between 0 and 255.
		return new HsvColor (h, s, v);
	}

	/// <summary>
	/// Returns a copy of the original color, replacing provided HSV components.
	/// HSV components not changed will retain their values from the original color.
	/// </summary>
	/// <param name="hue">Hue component, 0 - 360</param>
	/// <param name="sat">Saturation component, 0 - 1</param>
	/// <param name="value">Value component, 0 - 1</param>
	/// <param name="alpha">Alpha component, 0 - 1</param>
	public Color CopyHsv (double? hue = null, double? sat = null, double? value = null, double? alpha = null)
	{
		var hsv = ToHsv ();

		double h = hue ?? hsv.Hue;
		double s = sat ?? hsv.Sat;
		double v = value ?? hsv.Val;
		double a = alpha ?? A;

		return FromHsv (h, s, v, a);
	}

	/// <summary>
	/// Returns a RGBA Cairo color using the given HsvColor.
	/// </summary>
	/// <param name="alpha">Alpha of the new Cairo color, 0 - 1</param>
	public static Color FromHsv (HsvColor hsv, double alpha = 1) => FromHsv (hsv.Hue, hsv.Sat, hsv.Val, alpha);

	/// <summary>
	/// Returns a RGBA Cairo color using the given HSV values.
	/// </summary>
	/// <param name="hue">Hue component, 0 - 360</param>
	/// <param name="sat">Saturation component, 0 - 1</param>
	/// <param name="value">Value component, 0 - 1</param>
	/// <param name="alpha">Alpha component, 0 - 1</param>
	public static Color FromHsv (double hue, double sat, double value, double alpha = 1)
	{
		// HsvColor contains values scaled as in the color wheel.
		// Scale Hue to be between 0 and 360. Saturation
		// and value scale to be between 0 and 1.
		double h = hue % 360.0;

		// Stupid hack!
		// If v or s is set to 0, it results in data loss for hue / sat. So we force it to be slightly above zero.
		double s =
			(sat == 0)
			? 0.0001
			: sat;
		double v =
			(value == 0)
			? 0.0001
			: value;

		// If s is 0, all colors are the same.
		// This is some flavor of gray.
		if (s == 0)
			return new Color (v, v, v, alpha);

		// The color wheel consists of 6 sectors.
		// Figure out which sector you're in.
		double sectorPos = h / 60;
		int sectorNumber = (int) Math.Floor (sectorPos);

		// get the fractional part of the sector.
		// That is, how many degrees into the sector
		// are you?
		double fractionalSector = sectorPos - sectorNumber;

		// Calculate values for the three axes
		// of the color.
		double p = v * (1 - s);
		double q = v * (1 - (s * fractionalSector));
		double t = v * (1 - (s * (1 - fractionalSector)));

		double r;
		double g;
		double b;

		// Assign the fractional colors to r, g, and b
		// based on the sector the angle is in.
		switch (sectorNumber) {
			case 0:
				r = v;
				g = t;
				b = p;
				break;

			case 1:
				r = q;
				g = v;
				b = p;
				break;

			case 2:
				r = p;
				g = v;
				b = t;
				break;

			case 3:
				r = p;
				g = q;
				b = v;
				break;

			case 4:
				r = t;
				g = p;
				b = v;
				break;

			case 5:
				r = v;
				g = p;
				b = q;
				break;

			default:
				r = 0;
				g = 0;
				b = 0;
				break;
		}

		// return an RgbColor structure, with values scaled
		// to be between 0 and 255.
		return new Color (r, g, b, alpha);
	}

	public static Color Lerp (in Color from, in Color to, double frac)
	{
		return new (
			R: Mathematics.Lerp (from.R, to.R, frac),
			G: Mathematics.Lerp (from.G, to.G, frac),
			B: Mathematics.Lerp (from.B, to.B, frac),
			A: Mathematics.Lerp (from.A, to.A, frac));
	}
}
