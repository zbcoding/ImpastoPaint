// ObjectOpacity.cs
//
// Per-object opacity for the object-layer system: the first of the "simple sub-node layer
// effects". Each ShapeObject/TextObject carries its own 0..1 Opacity, applied where the object
// is composited into its layer's object surface — independent of the layer's own opacity.

using System;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// What every object sub-node (shape or text) has in common. Small on purpose: it exists so code
/// that treats the two kinds alike — the layers dock's object rows today, per-object rename /
/// visibility later — doesn't have to branch on which list the object came from.
/// </summary>
public interface ILayerObject
{
	double Opacity { get; set; }
}

public static class ObjectOpacity
{
	/// <summary>
	/// Runs <paramref name="draw"/> against <paramref name="target"/>, faded to
	/// <paramref name="opacity"/>. Full opacity draws straight into the target (no cost); anything
	/// less draws into a scratch surface first so the fade applies to the object as a whole rather
	/// than to each of its overlapping fill/stroke/background passes.
	/// </summary>
	// ponytail: one image-sized scratch surface per faded object per redraw. Only allocated when
	// opacity < 1; if many faded objects on one layer ever make redraws sluggish, render into a
	// surface sized to the object's bounds instead.
	public static void Draw (ImageSurface target, double opacity, Action<ImageSurface> draw)
	{
		if (opacity >= 1.0) {
			draw (target);
			return;
		}

		ImageSurface scratch = CairoExtensions.CreateImageSurface (Format.Argb32, target.Width, target.Height);
		draw (scratch);

		using Context g = new (target);
		g.SetSourceSurface (scratch, 0, 0);
		g.PaintWithAlpha (Math.Clamp (opacity, 0, 1));
	}

	/// <summary>
	/// Re-renders a layer's object surfaces from its object lists (the object-layer invariant) after
	/// an object's opacity changed, and invalidates. Shapes go through the Tools seam (which also
	/// rebuilds the live editing engines so the active layer picks up the new opacity); text is
	/// rendered here in Core.
	/// </summary>
	public static void RefreshLayer (IWorkspaceService workspace, IChromeService chrome, UserLayer layer)
	{
		LayerObjectSelection.RequestShapeReload (layer);

		layer.TextLayer.Layer.Surface.Clear ();
		TextObjectRenderer.RenderAll (layer.TextLayer.Layer.Surface, layer.TextObjects, chrome, antialias: true);

		workspace.Invalidate ();
	}
}

/// <summary>
/// Undoable change of one object's opacity. Only the scalar is stored — the object surface is a
/// pure function of the object list, so undo/redo just swaps the value and re-renders.
/// </summary>
public sealed class ObjectOpacityHistoryItem : BaseHistoryItem
{
	private readonly IWorkspaceService workspace;
	private readonly IChromeService chrome;
	private readonly UserLayer layer;
	private readonly bool is_text;
	private readonly int index;
	private double stored_opacity;

	public ObjectOpacityHistoryItem (
		IWorkspaceService workspace,
		IChromeService chrome,
		string icon,
		string text,
		UserLayer layer,
		bool isText,
		int index,
		double previousOpacity)
		: base (icon, text)
	{
		this.workspace = workspace;
		this.chrome = chrome;
		this.layer = layer;
		is_text = isText;
		this.index = index;
		stored_opacity = previousOpacity;
	}

	public override void Undo () => Swap ();
	public override void Redo () => Swap ();

	private void Swap ()
	{
		// The object may be gone (e.g. rasterized) if history was stepped oddly; then there is
		// nothing to swap.
		if (is_text) {
			if (index >= layer.TextObjects.Count)
				return;
			(stored_opacity, layer.TextObjects[index].Opacity) = (layer.TextObjects[index].Opacity, stored_opacity);
		} else {
			if (index >= layer.ShapeObjects.Count)
				return;
			(stored_opacity, layer.ShapeObjects[index].Opacity) = (layer.ShapeObjects[index].Opacity, stored_opacity);
		}

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);
	}
}
