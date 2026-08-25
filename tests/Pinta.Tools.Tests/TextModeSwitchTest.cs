using System;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Point/Area dropdown used to only set the mode for the NEXT object created - selecting a
/// different value while an existing text object was selected/being edited had no effect on it at
/// all, so there was no way to flip an object between point and area text after the fact. Changing
/// the dropdown now retroactively converts the object currently being edited.
/// </summary>
[TestFixture]
internal sealed class TextModeSwitchTest : ToolsTestHarness
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

		// The constructor subscribes HandleTextSelectRequested to LayerObjectSelection's static
		// event for the tool's whole lifetime - fine for the one TextTool the real app ever builds,
		// but this harness builds a throwaway one per test. Leaving it subscribed means a LATER
		// fixture's RequestTextSelect call reaches this dead instance too, which then drives the
		// real ToolManager.SetCurrentTool/ClearToolBar against a toolbar this headless harness never
		// wired up.
		var handlerMethod = typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!;
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (typeof (Action<UserLayer, int>), tool, handlerMethod);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	// Same construction as TextToolSelectionColorTest - see its comment for why this bypasses
	// ToolManager.SetCurrentTool.
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

	private static ToolBarDropDownButton TextModeButton (TextTool t)
		=> (ToolBarDropDownButton) typeof (TextTool).GetField ("text_mode_btn", NonPublicInstance)!.GetValue (t)!;

	// Selects the object directly through the tool's own StartEditing, the same method
	// HandleTextSelectRequested calls after it finishes switching tools/layers - sidesteps that
	// unrelated tool-switch machinery, which this harness never fully wires up.
	private static void Select (TextTool t, TextObject obj)
		=> typeof (TextTool).GetMethod ("StartEditing", NonPublicInstance,
			[typeof (TextObject), typeof (bool)])!.Invoke (t, [obj, false]);

	[Test]
	public void SwitchingDropdownToAreaGivesTheSelectedObjectAWrapWidth ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		Select (t, obj);

		Assert.That (obj.Engine.WrapWidth, Is.Zero, "setup: the object starts as point text");

		TextModeButton (t).SelectedIndex = 1; // Area

		Assert.That (obj.Engine.WrapWidth, Is.GreaterThan (0),
			"choosing Area for the object currently selected must give it a wrap width, not just affect future objects");
	}

	[Test]
	public void SwitchingDropdownToPointClearsTheSelectedObjectsWrapWidth ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.WrapWidth = 150;
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 1; // Start the toolbar in Area, matching the object.
		Select (t, obj);

		TextModeButton (t).SelectedIndex = 0; // Point

		Assert.That (obj.Engine.WrapWidth, Is.Zero,
			"choosing Point for the object currently selected must drop its wrap width so it grows freely again");
	}
}
