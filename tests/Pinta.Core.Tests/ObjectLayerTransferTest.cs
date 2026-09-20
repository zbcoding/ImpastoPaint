using System.Linq;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// An object sub-node dragged onto a different layer's row moves to that layer. Object geometry is
/// canvas-space and every layer of a document is canvas-sized, so the object keeps its position on
/// screen and only changes which stack composites it — which means both layers' object surfaces
/// (derived caches, pure functions of their object lists) have to be re-rendered, not just the one
/// that gained the object.
/// </summary>
[TestFixture]
internal sealed class ObjectLayerTransferTest : DocumentHarness
{
	private static readonly PointI TextOrigin = new (2, 2);
	private static readonly RectangleI GlyphRegion = new (2, 2, 10, 10);

	private static bool HasInk (UserLayer layer, RectangleI region)
	{
		if (!layer.ObjectLayer.IsLayerSetup)
			return false;

		for (int y = region.Top; y <= region.Bottom; ++y)
			for (int x = region.Left; x <= region.Right; ++x)
				if (layer.ObjectLayer.Layer.Surface.GetColorBgra (new PointI (x, y)).A != 0)
					return true;

		return false;
	}

	private LayersListViewItem RowFor (UserLayer layer, ILayerObject obj)
		=> obj switch {
			TextObject text => LayersListViewItem.NewTextObject (Document, layer, text, layer.Objects.IndexOf (obj)),
			ShapeObject shape => LayersListViewItem.NewShapeObject (Document, layer, shape, layer.Objects.IndexOf (obj)),
			_ => LayersListViewItem.NewModifierNode (Document, layer, (ILayerModifierNode) obj, layer.Objects.IndexOf (obj)),
		};

	private LayersListViewItemWidget WidgetFor (LayersListViewItem row)
	{
		LayersListViewItemWidget widget = LayersListViewItemWidget.New ();
		widget.SetItem (row);
		return widget;
	}

	private LayersListViewItem LayerRowFor (UserLayer layer)
		=> LayersListViewItem.New (Document, layer);

	/// <summary>
	/// Runs the drop the way the gesture does: the handler only schedules the move, because
	/// mutating the object lists inside it destroys the very widget whose handler is running.
	/// </summary>
	private static bool Drop (LayersListViewItemWidget target, LayersListViewItem source, bool dropAbove)
	{
		System.Action? deferred = null;
		DeferredAction.Scheduler = a => deferred = a;
		try {
			bool accepted = target.DropObjectRow (source, dropAbove);
			deferred?.Invoke ();
			return accepted;
		} finally {
			DeferredAction.ResetScheduler ();
		}
	}

	[TearDown]
	public void RestoreRealScheduler () => DeferredAction.ResetScheduler ();

	/// <summary>
	/// GTK picks the drag cursor from whether the row under the pointer accepts the drag, so a row
	/// that would refuse the drop has to refuse the drag as well — otherwise the "move" cursor
	/// promises a drop that silently does nothing. These pin the accept rule, not the drop.
	/// </summary>
	[Test]
	public void OnlyRowsThatWouldTakeTheDropAcceptTheDrag ()
	{
		UserLayer source = Layer (0);
		UserLayer other = AddLayer ();

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");
		EffectModifierNode invert = Invert ();
		AddObject (source, invert, "Invert");
		ShapeObject box = Box (new Cairo.Color (0, 1, 0, 1), new RectangleI (0, 0, 4, 4));
		AddObject (other, box, "Box");

		LayersListViewItem textRow = RowFor (source, caption);
		LayersListViewItem effectRow = RowFor (source, invert);

		Assert.Multiple (() => {
			Assert.That (WidgetFor (LayerRowFor (other)).CanAcceptDrop (textRow), Is.True,
				"another layer's row takes a text object");
			Assert.That (WidgetFor (RowFor (other, box)).CanAcceptDrop (textRow), Is.True,
				"so does a slot between another layer's object rows");
			Assert.That (WidgetFor (LayerRowFor (other)).CanAcceptDrop (effectRow), Is.False,
				"an effect cannot leave the layer it grades, so the drag must show no-drop over other layers");
			Assert.That (WidgetFor (RowFor (source, caption)).CanAcceptDrop (effectRow), Is.True,
				"but it still reorders against its own layer's object rows");
			Assert.That (WidgetFor (LayerRowFor (source)).CanAcceptDrop (textRow), Is.False,
				"an object's own layer row names no position, so it must not look droppable");
			Assert.That (WidgetFor (RowFor (source, caption)).CanAcceptDrop (LayerRowFor (other)), Is.False,
				"a whole layer dragged onto an object row names no move");
			Assert.That (WidgetFor (LayerRowFor (other)).CanAcceptDrop (LayerRowFor (source)), Is.True,
				"layer rows still reorder against each other");
			Assert.That (WidgetFor (LayerRowFor (other)).CanAcceptDrop (LayersListViewItem.NewMaskRow (Document, source)), Is.False,
				"a mask is a slot on its layer, not something that can be dragged off it");
		});
	}

	[Test]
	public void DroppingATextRowOnAnotherLayersRowMovesIt ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");

		bool accepted = Drop (WidgetFor (LayerRowFor (destination)), RowFor (source, caption), dropAbove: false);

		Assert.Multiple (() => {
			Assert.That (accepted, Is.True, "the layer row has to accept a text object dropped on it");
			Assert.That (destination.Objects, Is.EqualTo (new ILayerObject[] { caption }),
				"the drop has to move the text onto the layer whose row received it");
			Assert.That (source.Objects, Is.Empty, "and off the one it came from");
		});
	}

	[Test]
	public void DroppingATextRowBetweenAnotherLayersObjectRowsPlacesItThere ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		ShapeObject bottom = Box (new Cairo.Color (1, 0, 0, 1), new RectangleI (0, 0, 4, 4));
		ShapeObject top = Box (new Cairo.Color (0, 1, 0, 1), new RectangleI (0, 0, 4, 4));
		AddObject (destination, bottom, "Bottom");
		AddObject (destination, top, "Top");

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");

		// Rows are drawn top-first, so the lower half of the bottom shape's row is the slot beneath
		// it — and across layers no removal-shift correction applies, unlike a same-layer reorder.
		Drop (WidgetFor (RowFor (destination, bottom)), RowFor (source, caption), dropAbove: false);

		Assert.That (destination.Objects, Is.EqualTo (new ILayerObject[] { caption, bottom, top }),
			"the text has to land at the slot the pointer named, under the shape it was dropped below");
	}

	[Test]
	public void DroppingAnObjectRowOnItsOwnLayersRowDoesNothing ()
	{
		UserLayer source = Layer (0);
		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");
		int stepsBefore = Document.History.Items.Count ();

		bool accepted = Drop (WidgetFor (LayerRowFor (source)), RowFor (source, caption), dropAbove: false);

		Assert.Multiple (() => {
			Assert.That (accepted, Is.False, "a layer row names no position among its own objects");
			Assert.That (Document.History.Items.Count (), Is.EqualTo (stepsBefore), "and leaves no undo step");
		});
	}

	[Test]
	public void DroppingALayerRowOnAnObjectRowDoesNothing ()
	{
		UserLayer source = Layer (0);
		UserLayer other = AddLayer ();
		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");

		Assert.That (
			Drop (WidgetFor (RowFor (source, caption)), LayerRowFor (other), dropAbove: false),
			Is.False,
			"dragging a whole layer onto an object row names no move");
	}

	[Test]
	public void DroppingAnEffectRowOnAnotherLayerIsRefused ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		EffectModifierNode invert = Invert ();
		AddObject (source, invert, "Invert");

		bool accepted = Drop (WidgetFor (LayerRowFor (destination)), RowFor (source, invert), dropAbove: false);

		Assert.Multiple (() => {
			Assert.That (accepted, Is.False, "the drop has to be refused so the drag shows no drop target");
			Assert.That (source.Objects, Is.EqualTo (new ILayerObject[] { invert }), "the effect stays where it grades");
		});
	}


	[Test]
	public void TextDroppedOnAnotherLayerMovesToIt ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");
		Assert.That (HasInk (source, GlyphRegion), Is.True,
			"setup: the text has to start visible on the layer it is moving off");

		RowFor (source, caption).MoveObjectToLayer (destination, null);

		Assert.Multiple (() => {
			Assert.That (source.Objects, Is.Empty, "the object has to leave the layer it was dragged off");
			Assert.That (destination.Objects, Is.EqualTo (new ILayerObject[] { caption }),
				"the very same object has to land on the layer it was dropped on, not a copy");
			Assert.That (HasInk (destination, GlyphRegion), Is.True,
				"the destination's object surface has to be re-rendered, or the text is invisible until something else refreshes it");
			Assert.That (HasInk (source, GlyphRegion), Is.False,
				"the source's object surface has to be re-rendered too, or the text goes on showing on the layer it left");
		});
	}

	[Test]
	public void TheDestinationLayerBecomesCurrent ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();
		Document.Layers.SetCurrentUserLayer (source);

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");

		RowFor (source, caption).MoveObjectToLayer (destination, null);

		Assert.That (Document.Layers.CurrentUserLayer, Is.SameAs (destination),
			"the moved object stays selected in the dock, so its new layer has to be the current one - and the "
			+ "switch is what commits a text edit started by the click that began the drag");
	}

	[Test]
	public void UndoPutsTheObjectBackWhereItCameFrom ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");
		int stepsBefore = Document.History.Items.Count ();

		RowFor (source, caption).MoveObjectToLayer (destination, null);
		Assert.That (Document.History.Items.Count (), Is.EqualTo (stepsBefore + 1),
			"the whole move has to be one undoable step");

		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (source.Objects, Is.EqualTo (new ILayerObject[] { caption }),
				"undo has to put the object back on its original layer, at its original position");
			Assert.That (destination.Objects, Is.Empty, "undo has to take it off the layer it was moved to");
			Assert.That (HasInk (source, GlyphRegion), Is.True, "undo has to re-render the layer that got the object back");
			Assert.That (HasInk (destination, GlyphRegion), Is.False, "undo has to re-render the layer that lost it");
		});
	}

	[Test]
	public void ADroppedObjectLandsAtTheIndexItWasDroppedOn ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		ShapeObject bottom = Box (new Cairo.Color (1, 0, 0, 1), new RectangleI (0, 0, 4, 4));
		ShapeObject top = Box (new Cairo.Color (0, 1, 0, 1), new RectangleI (0, 0, 4, 4));
		AddObject (destination, bottom, "Bottom");
		AddObject (destination, top, "Top");

		TextObject caption = Text ("Hi", TextOrigin);
		AddObject (source, caption, "Text");

		// Dropped between the two shapes, the way the drop handler resolves a row's index.
		RowFor (source, caption).MoveObjectToLayer (destination, 1);

		Assert.That (destination.Objects, Is.EqualTo (new ILayerObject[] { bottom, caption, top }),
			"the object has to land at the z-order slot the drop named, not on top of the stack");
	}

	/// <summary>
	/// A modifier node grades everything beneath it <em>on its own layer</em>, so relocating one is
	/// not "the same object elsewhere": the layer it left would silently lose its grade and the
	/// layer it joined would have its whole stack graded. Only content — shapes and text — moves.
	/// </summary>
	[Test]
	public void AModifierNodeStaysOnItsOwnLayer ()
	{
		UserLayer source = Layer (0);
		UserLayer destination = AddLayer ();

		Fill (source.Surface, Red);
		EffectModifierNode invert = Invert ();
		AddObject (source, invert, "Invert");

		LayersListViewItem row = RowFor (source, invert);
		Assert.That (row.IsMovableBetweenLayers, Is.False, "an effect row must not offer a cross-layer drag");

		int stepsBefore = Document.History.Items.Count ();
		row.MoveObjectToLayer (destination, null);

		Assert.Multiple (() => {
			Assert.That (source.Objects, Is.EqualTo (new ILayerObject[] { invert }),
				"the effect has to stay on the layer it grades");
			Assert.That (destination.Objects, Is.Empty, "the destination must not pick up another layer's grade");
			Assert.That (Document.History.Items.Count (), Is.EqualTo (stepsBefore),
				"a rejected move must not leave an undo step behind");
		});
	}

	[Test]
	public void AShapeRowOffersTheCrossLayerDrag ()
	{
		UserLayer source = Layer (0);
		ShapeObject box = Box (new Cairo.Color (0, 1, 0, 1), new RectangleI (0, 0, 4, 4));
		AddObject (source, box, "Box");

		Assert.That (RowFor (source, box).IsMovableBetweenLayers, Is.True,
			"a shape carries canvas-space geometry, so it can move to another layer unchanged");
	}

	[Test]
	public void SubRowOrdinalsIgnoreObjectsThatHaveNoRow ()
	{
		UserLayer layer = Layer (0);

		ShapeObject transient = Box (new Cairo.Color (1, 0, 0, 1), new RectangleI (0, 0, 4, 4));
		transient.RasterizeOnFinalize = true;
		TextObject caption = Text ("Hi", TextOrigin);

		layer.Objects.Add (transient);
		layer.Objects.Add (caption);

		Assert.Multiple (() => {
			Assert.That (layer.SubRowOrdinalAt (1), Is.EqualTo (0),
				"the rasterize-on-finalize shape gets no row, so the text is the first sub-row");
			Assert.That (layer.SubRowOrdinalAt (0), Is.EqualTo (-1), "an object with no row has no ordinal");
			Assert.That (layer.ObjectIndexOfSubRow (0), Is.EqualTo (1), "the first sub-row addresses the text");
		});

		// What a tool commit does to the list: the transient object is baked and dropped, shifting
		// every raw index above it. The ordinal is what still names the same row afterwards.
		layer.Objects.Remove (transient);

		Assert.That (layer.ObjectIndexOfSubRow (0), Is.EqualTo (0),
			"the sub-row ordinal has to keep naming the text after a commit renumbered the list");
	}
}
