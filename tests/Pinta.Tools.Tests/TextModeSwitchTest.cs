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

	/// <summary>
	/// Selecting an object (e.g. clicking its sub-row in the layers dock) copies its style into the
	/// toolbar. Setting the font controls fired their change handlers, which re-applied the whole
	/// toolbar - including the not-yet-synced alignment - onto the object: a centred object turned
	/// left-aligned just by being selected.
	/// </summary>
	[Test]
	public void SelectingAnObjectKeepsItsOwnAlignmentAndFont ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		Pango.FontDescription font = Pango.FontDescription.FromString ("Sans Bold Italic 40");
		obj.Engine.SetFont (font, TextAlignment.Center, underline: true);
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		Select (t, obj);

		Assert.Multiple (() => {
			Assert.That (obj.Engine.Alignment, Is.EqualTo (TextAlignment.Center), "selecting must not realign the object");
			Assert.That (obj.Engine.Font.GetWeight (), Is.EqualTo (Pango.Weight.Bold), "selecting must not change the weight");
			Assert.That (obj.Engine.Font.GetStyle (), Is.EqualTo (Pango.Style.Italic), "selecting must not change the style");
			Assert.That (PangoExtensions.UnitsToPixels (obj.Engine.Font.GetSize ()), Is.EqualTo (40), "selecting must not change the size");
			Assert.That (obj.Engine.Underline, Is.True, "selecting must not drop the underline");
		});
	}

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
	/// Selecting an existing text object also shows its own Point/Area mode in the toolbar
	/// (SyncToolbarFromObject). The Point/Area dropdown has to move with it, or it keeps showing
	/// the tool's last-used default and silently misreports the object's real mode - and
	/// re-clicking the value already shown is a no-op, so the user can't even correct it.
	/// </summary>
	[Test]
	public void SelectingAnAreaTextObjectSyncsTheModeDropdownAwayFromPoint ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.WrapWidth = 150; // Area mode
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 0; // Dropdown shows Point; the object about to be selected is Area.

		Select (t, obj); // StartEditing -> SyncToolbarFromObject

		Assert.That (TextModeButton (t).SelectedIndex, Is.EqualTo (1),
			"selecting an Area text object must move the Point/Area dropdown to Area, not leave it on the last Point default");
	}

	/// <summary>
	/// Selecting a Point-mode object must sync the dropdown the other way too, and must not
	/// disturb the object: the sync fires the dropdown's convert-in-place handler, whose branches
	/// are no-ops when the mode already matches (WrapWidth stays 0 for a point object).
	/// </summary>
	[Test]
	public void SelectingAPointTextObjectSyncsTheModeDropdownAndLeavesTheObjectAlone ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText ("hello");
		layer.AddText (obj);

		Assert.That (obj.Engine.WrapWidth, Is.Zero, "setup: the object starts as point text");

		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 1; // Dropdown shows Area; the object about to be selected is Point.

		Select (t, obj); // StartEditing -> SyncToolbarFromObject

		Assert.Multiple (() => {
			Assert.That (TextModeButton (t).SelectedIndex, Is.Zero,
				"selecting a Point text object must move the Point/Area dropdown to Point");
			Assert.That (obj.Engine.WrapWidth, Is.Zero,
				"the sync must not convert the object - it was already in the mode being shown");
		});
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
		// Tiny, so its dashed box and badge land inside the harness's 32px canvas.
		obj.Engine.SetFont (Pango.FontDescription.FromString ("Sans 2"), TextAlignment.Left, underline: false);
		obj.Engine.Origin = new PointI (2, 2);
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

	private static void Mouse (string method, TextTool t, Document doc, PointD at)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!
			.Invoke (t, [doc, new ToolMouseEventArgs { PointDouble = at, MouseButton = MouseButton.Left }]);

	private static void LeftDown (TextTool t, Document doc, PointD at) => Mouse ("DoMouseDown", t, doc, at);

	private static TextObject? CurrentObject (TextTool t)
		=> (TextObject?) typeof (TextTool).GetField ("current_text_object", NonPublicInstance)!.GetValue (t);

	/// <summary>
	/// Creating a new object synced the toolbar from it before its Area wrap width was set, so the
	/// sync read it as point text: the dropdown flipped back to Point and the object was created
	/// as point text - every click on empty canvas in Area mode made point text instead of a box.
	/// </summary>
	[Test]
	public void ClickingEmptyCanvasInAreaModeCreatesAreaTextAndKeepsTheDropdownOnArea ()
	{
		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 1; // Area

		LeftDown (t, Document, new PointD (10, 10));

		Assert.Multiple (() => {
			Assert.That (TextModeButton (t).SelectedIndex, Is.EqualTo (1), "the dropdown must stay on Area");
			Assert.That (CurrentObject (t)?.Engine.WrapWidth, Is.GreaterThan (0), "the new object must be area text");
		});
	}

	/// <summary>
	/// Click in Point mode (a blank point object starts), change your mind to Area, then click
	/// elsewhere: the blank point is dropped and the new click starts an area box - no need to
	/// deselect the blank point first.
	/// </summary>
	[Test]
	public void SwitchingToAreaAfterABlankPointClickLetsTheNextClickDrawABox ()
	{
		UserLayer layer = Layer (0);
		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 0; // Point

		LeftDown (t, Document, new PointD (10, 10));
		TextObject? blank = CurrentObject (t);
		Assert.That (blank?.Engine.WrapWidth, Is.Zero, "setup: the first click starts a blank point object");

		TextModeButton (t).SelectedIndex = 1; // Area
		LeftDown (t, Document, new PointD (40, 40));

		TextObject? created = CurrentObject (t);
		Assert.Multiple (() => {
			Assert.That (created, Is.Not.SameAs (blank), "the second click must start a new object");
			Assert.That (created?.Engine.WrapWidth, Is.GreaterThan (0), "the new object must be area text");
			Assert.That (TextModeButton (t).SelectedIndex, Is.EqualTo (1), "the dropdown must stay on Area");
			Assert.That (layer.Objects, Does.Not.Contain (blank), "the abandoned blank point object must be dropped");
		});
	}

	/// <summary>
	/// An area box placed with a click has no text yet, but it's still a box: its corner handle
	/// must resize it before anything is typed, rather than the click dropping the blank box.
	/// </summary>
	[Test]
	public void AnEmptyAreaBoxCanBeResizedBeforeTyping ()
	{
		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 1; // Area

		Mouse ("DoMouseDown", t, Document, new PointD (10, 10));
		Mouse ("DoMouseUp", t, Document, new PointD (10, 10)); // plain click -> default-width box
		TextObject box = CurrentObject (t)!;
		int placedWidth = box.Engine.WrapWidth;

		PointD[] corners = (PointD[]) typeof (TextTool).GetMethod ("GetInteractionCorners", NonPublicInstance)!.Invoke (t, [box])!;
		PointD bottomRight = corners[2];
		Mouse ("DoMouseDown", t, Document, bottomRight);
		Mouse ("DoMouseMove", t, Document, new PointD (bottomRight.X + 100, bottomRight.Y + 20));
		Mouse ("DoMouseUp", t, Document, new PointD (bottomRight.X + 100, bottomRight.Y + 20));

		Assert.Multiple (() => {
			Assert.That (CurrentObject (t), Is.SameAs (box), "grabbing the empty box's corner must keep editing the same box");
			Assert.That (box.Engine.WrapWidth, Is.GreaterThan (placedWidth), "dragging the corner outward must widen the empty box");
		});
	}

	/// <summary>
	/// Converting a long single-line point object to Area must not produce a box running off the
	/// canvas: the width stops at the canvas's right edge so the text re-wraps inside it.
	/// </summary>
	[Test]
	public void PointToAreaStopsTheBoxAtTheCanvasEdge ()
	{
		UserLayer layer = Layer (0);

		TextObject obj = new (new TextEngine ());
		obj.Engine.InsertText (new string ('W', 200));
		layer.AddText (obj);

		TextTool t = ActivateOnLayer ();
		TextModeButton (t).SelectedIndex = 0; // Point, matching the object.
		Select (t, obj);

		TextModeButton (t).SelectedIndex = 1; // Area

		Assert.That (obj.Engine.Origin.X + obj.Engine.WrapWidth, Is.LessThanOrEqualTo (Document.ImageSize.Width),
			"the converted box must end at or before the canvas's right edge");
	}
}
