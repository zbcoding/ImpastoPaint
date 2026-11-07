using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// What a non-modal Layer Properties dialog edits, captured when it opens: the document and its
/// current layer, so switching either while the dialog is open never retargets it, and that layer's
/// starting properties. The selection layer is drawn with the current layer's properties (a floating
/// paste or moved selection would otherwise ignore them), so the preview mirrors onto it while the
/// captured layer is current.
/// </summary>
public sealed class LayerPropertiesSession
{
	private readonly WorkspaceManager workspace;

	public LayerPropertiesSession (WorkspaceManager workspace, Document document)
	{
		this.workspace = workspace;
		Document = document;
		Layer = document.Layers.CurrentUserLayer;
		InitialProperties = PropertiesOf (Layer);
	}

	public Document Document { get; }

	public UserLayer Layer { get; }

	public LayerProperties InitialProperties { get; }

	/// <summary>
	/// The captured layer still belongs to an open document (it was not deleted, nor its document closed).
	/// </summary>
	public bool IsLayerAttached
		=> IsDocumentOpen && Document.Layers.IndexOf (Layer) >= 0;

	private bool IsDocumentOpen
		=> workspace.OpenDocuments.Contains (Document);

	/// <summary>
	/// Shows <paramref name="properties"/> on the captured layer, and on the selection layer while the
	/// captured layer is the current one.
	/// </summary>
	public void Preview (LayerProperties properties)
	{
		properties.SetProperties (Layer);

		if (IsLayerAttached && Document.Layers.CurrentUserLayer == Layer)
			properties.SetProperties (Document.Layers.SelectionLayer);
	}

	/// <summary>
	/// Ends the session: without <paramref name="commit"/> the layer goes back to its starting
	/// properties. The selection layer may still carry the preview from while the captured layer was
	/// current, so it is drawn with the current layer's properties again — whichever layer that is now.
	/// </summary>
	public void End (bool commit)
	{
		if (!commit)
			InitialProperties.SetProperties (Layer);

		if (IsDocumentOpen)
			PropertiesOf (Document.Layers.CurrentUserLayer).SetProperties (Document.Layers.SelectionLayer);
	}

	private static LayerProperties PropertiesOf (Layer layer)
		=> new (layer.Name, layer.Hidden, layer.Opacity, layer.BlendMode);
}
