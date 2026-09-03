using NUnit.Framework;

namespace Pinta.Tools.Tests;

// The arrow size/angle/length spins accept values that are legal but almost never what the
// user meant: an arrowhead smaller than its line reads as a bump, and negative angle/length
// mirror the head backwards. The toolbar nudges those yellow instead of rejecting them, and
// this pins exactly which combinations warn.
[TestFixture]
internal sealed class ArrowWarningsTest
{
	[Test]
	public void SaneValuesWarnNothing ()
	{
		Assert.That (ArrowedEditEngine.ArrowWarnings (arrowSize: 10, lineWidth: 2, angle: 15, length: 10),
			Is.EqualTo ((Size: false, Angle: false, Length: false)));
	}

	[Test]
	public void SmallHeadWarnsSizeOnly ()
	{
		Assert.That (ArrowedEditEngine.ArrowWarnings (arrowSize: 2, lineWidth: 6, angle: 15, length: 10),
			Is.EqualTo ((Size: true, Angle: false, Length: false)));
	}

	[Test]
	public void NegativeAngleAndLengthWarnIndependently ()
	{
		Assert.That (ArrowedEditEngine.ArrowWarnings (arrowSize: 10, lineWidth: 2, angle: -10, length: 10),
			Is.EqualTo ((Size: false, Angle: true, Length: false)));

		Assert.That (ArrowedEditEngine.ArrowWarnings (arrowSize: 10, lineWidth: 2, angle: 15, length: -5),
			Is.EqualTo ((Size: false, Angle: false, Length: true)));
	}

	[Test]
	public void BoundariesDoNotWarn ()
	{
		Assert.That (ArrowedEditEngine.ArrowWarnings (arrowSize: 2, lineWidth: 2, angle: 0, length: 0),
			Is.EqualTo ((Size: false, Angle: false, Length: false)),
			"equal size and zeroes are the neutral positions, not mistakes");
	}
}
