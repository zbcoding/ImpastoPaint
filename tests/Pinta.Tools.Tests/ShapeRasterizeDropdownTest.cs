using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The shape tool's Raster/Object mode dropdown only stamped the mode for the NEXT shape created -
/// switching it while an existing shape was selected/being edited had no effect on that shape at
/// all, unlike TextTool's identical dropdown, which already retroactively converts the object
/// currently being edited. Changing the dropdown now does the same for the selected shape.
/// </summary>
[TestFixture]
internal sealed class ShapeRasterizeDropdownTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
	private static readonly Color ShapeFill = new (0, 1, 0, 1);

	private static ToolBarDropDownButton RasterizeModeButton (BaseEditEngine engine)
		=> (ToolBarDropDownButton) typeof (BaseEditEngine).GetField ("rasterize_mode_button", NonPublicInstance)!.GetValue (engine)!;

	[Test]
	public void SwitchingDropdownToObjectFlipsTheSelectedShapeBackFromRaster ()
	{
		UserLayer layer = Layer (0);
		ShapeObject source = Box (ShapeFill, new RectangleI (4, 4, CanvasSize - 8, CanvasSize - 8));
		source.RasterizeOnFinalize = true; // starts in Raster mode, matching the tool's current default.
		layer.AddShape (source);

		RectangleTool tool = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [tool]);

		try {
			BaseEditEngine.ReloadLayerShapes (layer);
			PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

			BaseEditEngine editEngine = (tool.EditEngine as BaseEditEngine)!;
			editEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "shapetest");
			editEngine.SelectedShapeIndex = 0;

			ToolBarDropDownButton rasterizeButton = RasterizeModeButton (editEngine);
			rasterizeButton.SelectedIndex = 0; // Known baseline: Raster, matching the shape's own state.

			rasterizeButton.SelectedIndex = 1; // Object

			Assert.Multiple (() => {
				Assert.That (editEngine.ActiveShapeEngine!.RasterizeOnFinalize, Is.False,
					"choosing Object for the shape currently selected/being edited must flip it, not just affect future shapes");
				Assert.That (layer.ShapeObjects[0].RasterizeOnFinalize, Is.False,
					"the flip must persist back to the stored object, which is what the layers dock reads");
				Assert.That (UserLayer.GetsSubRow (layer.ShapeObjects[0]), Is.True,
					"switching back to Object must restore the shape's sub-row in the layers dock");
			});
		} finally {
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			BaseEditEngine.SEngines.Clear ();
		}
	}

	/// <summary>
	/// The dropdown's change handler persists the live engines so the layers dock picks up the
	/// re-stamped mode. It must persist only into the layer those engines actually belong to: if
	/// the current layer has moved on since editing started, an unguarded persist rebuilds the new
	/// current layer's shape list from an engine list that isn't its own - which, filtered to that
	/// layer, is empty - and wipes its stored shapes.
	/// </summary>
	[Test]
	public void SwitchingTheDropdownNeverPersistsIntoADifferentCurrentLayer ()
	{
		UserLayer editedLayer = Layer (0);
		ShapeObject editedShape = Box (ShapeFill, new RectangleI (2, 2, 8, 8));
		editedShape.RasterizeOnFinalize = true;
		editedLayer.AddShape (editedShape);

		Document.Layers.AddNewLayer (string.Empty); // becomes the current layer
		UserLayer otherLayer = Layer (1);
		otherLayer.AddShape (Box (ShapeFill, new RectangleI (4, 4, 10, 10)));

		RectangleTool tool = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [tool]);

		try {
			SetCurrentLayerIndex (0); // editedLayer, so ReloadLayerShapes binds the engines to it
			BaseEditEngine.ReloadLayerShapes (editedLayer);
			PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

			BaseEditEngine editEngine = (tool.EditEngine as BaseEditEngine)!;
			editEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "shapetest");
			editEngine.SelectedShapeIndex = 0;

			SetCurrentLayerIndex (1); // current layer moves to otherLayer; the engines stay on editedLayer

			int otherLayerShapes = otherLayer.ShapeObjects.Count;

			ToolBarDropDownButton rasterizeButton = RasterizeModeButton (editEngine);
			rasterizeButton.SelectedIndex = 0; // Known baseline: Raster.
			rasterizeButton.SelectedIndex = 1; // Object - fires the handler, which persists.

			Assert.That (otherLayer.ShapeObjects.Count, Is.EqualTo (otherLayerShapes),
				"the persist must be scoped to the engines' own layer, not clobber whichever layer is current now");
		} finally {
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			BaseEditEngine.SEngines.Clear ();
		}
	}

	/// <summary>
	/// Selecting an existing shape refreshes the toolbar from that shape (UpdateToolbarSettings).
	/// The Raster/Object dropdown has to move with it, or it keeps showing the tool's last-used
	/// default and silently misreports the selected shape's real mode - and re-clicking the value
	/// already shown is a no-op, so the user can't even correct it.
	/// </summary>
	[Test]
	public void SelectingAnObjectModeShapeSyncsTheDropdownAwayFromRaster ()
	{
		UserLayer layer = Layer (0);
		ShapeObject source = Box (ShapeFill, new RectangleI (4, 4, CanvasSize - 8, CanvasSize - 8));
		source.RasterizeOnFinalize = false; // Object mode
		layer.AddShape (source);

		RectangleTool tool = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [tool]);

		try {
			BaseEditEngine.ReloadLayerShapes (layer);
			PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

			BaseEditEngine editEngine = (tool.EditEngine as BaseEditEngine)!;
			editEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "shapetest");

			ToolBarDropDownButton rasterizeButton = RasterizeModeButton (editEngine);
			rasterizeButton.SelectedIndex = 0; // Dropdown shows Raster; the shape about to be selected is Object.

			editEngine.SelectedShapeIndex = 0;
			// The seam UpdateToolbarSettingsForActiveShape drives whenever a shape becomes selected.
			editEngine.UpdateToolbarSettings (editEngine.ActiveShapeEngine!);

			Assert.That (rasterizeButton.SelectedIndex, Is.EqualTo (1),
				"selecting an Object-mode shape must move the dropdown to Object, not leave it on the last Raster default");
		} finally {
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			BaseEditEngine.SEngines.Clear ();
		}
	}

	private void SetCurrentLayerIndex (int index)
		=> typeof (DocumentLayers).GetProperty (nameof (DocumentLayers.CurrentUserLayerIndex))!
			.GetSetMethod (nonPublic: true)!.Invoke (Document.Layers, [index]);
}
