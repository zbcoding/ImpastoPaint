// ObjectOpacity.cs
//
// Per-object opacity for the object-layer system: the first of the "simple sub-node layer
// effects". Each ShapeObject/TextObject carries its own 0..1 Opacity, applied where the object
// is composited into its layer's object surface — independent of the layer's own opacity.

using System;
using System.Collections.Generic;
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

	/// <summary>Hidden objects are skipped by every render path (see <see cref="ObjectOpacity.Draw"/>).</summary>
	bool Hidden { get; set; }

	/// <summary>User-facing name shown on the object's row in the layers dock. Empty = use the default.</summary>
	string Name { get; set; }

	/// <summary>How this object blends with what is already on its layer's object surface (see
	/// <see cref="ObjectOpacity.Draw"/>). Reuses the layer blend modes so shapes and text can mix
	/// against the pixels beneath them on the same layer.</summary>
	BlendMode BlendMode { get; set; }
}

public static class ObjectOpacity
{
	/// <summary>
	/// The per-object chokepoint every render path goes through: a hidden object draws nothing, a
	/// faded one is composited as a whole, and a non-normal blend mode composites against what is
	/// already on the surface. All per-object effects live here so no renderer has to remember them.
	/// </summary>
	public static void Draw (ImageSurface target, ILayerObject obj, Action<ImageSurface> draw)
	{
		if (obj.Hidden)
			return;

		Draw (target, obj.Opacity, obj.BlendMode, draw);
	}

	/// <summary>
	/// Draws the object with its own opacity but **Normal** blend — used when baking an object into a
	/// layer's base raster. The object's blend mode is a compositing effect that only makes sense
	/// against its fellow objects on the layer's object surface; baking it onto the base raster must
	/// produce the object's own pixels (fill/stroke), not re-blend them against the image beneath.
	/// Skips hidden objects (they contribute nothing when rasterized).
	/// </summary>
	public static void DrawNormalForBake (ImageSurface target, ILayerObject obj, Action<ImageSurface> draw)
	{
		if (obj.Hidden)
			return;

		Draw (target, obj.Opacity, BlendMode.Normal, draw);
	}

	/// <summary>
	/// Runs <paramref name="draw"/> against <paramref name="target"/>, faded to
	/// <paramref name="opacity"/> with <paramref name="mode"/> blending. Normal at full opacity draws
	/// straight into the target (no cost); anything else draws into a scratch surface first so the
	/// fade and blend apply to the object as a whole rather than to each of its overlapping
	/// fill/stroke/background passes. The blend reuses the layer blend mechanism
	/// (<see cref="CairoExtensions"/>), so an object mixes with the pixels beneath it on the layer.
	/// </summary>
	// ponytail: one image-sized scratch surface per affected object per redraw. Only allocated when
	// opacity < 1 or blend != Normal; if many such objects on one layer ever make redraws sluggish,
	// render into a surface sized to the object's bounds instead.
	public static void Draw (ImageSurface target, double opacity, BlendMode mode, Action<ImageSurface> draw)
	{
		if (opacity >= 1.0 && mode == BlendMode.Normal) {
			draw (target);
			return;
		}

		ImageSurface scratch = CairoExtensions.CreateImageSurface (Format.Argb32, target.Width, target.Height);
		draw (scratch);

		using Context g = new (target);
		g.BlendSurface (scratch, mode, Math.Clamp (opacity, 0, 1));
	}

	/// <summary>Forwarding convenience for a fade-only (Normal) composite.</summary>
	public static void Draw (ImageSurface target, double opacity, Action<ImageSurface> draw)
		=> Draw (target, opacity, BlendMode.Normal, draw);

	/// <summary>
	/// Re-renders a layer's unified object surface (<see cref="UserLayer.ObjectLayer"/>) from its
	/// z-ordered object list (the object-layer invariant) after an object property changed, then
	/// invalidates, and asks the active shape engine to resync its live editing engines.
	/// </summary>
	public static void RefreshLayer (IWorkspaceService workspace, IChromeService chrome, UserLayer layer)
	{
		RefreshLayerNoInvalidate (chrome, layer);
		workspace.Invalidate ();
	}

	/// <summary>
	/// Rebuilds the unified object surface (render + live-engine reload) without invalidating;
	/// callers that redraw themselves use this.
	/// </summary>
	public static void RefreshLayerNoInvalidate (IChromeService chrome, UserLayer layer)
	{
		RenderLayerObjects (chrome, layer);

		// Resync the active shape engine's live edits to the rebuilt surface — beware: the engine's
		// reload path calls back into RenderLayerObjects, so it must not loop (see RenderLayerObjects).
		LayerObjectSelection.RequestShapeReload (layer);
	}

	/// <summary>
	/// Clears the unified object surface and redraws every object — shape or text — in z-order, so
	/// each one's blend mode composites against everything beneath it regardless of kind. Text
	/// renders here in Core; shapes go through the Tools seam. This is the render-only core: it does
	/// NOT resync the live editing engines, so <see cref="RedrawShapeLayerSurface"/> can call into it
	/// without recursing (the engine's reload would otherwise call right back here).
	/// </summary>
	public static void RenderLayerObjects (IChromeService chrome, UserLayer layer)
	{
		ImageSurface surface = layer.ObjectLayer.Layer.Surface;
		surface.Clear ();

		if (layer.HasModifiers) {
			// Accumulator path: modifiers apply to what is beneath them, so objects have to be folded
			// into a copy of the base raster rather than drawn onto a separate transparent surface.
			// The object surface stays cleared — GetLayersToPaint paints the composite instead.
			ImageSurface accumulator = EffectModifierNode.CopyOf (layer.Surface);
			foreach (ILayerObject obj in layer.Objects) {
				if (obj is EffectModifierNode node)
					node.Apply (accumulator);
				else
					RenderObject (accumulator, layer, obj, chrome);
			}
			accumulator.MarkDirty ();
			layer.Composite = accumulator;
			return;
		}

		layer.Composite = null;
		foreach (ILayerObject obj in layer.Objects)
			RenderObject (surface, layer, obj, chrome);
	}

	private static void RenderObject (ImageSurface surface, UserLayer layer, ILayerObject obj, IChromeService chrome)
	{
		if (obj is ShapeObject shape)
			LayerObjectSelection.RenderShape (surface, layer, shape);
		else if (obj is TextObject text)
			TextObjectRenderer.Render (surface, text, chrome, antialias: true);
	}

	/// <summary>Clones a unified object list, preserving kind and order.</summary>
	public static List<ILayerObject> CloneAll (IReadOnlyList<ILayerObject> source)
	{
		List<ILayerObject> result = [];
		foreach (ILayerObject o in source) {
			if (o is ShapeObject s)
				result.Add (s.Clone ());
			else if (o is TextObject t)
				result.Add (t.Clone ());
			else if (o is EffectModifierNode n)
				result.Add (n.Clone ());
		}
		return result;
	}
}
