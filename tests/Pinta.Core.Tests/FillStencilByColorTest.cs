using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class FillStencilByColorTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	// Global fill scans rows in parallel into one BitMask, whose bits are packed into shared machine
	// words. When a row's width is not a multiple of the word size, neighbouring rows share a word,
	// and two threads setting bits in it at once can each overwrite the other's update. A dropped
	// bit is a pixel the bucket or magic wand silently skips. The race is timing-dependent, so the
	// fill is repeated on images narrow enough that nearly every word straddles rows, and as tall as
	// cairo allows so the threads cross many of those words.
	[TestCase (1)]
	[TestCase (3)]
	public void EveryMatchingPixelIsSetAndBounded (int width)
	{
		const int Height = 32767;
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, Height);
		BitMask expected = PaintStripes (surface);
		using Region unusedLimit = CairoExtensions.CreateRegion (new RectangleI (0, 0, width, Height));
		BitMask stencil = new (width, Height);

		for (int attempt = 0; attempt < 40; ++attempt) {
			CairoExtensions.FillStencilByColor (surface, stencil, ColorBgra.Black, 0, out RectangleD bounds, unusedLimit, false);

			Assert.That (stencil, Is.EqualTo (expected), $"attempt {attempt}");
			Assert.That (bounds, Is.EqualTo (new RectangleD (0, 0, width, Height)), $"attempt {attempt}");
		}
	}

	// Column 0 is all black, so the fill's bounds span the whole image; elsewhere pixels are white
	// wherever (x + y) is a multiple of 3, so the stencil has misses to get wrong too. Returns the
	// stencil a correct black fill must produce.
	private static BitMask PaintStripes (ImageSurface surface)
	{
		int width = surface.Width;
		BitMask expected = new (width, surface.Height);
		System.Span<ColorBgra> pixels = surface.GetPixelData ();
		for (int y = 0; y < surface.Height; ++y) {
			for (int x = 0; x < width; ++x) {
				bool black = x == 0 || (x + y) % 3 != 0;
				pixels[y * width + x] = black ? ColorBgra.Black : ColorBgra.White;
				expected.Set (x, y, black);
			}
		}
		surface.MarkDirty ();
		return expected;
	}
}
