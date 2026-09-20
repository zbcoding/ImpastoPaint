using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Text tool's re-edit chrome — the dashed rectangle, its corner grips and the "Obj." badge —
/// lives on the OverlayLayer and only ever got redrawn from tool activity. Two things that are not
/// tool activity left it on screen pointing at nothing: hiding the layer from the layers dock
/// (the artwork vanished, the outline stayed), and the dock's selection moving off the object's
/// sub-row onto its plain layer row.
/// </summary>
[TestFixture]
internal sealed class TextOverlayLayerVisibilityTest : ToolsTestHarness
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
		// for the app's lifetime and never unsubscribes, so a throwaway test instance would linger
		// as a listener. (ObjectDeselected needs no such cleanup: it is subscribed on activation
		// and dropped on deactivation, which the DoDeactivated above already did.)
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (
			typeof (Action<UserLayer, int>), tool,
			typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	// Same reflection-based activation the other TextTool fixtures use: builds and activates a
	// real TextTool the way ToolManager.SetCurrentTool would, without its toolbar side effects.
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

	// Origin shifted left so the padded interaction box crosses the small test canvas.
	private static TextObject AddCaption (UserLayer layer)
	{
		TextObject obj = new (new TextEngine (["Impasto"]) { Origin = new PointI (-36, 4) });
		layer.AddText (obj);
		return obj;
	}

	private static void AssertChromeShowing (string why)
	{
		Assert.That (InkPixels (PintaCore.Workspace.ActiveDocument.Layers.OverlayLayer.Surface), Is.GreaterThan (0), why);
		Assert.That (PintaCore.Workspace.ActiveDocument.Layers.OverlayLayer.Hidden, Is.False, why);
	}

	private static void AssertChromeGone (string why)
	{
		Assert.Multiple (() => {
			Assert.That (InkPixels (PintaCore.Workspace.ActiveDocument.Layers.OverlayLayer.Surface), Is.EqualTo (0), why);
			Assert.That (PintaCore.Workspace.ActiveDocument.Layers.OverlayLayer.Hidden, Is.True, why);
		});
	}

	[Test]
	public void HidingTheLayerClearsTheTextChrome ()
	{
		UserLayer layer = Layer (0);
		AddCaption (layer);

		ActivateOnLayer ();
		AssertChromeShowing ("setup: the dashed rectangle has to be drawn while the layer shows");

		layer.Hidden = true;

		AssertChromeGone ("hiding the layer hides its text, so the outline around it must go too");
	}

	[Test]
	public void ShowingTheLayerAgainBringsTheChromeBack ()
	{
		UserLayer layer = Layer (0);
		AddCaption (layer);

		ActivateOnLayer ();
		layer.Hidden = true;
		AssertChromeGone ("setup: hiding the layer has to clear the chrome");

		layer.Hidden = false;

		AssertChromeShowing ("the text is editable again, so its re-edit rectangle has to come back");
	}

	[Test]
	public void HidingJustTheTextObjectClearsItsChrome ()
	{
		UserLayer layer = Layer (0);
		TextObject caption = AddCaption (layer);

		TextTool t = ActivateOnLayer ();
		AssertChromeShowing ("setup: the dashed rectangle has to be drawn while the object shows");

		caption.Hidden = true;
		typeof (TextTool).GetMethod ("DrawTextRectangles", NonPublicInstance)!.Invoke (t, [true]);

		AssertChromeGone ("a hidden object draws nothing, so its outline must not be drawn either");
	}

	[Test]
	public void SelectingSomethingThatIsNotAnObjectClearsTheChrome ()
	{
		UserLayer layer = Layer (0);
		AddCaption (layer);

		ActivateOnLayer ();
		AssertChromeShowing ("setup: the dashed rectangle has to be drawn before the selection moves");

		// What the dock raises when its selection lands on a plain layer row or the mask row.
		LayerObjectSelection.RaiseObjectDeselected ();

		AssertChromeGone ("selecting the layer rather than the object leaves nothing being edited");
	}
}
