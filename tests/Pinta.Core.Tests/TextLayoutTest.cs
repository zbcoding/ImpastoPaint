using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class TextLayoutTest
{
	[TestCase (100, 90, 10, true)]
	[TestCase (100, 91, 10, true)]
	[TestCase (100, 89, 10, false)]
	public void IsJustificationSpacingAcceptable (
		int layoutWidth,
		int lineWidth,
		int naturalSpaceWidth,
		bool expected)
	{
		bool actual = TextLayout.IsJustificationSpacingAcceptable (
			layoutWidth,
			lineWidth,
			naturalSpaceWidth);

		Assert.That (actual, Is.EqualTo (expected));
	}
}
