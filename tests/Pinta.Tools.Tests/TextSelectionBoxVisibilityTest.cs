using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class TextSelectionBoxVisibilityTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private TextTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);

		// TextTool's constructor subscribes to the static LayerObjectSelection.TextSelectRequested
		// event for the app's lifetime (by design - it must react even while some other tool is
		// current) and never unsubscribes. A throwaway test instance would otherwise linger as a
		// listener forever, so a later test's RequestTextSelect call would reach this dead instance
		// too and drive it through the real ToolManager.SetCurrentTool, which needs UI shell state
		// this headless harness never built.
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (
			typeof (Action<UserLayer, int>), tool,
			typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	// Same reflection-based activation TextToolSelectionColorTest uses: builds and activates a real
	// TextTool the way ToolManager.SetCurrentTool would, without its toolbar-building side effects
	// (see that file for why).
	private TextTool ActivateOnLayer ()
	{
		TextTool t = new (PintaCore.Services);

		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private static int InkPixels (ImageSurface surface)
	{
		var data = surface.GetReadOnlyPixelData ();
		int ink = 0;
		for (int i = 0; i < data.Length; ++i)
			if (data[i].A > 0)
				++ink;
		return ink;
	}

	// The bug this pins: the dashed re-edit rectangle (and its corner handles) around an existing
	// text object lives on the OverlayLayer, drawn by DrawTextRectangles. Switching away to another
	// tool and back must redraw it, the same as first activating the tool does.
	[Test]
	public void TheDashedRectangleReappearsAfterSwitchingToolsAwayAndBack ()
	{
		UserLayer layer = Layer (0);
		// Origin shifted left so the padded interaction box's right edge crosses the small
		// CanvasSize x CanvasSize test canvas - the default-font box is wider than the canvas.
		TextObject obj = new (new TextEngine (["Impasto"]) { Origin = new PointI (-36, 4) });
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();

		int initialInk = InkPixels (Document.Layers.OverlayLayer.Surface);
		Assert.That (initialInk, Is.GreaterThan (0),
			"the dashed rectangle must be drawn the first time the tool activates");

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		int inkAfterToolSwitch = InkPixels (Document.Layers.OverlayLayer.Surface);
		Assert.That (inkAfterToolSwitch, Is.GreaterThan (0),
			"the dashed rectangle around the text object must still be drawn after returning to the tool");
	}
}
