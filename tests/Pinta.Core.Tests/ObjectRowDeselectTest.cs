using System;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug this pins: clicking a text object's sub-row starts editing it, which puts the dashed
/// re-edit rectangle and the "Obj." badge on the canvas; clicking the object's own <em>layer</em>
/// row afterwards left that chrome on screen with nothing selected. On-canvas editing chrome marks
/// the object being worked on, so a dock selection that names no object has to take it away —
/// which the dock signals with <see cref="LayerObjectSelection.ObjectDeselected"/> and the text and
/// shape tools act on by clearing the overlay layer.
///
/// <para>
/// The event is counted rather than expected exactly once: every <see cref="LayersListView"/> ever
/// constructed in this process stays subscribed to the workspace (the dock lives as long as the
/// window does and has no teardown), so sibling fixtures' views answer the same selection. What
/// matters is that a non-object row deselects at all.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ObjectRowDeselectTest : DocumentHarness
{
	private LayersListView view = null!;

	[SetUp]
	public void CreateView ()
		=> view = LayersListView.New ();

	[Test]
	public void SelectingALayerRowDeselectsItsObjects ()
	{
		using DocumentScope scope = OpenSecondDocument ();
		Document second = scope.Document;
		UserLayer layer = second.Layers[0];

		TextObject caption = Text ("Hi", new PointI (2, 2));
		layer.Objects.Add (caption);
		LayerObjectSelection.RaiseObjectsChanged ();

		// Start from the object's own row, the state that puts the chrome on the canvas.
		SelectRow (second, LayersListViewItem.NewTextObject (second, layer, caption, layer.Objects.IndexOf (caption)));

		int deselects = CountDeselects (() => SelectRow (second, LayersListViewItem.New (second, layer)));

		Assert.That (deselects, Is.GreaterThan (0),
			"selecting the object's plain layer row names no object, so the canvas chrome has to be dropped");
	}

	[Test]
	public void SelectingAMaskRowDeselectsObjectsToo ()
	{
		using DocumentScope scope = OpenSecondDocument ();
		Document second = scope.Document;
		UserLayer layer = second.Layers[0];
		layer.CreateMask ();
		LayerObjectSelection.RaiseObjectsChanged ();

		int deselects = CountDeselects (() => SelectRow (second, LayersListViewItem.NewMaskRow (second, layer)));

		Assert.That (deselects, Is.GreaterThan (0),
			"painting a mask is not editing an object, so an object's chrome must not linger over it");
	}

	private static int CountDeselects (Action act)
	{
		int deselects = 0;
		void Count () => deselects++;

		LayerObjectSelection.ObjectDeselected += Count;
		try {
			act ();
		} finally {
			LayerObjectSelection.ObjectDeselected -= Count;
		}

		return deselects;
	}

	private void SelectRow (Document document, LayersListViewItem row)
	{
		LayersListViewItemWidget widget = LayersListViewItemWidget.New ();
		widget.SetItem (row);
		view.SelectRowOnPress (widget, (Gdk.ModifierType) 0);
	}

	// The view fills its rows from ActiveDocumentChanged, which has already fired for the
	// harness's document, so a second document is what makes it populate.
	private static DocumentScope OpenSecondDocument ()
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
}
