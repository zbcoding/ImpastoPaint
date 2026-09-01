using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The drop-index math the layers dock's drag-reorder used to run inline in the GTK drop handler
/// (c3962d95 moved the reorder itself to the next main-loop iteration to stop a re-entrant-drop
/// crash, but the index calculation was still only reachable through a real widget). Rows are drawn
/// top-first: a higher list index sits higher up, and a drop on the upper half of a target lands
/// above it.
/// </summary>
[TestFixture]
internal sealed class RowReorderTest
{
	// list: [0 1 2 3 4], drop row 1 ...
	[TestCase (1, 3, true, 3, TestName = "onto the top half of row 3 -> index 3")]
	[TestCase (1, 3, false, 2, TestName = "onto the bottom half of row 3 -> index 2")]
	[TestCase (3, 1, true, 2, TestName = "downward onto the top half of row 1 -> index 2")]
	[TestCase (3, 1, false, 1, TestName = "downward onto the bottom half of row 1 -> index 1")]
	public void ResolvesToTheExpectedInsertIndex (int from, int target, bool dropAbove, int expected)
		=> Assert.That (RowReorder.ResolveDropIndex (from, target, dropAbove), Is.EqualTo (expected));

	// A drop that would leave the row exactly where it is has to report "no move" so the caller
	// can bail before scheduling anything.
	[TestCase (2, 2, true, TestName = "top half of itself")]
	[TestCase (2, 2, false, TestName = "bottom half of itself")]
	[TestCase (2, 1, true, TestName = "top half of the row directly below (already there)")]
	[TestCase (2, 3, false, TestName = "bottom half of the row directly above (already there)")]
	public void ReportsNoMoveWhenThePositionDoesNotChange (int from, int target, bool dropAbove)
		=> Assert.That (RowReorder.ResolveDropIndex (from, target, dropAbove), Is.Null);
}
