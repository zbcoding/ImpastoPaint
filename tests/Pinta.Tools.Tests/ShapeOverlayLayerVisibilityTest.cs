using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A shape tool's control-point handles and "Obj." badge overlay are keyed to the current layer,
/// but nothing refreshed either one when that layer was hidden or deleted out from under it:
/// Handles was live-polled with no Hidden check, and DrawShapeBadges only ever ran from tool
/// activity (adding a point, switching the *selected* layer, deactivating), none of which fires
/// for a Hidden toggle or DeleteLayer. Both left stale editing chrome for a layer that no longer
/// showed - or no longer existed - on screen.
/// </summary>
[TestFixture]
internal sealed class ShapeOverlayLayerVisibilityTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

	private BaseEditEngine? engine;

	[TearDown]
	public void Deactivate ()
	{
		engine?.HandleDeactivated (null);
		engine = null;
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		BaseEditEngine.SEngines.Clear ();

		// RectangleTool's constructor registers itself into this static, process-lifetime map;
		// leaving it there would make an unrelated later test's ClosedLineCurveSeries-typed shape
		// look like it needs a tool switch mid-draw when some other tool is current. Also drop the
		// static runtime_layer binding left pointing at this test's (possibly just-deleted) layer,
		// so a later test starts from "no layer bound yet" instead of a stale mismatch.
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.ClosedLineCurveSeries);
		typeof (BaseEditEngine).GetField ("runtime_layer", BindingFlags.NonPublic | BindingFlags.Static)!
			.SetValue (null, null);
	}

	// HandleActivated alone (not the full ShapeTool.OnActivated/DoActivated path) is enough to wire
	// the layer-event subscriptions this test is about, without needing a realized canvas/window
	// for the rest of tool activation. CurrentTool still has to be set: DrawActiveShape's hover
	// handling reads PintaCore.Tools.CurrentTool directly to set the cursor. RectangleTool, not
	// LineCurveTool: constructing a tool registers it into the static, process-lifetime
	// CorrespondingTools[ShapeType] map, and every generic test shape from Box/Polygon below
	// hardcodes ShapeType = OpenLineCurveSeries (LineCurveTool's type) regardless of which tool
	// edits it - registering that one here would make an unrelated later test's shape (edited by
	// some other tool entirely) look like it needs a tool switch mid-draw.
	private BaseEditEngine Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		RectangleTool tool = new (PintaCore.Services);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [tool]);

		tool.EditEngine.HandleActivated ();
		engine = tool.EditEngine;
		return engine;
	}

	// Object mode only: DrawShapeBadges skips a Raster-mode shape (it has no re-edit affordance).
	private ShapeEngine AddObjectModeShape (UserLayer layer)
	{
		ShapeObject shape = Box (new Color (1, 0, 0, 1), new RectangleI (2, 2, 10, 10));
		ShapeEngine shapeEngine = LiveEngine (layer, shape);
		shapeEngine.RasterizeOnFinalize = false;
		ShapeEngineCollection.Store (layer, [shapeEngine]);
		return shapeEngine;
	}

	private static void InvokeDrawShapeBadges ()
		=> typeof (BaseEditEngine).GetMethod ("DrawShapeBadges", NonPublicStatic)!.Invoke (null, null);

	[Test]
	public void HidingTheCurrentLayerClearsShapeHandlesAndBadges ()
	{
		UserLayer layer = Layer (0);
		BaseEditEngine ee = Activate ();

		AddObjectModeShape (layer);
		BaseEditEngine.ReloadLayerShapes (layer);
		InvokeDrawShapeBadges ();

		Assert.That (ee.Handles.Count (), Is.GreaterThan (0),
			"setup: the shape's control points should show while its layer is visible");
		Assert.That (Document.Layers.OverlayLayer.Hidden, Is.False,
			"setup: the Obj. badge overlay should be showing");

		layer.Hidden = true;

		Assert.That (ee.Handles.Count (), Is.EqualTo (0),
			"control points must not show over a hidden layer");
		Assert.That (Document.Layers.OverlayLayer.Hidden, Is.True,
			"the Obj. badge overlay must clear when the current layer is hidden");
	}

	[Test]
	public void DeletingTheLayerAShapesBadgeBelongsToClearsTheOverlay ()
	{
		UserLayer shapeLayer = Layer (0);
		BaseEditEngine ee = Activate ();

		AddObjectModeShape (shapeLayer);
		BaseEditEngine.ReloadLayerShapes (shapeLayer);
		InvokeDrawShapeBadges ();

		Assert.That (Document.Layers.OverlayLayer.Hidden, Is.False,
			"setup: the Obj. badge overlay should be showing");

		Document.Layers.AddNewLayer (string.Empty); // second layer, so deleting the first is valid
		Document.Layers.SetCurrentUserLayer (0);
		BaseEditEngine.ReloadLayerShapes (shapeLayer);
		InvokeDrawShapeBadges ();

		Document.Layers.DeleteLayer (0);

		Assert.That (Document.Layers.OverlayLayer.Hidden, Is.True,
			"the Obj. badge overlay must clear once the layer its shape belonged to is deleted");
		// Handles always includes the hover-position indicator regardless of shape state; what
		// must be gone is the deleted layer's own shape engine (and, with it, its control points).
		Assert.That (BaseEditEngine.SEngines, Is.Empty,
			"control points must not keep showing for a shape whose layer no longer exists");
	}
}
