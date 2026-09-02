using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// <see cref="Document.FinishSelection"/> bakes the floating selection layer (a Move Selected
/// Pixels drag, or a paste) into the base raster. It used to do that with <see cref="Operator.Source"/>,
/// which replaces every destination pixel under the selection's clip with the selection layer's
/// pixel outright - including the selection layer's own transparent ones. Moving or pasting content
/// with any transparency inside its own footprint (near-universal for anything but a solid block)
/// onto other art already on the same layer punched holes in that other art wherever the moved
/// content was see-through, even though those pixels were never part of what got moved.
/// </summary>
[TestFixture]
internal sealed class FinishSelectionBlendTest : DocumentHarness
{
	private static readonly RectangleI Destination = new (10, 10, 8, 8);
	private static readonly RectangleI OpaqueHalf = new (10, 10, 4, 8);
	private static readonly RectangleI TransparentHalf = new (14, 10, 4, 8);

	[Test]
	public void ATransparentGapInTheMovedContentDoesNotEraseWhatWasAlreadyThere ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Blue); // pre-existing art covering the whole layer

		// The selection layer stands in for lifted content with a transparent gap inside its own
		// selection footprint: half opaque (the moved content itself), half left at the surface's
		// default fully-transparent (a gap in that content, not a hole cut into the destination).
		Document.Layers.CreateSelectionLayer ();
		Document.Layers.ShowSelectionLayer = true;
		FillRect (Document.Layers.SelectionLayer.Surface, OpaqueHalf, Red);

		Document.Selection = SelectionOf (Destination);
		Document.Selection.Visible = true;

		Document.FinishSelection ();

		Assert.That (Shown (layer, OpaqueHalf.Left, OpaqueHalf.Top), Is.EqualTo (Red),
			"the moved content's own opaque pixels must still land");
		Assert.That (Shown (layer, TransparentHalf.Left, TransparentHalf.Top), Is.EqualTo (Blue),
			"a transparent gap inside the moved content must not erase unrelated pixels already there");
	}
}
