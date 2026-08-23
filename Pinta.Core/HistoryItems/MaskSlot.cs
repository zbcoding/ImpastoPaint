using System;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// The two history items that toggle a layer's mask slot (<see cref="CompoundHistoryItem"/>'s
/// whole-image snapshots and <see cref="RasterizeObjectsHistoryItem"/>'s bakes) move the same
/// (surface, hidden, presence) triple between the item and the layer. Reference hand-off, not a
/// clone: each swap moves the surfaces between the two sides, so undo/redo alternate cleanly.
/// </summary>
internal static class MaskSlot
{
	public static void Swap (UserLayer layer, ref ImageSurface? surface, ref bool hidden, ref bool had_mask)
	{
		ImageSurface? current = layer.Mask?.Surface;
		bool currentHidden = layer.Mask?.Hidden ?? false;
		bool currentHad = layer.Mask is not null;

		if (had_mask) {
			layer.ReplaceMaskSurface (surface!); // NRT - set whenever had_mask was true
			layer.Mask!.Hidden = hidden;
		} else {
			layer.DropMask ();
		}

		surface = current;
		hidden = currentHidden;
		had_mask = currentHad;
	}
}
