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
}
