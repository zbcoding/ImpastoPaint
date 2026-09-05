using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class OtherExtensionsTest
{
	[TestCaseSource (nameof (create_polygon_set_arguments_for_empty))]
	public void EmptyStencilReturnsEmptyPolygonSet (RectangleD bounds, PointI translateOffset)
	{
		BitMask bitmask = new (0, 0);
		var polygonSet = bitmask.CreatePolygonSet (bounds, translateOffset);
		Assert.That (polygonSet.Count, Is.Zero);
	}

	private static readonly IReadOnlyList<TestCaseData> create_polygon_set_arguments_for_empty = [
		new (new RectangleD (0, 0, 1, 1), new PointI (1, 1)),
	];

	/// <summary>
	/// The island search stopped one short of the bounds' inclusive Right/Bottom, so an island that
	/// lives only in the region's last column or row - a stray matching pixel at the far edge of a
	/// global fill - produced no polygon at all, and the magic wand missed it.
	/// </summary>
	[TestCase (7, 3, TestName = "island in the last column")]
	[TestCase (3, 7, TestName = "island in the last row")]
	[TestCase (7, 7, TestName = "island in the far corner")]
	[TestCase (0, 0, TestName = "island in the first corner")]
	public void IslandAtTheRegionEdgeIsTraced (int x, int y)
	{
		BitMask stencil = new (8, 8);
		stencil.Set (x, y, true);

		var polygonSet = stencil.CreatePolygonSet (new RectangleD (0, 0, 8, 8), PointI.Zero);

		Assert.That (polygonSet, Has.Count.EqualTo (1));
		Assert.That (polygonSet[0], Has.Member (new PointI (x, y)));
	}

	[Test]
	public void EveryIslandIsTracedIncludingTheOneAtTheEdge ()
	{
		BitMask stencil = new (8, 8);
		stencil.Set (new RectangleI (1, 1, 3, 3), true);
		stencil.Set (7, 7, true);

		var polygonSet = stencil.CreatePolygonSet (new RectangleD (0, 0, 8, 8), PointI.Zero);

		Assert.That (polygonSet, Has.Count.EqualTo (2));
	}
}
