using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class HomographyTest
{
	// Dragging one corner of a square outwards: the inverse mapping's vanishing line runs through
	// the canvas origin, which the solve could not express before it re-centred the points, so the
	// perspective grip froze on this exact drag.
	[Test]
	public void SolvesAMappingWhoseVanishingLineCrossesTheOrigin ()
	{
		PointD[] dragged = [new (8, 8), new (16, 8), new (24, 24), new (8, 16)];
		PointD[] square = [new (8, 8), new (16, 8), new (16, 16), new (8, 16)];

		Assert.That (Homography.TryCreate (dragged, square, out Homography map), Is.True);
		for (int i = 0; i < 4; i++) {
			Assert.That (map.TryMap (dragged[i], out PointD mapped), Is.True);
			Assert.That (mapped.X, Is.EqualTo (square[i].X).Within (1e-9));
			Assert.That (mapped.Y, Is.EqualTo (square[i].Y).Within (1e-9));
		}
	}

	[Test]
	public void RejectsAQuadWithThreeCornersOnALine ()
	{
		PointD[] collapsed = [new (0, 0), new (5, 0), new (10, 0), new (0, 10)];
		PointD[] square = [new (0, 0), new (10, 0), new (10, 10), new (0, 10)];

		Assert.That (Homography.TryCreate (collapsed, square, out _), Is.False);
	}
}
