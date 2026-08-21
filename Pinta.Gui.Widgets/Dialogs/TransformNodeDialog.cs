// TransformNodeDialog.cs
//
// Creation and re-editing of a layer transform modifier node through the reflection-driven numeric
// dialog (SimpleEffectDialog). The node's LayerTransformData drives both: creating adds a fresh node
// to the current layer (one history item), editing mutates an existing node's data in place (one
// history item only when the values actually changed; Cancel restores the pre-dialog values).

using System.Collections.Generic;
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

public static class TransformNodeDialog
{
	/// <summary>Shows the numeric transform dialog for <paramref name="data"/> and returns whether OK.</summary>
	private static async Task<bool> Run (
		IChromeService chrome,
		IWorkspaceService workspace,
		UserLayer layer,
		LayerTransformData data,
		string title,
		string icon)
	{
		using SimpleEffectDialog dialog = SimpleEffectDialog.New (
			chrome.MainWindow,
			title,
			icon,
			data,
			new PintaLocalizer (),
			workspace);

		// Live preview: the layer re-renders with the node applied as the sliders move (the node is
		// already attached, see the callers), and the canvas repaints. Held in a local so the
		// unsubscribe below detaches the same delegate instance.
		System.ComponentModel.PropertyChangedEventHandler onChanged = (_, _) => {
			ObjectOpacity.RefreshLayerNoInvalidate (chrome, layer);
			workspace.Invalidate ();
		};
		dialog.EffectDataChanged += onChanged;

		Gtk.ResponseType response = await dialog.RunAsync ();
		dialog.Destroy ();
		dialog.EffectDataChanged -= onChanged;
		return response == Gtk.ResponseType.Ok;
	}

	/// <summary>
	/// Opens the dialog for a brand-new transform on <paramref name="layer"/>. The node is attached
	/// during the dialog so live preview works; on Cancel (or an identity transform) it is removed
	/// again, on OK it stays and one history item records the whole session.
	/// </summary>
	public static async Task Create (
		IChromeService chrome,
		IWorkspaceService workspace,
		UserLayer layer,
		string icon)
	{
		string title = Translations.GetString ("Transform Layer");
		LayerTransformNode node = new (new LayerTransformData ());
		List<ILayerObject> objectsBefore = ObjectOpacity.CloneAll (layer.Objects);

		layer.Objects.Insert (0, node);
		ObjectOpacity.RefreshLayer (workspace, chrome, layer);

		if (!await Run (chrome, workspace, layer, node.Data, title, icon) || node.Data.IsDefault) {
			layer.Objects.Remove (node);
			ObjectOpacity.RefreshLayer (workspace, chrome, layer);
			return;
		}

		workspace.ActiveDocument.History.PushNewItem (
			new LayerObjectsHistoryItem (workspace, chrome, icon, title, layer, objectsBefore));
		workspace.Invalidate ();
	}

	/// <summary>
	/// Re-opens an existing transform node's dialog. Mutates <paramref name="node"/>.Data in place;
	/// Cancel restores the saved values and Redo is skipped when nothing changed, so an edit the user
	/// dialled and dismissed without changing leaves no history step behind.
	/// </summary>
	public static async Task Edit (
		IChromeService chrome,
		IWorkspaceService workspace,
		UserLayer layer,
		LayerTransformNode node,
		string icon)
	{
		string title = Translations.GetString ("Transform Settings");
		LayerTransformData original = (LayerTransformData) node.Data.Clone ();
		List<ILayerObject> objectsBefore = ObjectOpacity.CloneAll (layer.Objects);

		if (!await Run (chrome, workspace, layer, node.Data, title, icon)) {
			node.Data = original;
			ObjectOpacity.RefreshLayer (workspace, chrome, layer);
			return;
		}

		if (node.Data.Equals (original)) {
			ObjectOpacity.RefreshLayer (workspace, chrome, layer);
			return;
		}

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
		workspace.ActiveDocument.History.PushNewItem (
			new LayerObjectsHistoryItem (workspace, chrome, icon, title, layer, objectsBefore));
	}
}
