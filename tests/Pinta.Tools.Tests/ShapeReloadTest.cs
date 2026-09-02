using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class ShapeReloadTest : ToolsTestHarness
{
	[TearDown]
	public void ClearSEngines ()
	{
		BaseEditEngine.SEngines.Clear ();

		// Every ShapeTool constructor registers itself into this static, process-lifetime map;
		// leaving this fixture's instance in it makes a later test's shape of the same type look
		// like it needs a mid-draw tool switch (see LineToolSegmentModeTest's own note).
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.OpenLineCurveSeries);
	}

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

	// SEngines is shared by every shape tool, and building one resets it - so a tool constructed
	// after a layer's engines were loaded emptied the list while the "which layer are these engines
	// for" binding still named that layer. EnsureShapesForCurrentLayer then saw the layer as already
	// loaded and left the list empty, and the tool's next persist wrote that emptiness back: every
	// shape on the layer silently gone.
	[Test]
	public void BuildingAnotherShapeToolDoesNotStrandTheCurrentLayersShapes ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, 8, 8)), "Box");
		LayerObjectSelection.RequestShapeReload (layer);
		Assert.That (BaseEditEngine.SEngines, Has.Count.EqualTo (1), "setup: the layer's shape is loaded");

		// Constructing a shape tool runs BaseEditEngine's constructor, which resets the shared list.
		// The canvas and CurrentTool are what the engine's redraw dereferences; see the note on
		// ShapeEditEngineDragStateTest.Activate for why the engine is driven directly here.
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		LineCurveTool tool = new (PintaCore.Services);
		CurrentToolProperty.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [tool]);
		tool.EditEngine.HandleActivated ();

		try {
			Assert.That (BaseEditEngine.SEngines, Has.Count.EqualTo (1),
				"activating has to reload the current layer's shapes into the emptied list");

			BaseEditEngine.PersistShapeObjects (layer);

			Assert.That (layer.ShapeObjects, Has.Count.EqualTo (1),
				"persisting must not write an empty engine list over the layer's shapes");
		} finally {
			tool.EditEngine.HandleDeactivated (null);
			CurrentToolProperty.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [null]);
		}
	}

	private static PropertyInfo CurrentToolProperty
		=> typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;
}
