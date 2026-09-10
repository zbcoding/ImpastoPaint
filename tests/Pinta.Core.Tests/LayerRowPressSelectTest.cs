using System;
using System.Linq;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug this pins: clicking a layer row while the pointer is still moving selected nothing.
///
/// <para>
/// GTK selects a list row on button <em>release</em>, but every row also carries a
/// <c>Gtk.DragSource</c>: a press that drifts past the drag threshold claims the sequence and
/// cancels the row's click gesture, so the release never arrives as a selection. The remedy is
/// to select at press — before any drag can start — through
/// <see cref="LayersListView.SelectRowOnPress"/>, the same path the row's press gesture drives.
/// </para>
/// </summary>
[TestFixture]
internal sealed class LayerRowPressSelectTest : DocumentHarness
{
	[Test]
	public void PressingARowMakesItsLayerCurrent ()
	{
		using DocumentScope scope = OpenSecondDocument ();
		Document second = scope.Document;

		UserLayer target = second.Layers[0];
		Assert.That (second.Layers.CurrentUserLayer, Is.Not.SameAs (target), "setup: another layer has to be current");

		SelectRow (second, target, (Gdk.ModifierType) 0);

		Assert.That (second.Layers.CurrentUserLayer, Is.SameAs (target),
			"a press has to select the row, or a click with a moving pointer selects nothing");
	}

	// Modifier presses are left to the native release path, which implements the toggle (Ctrl)
	// and extend (Shift) semantics; handling them at press too would toggle twice.
	[Test]
	public void PressingARowWithModifiersLeavesSelectionToRelease ()
	{
		using DocumentScope scope = OpenSecondDocument ();
		Document second = scope.Document;

		UserLayer current = second.Layers.CurrentUserLayer;
		UserLayer target = second.Layers[0];

		SelectRow (second, target, Gdk.ModifierType.ControlMask);

		Assert.That (second.Layers.CurrentUserLayer, Is.SameAs (current),
			"a modifier press must not select; the native release path owns toggle/extend");
	}

	// Re-selecting the row the view already sits on must reach the same no-op the native
	// release-time selection does: no history item, no disturbance.
	[Test]
	public void PressingTheSelectedRowAgainChangesNothing ()
	{
		using DocumentScope scope = OpenSecondDocument ();
		Document second = scope.Document;

		UserLayer current = second.Layers.CurrentUserLayer;
		int historyDepth = second.History.Items.Count ();

		SelectRow (second, current, (Gdk.ModifierType) 0);

		Assert.Multiple (() => {
			Assert.That (second.Layers.CurrentUserLayer, Is.SameAs (current));
			Assert.That (second.History.Items.Count (), Is.EqualTo (historyDepth),
				"re-selecting the current layer must not add a history step");
		});
	}

	private LayersListView view = null!;

	[SetUp]
	public void CreateView ()
		=> view = LayersListView.New ();

	// The view fills its rows from ActiveDocumentChanged, which has already fired for the
	// harness's document, so a second document is what makes it populate. It is closed again
	// so no other test sees a stale document in the workspace.
	private DocumentScope OpenSecondDocument ()
	{
		Document second = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));

		second.Layers.AddNewLayer (string.Empty);
		second.Layers.AddNewLayer (string.Empty);
		PintaCore.Workspace.ActivateDocument (second);

		return new DocumentScope (second);
	}

	private sealed class DocumentScope (Document document) : IDisposable
	{
		public Document Document { get; } = document;

		public void Dispose ()
			=> PintaCore.Workspace.CloseDocument (Document);
	}

	private void SelectRow (Document document, UserLayer layer, Gdk.ModifierType modifiers)
	{
		LayersListViewItemWidget widget = LayersListViewItemWidget.New ();
		widget.SetItem (LayersListViewItem.New (document, layer));
		view.SelectRowOnPress (widget, modifiers);
	}
}
