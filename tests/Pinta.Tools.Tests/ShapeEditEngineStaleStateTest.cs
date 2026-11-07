using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The shape editor keeps one set of live engines (SEngines) bound to one layer, plus a pointer
/// gesture that runs from mouse-down to mouse-up. Both outlive the events that should end them:
/// a document switch fires no layer event, and an undo pressed mid-drag rebuilds the engines under
/// a still-running drag. Each test pins that the stale state can't edit shapes it doesn't own.
/// </summary>
[TestFixture]
internal sealed class ShapeEditEngineStaleStateTest : ToolsTestHarness
{
	private EllipseTool? tool;
	private Document? other_document;

	[TearDown]
	public void DeactivateTool ()
	{
		if (other_document is not null && PintaCore.Workspace.OpenDocuments.Contains (other_document))
			PintaCore.Workspace.CloseDocument (other_document);
		other_document = null;

		if (tool is not null)
			tool.EditEngine.HandleDeactivated (null);
		CurrentToolProperty.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [null]);
		tool = null;

		BaseEditEngine.SEngines.Clear ();
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.Ellipse);
		typeof (BaseEditEngine).GetField ("runtime_layer", BindingFlags.NonPublic | BindingFlags.Static)!
			.SetValue (null, null);
	}

	// See ShapeEditEngineDragStateTest.Activate for why the engine is driven directly.
	private EllipseTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		EllipseTool t = new (PintaCore.Services);
		CurrentToolProperty.GetSetMethod (nonPublic: true)!.Invoke (PintaCore.Tools, [t]);

		t.EditEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "ellipse");
		t.EditEngine.HandleActivated ();

		tool = t;
		return t;
	}

	private static PropertyInfo CurrentToolProperty
		=> typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	private void Drag (BaseEditEngine engine, PointD from, PointD to)
	{
		engine.HandleMouseDown (Document, MouseArgs (from));
		engine.HandleMouseMove (Document, MouseArgs (to));
		engine.HandleMouseUp (Document, MouseArgs (to));
	}

	// Ctrl+S on a document reached by switching tabs runs Commit before the pointer ever touches
	// its canvas, so the live engines still belonged to the previous document's layer. Persisting
	// them into the new document's current layer kept none of them and emptied that layer: its
	// shapes were deleted, with no history item, and then saved that way.
	[Test]
	public void CommittingAfterADocumentSwitchKeepsTheNewDocumentsShapes ()
	{
		EllipseTool t = Activate ();

		other_document = new Document (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));
		other_document.Layers.AddNewLayer (string.Empty);
		other_document.Workspace.Canvas = Gtk.DrawingArea.New ();
		other_document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();
		UserLayer otherLayer = other_document.Layers.CurrentUserLayer;
		otherLayer.Objects.Add (Box (new Color (0, 1, 0, 1), new RectangleI (2, 2, 8, 8)));
		Assert.That (otherLayer.ShapeObjects, Has.Count.EqualTo (1), "setup: the other document has a shape");

		PintaCore.Workspace.ActivateDocument (other_document);
		t.EditEngine.HandleCommit ();

		Assert.That (otherLayer.ShapeObjects, Has.Count.EqualTo (1),
			"committing must persist the engines into the layer they came from, not the newly current one");
	}

	// Undo replaces the engines and restores the pre-shape selection while the button is still
	// down. The drag kept running against whatever shape now sat at the selected index: the next
	// motion dragged the earlier shape's corner, and pushed no history item for it.
	[Test]
	public void UndoMidDragEndsTheGestureInsteadOfEditingAnotherShape ()
	{
		EllipseTool t = Activate ();
		BaseEditEngine engine = t.EditEngine;

		Drag (engine, new PointD (4, 4), new PointD (12, 12));
		Assert.That (BaseEditEngine.SEngines, Has.Count.EqualTo (1), "setup: shape A is drawn");
		PointD[] shapeABefore = [.. BaseEditEngine.SEngines[0].ControlPoints.Select (cp => cp.Position)];

		// Start drawing shape B well clear of A, then undo while the button is still held.
		engine.HandleMouseDown (Document, MouseArgs (new PointD (24, 24)));
		engine.HandleMouseMove (Document, MouseArgs (new PointD (28, 28)));
		Assert.That (BaseEditEngine.SEngines, Has.Count.EqualTo (2), "setup: shape B is being drawn");

		engine.HandleBeforeUndo ();
		Document.History.Undo ();
		engine.HandleAfterUndo ();
		int historyPointer = Document.History.Pointer;

		engine.HandleMouseMove (Document, MouseArgs (new PointD (6, 6)));
		engine.HandleMouseUp (Document, MouseArgs (new PointD (6, 6)));

		Assert.That (engine.IsDrawing, Is.False, "undo has to end the drag it interrupted");
		Assert.That (BaseEditEngine.SEngines, Has.Count.EqualTo (1), "the undo removed shape B");
		Assert.That (BaseEditEngine.SEngines[0].ControlPoints.Select (cp => cp.Position), Is.EqualTo (shapeABefore),
			"the rest of the interrupted drag must not move shape A");
		Assert.That (Document.History.Pointer, Is.EqualTo (historyPointer), "nothing was edited, so nothing was recorded");
	}
}
