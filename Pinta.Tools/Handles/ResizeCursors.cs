using System;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Tools;

/// <summary>
/// Picks the resize cursor glyph for a corner of a (possibly rotated) rectangle,
/// mirroring the octant logic used by the image transform tools (see
/// <see cref="RectangleHandle.DirectionCursor"/>). The glyph snaps within the
/// corner's own diagonal family so the arrow always points along the rotated edge.
/// </summary>
internal static class ResizeCursors
{
	//Resize cursors in screen-space octant order [E, SE, S, SW, W, NW, N, NE].
	private static readonly Gdk.Cursor[] direction_cursors = [
		GdkExtensions.CursorFromName (StandardCursors.ResizeE),
		GdkExtensions.CursorFromName (StandardCursors.ResizeSE),
		GdkExtensions.CursorFromName (StandardCursors.ResizeS),
		GdkExtensions.CursorFromName (StandardCursors.ResizeSW),
		GdkExtensions.CursorFromName (StandardCursors.ResizeW),
		GdkExtensions.CursorFromName (StandardCursors.ResizeNW),
		GdkExtensions.CursorFromName (StandardCursors.ResizeN),
		GdkExtensions.CursorFromName (StandardCursors.ResizeNE),
	];

	//Axis-aligned octant of each local corner: 0 TL, 1 TR, 2 BR, 3 BL.
	//(Matches RectangleHandle.base_octant for UpperLeft/UpperRight/LowerRight/LowerLeft.)
	private static readonly int[] corner_base_octant = [5, 7, 1, 3];

	private static int DirectionOctant (int baseOct, double thetaDeg)
	{
		int parity = baseOct & 1; // 0 = straight/edge, 1 = diagonal/corner
		double rotatedDeg = baseOct * 45.0 + thetaDeg;
		int k = (int) Math.Round ((rotatedDeg - parity * 45.0) / 90.0);
		return ((parity + 2 * k) % 8 + 8) % 8;
	}

	/// <summary>
	/// The resize cursor for a local corner (0 TL, 1 TR, 2 BR, 3 BL) whose content is
	/// rotated by <paramref name="thetaDeg"/> degrees on screen (angle of the content's
	/// X axis: 0 = axis-aligned, positive rotating clockwise on screen).
	/// </summary>
	public static Gdk.Cursor ForCorner (int corner, double thetaDeg)
	{
		int oct = DirectionOctant (corner_base_octant[corner % 4], thetaDeg);
		return direction_cursors[oct];
	}
}
