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

	private static ShapeObject NamedBox (string name, RectangleI region)
	{
		ShapeObject shape = Box (new Color (0, 0, 1), region);
		shape.Name = name;
		return shape;
	}
}
