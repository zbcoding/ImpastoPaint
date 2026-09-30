using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The largest image Impasto can create is bounded by the bytes a surface's pixel span can
/// address (int.MaxValue), not only by cairo's per-side limit. Every entry point that turns
/// user input into a document size asks this predicate, so a wrong boundary either crashes on
/// the first pixel read or refuses images that would work.
/// </summary>
[TestFixture]
internal sealed class ImageSizeLimitTest
{
	[TestCase (23170, 23170, Description = "Largest square: 2,147,395,600 bytes")]
	[TestCase (30000, 10000, Description = "Wide but within the byte limit")]
	[TestCase (10000, 30000, Description = "Tall but within the byte limit")]
	[TestCase (32767, 16384, Description = "Widest side with the most rows that still fit")]
	[TestCase (32767, 1, Description = "Cairo's per-side limit")]
	[TestCase (1, 1)]
	public void SizesWhosePixelsFitInOneSpanAreSupported (int width, int height)
	{
		Assert.That (CairoExtensions.IsSupportedImageSize (new Size (width, height)), Is.True);
	}

	[TestCase (23171, 23171, Description = "One past the largest square")]
	[TestCase (30000, 30000, Description = "Each side is legal, but 3.6 GB is not")]
	[TestCase (32767, 16385, Description = "One row past the byte limit")]
	[TestCase (32768, 1, Description = "Past cairo's per-side limit")]
	[TestCase (1, 32768, Description = "Past cairo's per-side limit")]
	[TestCase (0, 100, Description = "Empty")]
	[TestCase (100, -1, Description = "Negative")]
	public void SizesPastEitherLimitAreRefused (int width, int height)
	{
		Assert.That (CairoExtensions.IsSupportedImageSize (new Size (width, height)), Is.False);
	}
}
