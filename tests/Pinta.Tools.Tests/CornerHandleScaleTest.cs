using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// BaseTransformTool.ComputeCornerScaleTransform collapses HandleGripDrag's two near-identical
/// corner-drag branches (scale-from-opposite-corner vs scale-from-center) into one formula
/// parameterized on the anchor point and its extent on each axis. These pin the two anchor modes'
/// distinct fixed-point and keepAspect behavior so a future edit to the shared formula cannot
/// silently drop the anchor invariant or break aspect clamping for just one mode.
/// </summary>
[TestFixture]
internal sealed class CornerHandleScaleTest : ToolsTestHarness
{
	private static readonly RectangleD Rect = new (0, 0, 20, 10);
	private static readonly PointD TopLeft = new (0, 0);
	private static readonly PointD BottomRight = new (20, 10);
	private static readonly PointD Center = Rect.GetCenter (); // (10, 5)

	[Test]
	public void FromOppositeCornerAnchorsTheOppositeCornerAndDraggedCornerLandsUnderMouse ()
	{
		// Dragging the top-left corner from (0,0) to (-10,-5); the opposite (bottom-right) corner
		// at (20,10) is the anchor and must not move.
		Matrix t = BaseTransformTool.ComputeCornerScaleTransform (
			TopLeft, new PointD (-10, -5), BottomRight, Rect.Width, Rect.Height, keepAspect: false);

		Assert.Multiple (() => {
			PointD dragged = t.TransformPoint (TopLeft);
			Assert.That (dragged.X, Is.EqualTo (-10).Within (1e-9), "the dragged corner has to land under the mouse (X)");
			Assert.That (dragged.Y, Is.EqualTo (-5).Within (1e-9), "the dragged corner has to land under the mouse (Y)");

			PointD anchor = t.TransformPoint (BottomRight);
			Assert.That (anchor.X, Is.EqualTo (BottomRight.X).Within (1e-9), "the opposite corner is the anchor and must not move (X)");
			Assert.That (anchor.Y, Is.EqualTo (BottomRight.Y).Within (1e-9), "the opposite corner is the anchor and must not move (Y)");
		});
	}

	[Test]
	public void FromCenterAnchorsTheCenterAndScalesTheOppositeCornerSymmetrically ()
	{
		// Ctrl-drag the top-left corner from (0,0) to (-10,-5): a scale about the center, not the
		// opposite corner.
		Matrix t = BaseTransformTool.ComputeCornerScaleTransform (
			TopLeft, new PointD (-10, -5), Center, Rect.Width / 2, Rect.Height / 2, keepAspect: false);

		Assert.Multiple (() => {
			PointD dragged = t.TransformPoint (TopLeft);
			Assert.That (dragged.X, Is.EqualTo (-10).Within (1e-9), "the dragged corner lands under the mouse (X)");
			Assert.That (dragged.Y, Is.EqualTo (-5).Within (1e-9), "the dragged corner lands under the mouse (Y)");

			PointD opposite = t.TransformPoint (BottomRight);
			Assert.That (opposite.X, Is.EqualTo (30).Within (1e-9),
				"the opposite corner moves the same amount the other way - it is not the anchor (X)");
			Assert.That (opposite.Y, Is.EqualTo (15).Within (1e-9),
				"the opposite corner moves the same amount the other way - it is not the anchor (Y)");

			PointD center = t.TransformPoint (Center);
			Assert.That (center.X, Is.EqualTo (Center.X).Within (1e-9), "the center itself is the fixed point (X)");
			Assert.That (center.Y, Is.EqualTo (Center.Y).Within (1e-9), "the center itself is the fixed point (Y)");
		});
	}

	[Test]
	public void KeepAspectClampsTheSmallerAxisRatioUpToMatchTheLarger ()
	{
		// Dragging the top-left corner to (-20, -2): the X ratio (40/20 = 2) is larger than the Y
		// ratio (12/10 = 1.2), so Y must be stretched up to match X's ratio instead of landing
		// exactly under the mouse on Y.
		Matrix t = BaseTransformTool.ComputeCornerScaleTransform (
			TopLeft, new PointD (-20, -2), BottomRight, Rect.Width, Rect.Height, keepAspect: true);

		PointD dragged = t.TransformPoint (TopLeft);
		Assert.Multiple (() => {
			Assert.That (dragged.X, Is.EqualTo (-20).Within (1e-9), "the larger-ratio axis lands exactly under the mouse");
			Assert.That (dragged.Y, Is.EqualTo (-10).Within (1e-9),
				"the smaller-ratio axis has to scale by the same ratio as the larger one, not the raw mouse position");
		});
	}
}
