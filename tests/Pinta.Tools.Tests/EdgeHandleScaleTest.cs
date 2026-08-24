using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// BaseTransformTool.ComputeEdgeScaleTransform collapses the Left/Right/Up/Down edge-handle drag
/// cases (T12's leftover) into one block parameterized on axis and which edge is under the cursor.
/// These pin the four handles' distinct anchor/sign behavior so a future edit to the shared formula
/// cannot silently swap an axis or drop a sign for just one handle.
/// </summary>
[TestFixture]
internal sealed class EdgeHandleScaleTest : ToolsTestHarness
{
	private static readonly RectangleD Rect = new (0, 0, 20, 10);
	private static readonly PointD Center = Rect.GetCenter ();

	[Test]
	public void LeftEdgeAwayFromCenterAnchorsTheRightEdge ()
	{
		// Dragging the left edge from x=0 to x=-10 (opposite/right edge at x=20 fixed).
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (-10, 0), horizontal: true, nearIsMin: true, fromCenter: false, keepAspect: false);

		Assert.Multiple (() => {
			Assert.That (t.TransformPoint (new PointD (0, 0)), Is.EqualTo (new PointD (-10, 0)).Using (PointComparer),
				"the dragged (left) edge has to land under the mouse");
			Assert.That (t.TransformPoint (new PointD (20, 10)), Is.EqualTo (new PointD (20, 10)).Using (PointComparer),
				"the opposite (right) edge is the anchor and must not move");
		});
	}

	[Test]
	public void RightEdgeAwayFromCenterAnchorsTheLeftEdge ()
	{
		// Dragging the right edge from x=20 to x=30 (opposite/left edge at x=0 fixed).
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (30, 0), horizontal: true, nearIsMin: false, fromCenter: false, keepAspect: false);

		Assert.Multiple (() => {
			Assert.That (t.TransformPoint (new PointD (20, 0)), Is.EqualTo (new PointD (30, 0)).Using (PointComparer),
				"the dragged (right) edge has to land under the mouse");
			Assert.That (t.TransformPoint (new PointD (0, 10)), Is.EqualTo (new PointD (0, 10)).Using (PointComparer),
				"the opposite (left) edge is the anchor and must not move");
		});
	}

	[Test]
	public void UpEdgeAwayFromCenterAnchorsTheBottomEdge ()
	{
		// Dragging the top edge from y=0 to y=-5 (opposite/bottom edge at y=10 fixed).
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (0, -5), horizontal: false, nearIsMin: true, fromCenter: false, keepAspect: false);

		Assert.Multiple (() => {
			Assert.That (t.TransformPoint (new PointD (0, 0)), Is.EqualTo (new PointD (0, -5)).Using (PointComparer),
				"the dragged (top) edge has to land under the mouse");
			Assert.That (t.TransformPoint (new PointD (20, 10)), Is.EqualTo (new PointD (20, 10)).Using (PointComparer),
				"the opposite (bottom) edge is the anchor and must not move");
		});
	}

	[Test]
	public void DownEdgeAwayFromCenterAnchorsTheTopEdge ()
	{
		// Dragging the bottom edge from y=10 to y=15 (opposite/top edge at y=0 fixed).
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (0, 15), horizontal: false, nearIsMin: false, fromCenter: false, keepAspect: false);

		Assert.Multiple (() => {
			Assert.That (t.TransformPoint (new PointD (0, 10)), Is.EqualTo (new PointD (0, 15)).Using (PointComparer),
				"the dragged (bottom) edge has to land under the mouse");
			Assert.That (t.TransformPoint (new PointD (20, 0)), Is.EqualTo (new PointD (20, 0)).Using (PointComparer),
				"the opposite (top) edge is the anchor and must not move");
		});
	}

	[Test]
	public void FromCenterScalesBothEdgesSymmetrically ()
	{
		// Ctrl-drag the left edge to x=5 - a scale about the center, not the opposite edge.
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (5, 0), horizontal: true, nearIsMin: true, fromCenter: true, keepAspect: false);

		Assert.Multiple (() => {
			Assert.That (t.TransformPoint (new PointD (0, 0)), Is.EqualTo (new PointD (5, 0)).Using (PointComparer),
				"the dragged (left) edge lands under the mouse");
			Assert.That (t.TransformPoint (new PointD (20, 0)), Is.EqualTo (new PointD (15, 0)).Using (PointComparer),
				"the opposite (right) edge moves the same amount the other way - it is not the anchor");
			Assert.That (t.TransformPoint (Center), Is.EqualTo (Center).Using (PointComparer),
				"the center itself is the fixed point of a from-center scale");
		});
	}

	[Test]
	public void KeepAspectAppliesTheSameRatioToTheOtherAxis ()
	{
		// Left edge dragged to x=-10 doubles the width (20 -> 40); height must double to match.
		Matrix t = BaseTransformTool.ComputeEdgeScaleTransform (
			Rect, Center, new PointD (-20, 0), horizontal: true, nearIsMin: true, fromCenter: false, keepAspect: true);

		// Anchored on the right edge (x=20, y=center=5): top-left corner (0,0) is 20 wide, 5 tall from
		// the anchor: doubling width also doubles that vertical offset from the anchor.
		Assert.That (t.TransformPoint (new PointD (0, 0)), Is.EqualTo (new PointD (-20, -5)).Using (PointComparer),
			"height has to scale by the same ratio as the dragged width");
	}

	private static readonly PointDApproximatelyEqualComparer PointComparer = new ();

	private sealed class PointDApproximatelyEqualComparer : System.Collections.Generic.IComparer<PointD>
	{
		public int Compare (PointD x, PointD y)
		{
			bool close = System.Math.Abs (x.X - y.X) < 1e-9 && System.Math.Abs (x.Y - y.Y) < 1e-9;
			return close ? 0 : 1;
		}
	}
}
