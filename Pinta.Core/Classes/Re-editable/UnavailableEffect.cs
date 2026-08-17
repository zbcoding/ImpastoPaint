// UnavailableEffect.cs
//
// Stands in for an effect a saved document names but this build cannot supply — a node written by a
// newer Impasto, or one whose add-in is no longer installed. The node stays in the layer's list,
// keeps its identifier and its saved parameters, and writes them back out unchanged, so opening and
// re-saving a document does not quietly drop what it could not run.
//
// It renders nothing: the pixels it would have contributed are not in the file (the layer raster is
// the base raster), and guessing is worse than showing the document as it is.

using System;
using System.Collections.Generic;
using Cairo;

namespace Pinta.Core;

public sealed class UnavailableEffect : BaseEffect
{
	private readonly string effect_id;
	private readonly string display_name;

	/// <summary>The parameter text as it was read, written back verbatim on save.</summary>
	public IReadOnlyDictionary<string, string> SavedParameters { get; }

	public UnavailableEffect (string effectId, string displayName, IReadOnlyDictionary<string, string> savedParameters)
	{
		effect_id = effectId;
		display_name = string.IsNullOrEmpty (displayName) ? effectId : displayName;
		SavedParameters = savedParameters;
	}

	public override string EffectId => effect_id;
	public override string Name => display_name;
	public override bool IsTileable => true;
	public override bool IsConfigurable => false;

	public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
	{
		// Pass-through: the destination already holds a copy of the source.
	}
}
