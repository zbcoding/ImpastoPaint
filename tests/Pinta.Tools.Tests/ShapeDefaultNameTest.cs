using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Default shape names have to be unique within their layer: the layers dock lists one sub-row per
/// object with nothing but the name to tell them apart, and the rasterize prompt names the objects
/// it is about to bake the same way. A session-global running counter could not see the shapes that
/// arrive already named — from a reloaded file, a merged-down layer, a duplicated one — so drawing
/// after any of those handed out a name the layer was already using, and the user got two rows
/// (and two prompt entries) reading "Open Curve Shape 1".
/// </summary>
[TestFixture]
internal sealed class ShapeDefaultNameTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private LineCurveTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is not null) {
			tool.EditEngine.HandleDeactivated (null);
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			tool = null;
		}

		BaseEditEngine.SEngines.Clear ();

		// Every ShapeTool constructor registers itself into this static, process-lifetime map;
		// leaving this fixture's instance in it makes a later test's shape of the same type look
		// like it needs a mid-draw tool switch (see LineToolSegmentModeTest's own note).
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.OpenLineCurveSeries);
	}

	// Same headless activation ShapeEditEngineDragStateTest uses - see its note on why the engine is
	// driven directly instead of through BaseTool.DoActivated.
	private LineCurveTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		LineCurveTool t = new (PintaCore.Services);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		t.EditEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "line");

		// Object mode, the one that leaves shapes as named sub-rows. In Raster mode (the default)
		// starting a second shape bakes the first, so there is nothing to collide with.
		ToolBarDropDownButton rasterizeButton = (ToolBarDropDownButton)
			typeof (BaseEditEngine).GetField ("rasterize_mode_button", NonPublicInstance)!.GetValue (t.EditEngine)!;
		rasterizeButton.SelectedIndex = 0; // Known baseline: Raster, so selecting Object fires the handler.
		rasterizeButton.SelectedIndex = 1;

		t.EditEngine.HandleActivated ();

		tool = t;
		return t;
	}

	// Ctrl starts a fresh shape rather than extending the open curve already being edited, which is
	// how the tool separates two curves drawn back to back.
	private static void DrawShape (BaseEditEngine engine, PointD from, PointD to, bool startNew = false)
	{
		Document doc = PintaCore.Workspace.ActiveDocument;
		engine.HandleMouseDown (doc, MouseArgs (from, startNew));
		engine.HandleMouseMove (doc, MouseArgs (to));
		engine.HandleMouseUp (doc, MouseArgs (to));
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos, bool control = false) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
		State = control ? Gdk.ModifierType.ControlMask : Gdk.ModifierType.NoModifierMask,
	};

	[Test]
	public void ShapeDrawnOnALayerThatAlreadyCarriesDefaultNamesGetsAFreeNumber ()
	{
		UserLayer layer = Layer (0);
		// Shapes that came from somewhere the tool never saw: a reloaded file, a merged-down layer.
		for (int i = 1; i <= 3; ++i)
			AddObject (layer, NamedBox ($"Open Curve Shape {i}", new RectangleI (2 * i, 2, 4, 4)), "Existing shape");

		LineCurveTool t = Activate ();
		DrawShape (t.EditEngine, new PointD (18, 18), new PointD (26, 26));
		BaseEditEngine.PersistShapeObjects (layer);

		string[] names = [.. layer.ShapeObjects.Select (shape => shape.Name)];

		Assert.Multiple (() => {
			Assert.That (names, Is.Unique,
				"two sub-rows sharing a name are indistinguishable in the dock and in the rasterize prompt");
			Assert.That (names, Is.EqualTo (new[] {
				"Open Curve Shape 1", "Open Curve Shape 2", "Open Curve Shape 3", "Open Curve Shape 4" }),
				"the numbering picks up from what the layer already carries, leaving those names alone");
		});
	}

	private static ShapeObject NamedBox (string name, RectangleI region)
	{
		ShapeObject shape = Box (new Color (0, 0, 1), region);
		shape.Name = name;
		return shape;
	}

	[Test]
	public void ShapesDrawnBackToBackKeepCountingUp ()
	{
		UserLayer layer = Layer (0);

		LineCurveTool t = Activate ();
		DrawShape (t.EditEngine, new PointD (4, 4), new PointD (10, 10));
		DrawShape (t.EditEngine, new PointD (18, 18), new PointD (26, 26), startNew: true);
		BaseEditEngine.PersistShapeObjects (layer);

		Assert.That (layer.ShapeObjects.Select (shape => shape.Name),
			Is.EqualTo (new[] { "Open Curve Shape 1", "Open Curve Shape 2" }),
			"an empty layer still numbers from one, in drawing order");
	}
}
