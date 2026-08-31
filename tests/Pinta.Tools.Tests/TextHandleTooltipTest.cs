using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class TextHandleTooltipTest : ToolsTestHarness
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

		// The constructor's lifetime subscription to the static selection event outlives the
		// instance; drop it so a later fixture's RequestTextSelect can't reach this dead tool
		// (see the same teardown in TextSelectionBoxVisibilityTest).
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (
			typeof (Action<UserLayer, int>), tool,
			typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	// Same reflection-based activation the other text fixtures use: a real TextTool without
	// ToolManager.SetCurrentTool's toolbar-building side effects (see TextToolSelectionColorTest).
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

	private static PointD[] InteractionCorners (TextTool t, TextObject obj)
		=> (PointD[]) typeof (TextTool).GetMethod ("GetInteractionCorners", NonPublicInstance)!
			.Invoke (t, [obj])!;

	// The gap this pins: the corner grips used to be dots painted onto the OverlayLayer, which the
	// canvas cannot hit-test - so hovering one showed nothing, while every shape grip (a real
	// IToolHandle) showed its tooltip. The grips are handles now, so the canvas can answer
	// OnQueryTooltip for them; a text object hovered mid-edit is exactly the case that had no hint
	// at all, since the popover path suppresses itself while editing.
	[Test]
	public void EachCornerGripCarriesAHoverTooltip ()
	{
		UserLayer layer = Layer (0);
		TextObject obj = new (new TextEngine (["Impasto"]) { Origin = new PointI (4, 4) });
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();

		List<IToolHandle> handles = t.Handles.ToList ();
		Assert.That (handles, Has.Count.EqualTo (4), "one grip per corner of the text object's interaction box");
		Assert.That (handles.All (h => h.Active), "grips must be active or the canvas neither draws nor hit-tests them");
		Assert.That (handles.All (h => !string.IsNullOrEmpty (h.TooltipText)), "every grip must have hover text");

		PointD[] corners = InteractionCorners (t, obj);
		foreach (PointD corner in corners)
			Assert.That (
				handles.Cast<MoveHandle> ().Any (h => h.CanvasPosition == corner),
				$"no grip sits on the interaction corner at {corner.X}, {corner.Y}");
	}

	// The grips are tool state, not document state (the dashed rectangle they belong to lives on
	// the document's OverlayLayer). Leaving them behind would draw and hit-test stale corners over
	// whatever the next tool shows.
	[Test]
	public void GripsAreDroppedWhenTheToolIsDeactivated ()
	{
		UserLayer layer = Layer (0);
		layer.AddText (new TextObject (new TextEngine (["Impasto"]) { Origin = new PointI (4, 4) }));

		TextTool t = ActivateOnLayer ();
		Assert.That (t.Handles.Any (), "sanity: the tool must have grips while it is active");

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (t, [Document, null]);

		Assert.That (t.Handles, Is.Empty, "grips must not outlive the tool's activation");
	}
}
