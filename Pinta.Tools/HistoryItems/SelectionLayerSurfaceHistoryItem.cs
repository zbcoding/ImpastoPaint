using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Swaps the floating selection's pixels, for edits that resample them in place (skew,
/// perspective) rather than through the selection layer's transform matrix.
/// </summary>
internal sealed class SelectionLayerSurfaceHistoryItem : BaseHistoryItem
{
	private readonly Document document;
	private ImageSurface surface;

	public SelectionLayerSurfaceHistoryItem (string icon, string text, Document document, ImageSurface surfaceBefore)
		: base (icon, text)
	{
		this.document = document;
		surface = surfaceBefore;
	}

	public override void Undo () => SwapSurface ();

	public override void Redo () => SwapSurface ();

	private void SwapSurface ()
	{
		Layer layer = document.Layers.SelectionLayer;
		(layer.Surface, surface) = (surface, layer.Surface);
		document.Workspace.Invalidate ();
	}
}
