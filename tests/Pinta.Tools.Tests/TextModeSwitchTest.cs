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

	private static ToolBarDropDownButton RasterizeModeButton (TextTool t)
		=> (ToolBarDropDownButton) typeof (TextTool).GetField ("rasterize_mode_btn", NonPublicInstance)!.GetValue (t)!;

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

	[Test]
	public void SwitchingDropdownToRasterFlipsTheSelectedObjectsRasterizeOnFinalize ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = false };
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		// Force a known baseline: the setting persists across tests, so the button may already
		// start on Raster, making the switch below a no-op that never fires the event.
		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 1; // Object, matching the object's own state.
		Select (t, obj);

		RasterizeModeButton (t).SelectedIndex = 0; // Raster

		Assert.That (obj.RasterizeOnFinalize, Is.True,
			"choosing Raster for the object currently selected/being edited must flip it, not just affect future objects");
	}

	[Test]
	public void SwitchingDropdownToObjectClearsTheSelectedObjectsRasterizeOnFinalize ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = true };
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 0; // Start the toolbar in Raster, matching the object.
		Select (t, obj);

		RasterizeModeButton (t).SelectedIndex = 1; // Object

		Assert.That (obj.RasterizeOnFinalize, Is.False,
			"choosing Object for the object currently selected/being edited must flip it back to editable");
	}

	/// <summary>
	/// Selecting an existing text object shows its own font/style in the toolbar
	/// (SyncToolbarFromObject). The Raster/Object dropdown has to move with it, or it keeps showing
	/// the last-used default and silently misreports the selected object's real mode - and
	/// re-clicking the value already shown is a no-op, so the user can't even correct it.
	/// </summary>
	[Test]
	public void SelectingAnObjectModeTextObjectSyncsTheRasterDropdownAwayFromRaster ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = false }; // Object mode
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 0; // Dropdown shows Raster; the object about to be selected is Object.

		Select (t, obj); // StartEditing -> SyncToolbarFromObject

		Assert.That (RasterizeModeButton (t).SelectedIndex, Is.EqualTo (1),
			"selecting an Object-mode text object must move the Raster/Object dropdown to Object, not leave it on the last Raster default");
	}

	/// <summary>
	/// A rasterize-on-finalize text object is transient (see RasterizeOnFinalizeSubRowTest in
	/// Pinta.Core.Tests), so it must not get a sub-row in the layers dock - switching the currently
	/// selected/edited object to Raster must tell the dock to drop its row, the same seam the object's
	/// own creation uses to appear without a history push.
	/// </summary>
	[Test]
	public void SwitchingDropdownToRasterNotifiesTheDockToDropTheObjectsSubRow ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = false };
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		// Force a known baseline: the setting persists across tests, so the button may already
		// start on Raster, making the switch below a no-op that never fires the event.
		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 1; // Object, matching the object's own state.
		Select (t, obj);

		bool raised = false;
		void Handler () => raised = true;
		LayerObjectSelection.ObjectsChanged += Handler;
		try {
			RasterizeModeButton (t).SelectedIndex = 0; // Raster
		} finally {
			LayerObjectSelection.ObjectsChanged -= Handler;
		}

		Assert.That (raised, Is.True,
			"the dock must be told to refresh so the now-transient object's sub-row disappears");
	}

	/// <summary>
	/// End-to-end regression for the reported bug: a brand-new (not yet finalized) text object,
	/// switched to Raster and back to Object via the dropdown, must have its layers-dock sub-row
	/// disappear and then reappear - not get stuck hidden, and not linger visible while in Raster
	/// mode (see RasterizeOnFinalizeSubRowTest in Pinta.Core.Tests for the row-visibility rule
	/// itself). A brand-new object starts in Object mode exactly like TextTool.HandleLeftClick
	/// creates one, so RasterizeOnFinalize starts false here too.
	/// </summary>
	[Test]
	public void RoundTrippingTheRasterDropdownHidesThenRestoresTheObjectsSubRow ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = false };
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		// Force a known baseline: the setting persists across tests, so the button may already
		// start on Raster, making a switch below a no-op that never fires the event.
		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 1; // Object, matching the object's own state.
		Select (t, obj);

		Assert.That (UserLayer.GetsSubRow (obj), Is.True, "setup: a brand-new object gets a sub-row");

		int changes = 0;
		void Handler () => changes++;
		LayerObjectSelection.ObjectsChanged += Handler;
		try {
			RasterizeModeButton (t).SelectedIndex = 0; // Raster

			Assert.That (UserLayer.GetsSubRow (obj), Is.False,
				"switching the brand-new object to Raster must drop its sub-row");
			Assert.That (changes, Is.EqualTo (1), "the dock must be told to refresh after the drop");

			RasterizeModeButton (t).SelectedIndex = 1; // Back to Object

			Assert.That (UserLayer.GetsSubRow (obj), Is.True,
				"switching back to Object must restore the object's sub-row");
			Assert.That (changes, Is.EqualTo (2), "the dock must be told to refresh again after it reappears");
		} finally {
			LayerObjectSelection.ObjectsChanged -= Handler;
		}
	}

	// A snapshot of the overlay layer's pixels - what RedrawText's on-canvas chrome (dashed
	// rectangle, handles, "Obj." badge, caret) actually drew - independent of the badge's exact
	// screen position, which depends on font-layout geometry this test has no need to replicate.
	private ColorBgra[] OverlaySnapshot ()
		=> Document.Layers.OverlayLayer.Surface.GetReadOnlyPixelData ().ToArray ();

	private static void RedrawText (TextTool t, bool showCursor)
		=> typeof (TextTool).GetMethod ("RedrawText", NonPublicInstance)!.Invoke (t, [showCursor]);

	/// <summary>
	/// The dropdown flipping RasterizeOnFinalize used to leave the on-canvas "Obj." badge stale
	/// until some unrelated redraw ran (DrawTextRectangles skips the badge for Raster-mode text,
	/// but nothing repainted the overlay right after the flip) - switching to Raster must make the
	/// badge disappear immediately, and switching back to Object must bring it straight back.
	/// </summary>
	[Test]
	public void SwitchingTheDropdownRedrawsTheOnCanvasBadgeImmediately ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ()) { RasterizeOnFinalize = false };
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		RasterizeModeButton (t).SelectedIndex = 1; // Object - known baseline, matching the object's own state.
		Select (t, obj);

		// Establish what the overlay looks like with the badge on, at a fixed point in the editing
		// session (so the caret - also drawn here - is in the same state in every snapshot below).
		RedrawText (t, true);
		ColorBgra[] withBadge = OverlaySnapshot ();

		RasterizeModeButton (t).SelectedIndex = 0; // Raster
		ColorBgra[] afterSwitchToRaster = OverlaySnapshot ();

		Assert.That (afterSwitchToRaster, Is.Not.EqualTo (withBadge),
			"switching to Raster must redraw the overlay immediately, dropping the badge - not leave it stale");

		RasterizeModeButton (t).SelectedIndex = 1; // Back to Object
		ColorBgra[] afterSwitchBackToObject = OverlaySnapshot ();

		Assert.That (afterSwitchBackToObject, Is.EqualTo (withBadge),
			"switching back to Object must redraw the overlay immediately, restoring the badge exactly");
	}
}
