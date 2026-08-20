using System.Collections.Generic;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The document-level coordinate changes: image resize, canvas resize, crop. Each one transforms
/// raster pixels only, so each bakes every layer's live objects first — an un-baked shape, text run
/// or modifier node keeps vector coordinates the transform never touched and snaps back to its old
/// placement on the next redraw. What these pin is that contract: the objects are gone afterwards,
/// their ink landed where the transform says it should, and one undo puts both the old size and the
/// objects back, because the bakes and the transform share a single compound history item.
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
	// the new region is empty. The shape is baked all the same, since its coordinates would survive a
	// move to any other anchor.
	[Test]
	public void ResizingTheCanvasKeepsAnchoredPixelsAndBakesObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 48), Anchor.NW, compoundAction: null);

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (48, 48)));
			Assert.That (Only.Objects, Is.Empty, "the shape must have been baked before the canvas grew");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255), "anchored north-west, the shape's ink does not move");
			Assert.That (Shown (Only, 20, 20).R, Is.EqualTo (255), "and neither does the raster under it");
			Assert.That (Shown (Only, 40, 40).A, Is.EqualTo (0), "the canvas grew into empty pixels");
		});
	}

	[Test]
	public void OneUndoOfACanvasResizeRestoresBothTheSizeAndTheObjects ()
	{
		PaintSceneWithLiveShape (new RectangleI (0, 0, 16, 16));

		Document.ResizeCanvas (new Size (48, 48), Anchor.NW, compoundAction: null);
		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (Document.ImageSize, Is.EqualTo (new Size (CanvasSize, CanvasSize)));
			Assert.That (Only.ShapeObjects.Count, Is.EqualTo (1), "the shape has to come back editable, not as baked pixels");
			Assert.That (Shown (Only, 4, 4).B, Is.EqualTo (255));
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
