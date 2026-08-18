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

public sealed class EffectModifierNode : ILayerModifierNode
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

	ILayerModifierNode ILayerModifierNode.CloneModifier () => Clone ();

	/// <summary>
	/// Runs the effect over <paramref name="surface"/> in place, honouring Hidden, the frozen clip,
	/// and blending the result back per Opacity/BlendMode. The effect reads from a snapshot so it
	/// never sees its own partial output.
	/// </summary>
	public void Apply (ImageSurface surface)
	{
		if (Hidden || Opacity <= 0)
			return;

		using ImageSurface rendered = Render (surface);

		using Context g = new (surface);
		if (Clip is not null) {
			g.AppendPath (Clip.SelectionPath);
			g.FillRule = FillRule.EvenOdd;
			g.Clip ();
		}
		g.BlendSurface (rendered, BlendMode, Math.Clamp (Opacity, 0, 1));
	}

	// The last render this node produced, with a fingerprint of the pixels it was produced from and
	// the parameter version it used. Toggling a node's visibility, undoing, redoing or changing a
	// node above it all feed the effect the exact same input, so the expensive pass runs once.
	private ImageSurface? cached_output;
	private ulong cached_input_fingerprint;
	private int cached_data_version;

	// Bumped whenever the effect's settings change, so a reopened dialog invalidates the cache.
	private int data_version;
	private EffectData? watched_data;

	/// <summary>Drops the cached render. Call when the node's effect or settings changed out of band.</summary>
	public void InvalidateCache ()
	{
		cached_output?.Dispose ();
		cached_output = null;
	}

	private ImageSurface Render (ImageSurface input)
	{
		WatchEffectData ();

		ulong fingerprint = Fingerprint (input);
		if (cached_output is not null
			&& cached_input_fingerprint == fingerprint
			&& cached_data_version == data_version
			&& cached_output.Width == input.Width
			&& cached_output.Height == input.Height)
			return CopyOf (cached_output);

		// Deliberately not ImageSurface.Clone(): that signals the workspace that a surface was cloned
		// (a history/autosave seam), which must not fire on a render pass that runs on every paint.
		using ImageSurface source = CopyOf (input);
		ImageSurface rendered = CopyOf (input);

		try {
			RenderTiled (source, rendered, RenderBoundsIn (input));
		} catch (Exception e) {
			// A node re-renders on every paint stroke, undo and visibility toggle, so an effect that
			// throws would otherwise take the application down mid-edit - and add-in effects are code
			// this build cannot vouch for. The node contributes nothing instead, loudly.
			Console.Error.WriteLine ($"Effect \"{Effect.Name}\" failed while rendering a layer effect node: {e}");
			rendered.Dispose ();
			rendered = CopyOf (input);
		}

		rendered.MarkDirty ();

		cached_output?.Dispose ();
		cached_output = CopyOf (rendered);
		cached_input_fingerprint = fingerprint;
		cached_data_version = data_version;

		return rendered;
	}

	// The region handed to the effect. The live preview runs the effect over the selection's bounding
	// box (LivePreviewManager.RenderBounds), so a node that rendered the whole canvas would place a
	// region-dependent effect - a twist's centre, a polar inversion's origin - somewhere other than
	// where the dialog showed it. Clipping the output afterwards cannot undo that. Rendering the same
	// box the preview used makes what is committed what was previewed, and costs less on a small clip.
	private RectangleI RenderBoundsIn (ImageSurface input)
	{
		RectangleI canvas = new (0, 0, input.Width, input.Height);

		if (Clip is null)
			return canvas;

		return Clip.GetBounds ().ToInt ().Intersect (canvas);
	}

	private void WatchEffectData ()
	{
		if (ReferenceEquals (watched_data, Effect.EffectData))
			return;

		if (watched_data is not null)
			watched_data.PropertyChanged -= EffectDataChanged;

		watched_data = Effect.EffectData;
		if (watched_data is not null)
			watched_data.PropertyChanged += EffectDataChanged;

		data_version++;
	}

	private void EffectDataChanged (object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		=> data_version++;

	// Cheap content fingerprint (FNV-1a over the pixel buffer). Reading the buffer costs a fraction
	// of what the effects worth caching cost, and comparing content rather than tracking every edit
	// site means no code path can silently leave a stale render on screen.
	private static ulong Fingerprint (ImageSurface surface)
	{
		ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes (surface.GetReadOnlyPixelData ());

		ulong hash = 14695981039346656037;
		foreach (byte b in bytes)
			hash = (hash ^ b) * 1099511628211;

		return hash;
	}

	/// <summary>
	/// Runs the effect across all cores when it allows it. A node re-renders on every visibility
	/// toggle, undo and redo, and a single-threaded full-canvas pass through an expensive effect is
	/// several seconds of frozen UI. Effects that accumulate across pixels declare themselves
	/// non-tileable and still get one whole-region call.
	/// </summary>
	private void RenderTiled (ImageSurface source, ImageSurface destination, RectangleI bounds)
	{
		int threads = Math.Max (1, Environment.ProcessorCount);

		// Banding hands the effect a region it would never see when applied destructively, one band per
		// core. Effects that ship here are rendered that way and checked; an add-in's is not, and one
		// that derives a value from the region it is given (a drag length, a wavelength) can compute
		// nonsense - or throw - on a band a fraction of the canvas height. Its own IsTileable claim is
		// about independence per pixel, not about region size, so pay the single-threaded pass instead.
		bool ownEffect = AddinMenu.AddinNameOf (Effect.GetType ()) is null;
		if (!Effect.IsTileable || !ownEffect || threads == 1 || bounds.Height < threads * 2) {
			Effect.Render (source, destination, [bounds]);
			return;
		}

		// Horizontal bands: contiguous rows keep each worker on its own stretch of the pixel buffer.
		int bandHeight = (bounds.Height + threads - 1) / threads;
		System.Threading.Tasks.Parallel.For (0, threads, band => {
			int top = bounds.Y + (band * bandHeight);
			int height = Math.Min (bandHeight, bounds.Bottom + 1 - top);
			if (height <= 0)
				return;

			Effect.Render (source, destination, [new RectangleI (bounds.X, top, bounds.Width, height)]);
		});
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
