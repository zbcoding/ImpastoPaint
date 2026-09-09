using System;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The bug this pins: while a text edit was open, the text tool claimed Cut and Copy no matter
/// what — <c>OnHandleCut</c> returned an unconditional <c>true</c> for any <c>is_editing</c>
/// state. With no *text* selected the engine's own cut is a no-op, so Edit &gt; Cut did nothing at
/// all: the canvas selection was neither copied nor erased, the clipboard was unchanged, and no
/// history item was pushed. Silently. Reported as "cutting doesn't always cut the selection".
///
/// <para>
/// The tool may only claim the command when it has selected text to act on; otherwise it has to
/// decline, so <c>EditActions</c> falls through to <c>tools.Commit ()</c> and the canvas-level
/// cut/copy. The claimed path is not covered here: it writes to the clipboard, and this harness
/// opens no display to get one from.
/// </para>
/// </summary>
[TestFixture]
internal sealed class TextToolClipboardClaimTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// No clipboard can be obtained headless, and the declining path needs none: both handlers bail
	// out before writing anything. If a future change reaches for it here, this test reports that
	// by throwing rather than passing on a technicality.
	private static readonly Gdk.Clipboard UnusedClipboard = null!;

	private TextTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		ReleaseTextSelectSubscription (tool);
		tool = null;
	}

	// TextTool's constructor subscribes to the static LayerObjectSelection.TextSelectRequested and
	// nothing ever unsubscribes — harmless in the app, which builds one TextTool for its lifetime,
	// but in a test run every instance ever constructed stays subscribed. A later
	// RequestTextSelect then fans out to this fixture's retired tools too, and each one asks
	// ToolManager to make *itself* current, which tears down the live tool's toolbar and crashes
	// headless. Drop this fixture's subscription so it leaves the static as it found it.
	private static void ReleaseTextSelectSubscription (TextTool t)
	{
		MethodInfo handler = typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!;
		LayerObjectSelection.TextSelectRequested -=
			(Action<UserLayer, int>) Delegate.CreateDelegate (typeof (Action<UserLayer, int>), t, handler);
	}

	// Same headless activation the other TextTool fixtures use — see TextToolSelectionColorTest for
	// why ToolManager.SetCurrentTool is bypassed.
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

	private static bool IsEditing (TextTool t)
		=> (bool) typeof (TextTool).GetField ("is_editing", NonPublicInstance)!.GetValue (t)!;

	// Selects the object through the tool's own StartEditing, the same method
	// HandleTextSelectRequested reaches after it finishes switching tools — that switch runs
	// ToolManager.ClearToolBar, which needs a chrome toolbar this headless harness has none of.
	private static void StartEditing (TextTool t, TextObject obj)
		=> typeof (TextTool).GetMethod ("StartEditing", NonPublicInstance,
			[typeof (TextObject), typeof (bool)])!.Invoke (t, [obj, false]);

	// A live text edit holding a caret but no selected characters: what clicking a text object's
	// sub-row in the layers dock leaves behind.
	private TextTool EditingWithNoSelectedText ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		StartEditing (t, obj);

		Assert.That (IsEditing (t), Is.True, "setup: the tool has to be in an editing session");
		Assert.That (obj.Engine.PerformCut (UnusedClipboard), Is.False,
			"setup: with no characters selected the engine's own cut has nothing to do");

		return t;
	}

	[Test]
	public void CutIsDeclinedWhileEditingWithNoSelectedText ()
	{
		TextTool t = EditingWithNoSelectedText ();

		Assert.That (t.DoHandleCut (Document, UnusedClipboard), Is.False,
			"with no selected text the tool must decline Cut, so the canvas selection is cut instead of nothing at all");
	}

	[Test]
	public void CopyIsDeclinedWhileEditingWithNoSelectedText ()
	{
		TextTool t = EditingWithNoSelectedText ();

		Assert.That (t.DoHandleCopy (Document, UnusedClipboard), Is.False,
			"with no selected text the tool must decline Copy, so the canvas selection reaches the clipboard");
	}
}
