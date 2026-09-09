using System.Linq;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug this pins: clicking a layer row in the dock did not always make that layer current, so
/// the next Cut acted on the layer that was still selected.
///
/// <para>
/// GTK selects a list row on button <em>release</em>, but every row also carries a
/// <c>Gtk.DragSource</c>, and a press that drifts past the drag threshold starts a drag-and-drop
/// instead — which resets the row's click gesture, so the release never arrives, the selection
/// never changes and the layer never becomes current. Dropping back on the row it came from is a
/// no-op, so the whole gesture did nothing visible and the user's second click "fixed" it. The
/// remedy is the guarantee right-click already had: the gesture makes the row's layer current
/// itself, through <see cref="LayersListViewItemWidget.MakeRowLayerCurrent"/>.
/// </para>
/// </summary>
[TestFixture]
internal sealed class LayerRowMakeCurrentTest : DocumentHarness
{
	private LayersListViewItemWidget RowFor (UserLayer layer)
	{
		LayersListViewItemWidget widget = LayersListViewItemWidget.New ();
		widget.SetItem (LayersListViewItem.New (Document, layer));
		return widget;
	}

	[Test]
	public void ARowMakesItsOwnLayerCurrent ()
	{
		UserLayer other = AddLayer ();
		Document.Layers.SetCurrentUserLayer (0);
		Assert.That (Document.Layers.CurrentUserLayer, Is.Not.SameAs (other), "setup: another layer has to be current");

		RowFor (other).MakeRowLayerCurrent ();

		Assert.That (Document.Layers.CurrentUserLayer, Is.SameAs (other),
			"the row's own layer has to become current, or the next Cut acts on the previous one");
	}

	// An object or mask sub-row belongs to a layer too, and dragging one has the same
	// swallowed-click problem, so it has to make its parent layer current as well.
	[Test]
	public void AMaskSubRowMakesItsParentLayerCurrent ()
	{
		UserLayer other = AddLayer ();
		other.CreateMask ();
		Document.Layers.SetCurrentUserLayer (0);

		LayersListViewItemWidget row = LayersListViewItemWidget.New ();
		row.SetItem (LayersListViewItem.NewMaskRow (Document, other));
		row.MakeRowLayerCurrent ();

		Assert.That (Document.Layers.CurrentUserLayer, Is.SameAs (other),
			"a sub-row's parent layer has to become current, the same as the layer's own row");
	}

	// Already-current is the common case (most presses are on the selected row); it must not push a
	// history item or otherwise disturb the document.
	[Test]
	public void ARowThatIsAlreadyCurrentChangesNothing ()
	{
		UserLayer current = Layer (Document.Layers.CurrentUserLayerIndex);
		int historyDepth = Document.History.Items.Count ();

		RowFor (current).MakeRowLayerCurrent ();

		Assert.Multiple (() => {
			Assert.That (Document.Layers.CurrentUserLayer, Is.SameAs (current));
			Assert.That (Document.History.Items.Count (), Is.EqualTo (historyDepth),
				"re-selecting the current layer must not add a history step");
		});
	}
}
