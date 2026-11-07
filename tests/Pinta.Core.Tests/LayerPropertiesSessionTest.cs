using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The Layer Properties dialog is non-modal, so the current layer can change while it is open. Its
/// preview mirrors onto the selection layer (which is drawn with the current layer's properties)
/// only while its own layer is current; Cancel used to restore the selection layer under the same
/// condition, so hiding the layer, switching layers and cancelling left the selection layer hidden —
/// a later paste or moved selection on the now-current layer drew nothing.
/// </summary>
[TestFixture]
internal sealed class LayerPropertiesSessionTest : DocumentHarness
{
	[Test]
	public void CancelAfterSwitchingLayersDoesNotLeaveTheSelectionLayerHidden ()
	{
		UserLayer opened = Layer (0);
		UserLayer other = AddLayer ();
		Document.Layers.SetCurrentUserLayer (opened);

		LayerPropertiesSession session = new (PintaCore.Workspace, Document);
		session.Preview (session.InitialProperties with { Hidden = true });
		Document.Layers.SetCurrentUserLayer (other);

		session.End (commit: false);

		Assert.Multiple (() => {
			Assert.That (opened.Hidden, Is.False, "Cancel restores the layer the dialog was opened on");
			Assert.That (Document.Layers.SelectionLayer.Hidden, Is.False, "the selection layer is drawn for the visible current layer");
		});
	}

	[Test]
	public void CommitAfterSwitchingLayersDrawsTheSelectionLayerForTheCurrentLayer ()
	{
		UserLayer opened = Layer (0);
		UserLayer other = AddLayer ();
		other.Opacity = 0.75;
		Document.Layers.SetCurrentUserLayer (opened);

		LayerPropertiesSession session = new (PintaCore.Workspace, Document);
		session.Preview (session.InitialProperties with { Hidden = true, Opacity = 0.25 });
		Document.Layers.SetCurrentUserLayer (other);

		session.End (commit: true);

		Assert.Multiple (() => {
			Assert.That (opened.Hidden, Is.True, "OK keeps the edit on the layer the dialog was opened on");
			Assert.That (Document.Layers.SelectionLayer.Hidden, Is.False);
			Assert.That (Document.Layers.SelectionLayer.Opacity, Is.EqualTo (0.75), "not the opened layer's previewed opacity");
		});
	}

	[Test]
	public void PreviewAfterSwitchingLayersStillEditsTheOpenedLayer ()
	{
		UserLayer opened = Layer (0);
		UserLayer other = AddLayer ();
		Document.Layers.SetCurrentUserLayer (opened);

		LayerPropertiesSession session = new (PintaCore.Workspace, Document);
		Document.Layers.SetCurrentUserLayer (other);
		session.Preview (session.InitialProperties with { Opacity = 0.25 });

		Assert.Multiple (() => {
			Assert.That (opened.Opacity, Is.EqualTo (0.25));
			Assert.That (other.Opacity, Is.EqualTo (1.0), "the newly current layer is not the one being edited");
			Assert.That (Document.Layers.SelectionLayer.Opacity, Is.EqualTo (1.0), "the selection layer is drawn for the newly current layer");
		});
	}
}
