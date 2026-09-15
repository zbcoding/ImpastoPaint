using NUnit.Framework;

namespace Pinta.Core.Tests;

// The status bar chip is a fixed-width slot, so a ratio only earns its place there when it is
// short enough to read at a glance and close enough to the real shape to be worth trusting.
[TestFixture]
public sealed class AspectRatioLabelTest
{
	[TestCase (800, 600, "4:3")]
	[TestCase (1920, 1080, "16:9")]
	[TestCase (1080, 1920, "9:16")]
	[TestCase (512, 512, "1:1")]
	public void Ratio_With_Short_Terms_Is_Exact (int width, int height, string expected)
	{
		Assert.That (ActionManager.GetAspectRatio (width, height), Is.EqualTo (expected));
	}

	[TestCase (1601, 1423, "≈9:8")]
	[TestCase (1366, 768, "≈16:9")]
	// Exactly a tenth of a percent off 1:1 - the tolerance is inclusive, and is decided by
	// integer products rather than by which way a double happened to round.
	[TestCase (1000, 999, "≈1:1")]
	public void Ratio_With_Long_Terms_Falls_Back_To_Closest_Short_Terms (int width, int height, string expected)
	{
		Assert.That (ActionManager.GetAspectRatio (width, height), Is.EqualTo (expected));
	}

	// A shape no two-digit ratio describes, or no shape at all, leaves the chip showing only the
	// pixel dimensions rather than a ratio that is either unreadable or a lie.
	[TestCase (4000, 3)]
	[TestCase (1, 100000)]
	[TestCase (0, 600)]
	public void Ratio_Is_Dropped_When_No_Short_Terms_Fit (int width, int height)
	{
		Assert.That (ActionManager.GetAspectRatio (width, height), Is.Empty);
	}
}
