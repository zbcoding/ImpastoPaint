// LayerMask.cs
//
// A layer's mask slot: one alpha surface per layer, applied last to the layer's rendered result.
// It is a slot on UserLayer (UserLayer.Mask), not a child in the z-ordered object list, so it
// affects the whole layer rather than sitting at a position among the objects. Painting on it is
// an alpha paint: the mask's alpha multiplies the layer's rendered alpha, so painting opaque with
// any tool reveals and erasing hides. A freshly added mask is fully transparent, so it hides the
// layer until the user paints reveal into it.

using System;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// A layer mask: a full-canvas surface whose alpha is applied, last, to the layer's accumulated
/// composite. Painting tools draw into <see cref="Surface"/> when the mask row is the active edit
/// target; only the alpha channel affects rendering, so any opaque stroke reveals and erasing hides.
/// </summary>
public sealed class LayerMask
{
	/// <summary>The mask surface, sized to its layer. Its alpha is the mask.</summary>
	public ImageSurface Surface { get; set; }

	/// <summary>Whether the mask is disabled: a hidden mask does not affect the layer's rendering.</summary>
	public bool Hidden { get; set; }

	/// <summary>Reserved position for a future draggable mask row. Always 0 (applied last) for now.</summary>
	public int Position { get; set; }

	public LayerMask (ImageSurface surface)
		=> Surface = surface;

	/// <summary>
	/// Applies a mask to <paramref name="target"/> in place, last: every pixel's alpha (and, because
	/// Cairo's surfaces are premultiplied, its colour channels with it) is scaled by the mask
	/// surface's alpha at that pixel. White mask pixels leave the layer untouched; transparent ones
	/// erase it. Both surfaces must be the same size.
	/// </summary>
	public static void ApplyMask (ImageSurface target, ImageSurface mask)
	{
		Span<ColorBgra> dst = target.GetPixelData ();
		ReadOnlySpan<ColorBgra> src = mask.GetReadOnlyPixelData ();
		int n = Math.Min (dst.Length, src.Length);

		for (int i = 0; i < n; i++) {
			byte m = src[i].A;
			if (m == 255)
				continue;

			dst[i] = ColorBgra.FromBgra (
				(byte) (dst[i].B * m / 255),
				(byte) (dst[i].G * m / 255),
				(byte) (dst[i].R * m / 255),
				(byte) (dst[i].A * m / 255));
		}
	}

	/// <summary>An independent copy of the mask (its own surface, not a shared reference).</summary>
	public LayerMask CloneSurface ()
		=> new (EffectModifierNode.CopyOf (Surface)) { Hidden = Hidden, Position = Position };

	// Geometry ops mirror Layer's, so the mask stays aligned with the layer's raster when a
	// destructive operation (flip / crop / canvas resize / image resize) changes the geometry.
	// Selection clipping is deliberately not applied to the mask: it is an alpha channel for the
	// whole layer, and the layer's own pixels are already clipped on crop.

	public void ApplyTransform (Matrix xform, Size oldSize, Size newSize)
	{
		ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, newSize.Width, newSize.Height);
		using Context g = new (dest);
		g.Transform (xform);
		g.SetSourceSurface (Surface, 0, 0);
		g.Paint ();
		Surface = dest;
	}

	public void Crop (RectangleI rect)
	{
		ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, rect.Width, rect.Height);
		using Context g = new (dest);
		g.Translate (-rect.X, -rect.Y);
		g.Antialias = Antialias.None;
		g.SetSourceSurface (Surface, 0, 0);
		g.Paint ();
		Surface = dest;
	}

	public void ResizeCanvas (Size newSize, Anchor anchor)
	{
		Size oldSize = new (Surface.Width, Surface.Height);
		PointI delta = new (
			X: Surface.Width - newSize.Width,
			Y: Surface.Height - newSize.Height);

		PointD anchorPoint = anchor switch {
			Anchor.NW => new (0, 0),
			Anchor.N => new (-delta.X / 2.0, 0),
			Anchor.NE => new (-delta.X, 0),
			Anchor.E => new (-delta.X, -delta.Y / 2.0),
			Anchor.SE => new (-delta.X, -delta.Y),
			Anchor.S => new (-delta.X / 2.0, -delta.Y),
			Anchor.SW => new (0, -delta.Y),
			Anchor.W => new (0, -delta.Y / 2.0),
			Anchor.Center => new (-delta.X / 2.0, -delta.Y / 2.0),
			_ => throw new System.ComponentModel.InvalidEnumArgumentException (nameof (anchor), (int) anchor, typeof (Anchor)),
		};

		ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, newSize.Width, newSize.Height);
		using Context g = new (dest);
		g.SetSourceSurface (Surface, anchorPoint.X, anchorPoint.Y);
		g.Paint ();
		Surface = dest;
	}

	public void Resize (Size newSize, ResamplingMode resamplingMode)
	{
		ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, newSize.Width, newSize.Height);
		using Context g = new (dest);
		g.Scale (newSize.Width / (double) Surface.Width, newSize.Height / (double) Surface.Height);
		g.SetSourceSurface (Surface, resamplingMode);
		g.Paint ();
		Surface = dest;
	}
}
