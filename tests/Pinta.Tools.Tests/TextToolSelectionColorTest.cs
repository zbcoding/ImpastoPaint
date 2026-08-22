using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class TextToolSelectionColorTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private TextTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		// Undo what ActivateOnLayer below faked: drop the tool's palette/workspace subscriptions
		// (PintaCore.Palette and PintaCore.Workspace are shared statics across the whole test run) and
		// clear the borrowed ToolManager.CurrentTool so later fixtures see their expected null.
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	// Builds and activates a real TextTool the way ToolManager.SetCurrentTool would, but without
	// going through it: SetCurrentTool also builds the *toolbox's* toolbar UI (ChromeManager
	// .ToolToolBar and friends), which this headless harness has no shell for (see
	// ToolsTestHarness). Building the tool's own toolbar into a standalone Box and activating it
	// directly gets the same object state (font_button, current_text_object, etc.) without that.
	private TextTool ActivateOnLayer ()
	{
		TextTool t = new (PintaCore.Services);

		// Nothing built one for this headless document; SetCursor (run from DoActivated) and the
		// text tool's own im_context both need a real widget reference, even though nothing here
		// ever realizes or shows it.
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		// HandleTextSelectRequested only reaches StartEditing once the tool is already current;
		// fake that directly rather than through ToolManager.SetCurrentTool (same toolbar problem).
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	// The bug this pins: selecting an *existing* text object - a layers-dock sub-row click, wired
	// through LayerObjectSelection.RequestTextSelect - used to stamp the palette's current
	// Primary/Secondary color onto the object, the same stamp a brand-new object needs so it starts
	// out in whatever color is currently selected. So changing color while editing one text object,
	// then clicking a different, pre-existing text object's sub-row, silently recolored that other
	// object - no typing, no color action taken on it at all. StartEditing's isNewObject gate is the
	// fix: only a freshly-created object picks up the palette's color; re-selecting an existing one
	// leaves its stored color alone.
	[Test]
	public void SelectingAnExistingTextObjectDoesNotRecolorIt ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		Color ownColor = new (0, 1, 0);
		obj.Engine.PrimaryColor = ownColor;
		layer.AddText (obj);

		ActivateOnLayer ();

		// Palette state left over from editing some other, unrelated object.
		PintaCore.Palette.PrimaryColor = new Color (1, 0, 0);

		LayerObjectSelection.RequestTextSelect (layer, 0);

		Assert.That (obj.Engine.PrimaryColor, Is.EqualTo (ownColor),
			"re-selecting an existing text object must not repaint it in whatever color the palette happens to hold");
	}
}
