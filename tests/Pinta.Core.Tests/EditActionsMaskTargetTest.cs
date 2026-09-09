using System;
using System.Reflection;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug these pin: Cut, Erase Selection and Fill Selection always read and wrote
/// <c>CurrentUserLayer.Surface</c>, while every paint tool goes through the mask-aware
/// <c>CurrentPaintSurface</c>/<c>CurrentMaskIsTarget</c> pair. So with a layer's mask row selected
/// in the layers dock — the state that makes brush strokes paint the mask — Cut destroyed the
/// layer's colour pixels and left the mask the user was editing untouched, and the history item it
/// pushed restored the wrong surface.
/// </summary>
[TestFixture]
internal sealed class EditActionsMaskTargetTest : DocumentHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private static readonly RectangleI Region = new (4, 4, 8, 8);
	private static readonly PointI Inside = new (6, 6);
	private static readonly PointI Outside = new (20, 20);

	[TearDown]
	public void ClearMaskTarget ()
	{
		// The active mask target is a static shared by the whole run; a fixture that sets it has to
		// put it back or every later test paints into a mask.
		LayerMaskSelection.SetActiveMaskLayer (null);
	}

	// The action handlers are invoked directly: reaching them through Gio actions needs the app-level
	// RegisterActions this headless harness does not run, and a disabled Gio action swallows
	// Activate without a word.
	private static void Invoke (string handler, object sender)
		=> typeof (EditActions).GetMethod (handler, NonPublicInstance)!
			.Invoke (PintaCore.Actions.Edit, [sender, EventArgs.Empty]);

	private static void EraseSelection (string sender = "Erase")
		=> Invoke ("HandlePintaCoreActionsEditEraseSelectionActivated", sender);

	private static void FillSelection ()
		=> Invoke ("HandlePintaCoreActionsEditFillSelectionActivated", "Fill");

	/// <summary>
	/// An opaque red layer under a fully revealing (opaque white) mask, with that mask set as the
	/// dock's edit target and <see cref="Region"/> selected — a user painting a mask, about to cut.
	/// </summary>
	private UserLayer MaskBeingEdited ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		LayerMask mask = layer.CreateMask ();
		Fill (mask.Surface, ColorBgra.White);

		LayerMaskSelection.SetActiveMaskLayer (layer);
		Assert.That (Document.Layers.CurrentMaskIsTarget, Is.True, "setup: the mask has to be the paint target");

		Document.Selection.CreateRectangleSelection (
			new RectangleD (Region.X, Region.Y, Region.Width, Region.Height));
		Document.Selection.Visible = true;

		return layer;
	}

	[Test]
	public void ErasingASelectionOnAMaskClearsTheMaskAndNotTheRaster ()
	{
		UserLayer layer = MaskBeingEdited ();

		EraseSelection ();

		Assert.Multiple (() => {
			Assert.That (layer.Mask!.Surface.GetColorBgra (Inside).A, Is.Zero,
				"the erase has to clear the mask the user is editing");
			Assert.That (layer.Mask!.Surface.GetColorBgra (Outside).A, Is.EqualTo (255),
				"only the selected region may be cleared");
			Assert.That (layer.Surface.GetColorBgra (Inside), Is.EqualTo (Red),
				"the layer's colour pixels must survive: they are not what the user was editing");
		});
	}

	[Test]
	public void UndoingAMaskEraseRestoresTheMask ()
	{
		UserLayer layer = MaskBeingEdited ();

		EraseSelection ("Cut");
		Assert.That (layer.Mask!.Surface.GetColorBgra (Inside).A, Is.Zero, "setup: the cut has to land on the mask");

		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (layer.Mask!.Surface.GetColorBgra (Inside).A, Is.EqualTo (255),
				"undo has to restore the mask, which is the surface the cut changed");
			Assert.That (layer.Surface.GetColorBgra (Inside), Is.EqualTo (Red),
				"undo must not write a mask-derived snapshot over the colour raster");
		});
	}

	// Fill Selection had the same gap by a different route, so it gets the same guarantee: filling
	// while editing a mask reveals that region rather than painting colour into the layer.
	[Test]
	public void FillingASelectionOnAMaskPaintsTheMaskAndNotTheRaster ()
	{
		UserLayer layer = MaskBeingEdited ();
		Fill (layer.Mask!.Surface, Transparent);
		PintaCore.Palette.PrimaryColor = new Cairo.Color (1, 1, 1);

		FillSelection ();

		Assert.Multiple (() => {
			Assert.That (layer.Mask!.Surface.GetColorBgra (Inside).A, Is.EqualTo (255),
				"the fill has to reveal the selected region of the mask");
			Assert.That (layer.Mask!.Surface.GetColorBgra (Outside).A, Is.Zero,
				"only the selected region may be filled");
			Assert.That (layer.Surface.GetColorBgra (Inside), Is.EqualTo (Red),
				"the layer's colour pixels must be left alone");
		});
	}

	// The ordinary path has to keep working: with no mask target the same handlers act on the raster.
	[Test]
	public void ErasingASelectionWithNoMaskTargetStillClearsTheRaster ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		Document.Selection.CreateRectangleSelection (
			new RectangleD (Region.X, Region.Y, Region.Width, Region.Height));
		Document.Selection.Visible = true;

		EraseSelection ();

		Assert.Multiple (() => {
			Assert.That (layer.Surface.GetColorBgra (Inside).A, Is.Zero, "the selected raster pixels have to be cleared");
			Assert.That (layer.Surface.GetColorBgra (Outside), Is.EqualTo (Red), "only the selected region may be cleared");
		});
	}
}
