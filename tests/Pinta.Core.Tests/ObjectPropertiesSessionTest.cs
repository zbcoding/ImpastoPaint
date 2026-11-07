using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The Object Properties window is non-modal and holds its object by reference, but the shape and
/// text tools swap every object for a new instance each time they persist, and objects carry no
/// identity that survives the swap. The window used to stay open after that with every control and
/// both buttons silently doing nothing. Once its object is gone the session has to end and say so,
/// so the window closes.
/// </summary>
[TestFixture]
internal sealed class ObjectPropertiesSessionTest : DocumentHarness
{
	[Test]
	public void ReplacingTheObjectEndsTheSessionOnTheNextHistoryStep ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		layer.Objects.Add (opened);
		ObjectPropertiesSession session = OpenSession (layer, opened);
		bool lost = false;
		session.ObjectLost += (_, _) => lost = true;

		// What a shape tool persist followed by its history step does.
		layer.ReplaceShapes ([opened.Clone ()]);
		Document.History.PushNewItem (new UpdateLayerPropertiesHistoryItem (
			Resources.Icons.LayerProperties, "step", 0, Properties (layer), Properties (layer)));

		Assert.That (lost, Is.True, "the window must close instead of staying open and inert");
	}

	[Test]
	public void ALiveChangeAfterASilentReplaceEndsTheSessionWithoutTouchingTheReplacement ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		layer.Objects.Add (opened);
		ObjectPropertiesSession session = OpenSession (layer, opened);
		bool lost = false;
		session.ObjectLost += (_, _) => lost = true;

		// A persist that records no history step and raises no notification.
		ShapeObject replacement = opened.Clone ();
		layer.ReplaceShapes ([replacement]);
		session.SetOpacity (0.25);

		Assert.Multiple (() => {
			Assert.That (lost, Is.True);
			Assert.That (replacement.Opacity, Is.EqualTo (1.0));
		});
	}

	[Test]
	public void CommitAfterTheObjectIsGoneRecordsNothing ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		ShapeObject other = new () { Name = "other" };
		layer.Objects.Add (opened);
		layer.Objects.Add (other);
		ObjectPropertiesSession session = OpenSession (layer, opened);

		session.SetOpacity (0.25);
		layer.Objects.Remove (opened);
		int stepsBefore = Document.History.Pointer;

		session.Commit ("renamed");

		Assert.Multiple (() => {
			Assert.That (Document.History.Pointer, Is.EqualTo (stepsBefore));
			Assert.That (other.Name, Is.EqualTo ("other"), "the object now in its slot is not the one being edited");
		});
	}

	[Test]
	public void CancelPutsTheStartingValuesBackOnTheObject ()
	{
		UserLayer layer = Layer (0);
		ShapeObject opened = new () { Name = "opened" };
		layer.Objects.Add (opened);
		ObjectPropertiesSession session = OpenSession (layer, opened);
		int stepsBefore = Document.History.Pointer;

		session.SetOpacity (0.25);
		session.SetHidden (true);
		session.Cancel ();

		Assert.Multiple (() => {
			Assert.That (opened.Opacity, Is.EqualTo (1.0));
			Assert.That (opened.Hidden, Is.False);
			Assert.That (Document.History.Pointer, Is.EqualTo (stepsBefore), "the live edits never reach history");
		});
	}

	private ObjectPropertiesSession OpenSession (UserLayer layer, ShapeObject shape)
		=> new (LayersListViewItem.NewShapeObject (Document, layer, shape, layer.Objects.IndexOf (shape)));

	private static LayerProperties Properties (UserLayer layer)
		=> new (layer.Name, layer.Hidden, layer.Opacity, layer.BlendMode);
}
