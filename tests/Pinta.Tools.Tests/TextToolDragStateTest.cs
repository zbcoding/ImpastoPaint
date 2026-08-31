using System;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A mid-drag tool switch (a keyboard shortcut firing while a corner grip, border, or rotate
/// handle is held) used to deactivate TextTool with `tracking` still true. OnDeactivated's own
/// CommitCurrentText clears `current_text_object` to null, so the very next OnMouseMove - no
/// button required - threw a NullReferenceException dereferencing it.
/// </summary>
[TestFixture]
internal sealed class TextToolDragStateTest : ToolsTestHarness
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

		// See TextModeSwitchTest's teardown for why this unsubscribe is needed.
		var handlerMethod = typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!;
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (typeof (Action<UserLayer, int>), tool, handlerMethod);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	// Same construction as TextModeSwitchTest - see its comment for why this bypasses
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

	private static void Select (TextTool t, TextObject obj)
		=> typeof (TextTool).GetMethod ("StartEditing", NonPublicInstance,
			[typeof (TextObject), typeof (bool)])!.Invoke (t, [obj, false]);

	private static void SetField (TextTool t, string name, object? value)
		=> typeof (TextTool).GetField (name, NonPublicInstance)!.SetValue (t, value);

	private static object GetField (TextTool t, string name)
		=> typeof (TextTool).GetField (name, NonPublicInstance)!.GetValue (t)!;

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	// Mirrors what BeginManipulation leaves behind mid-grab: tracking true and a manipulation kind
	// picked - the exact state a corner-grip drag is in the instant a keyboard shortcut fires
	// HandleGlobalKeyPress's SetCurrentTool.
	private static void BeginResizeGrab (TextTool t)
	{
		object resize = Enum.Parse (typeof (TextTool).GetNestedType ("TextManipulation", NonPublicInstance)!, "Resize");
		SetField (t, "tracking", true);
		SetField (t, "manipulation", resize);
	}

	private TextObject SelectedObject (TextTool t)
	{
		UserLayer layer = Layer (0);
		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);
		Select (t, obj);
		return obj;
	}

	[Test]
	public void DeactivatingMidGrabClearsTrackingState ()
	{
		TextTool t = ActivateOnLayer ();
		SelectedObject (t);
		BeginResizeGrab (t);

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);

		Assert.Multiple (() => {
			Assert.That (GetField (t, "tracking"), Is.False,
				"deactivating mid-grab must release tracking, or the next mouse move dereferences the now-null current_text_object");
			Assert.That (GetField (t, "manipulation").ToString (), Is.EqualTo ("None"));
		});
	}

	[Test]
	public void MouseMoveAfterDeactivatingMidGrabDoesNotThrow ()
	{
		TextTool t = ActivateOnLayer ();
		SelectedObject (t);
		BeginResizeGrab (t);

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);

		Assert.DoesNotThrow (() =>
			typeof (BaseTool).GetMethod ("DoMouseMove", NonPublicInstance)!
				.Invoke (t, [Document, MouseArgs (new PointD (8, 8))]),
			"a mouse move right after a mid-grab deactivation must not throw");
	}
}
