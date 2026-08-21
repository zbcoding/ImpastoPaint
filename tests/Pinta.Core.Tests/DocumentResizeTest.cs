using System.Collections.Generic;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The document-level coordinate changes: image resize, canvas resize, crop. Image resize, crop, and
/// a canvas resize that shrinks any dimension all transform raster pixels in a way a live object's
/// vector coordinates cannot follow, so each bakes every layer's objects first — otherwise an
/// un-baked shape, text run or modifier node would keep its old coordinates and snap back to its old
/// placement on the next redraw. A canvas resize that only grows is the exception: nothing is
/// cropped away, so a layer with just shapes/text keeps them live and shifts their coordinates by the
/// same anchor offset the raster moves by instead (a modifier node still bakes there too, since one
/// can depend on the layer's current size when it renders). Either way, one undo puts both the old
/// size and the objects back, because the bakes/shifts and the resize share a single compound history
/// item.
/// </summary>
[TestFixture]
internal sealed class DocumentResizeTest : DocumentHarness
{
	private static readonly Color ShapeFill = new (0, 0, 1, 1);

	private UserLayer Only => Layer (0);

	// A red raster with a blue square sitting on it as a live shape object, not as pixels.
	private void PaintSceneWithLiveShape (RectangleI square)
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Box (ShapeFill, square), "Box");
		Assert.That (Shown (Only, square.Left + 1, square.Top + 1).B, Is.EqualTo (255),
			"the scene has to start with the shape on screen, or the resize has nothing to bake");
	}

	// Doubling the image scales the raster, and the shape has to be baked into that raster before the
	// scale runs: its control points are canvas coordinates the scale never rewrites, so a surviving
	// shape would re-render at its old size over the enlarged pixels.
	[Test]
	public void ResizingTheImageBakesObjectsAndScalesTheirPixels ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeImage (new Size (CanvasSize * 2, CanvasSize * 2), ResamplingMode.NearestNeighbor);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (64, 64)));
			Assert.That (Only.Surface.Width, Is.EqualTo (64), "the layer's raster has to follow the document");
			Assert.That (Only.Objects, Is.Empty, "the shape must have been baked, not carried across the scale");
			Assert.That (Shown (Only, 24, 24).B, Is.EqualTo (255),
				"the shape's ink covered a quarter of the canvas before and has to cover a quarter after");
			Assert.That (Shown (Only, 40, 40).R, Is.EqualTo (255), "and the raster outside it scaled with it");
		});
	}

	// The bake and the scale are pushed as one compound item precisely so the user does not have to
	// discover that undoing a resize takes two steps and leaves an intermediate state where the
	// objects are still pixels.
	[Test]
	public void OneUndoOfAnImageResizeRestoresBothTheSizeAndTheObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeImage (new Size (64, 64), ResamplingMode.NearestNeighbor);
		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape has to come back editable, not as baked pixels");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255));
			Assert.That (Shown (Only, 20, 20).R, Is.EqualTo (255), "the pre-resize raster is back at its old scale");
		});
	}

	// Growing the canvas anchored north-west moves nothing: the old pixels keep their coordinates and
	// the new region is empty. The shape's control points do not need to move either, so it stays a
	// live object instead of being baked.
	[Test]
	public void ResizingTheCanvasNorthWestGrowsWithoutBakingObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 48), Anchor.NW, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 48)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "growing crops nothing away, so the shape stays editable");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255), "anchored north-west, the shape's ink does not move");
			Assert.That (Shown (Only, 20, 20).R, Is.EqualTo (255), "and neither does the raster under it");
			Assert.That (Shown (Only, 40, 40).A, Is.EqualTo (0), "the canvas grew into empty pixels");
		});
	}

	// A centred grow moves the old raster's origin, not just the new empty region, so the live shape
	// has to move with it to still land on the pixels it used to cover.
	[Test]
	public void ResizingTheCanvasCentredTranslatesLiveObjectsToMatchTheRaster ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 48), Anchor.Center, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 48)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "growing crops nothing away, so the shape stays editable");
			Assert.That (Shown (Only, 12, 12).B, Is.EqualTo (255),
				"centring an 32x32-to-48x48 grow shifts old content by (8,8); the shape's corner has to follow");
			Assert.That (Shown (Only, 4, 4).A, Is.EqualTo (0), "the shape's old, un-shifted position is empty now");
		});
	}

	// A shape drawn inside a selection keeps a frozen Clip in canvas coordinates (see ShapeObject.Clip)
	// — that has to move by the same offset as the shape's own control points, or growing the canvas
	// would leave the shape clipped to a region it was never drawn relative to.
	[Test]
	public void ResizingTheCanvasTranslatesAShapesFrozenClipToMatch ()
	{
		Fill (Only.Surface, Red);
		ShapeObject shape = Box (ShapeFill, new RectangleI (0, 0, 16, 16));
		shape.Clip = SelectionOf (new RectangleI (0, 0, 8, 8));
		AddObject (Only, shape, "Box");

		Assert.That (Shown (Only, 2, 2).B, Is.EqualTo (255), "the scene has to start clipped to the top-left 8x8");
		Assert.That (Shown (Only, 12, 12).B, Is.EqualTo (0), "outside the clip, the shape's own fill must not show yet");

		Document.ResizeCanvas (new Size (48, 48), Anchor.Center, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Shown (Only, 10, 10).B, Is.EqualTo (255), "the clip has to move by the same (8,8) offset as the shape");
			Assert.That (Shown (Only, 2, 2).B, Is.EqualTo (0), "and no longer show at the old, un-shifted position");
		});
	}

	// A shape tool keeps its own live copy of a layer's shapes' control points while editing
	// (Pinta.Tools' SEngines), built once from UserLayer.Objects and not automatically kept in sync
	// with it. TranslateObjects only moves UserLayer.Objects, so the resize (and its undo/redo) has to
	// ask for a reload through this seam — the same one ObjectRasterizer already uses after a bake —
	// or a shape tool's next redraw uses the stale, pre-resize control points.
	[Test]
	public void ResizingTheCanvasRequestsAShapeReloadOnResizeUndoAndRedo ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		List<UserLayer> reloaded = [];
		void OnReload (UserLayer layer) => reloaded.Add (layer);
		LayerObjectSelection.ShapeReloadRequested += OnReload;
		try {
			Document.ResizeCanvas (new Size (48, 48), Anchor.Center, compoundAction: null);
			Assert.That (reloaded, Does.Contain (Only), "the resize itself has to request a reload");

			reloaded.Clear ();
			Document.History.Undo ();
			Assert.That (reloaded, Does.Contain (Only), "undoing the shift has to request a reload too");

			reloaded.Clear ();
			Document.History.Redo ();
			Assert.That (reloaded, Does.Contain (Only), "and so does redoing it");
		} finally {
			LayerObjectSelection.ShapeReloadRequested -= OnReload;
		}
	}

	[Test]
	public void OneUndoOfACanvasResizeRestoresBothTheSizeAndTheObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 48), Anchor.Center, compoundAction: null);
		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape has to come back editable, not as baked pixels");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255), "undo has to move the shape back, not just the canvas size");
		});
	}

	// A layer with a modifier node still bakes on a grow: an EffectModifierNode's clip is a frozen
	// selection whose coordinates a resize does not rewrite, so it would go on masking the wrong
	// region if it survived un-baked (same as the non-grow case below).
	[Test]
	public void GrowingTheCanvasStillBakesLayersWithModifierNodes ()
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Invert (SelectionOf (new RectangleI (0, 0, 16, 16))), "Invert");

		Document.ResizeCanvas (new Size (48, 48), Anchor.NW, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 48)));
			Assert.That (Only.HasModifiers, Is.False, "the node has to be baked; its clip cannot follow the grow");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255), "the inverted region is still there, now as pixels");
		});
	}

	// Shrinking on either axis can crop content away, which a coordinate shift cannot express, so it
	// keeps baking everything up front rather than trying to translate what remains.
	[Test]
	public void ShrinkingTheCanvasStillBakesObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (16, 16), Anchor.NW, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (16, 16)));
			Assert.That (Only.Objects, Is.Empty, "the shape must have been baked before the canvas shrank");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255));
		});
	}

	// Growing one axis while shrinking the other still risks cropping content on the shrinking axis,
	// so it has to take the bake path, not the translate one that a pure grow takes.
	[Test]
	public void ResizingWithOneDimensionShrinkingStillBakesObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 16), Anchor.NW, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 16)));
			Assert.That (Only.Objects, Is.Empty, "one shrinking axis is enough to require the bake");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255));
		});
	}

	// Paste-and-expand drives ResizeCanvas with its own compound history item (so the paste and the
	// resize undo together), which used to skip the bake entirely — the shape kept its old vector
	// coordinates while the raster shifted under it, so it rendered off from its own layer's pixels
	// instead of at the anchored position the resize gave everything else. The fix for that is what a
	// grow resize does for every caller now: translate the shape's coordinates by the same offset.
	[Test]
	public void ResizingTheCanvasAsPartOfACompoundActionTranslatesObjectsToMatch ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		CompoundHistoryItem pasteAction = new (Resources.Icons.ImageResizeCanvas, "Paste Into New Layer");
		Document.ResizeCanvas (new Size (48, 48), Anchor.Center, pasteAction);
		Document.History.PushNewItem (pasteAction);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 48)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape must have stayed live, same as a standalone grow");
			Assert.That (Shown (Only, 12, 12).B, Is.EqualTo (255), "and shifted by the same (8,8) offset as the raster");
		});
	}

	// ResizeCanvas returns whether it actually happened. A caller mid-way through a larger action (the
	// paste-and-expand flow, via a compound action) has to check that: nothing resized, baked or
	// moved, so carrying on regardless would silently land the rest of that action on the
	// original-sized canvas. A modifier node is what still prompts on a grow, so this scene needs one.
	[Test]
	public void CancellingTheRasterizePromptReportsFailureAndResizesNothing ()
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Invert (SelectionOf (new RectangleI (0, 0, 16, 16))), "Invert");

		ObjectRasterizer.ConfirmPrompt = _ => false;
		CompoundHistoryItem pasteAction = new (Resources.Icons.ImageResizeCanvas, "Paste Into New Layer");
		bool resized = Document.ResizeCanvas (new Size (48, 48), Anchor.NW, pasteAction);

		Assert.Multiple (() => {
			Assert.That (resized, Is.False, "a caller has to be able to tell the resize did not happen");
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.HasModifiers, Is.True, "the modifier node stays live, nothing was baked");
		});
	}

	// A modifier node's clip is a frozen selection in canvas coordinates. Nothing rewrites it, so a
	// node that outlived a resize would go on masking the region those coordinates used to name. The
	// bake is what makes that impossible, and this is the case that says so in pixels.
	[Test]
	public void AClippedNodeIsBakedRatherThanLeftPointingAtOldCoordinates ()
	{
		Fill (Only.Surface, Red);
		AddObject (Only, Invert (SelectionOf (new RectangleI (0, 0, 16, 16))), "Invert");
		Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255), "red inverted is cyan inside the clip");

		Document.ResizeImage (new Size (64, 64), ResamplingMode.NearestNeighbor);

		Assert.Multiple (() => {
			Assert.That (Only.HasModifiers, Is.False, "the node has to be baked; its clip cannot be rescaled");
			Assert.That (Shown (Only, 24, 24).B, Is.EqualTo (255), "the inverted region scaled with the raster");
			Assert.That (Shown (Only, 40, 40).B, Is.EqualTo (0), "and the un-inverted red outside it did too");
		});
	}

	// Crop to selection is the third caller of the same bake, driven through the action the menu item
	// activates. The cropped-to rectangle becomes the whole canvas, so what was at the selection's
	// top-left corner has to end up at the origin.
	[Test]
	public void CroppingToASelectionMovesThePixelsToTheOriginAndBakesObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (8, 8, 8, 8));

		Document.Selection.CreateRectangleSelection (new RectangleD (8, 8, 16, 16));
		CropToSelection ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (16, 16)));
			Assert.That (Only.Objects, Is.Empty, "the shape must have been baked before the crop");
			Assert.That (Shown (Only, 1, 1).B, Is.EqualTo (255), "the shape's corner pixel was at (8,8) and is now the origin");
			Assert.That (Shown (Only, 12, 12).R, Is.EqualTo (255), "the raster past the shape came along with it");
		});
	}

	// Cancelling the prompt must leave the document exactly as it was. The half-done outcome is the
	// one that would hurt: objects baked into pixels, with the resize the bake was for abandoned.
	[Test]
	public void CancellingTheRasterizePromptBakesNothingAndResizesNothing ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));
		int stepsBefore = Document.History.Pointer;

		List<string> asked = [];
		ObjectRasterizer.ConfirmPrompt = labels => { asked.AddRange (labels); return false; };

		Document.ResizeImage (new Size (64, 64), ResamplingMode.NearestNeighbor);

		Assert.Multiple (() => {
			Assert.That (asked, Is.Not.Empty, "the prompt has to name what the resize would make permanent");
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape stays editable");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255));
			Assert.That (Document.History.Pointer, Is.EqualTo (stepsBefore),
				"an abandoned resize must not leave a history item behind");
		});
	}

	// Crop reaches the prompt through the menu action rather than through Document, and has its own
	// early return to get wrong.
	[Test]
	public void CancellingTheRasterizePromptCropsNothing ()
	{
		PaintSceneWithLiveShape (new RectangleI (8, 8, 8, 8));
		int stepsBefore = Document.History.Pointer;

		ObjectRasterizer.ConfirmPrompt = _ => false;
		Document.Selection.CreateRectangleSelection (new RectangleD (8, 8, 16, 16));
		CropToSelection ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape stays editable");
			Assert.That (Document.History.Pointer, Is.EqualTo (stepsBefore));
		});
	}

	// A non-rectangular selection crops to its bounding box, but the pixels outside the path are
	// cleared rather than kept — the same bounding-box-versus-geometry split the clip work turns on.
	// An ellipse misses the corners of its own bounding box, so a crop that only took the box would
	// leave those corners painted.
	[Test]
	public void CroppingToAnEllipseClearsTheCornersOfItsBoundingBox ()
	{
		Fill (Only.Surface, Red);

		Document.Selection = EllipseIn (new RectangleI (8, 8, 16, 16));

		CropToSelection ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (16, 16)),
				"the crop takes the bounding box, all 16 rows of it");
			Assert.That (Shown (Only, 8, 8).R, Is.EqualTo (255), "the middle of the ellipse is inside the path and stays");
			Assert.That (Shown (Only, 0, 0).A, Is.EqualTo (0), "the box's corner is outside the path and has to be cleared");
		});
	}
}
