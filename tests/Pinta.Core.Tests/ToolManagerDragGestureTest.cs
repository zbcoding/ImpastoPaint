using System;
using System.Reflection;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A tool's drag ends exactly once, with its release. Commit (run by a live preview starting, a
/// layer switch, Save) finishes the tool's work, so a drag still held across it has to end there,
/// or the release arriving later lands on a document that has moved on — mid-preview it committed a
/// stroke whose history push disposed the composite the preview was rendering from. And an undo
/// mid-drag rewinds history under a stroke that has not been recorded yet.
/// </summary>
[TestFixture]
internal sealed class ToolManagerDragGestureTest : DocumentHarness
{
	private RecordingTool tool = null!;

	// Same seam as ToolManagerRasterizeGuardTest: SetCurrentTool builds a real toolbar.
	private static readonly PropertyInfo current_tool_property =
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;

	[OneTimeSetUp]
	public void CreateTool () => tool = new RecordingTool (PintaCore.Services);

	[SetUp]
	public void ActivateTool ()
	{
		current_tool_property.SetValue (PintaCore.Tools, tool);
		tool.MouseUpCount = 0;
		tool.MoveCount = 0;
		tool.CommitDuringMouseDown = false;
	}

	[TearDown]
	public void DeactivateTool ()
	{
		// The gesture state lives on the shared ToolManager; a release clears whatever a test left.
		PintaCore.Tools.DoMouseUp (Document, Mouse (MouseButton.Left));
		current_tool_property.SetValue (PintaCore.Tools, null);
	}

	[Test]
	public void CommitEndsTheDragWithOneReleaseAndSwallowsTheRestOfThePress ()
	{
		PintaCore.Tools.DoMouseDown (Document, Mouse (MouseButton.Left));

		PintaCore.Tools.Commit ();
		Assert.That (tool.MouseUpCount, Is.EqualTo (1), "commit releases the drag");

		PintaCore.Tools.DoMouseMove (Document, Mouse (MouseButton.Left));
		PintaCore.Tools.DoMouseUp (Document, Mouse (MouseButton.Left));

		Assert.Multiple (() => {
			Assert.That (tool.MouseUpCount, Is.EqualTo (1), "the physical release reaches no tool");
			Assert.That (tool.MoveCount, Is.Zero, "nor do the held moves after it");
			Assert.That (PintaCore.Tools.IsToolDragActive, Is.False);
		});
	}

	[Test]
	public void CommitFromInsideTheToolsOwnHandlerKeepsTheDrag ()
	{
		tool.CommitDuringMouseDown = true;
		PintaCore.Tools.DoMouseDown (Document, Mouse (MouseButton.Left));

		Assert.Multiple (() => {
			Assert.That (tool.MouseUpCount, Is.Zero, "the tool's own commit does not end its press");
			Assert.That (PintaCore.Tools.IsToolDragActive, Is.True);
		});
	}

	[Test]
	public void UndoMidDragLeavesHistoryAlone ()
	{
		AddObject (Layer (0), Box (new Cairo.Color (1, 0, 0, 1), new RectangleI (4, 4, 8, 8)), "Box");
		int pointer = Document.History.Pointer;

		PintaCore.Tools.DoMouseDown (Document, Mouse (MouseButton.Left));
		typeof (EditActions).GetMethod ("HandlerPintaCoreActionsEditUndoActivated", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke (PintaCore.Actions.Edit, [this, EventArgs.Empty]);

		Assert.Multiple (() => {
			Assert.That (Document.History.Pointer, Is.EqualTo (pointer), "undo waits for the stroke to finish");
			Assert.That (Layer (0).ShapeObjects, Has.Count.EqualTo (1));
		});
	}

	private static ToolMouseEventArgs Mouse (MouseButton button)
		=> new () {
			MouseButton = button,
			PointDouble = new PointD (20, 20),
			RootPoint = new PointD (20, 20),
			WindowPoint = new PointD (20, 20),
		};

	private sealed class RecordingTool : BaseTool
	{
		public RecordingTool (IServiceProvider services) : base (services) { }

		public int MouseUpCount { get; set; }
		public int MoveCount { get; set; }
		public bool CommitDuringMouseDown { get; set; }

		public override string Name => "Recording (test)";
		public override string Icon => string.Empty;

		protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
		{
			if (CommitDuringMouseDown)
				PintaCore.Tools.Commit ();
		}

		protected override void OnMouseMove (Document document, ToolMouseEventArgs e) => MoveCount++;
		protected override void OnMouseUp (Document document, ToolMouseEventArgs e) => MouseUpCount++;
	}
}
