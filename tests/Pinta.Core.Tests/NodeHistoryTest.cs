using System.Linq;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// One layer, one stack, driven through history. These are the single-axis cases that
/// <see cref="DocumentInteractionsTest"/>'s scenes are built out of: if a scene breaks, one of these
/// says which piece did it.
/// </summary>
[TestFixture]
internal sealed class NodeHistoryTest : DocumentHarness
{
	private UserLayer Only => Layer (0);

	[SetUp]
	public void PaintTheLayer () => Fill (Only.Surface, Red);

	// The base case the rest build on: adding a node is one step, and stepping over it in either
	// direction has to move the pixels the canvas shows with it.
	[Test]
	public void UndoAndRedoOfAnAddedNodeMovesTheComposite ()
	{
		AddObject (Only, Invert (), "Invert");
		Assert.That (Shown (Only, 0, 0).R, Is.EqualTo (0), "the node should have inverted the red raster to cyan");

		Document.History.Undo ();
		Assert.That (Only.Objects, Is.Empty);
		Assert.That (Shown (Only, 0, 0).R, Is.EqualTo (255), "undo should put the raster back on screen unmodified");

		Document.History.Redo ();
		Assert.That (Only.Objects.Count, Is.EqualTo (1));
		Assert.That (Shown (Only, 0, 0).R, Is.EqualTo (0), "redo should bring the node's output back");
	}

	// The list is the composition order, and it has to stay the order across a history step. Two
	// non-commuting nodes make a swapped-back list visible: invert-then-halve is 127, the other way
	// round is 128, so a Swap that restored the wrong contents cannot pass by coincidence.
	[Test]
	public void NodeOrderSurvivesUndoAndRedo ()
	{
		AddObject (Only, Invert (), "Invert");
		AddObject (Only, Halve (), "Halve");

		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (127), "red inverted is cyan (B=255), halved is 127");

		Document.History.Undo ();
		Document.History.Undo ();
		Document.History.Redo ();
		Document.History.Redo ();

		Assert.That (Only.Objects.Count, Is.EqualTo (2));
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (127), "the nodes must come back in the order they were applied in");
	}

	// EffectModifierNode caches its render against a fingerprint of its input. Painting on the base
	// raster underneath a node changes that input without touching the node, so a cache keyed on
	// anything weaker than the pixels would leave the pre-paint render on screen.
	[Test]
	public void PaintingUnderANodeReRendersIt ()
	{
		AddObject (Only, Invert (), "Invert");
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (255), "red inverted is cyan");

		PaintRaster (Only, s => Fill (s, Blue), "Paint");

		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (0), "blue inverted is yellow; a stale cache would still show cyan");
	}

	// Undoing a raster edit made underneath a node restores the raster, and the node has to re-run
	// over what came back rather than over what it was last rendered from.
	[Test]
	public void UndoOfARasterEditUnderANodeReRunsTheNode ()
	{
		AddObject (Only, Invert (), "Invert");
		PaintRaster (Only, s => Fill (s, Blue), "Paint");
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (0), "blue inverted is yellow");

		Document.History.Undo ();

		Assert.That (Only.Surface.GetColorBgra (PointI.Zero).R, Is.EqualTo (255), "the raster should be red again");
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (255), "and the node should have re-inverted it to cyan");
	}

	// The mask is applied last, after every node. Undoing its addition has to take it out of that
	// chain, not merely off the layer, so the node's output comes back unscaled.
	[Test]
	public void UndoOfAMaskLeavesTheNodeStackIntact ()
	{
		AddObject (Only, Invert (), "Invert");

		LayerMask mask = Only.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (Only);
		Document.History.PushNewItem (
			new LayerMaskHistoryItem (PintaCore.Workspace, string.Empty, "Add Mask", Only, null, mask.Surface));

		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (128), "cyan under a half mask is premultiplied to 128");

		Document.History.Undo ();

		Assert.That (Only.Mask, Is.Null);
		Assert.That (Only.Objects.Count, Is.EqualTo (1), "undoing the mask must not disturb the node list");
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (255), "the node's output should be back at full alpha");

		Document.History.Redo ();
		Assert.That (Only.Mask, Is.Not.Null);
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (128));
	}

	// Pushing after an undo drops the redo tail. With modifier nodes in play the dropped items own
	// cloned node lists, so a pointer left pointing into the discarded tail replays a list that is no
	// longer reachable — the desync PushNewItem's redo sweep exists to prevent.
	[Test]
	public void PushingAfterAnUndoDropsTheRedoTail ()
	{
		AddObject (Only, Invert (), "Invert");
		AddObject (Only, Halve (), "Halve");
		Document.History.Undo ();

		AddObject (Only, Halve (), "Halve again");

		Assert.That (Document.History.CanRedo, Is.False);
		Assert.That (Document.History.Items.Count (), Is.EqualTo (2));
		Assert.That (Document.History.Pointer, Is.EqualTo (1));
		Assert.That (Only.Objects.Count, Is.EqualTo (2));
	}

	// A layer holding nodes is deleted and brought back. The nodes have to return with it and still
	// render, which they only do if the layer's stack was restored rather than rebuilt empty.
	[Test]
	public void ARestoredLayerKeepsItsNodes ()
	{
		AddObject (Only, Invert (), "Invert");

		UserLayer second = AddLayer (Green);
		Document.History.PushNewItem (
			new AddLayerHistoryItem (string.Empty, "Add Layer", Document.Layers.IndexOf (second)));

		Assert.That (Document.Layers.Count (), Is.EqualTo (2));

		Document.History.Undo ();
		Assert.That (Document.Layers.Count (), Is.EqualTo (1));
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (255), "the surviving layer's node should still be applying");

		Document.History.Redo ();
		Assert.That (Document.Layers.Count (), Is.EqualTo (2));
		Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (255));
	}

	// An op that bakes the layer's stack folds an applying mask into the pixels and drops it. Undo
	// restores the objects from the same history item, so it has to restore the mask with them — the
	// item records the mask after the bake has already dropped it unless the caller hands over the
	// one it captured beforehand, which read as "this layer never had a mask" and lost it for good.
	[Test]
	public void UndoOfAFlipRestoresTheMaskTheBakeDropped ()
	{
		AddObject (Only, Invert (), "Invert");

		LayerMask mask = Only.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (Only);
		Document.History.PushNewItem (
			new LayerMaskHistoryItem (PintaCore.Workspace, string.Empty, "Add Mask", Only, null, mask.Surface));

		FlipImageHorizontally ();
		Assert.That (Only.HasMask, Is.False, "the flip baked the stack, mask included");

		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (Only.HasMask, Is.True, "undo has to give the mask back, not only the node");
			Assert.That (Only.Objects.Count, Is.EqualTo (1));
			Assert.That (Shown (Only, 0, 0).B, Is.EqualTo (128), "cyan under the half mask, as before the flip");
		});
	}

	// The layer renders from its accumulated surface whenever it has one, so redoing a bake has to
	// drop the surface the undo rebuilt. Leaving it behind painted the pre-bake picture over pixels
	// that had already been baked and flipped — the raster was right and the canvas was a step behind.
	[Test]
	public void RedoOfAFlipStopsPaintingTheCompositeTheUndoRebuilt ()
	{
		AddObject (Only, Invert (), "Invert");

		FlipImageHorizontally ();
		ColorBgra flipped = Shown (Only, 0, 0);

		Document.History.Undo ();
		Assert.That (Only.Composite, Is.Not.Null, "undo puts the node back, so the layer renders from its composite again");

		Document.History.Redo ();

		Assert.Multiple (() => {
			Assert.That (Only.Composite, Is.Null, "a baked layer has no stack left to accumulate");
			Assert.That (Shown (Only, 0, 0), Is.EqualTo (flipped), "and the canvas shows the flipped, baked pixels");
		});
	}

	private static void FlipImageHorizontally ()
	{
		PintaCore.Actions.Image.FlipHorizontal.Sensitive = true;
		PintaCore.Actions.Image.FlipHorizontal.Activate ();
	}

	// IsDirty decides whether closing prompts, so getting it wrong loses work silently. Undoing back
	// to where the document was last clean has to clear it, not just moving forward set it.
	[Test]
	public void DirtyStateFollowsThePointer ()
	{
		Assert.That (Document.IsDirty, Is.False);

		AddObject (Only, Invert (), "Invert");
		Assert.That (Document.IsDirty, Is.True);

		Document.History.Undo ();
		Assert.That (Document.IsDirty, Is.False, "back at the clean point, the document matches its file again");

		Document.History.Redo ();
		Assert.That (Document.IsDirty, Is.True);
	}
}
