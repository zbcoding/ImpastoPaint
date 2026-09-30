using System.Collections.Generic;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Starting a move-selected drag over live objects must offer to rasterize once, not twice.
/// MoveSelectedTool runs that offer itself in OnStartTransform, over the whole selection it is about
/// to lift; ToolManager's down-point guard ran first over a one-pixel probe at the cursor, so one
/// drag put up two dialogs listing different objects - the one under the cursor, then everything the
/// selection reaches. Drives PintaCore.Tools.DoMouseDown, the seam the app uses; the tool-level
/// suites call OnMouseDown directly and never see the guard.
/// </summary>
[TestFixture]
internal sealed class MoveSelectedRasterizePromptTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private static readonly PropertyInfo current_tool_property =
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;

	private BaseTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		current_tool_property.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	private T Activate<T> (T t) where T : BaseTool
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		current_tool_property.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	[Test]
	public void StartingAMoveOverTwoObjectsAsksToRasterizeOnce ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, NamedBox ("Open Curve Shape 1", new RectangleI (4, 4, 8, 8)), "First");
		AddObject (layer, NamedBox ("Open Curve Shape 2", new RectangleI (16, 16, 8, 8)), "Second");

		List<IReadOnlyList<string>> prompts = [];
		ObjectRasterizer.ConfirmPrompt = labels => { prompts.Add (labels); return true; };
		try {
			Activate (new MoveSelectedTool (PintaCore.Services));
			// No selection: the tool falls back to the whole canvas, which reaches both objects.
			PintaCore.Tools.DoMouseDown (Document, new ToolMouseEventArgs {
				PointDouble = new PointD (8, 8), // over the first object, so the old guard fired too
				MouseButton = MouseButton.Left,
			});
		} finally {
			ObjectRasterizer.ConfirmPrompt = null;
		}

		Assert.Multiple (() => {
			Assert.That (prompts, Has.Count.EqualTo (1),
				"one drag, one offer - the tool's own covers the down point the guard was probing");
			Assert.That (prompts[0], Is.EquivalentTo (new[] { "Open Curve Shape 1", "Open Curve Shape 2" }),
				"the surviving offer is the tool's, which lists everything the lifted selection reaches");
		});
	}

	// A paste lands floating over whatever sits at the viewport's corner, often a text object. The
	// drag only moves the selection layer and never touches the raster beneath until the selection
	// is finished, so asking to rasterize that object blocked the move for nothing.
	[Test]
	public void DraggingAFloatingPasteOverAnObjectMovesWithoutAskingToRasterize ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, NamedBox ("Open Curve Shape 1", new RectangleI (4, 4, 8, 8)), "First");

		Document.Layers.CreateSelectionLayer ();
		Document.Layers.ShowSelectionLayer = true;
		Fill (Document.Layers.SelectionLayer.Surface, Red);
		Document.Selection = SelectionOf (new RectangleI (0, 0, 16, 16));
		Document.Selection.Visible = true;

		int prompts = 0;
		ObjectRasterizer.ConfirmPrompt = _ => { prompts++; return false; };
		ObjectRasterizer.ConfirmLiftPrompt = _ => { prompts++; return SelectionLiftChoice.Cancel; };
		try {
			Activate (new MoveSelectedTool (PintaCore.Services));
			Click (new PointD (8, 8));
		} finally {
			ObjectRasterizer.ConfirmPrompt = null;
			ObjectRasterizer.ConfirmLiftPrompt = null;
		}

		Assert.Multiple (() => {
			Assert.That (prompts, Is.Zero, "a floating selection has nothing on the raster to bake");
			Assert.That (layer.ShapeObjects, Has.Count.EqualTo (1), "the object beneath stays editable");
			Assert.That (Document.History.Current, Is.InstanceOf<MovePixelsHistoryItem> (), "the move was not declined");
		});
	}

	// Choosing "Move to New Layer" lifts only the base raster onto a new layer above, so the pixels
	// move while the objects stay live on the source - and undo puts back the one-layer document.
	[Test]
	public void MoveToNewLayerCarriesRasterPixelsAndLeavesObjectsOnTheSource ()
	{
		UserLayer source = Layer (0);
		Fill (source.Surface, Red);
		AddObject (source, NamedBox ("Open Curve Shape 1", new RectangleI (4, 4, 8, 8)), "First");
		Document.Selection = SelectionOf (new RectangleI (0, 0, 16, 16));
		Document.Selection.Visible = true;

		ObjectRasterizer.ConfirmLiftPrompt = _ => SelectionLiftChoice.NewLayer;
		try {
			Activate (new MoveSelectedTool (PintaCore.Services));
			Click (new PointD (8, 8));
		} finally {
			ObjectRasterizer.ConfirmLiftPrompt = null;
		}
		Document.FinishSelection ();

		Assert.Multiple (() => {
			Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (2));
			Assert.That (source.ShapeObjects, Has.Count.EqualTo (1), "the object was not rasterized");
			Assert.That (source.Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Transparent), "the lifted region left the source");
			Assert.That (source.Surface.GetColorBgra (new PointI (20, 20)), Is.EqualTo (Red), "pixels outside the selection stayed");
			Assert.That (Layer (1).Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Red), "the lifted pixels landed on the new layer");
		});

		Document.History.Undo (); // finish
		Document.History.Undo (); // move to new layer

		Assert.Multiple (() => {
			Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (1), "one undo step removes the new layer");
			Assert.That (Layer (0).Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Red), "and restores the lifted pixels");
			Assert.That (Layer (0).ShapeObjects, Has.Count.EqualTo (1));
		});
	}

	// Undo leaves the move tool current (undoing the finish selects it), and redoing the added layer
	// switched layers through the path that commits the current tool - which finished the floating
	// pixels onto the source layer before the finish step put them on the new one, duplicating them.
	[Test]
	public void RedoingMoveToNewLayerPutsThePixelsOnlyOnTheNewLayer ()
	{
		UserLayer source = Layer (0);
		Fill (source.Surface, Red);
		AddObject (source, NamedBox ("Open Curve Shape 1", new RectangleI (4, 4, 8, 8)), "First");
		Document.Selection = SelectionOf (new RectangleI (0, 0, 16, 16));
		Document.Selection.Visible = true;

		ObjectRasterizer.ConfirmLiftPrompt = _ => SelectionLiftChoice.NewLayer;
		try {
			Activate (new MoveSelectedTool (PintaCore.Services));
			Click (new PointD (8, 8));
		} finally {
			ObjectRasterizer.ConfirmLiftPrompt = null;
		}
		Document.FinishSelection ();

		Document.History.Undo (); // finish
		Document.History.Undo (); // move to new layer
		Document.History.Redo (); // move to new layer

		Assert.Multiple (() => {
			Assert.That (Document.Layers.UserLayers, Has.Count.EqualTo (2));
			Assert.That (Document.Layers.CurrentUserLayerIndex, Is.EqualTo (1), "the new layer is current again");
			Assert.That (Document.Layers.ShowSelectionLayer, Is.True, "the lifted pixels float until the finish is redone");
			Assert.That (source.Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Transparent), "the lifted region stays off the source");
		});

		Document.History.Redo (); // finish

		Assert.Multiple (() => {
			Assert.That (source.Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Transparent), "the pixels were not finished onto the source");
			Assert.That (source.Surface.GetColorBgra (new PointI (20, 20)), Is.EqualTo (Red), "pixels outside the selection stayed");
			Assert.That (Layer (1).Surface.GetColorBgra (new PointI (2, 2)), Is.EqualTo (Red), "the lifted pixels landed on the new layer");
		});
	}

	// Down and up at one point: starts (and ends) the move without displacing anything, so the
	// lifted pixels stay where the assertions look for them.
	private static void Click (PointD point)
	{
		ToolMouseEventArgs args = new () { PointDouble = point, MouseButton = MouseButton.Left };
		PintaCore.Tools.DoMouseDown (PintaCore.Workspace.ActiveDocument, args);
		PintaCore.Tools.DoMouseUp (PintaCore.Workspace.ActiveDocument, args);
	}

	private static ShapeObject NamedBox (string name, RectangleI region)
	{
		ShapeObject shape = Box (new Color (0, 0, 1), region);
		shape.Name = name;
		return shape;
	}
}
