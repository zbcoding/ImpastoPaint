using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug these pin: <see cref="PasteHistoryItem"/> recorded the pasted pixels but not where they
/// landed. Its <c>Redo</c> built a fresh, document-sized selection layer with an identity
/// transform and painted the image at (0,0), while <c>Swap</c> put the selection marquee back at
/// the paste position — so undo followed by redo teleported the pasted content to the canvas's top
/// left, away from its own outline. An image larger than the canvas lost its overhang the same way,
/// because the redone selection layer was only as big as the document.
/// </summary>
[TestFixture]
internal sealed class PasteHistoryPositionTest : DocumentHarness
{
	private static readonly PointI PastePosition = new (7, 5);
	private const int PasteSize = 6;

	private static ImageSurface PastedImage (int width, int height)
	{
		ImageSurface image = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		Fill (image, Blue);
		return image;
	}

	/// <summary>
	/// Replays what <c>PasteAction.Paste</c> does to the document, then hands back the history item
	/// it pushes. Kept in step with that method deliberately: the history item's job is to be able
	/// to reproduce this state, so the test has to start from it.
	/// </summary>
	private PasteHistoryItem PasteAt (ImageSurface image, PointI position)
	{
		Document.Layers.CreateSelectionLayer (
			System.Math.Max (Document.ImageSize.Width, image.Width),
			System.Math.Max (Document.ImageSize.Height, image.Height));
		Document.Layers.ShowSelectionLayer = true;

		using (Context g = new (Document.Layers.SelectionLayer.Surface)) {
			g.SetSourceSurface (image, 0, 0);
			g.Paint ();
		}

		Document.Layers.SelectionLayer.Transform.InitIdentity ();
		Document.Layers.SelectionLayer.Transform.Translate (position.X, position.Y);

		DocumentSelection oldSelection = Document.Selection.Clone ();
		Document.Selection.CreateRectangleSelection (
			new RectangleD (position.X, position.Y, image.Width, image.Height));
		Document.Selection.Visible = true;

		PasteHistoryItem item = new (image, position, oldSelection);
		Document.History.PushNewItem (item);
		return item;
	}

	private static PointD TransformOffset (Layer layer)
	{
		PointD origin = layer.Transform.TransformPoint (new PointD (0, 0));
		return origin;
	}

	[Test]
	public void RedoingAPastePutsThePixelsBackWhereTheyWere ()
	{
		PasteAt (PastedImage (PasteSize, PasteSize), PastePosition);

		Document.History.Undo ();
		Document.History.Redo ();

		Assert.That (TransformOffset (Document.Layers.SelectionLayer),
			Is.EqualTo (new PointD (PastePosition.X, PastePosition.Y)),
			"redo has to restore the offset the paste landed at, not drop the content at canvas (0,0)");
	}

	// The marquee and the pixels are restored by different halves of the item (Swap and the layer
	// rebuild), so pin that they agree: a selection outline with nothing under it is the visible
	// symptom users would report.
	[Test]
	public void RedoingAPasteLeavesTheMarqueeOverThePixels ()
	{
		PasteAt (PastedImage (PasteSize, PasteSize), PastePosition);

		Document.History.Undo ();
		Document.History.Redo ();

		Assert.That (Document.GetSelectedBounds (true),
			Is.EqualTo (new RectangleI (PastePosition.X, PastePosition.Y, PasteSize, PasteSize)),
			"setup: the marquee is restored at the paste position");
		Assert.That (TransformOffset (Document.Layers.SelectionLayer),
			Is.EqualTo (new PointD (PastePosition.X, PastePosition.Y)),
			"the pixels have to sit under the marquee, not somewhere else on the canvas");
	}

	// Pasting an image bigger than the canvas is allowed (the user may decline to expand it), so the
	// paste's selection layer is oversized. Redo has to rebuild it that way or the overhang - the
	// part the user would drag into view with the move tool - is clipped away.
	[Test]
	public void RedoingAnOversizedPasteKeepsTheSurfaceBigEnoughForIt ()
	{
		int oversize = CanvasSize + 10;
		PasteAt (PastedImage (oversize, oversize), PointI.Zero);

		Document.History.Undo ();
		Document.History.Redo ();

		Assert.Multiple (() => {
			Assert.That (Document.Layers.SelectionLayer.Surface.Width, Is.GreaterThanOrEqualTo (oversize),
				"the redone selection layer has to hold the whole pasted image, not just the canvas");
			Assert.That (Document.Layers.SelectionLayer.Surface.Height, Is.GreaterThanOrEqualTo (oversize));
		});
	}
}
