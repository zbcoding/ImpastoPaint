// EffectModifierNode.cs
//
// A non-destructive adjustment/effect sitting in a layer's unified object list. Unlike a shape or
// text object, which contributes pixels, a modifier transforms everything beneath it in the list
// (see UserLayer.Objects). Opacity is effect strength and BlendMode blends the modified result
// against the unmodified input, so the ILayerObject properties keep their dock plumbing.
//
// See docs-private/layer-effects-model.md for the full model.

using System;
using Cairo;

namespace Pinta.Core;

public sealed class EffectModifierNode : ILayerObject
{
	/// <summary>
	/// The effect this node runs. Its <see cref="BaseEffect.EffectData"/> is this node's own copy —
	/// re-opening the node's dialog edits it in place, so it must never be the live menu instance's.
	/// </summary>
	public BaseEffect Effect { get; }

	/// <summary>
	/// The selection active when the node was created, or null for the whole layer. Frozen so the
	/// node keeps applying where it was applied, rather than following the live selection.
	/// </summary>
	public DocumentSelection? Clip { get; set; }

	public double Opacity { get; set; } = 1.0;
	public bool Hidden { get; set; } = false;
	public string Name { get; set; } = string.Empty;
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	// Kept so Clone can build another instance of the effect the same way this one was built.
	private readonly IServiceProvider? services;

	public EffectModifierNode (BaseEffect effect, DocumentSelection? clip = null, IServiceProvider? services = null)
	{
		Effect = effect;
		Clip = clip;
		this.services = services;
	}

	/// <summary>
	/// Builds a node from the live menu instance of an effect. The node gets its own effect instance
	/// and its own copy of the parameters, so later menu use cannot rewrite a node already placed.
	/// </summary>
	public static EffectModifierNode FromEffect (BaseEffect effect, DocumentSelection? clip, IServiceProvider? services = null)
		=> new (CopyOf (effect, services), clip, services);

	/// <summary>
	/// A second instance of <paramref name="effect"/> carrying a copy of its parameters. Most effects
	/// take the service provider they were registered with, a few take nothing, so both shapes are
	/// tried. An effect whose constructor takes something else falls back to sharing the instance:
	/// the node still renders correctly, but editing its settings also moves the menu's copy.
	/// </summary>
	private static BaseEffect CopyOf (BaseEffect effect, IServiceProvider? services)
	{
		BaseEffect copy = Instantiate (effect.GetType (), services) ?? effect;

		if (!ReferenceEquals (copy, effect) && effect.EffectData is not null)
			copy.EffectData = effect.EffectData.Clone ();

		return copy;
	}

	private static BaseEffect? Instantiate (Type type, IServiceProvider? services)
	{
		if (services is not null) {
			try {
				return (BaseEffect?) Activator.CreateInstance (type, services);
			} catch (MissingMethodException) {
				// Falls through to the parameterless shape.
			}
		}

		try {
			return (BaseEffect?) Activator.CreateInstance (type);
		} catch (MissingMethodException) {
			return null;
		}
	}

	/// <summary>The dock label: the user's name if they set one, otherwise the effect's own name.</summary>
	public string DisplayName => string.IsNullOrEmpty (Name) ? Effect.Name : Name;

	/// <summary>
	/// Deep-copies the node, including a fresh effect instance carrying a copy of the parameters, so
	/// history snapshots and Duplicate Layer cannot alias one another's settings.
	/// </summary>
	public EffectModifierNode Clone ()
	{
		return new (CopyOf (Effect, services), Clip, services) {
			Opacity = Opacity,
			Hidden = Hidden,
			Name = Name,
			BlendMode = BlendMode,
		};
	}

	/// <summary>
	/// Runs the effect over <paramref name="surface"/> in place, honouring Hidden, the frozen clip,
	/// and blending the result back per Opacity/BlendMode. The effect reads from a snapshot so it
	/// never sees its own partial output.
	/// </summary>
	public void Apply (ImageSurface surface)
	{
		if (Hidden || Opacity <= 0)
			return;

		// Deliberately not ImageSurface.Clone(): that signals the workspace that a surface was cloned
		// (a history/autosave seam), which must not fire on a render pass that runs on every paint.
		using ImageSurface source = CopyOf (surface);
		using ImageSurface rendered = CopyOf (surface);

		RectangleI bounds = new (0, 0, surface.Width, surface.Height);
		Effect.Render (source, rendered, [bounds]);
		rendered.MarkDirty ();

		using Context g = new (surface);
		if (Clip is not null) {
			g.AppendPath (Clip.SelectionPath);
			g.FillRule = FillRule.EvenOdd;
			g.Clip ();
		}
		g.BlendSurface (rendered, BlendMode, Math.Clamp (Opacity, 0, 1));
	}

	internal static ImageSurface CopyOf (ImageSurface source)
	{
		ImageSurface copy = CairoExtensions.CreateImageSurface (source.Format, source.Width, source.Height);
		using Context g = new (copy);
		g.SetSourceSurface (source, 0, 0);
		g.Paint ();
		copy.MarkDirty ();
		return copy;
	}
}
