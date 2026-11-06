using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The Object Properties window is non-modal, so the dock can reorder or delete objects while it is
/// open. It used to address its object by the row's index, so Cancel restored the starting values
/// onto whichever object had moved into that slot and OK recorded history against it. The window's
/// pinned copy of the row has to keep editing the object it was opened for, and do nothing once
/// that object is gone.
/// </summary>
[TestFixture]
internal sealed class PinnedObjectRowTest : DocumentHarness
{
	[Test]
	public void ReorderingUnderThePinnedRowKeepsEditingItsOwnObject ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		ShapeObject other = new () { Name = "other" };
		layer.Objects.Add (opened);
		layer.Objects.Add (other);

		LayersListViewItem pinned = LayersListViewItem.NewPinnedToObject (
			LayersListViewItem.NewShapeObject (Document, layer, opened, 0));

		pinned.SetObjectOpacity (0.25);
		Assert.That (layer.MoveObjectAt (0, 1), Is.True);

		// OK: one history step, recorded at the object's new position.
		pinned.PushObjectOpacityHistory (1.0);
		Document.History.Undo ();

		Assert.Multiple (() => {
			Assert.That (opened.Opacity, Is.EqualTo (1.0), "undo must restore the object the window was opened for");
			Assert.That (other.Opacity, Is.EqualTo (1.0), "the object that took its old slot must be untouched");
		});
	}

	[Test]
	public void DeletedObjectMakesThePinnedRowInert ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		ShapeObject other = new () { Name = "other" };
		layer.Objects.Add (opened);
		layer.Objects.Add (other);

		LayersListViewItem pinned = LayersListViewItem.NewPinnedToObject (
			LayersListViewItem.NewShapeObject (Document, layer, opened, 0));

		layer.Objects.Remove (opened);
		int stepsBefore = Document.History.Pointer;

		// Cancel and OK both, against an object that is no longer on the layer.
		pinned.SetObjectOpacity (0.25);
		pinned.PushObjectOpacityHistory (1.0);

		Assert.Multiple (() => {
			Assert.That (other.Opacity, Is.EqualTo (1.0), "the object now at index 0 is not the one the window edits");
			Assert.That (Document.History.Pointer, Is.EqualTo (stepsBefore), "no history for an object that is gone");
		});
	}
}
