// LayerMaskSelection.cs
//
// Cross-assembly seam for "the user is editing a layer's mask right now". The layers dock lives in
// Pinta.Gui.Widgets and the paint tools live in Pinta.Tools; neither references the other, so they
// meet here in Core. Selecting a layer's mask row in the dock sets the active mask; paint tools read
// it to decide whether their strokes land on the layer raster or the mask surface. The canvas
// subscribes to the change event to show its "editing the mask" border.

using System;

namespace Pinta.Core;

/// <summary>
/// Whether the user is currently painting a layer's mask. Set by the layers dock when a mask row is
/// selected and cleared when any non-mask row, layer switch, or document switch happens. Also carries
/// whole-canvas notifications so the dock's mask row and the canvas border indicator stay in step.
/// </summary>
public static class LayerMaskSelection
{
	/// <summary>The layer whose mask is the current paint target, or null when painting the layer.</summary>
	public static UserLayer? ActiveMaskLayer { get; private set; }

	/// <summary>Fired when <see cref="ActiveMaskLayer"/> changes, so the canvas can redraw its indicator.</summary>
	public static event Action? MaskEditingChanged;

	public static void SetActiveMaskLayer (UserLayer? layer)
	{
		if (ReferenceEquals (ActiveMaskLayer, layer))
			return;

		ActiveMaskLayer = layer;
		MaskEditingChanged?.Invoke ();
	}

	/// <summary>Whether <paramref name="layer"/>'s mask is the current paint target.</summary>
	public static bool IsActiveMaskLayer (UserLayer layer)
		=> ReferenceEquals (ActiveMaskLayer, layer);
}
