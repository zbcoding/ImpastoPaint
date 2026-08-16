using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Color = Cairo.Color;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class PaletteHelperTests
{
	// The 14 dark "127,x" hues that Pinta's status-bar palette (PR #154) shipped and
	// that issue #812 reported missing from the default palette.
	private static readonly (byte r, byte g, byte b)[] dark_hues = [
		(127, 0, 0), (127, 51, 0), (127, 106, 0), (91, 127, 0), (38, 127, 0),
		(0, 127, 14), (0, 127, 70), (0, 127, 127), (0, 74, 127), (0, 19, 127),
		(33, 0, 127), (87, 0, 127), (127, 0, 110), (127, 0, 55),
	];

	[Test]
	public void DefaultPaletteHasThirtyFourColors ()
	{
		List<Color> colors = PaletteHelper.EnumerateDefaultColors ().ToList ();
		Assert.That (colors, Has.Count.EqualTo (34));
	}

	[Test]
	public void ExtendedPaletteRestoresTheFourteenDarkHues ()
	{
		List<Color> colors = PaletteHelper.EnumerateDefaultColors (includeDarkRow: true).ToList ();
		Assert.That (colors, Has.Count.EqualTo (48));

		foreach ((byte r, byte g, byte b) in dark_hues) {
			int index = colors.FindIndex (c => ColorsMatch (c, r, g, b));
			Assert.That (index, Is.GreaterThanOrEqualTo (0), $"({r},{g},{b}) missing from extended palette");
			// The widget lays out PALETTE_ROWS=3 as a column-major grid, so the extra
			// darker row is every third swatch (index % 3 == 2).
			Assert.That (index % 3, Is.EqualTo (2), $"({r},{g},{b}) should sit in the third row");
		}
	}

	[TestCase (false, 8)]
	[TestCase (true, 12)]
	public void DefaultRecentColorCountTracksPaletteRows (bool extendedPaletteRows, int expected)
		=> Assert.That (PaletteHelper.GetDefaultRecentColorCount (extendedPaletteRows), Is.EqualTo (expected));

	[Test]
	public void RecentColorLimitAllowsTwentyFourColors ()
		=> Assert.That (PaletteHelper.MAX_RECENT_COLOR_COUNT, Is.EqualTo (24));

	private static bool ColorsMatch (Color color, byte r, byte g, byte b)
		=> (int) Math.Round (color.R * 255) == r
			&& (int) Math.Round (color.G * 255) == g
			&& (int) Math.Round (color.B * 255) == b;
}
