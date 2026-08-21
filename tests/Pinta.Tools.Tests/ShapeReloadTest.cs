using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class ShapeReloadTest : ToolsTestHarness
{
	[TearDown]
	public void ClearSEngines () => BaseEditEngine.SEngines.Clear ();

	// The bug this pins: a shape tool's own live copy of a shape's control points
	// (BaseEditEngine.SEngines) is a snapshot taken when the engine was built, not a reference into
	// UserLayer.Objects - it does not automatically track a ShapeObject moving. Anything that moves a
	// shape's coordinates from outside the tool (UserLayer.TranslateObjects after a canvas grow, a
	// bake, an undo) has to explicitly ask for a reload through
	// LayerObjectSelection.RequestShapeReload, or the tool's next redraw uses the stale, pre-move
	// control points. Document.ResizeCanvas fires that reload now; this is the Tools-side half of the
	// fix, verifying the reload actually rebuilds SEngines rather than just that the request went out.
	[Test]
	public void ReloadRebuildsSEnginesFromTheShapeObjectsCurrentPosition ()
	{
		UserLayer layer = Layer (0);
		ShapeObject shape = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, 8, 8));
		AddObject (layer, shape, "Box");

		BaseEditEngine.SEngines.Clear ();
		BaseEditEngine.SEngines.Add (LiveEngine (layer, shape));
		PointD staleCorner = BaseEditEngine.SEngines[0].ControlPoints[0].Position;

		// Move the ShapeObject the same way UserLayer.TranslateObjects does: mutate the control
		// points directly, entirely outside the tool that built SEngines.
		PointD delta = new (10, 10);
		foreach (ShapeControlPoint cp in shape.ControlPoints)
			cp.Position += delta;

		Assert.That (BaseEditEngine.SEngines[0].ControlPoints[0].Position, Is.EqualTo (staleCorner),
			"before a reload, the live copy has to still be stale - otherwise there is nothing this fix needed to do");

		LayerObjectSelection.RequestShapeReload (layer);

		Assert.That (BaseEditEngine.SEngines[0].ControlPoints[0].Position, Is.EqualTo (staleCorner + delta),
			"the reload has to rebuild the live copy from the shape's current, moved position");
	}
}
