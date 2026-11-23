using System;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// The flattened canvas as a grid of textures. A change rebuilds and re-uploads only the tiles it
/// touches, so a brush stroke on a large image costs a few small uploads per frame instead of the
/// whole image. Each changed tile is a new texture rather than a chained update region, which
/// GTK's renderers repaint with the same sampling as a full repaint at fractional display scales.
/// </summary>
/// <remarks>
/// At a fractional display scale GTK fades the outermost device pixel of a scaled texture, which
/// would leave a grid of faint seams. So each tile's texture also holds a few pixels of its
/// neighbours, and is clipped back to the tile's own bounds snapped to whole device pixels: the
/// faded margin falls outside the clip, and adjacent clips meet exactly.
/// </remarks>
internal sealed class CanvasTextureTiles
{
	private const int SNAPPED_TILE_SIZE = 512;

	/// <summary>
	/// Pixels of neighbouring content around each tile; must cover the faded margin, which is at
	/// most a couple of logical pixels, i.e. at most two image pixels at zoom 1 and above.
	/// </summary>
	private const int OVERLAP = 4;

	private readonly bool snap_edges;
	private readonly int tile_size;
	private readonly int columns;
	private readonly int rows;
	private readonly Gdk.Texture?[] tiles;
	private readonly byte[] tile_bytes;

	public Size ImageSize { get; }

	public CanvasTextureTiles (Size imageSize)
	{
		ImageSize = imageSize;
		// ponytail: GTK before 4.24 cannot snap the clips, so it gets one whole-image tile and
		// re-uploads the full image per change. Upgrade path: drop this once GTK 4.24 is the minimum.
		snap_edges = GtkExtensions.SupportsSnapshotSnap;
		tile_size = snap_edges ? SNAPPED_TILE_SIZE : Math.Max (imageSize.Width, imageSize.Height);
		columns = (imageSize.Width + tile_size - 1) / tile_size;
		rows = (imageSize.Height + tile_size - 1) / tile_size;
		tiles = new Gdk.Texture?[columns * rows];
		int maxTextureSize = tile_size + 2 * OVERLAP;
		tile_bytes = new byte[Math.Min (maxTextureSize, imageSize.Width) * Math.Min (maxTextureSize, imageSize.Height) * 4];
	}

	/// <summary>
	/// Rebuilds every tile whose texture <paramref name="changed"/> touches from <paramref name="surface"/>.
	/// </summary>
	public void Update (Cairo.ImageSurface surface, RectangleI changed)
	{
		// A change near a tile edge is also part of the neighbouring tile's overlap.
		RectangleI area = changed.Inflated (OVERLAP, OVERLAP).Intersect (new RectangleI (PointI.Zero, ImageSize));
		if (area.IsEmpty)
			return;

		int firstColumn = area.X / tile_size;
		int lastColumn = (area.X + area.Width - 1) / tile_size;
		int firstRow = area.Y / tile_size;
		int lastRow = (area.Y + area.Height - 1) / tile_size;

		surface.Flush ();
		ReadOnlySpan<byte> pixels = surface.GetData ();

		for (int row = firstRow; row <= lastRow; row++)
			for (int column = firstColumn; column <= lastColumn; column++)
				tiles[row * columns + column] = CreateTexture (pixels, surface.Stride, TextureBounds (column, row));
	}

	/// <summary>
	/// Draws the tiles scaled into <paramref name="viewBounds"/>, which covers the whole image.
	/// </summary>
	public void Append (Gtk.Snapshot snapshot, Gsk.ScalingFilter filter, Graphene.Rect viewBounds)
	{
		float scaleX = viewBounds.GetWidth () / ImageSize.Width;
		float scaleY = viewBounds.GetHeight () / ImageSize.Height;

		snapshot.Save ();
		if (snap_edges)
			snapshot.SetSnapRound ();

		Graphene.Rect clipBounds = Graphene.Rect.Alloc ();
		Graphene.Rect textureViewBounds = Graphene.Rect.Alloc ();
		for (int row = 0; row < rows; row++) {
			for (int column = 0; column < columns; column++) {
				Gdk.Texture texture = tiles[row * columns + column]
					?? throw new InvalidOperationException ("Canvas was never invalidated!");

				SetViewBounds (clipBounds, TileBounds (column, row), viewBounds, scaleX, scaleY);
				SetViewBounds (textureViewBounds, TextureBounds (column, row), viewBounds, scaleX, scaleY);

				snapshot.PushClip (clipBounds);
				AppendTexture (snapshot, texture, filter, textureViewBounds);
				snapshot.Pop ();
			}
		}

		snapshot.Restore ();
	}

	/// <summary>
	/// A linear filter is drawn as a plain texture node, which GTK samples directly; a scaled
	/// texture node is drawn through an offscreen at fractional display scales, whose faded
	/// margin grows beyond <see cref="OVERLAP"/> when zoomed out.
	/// </summary>
	private static void AppendTexture (Gtk.Snapshot snapshot, Gdk.Texture texture, Gsk.ScalingFilter filter, Graphene.Rect bounds)
	{
		if (filter == Gsk.ScalingFilter.Linear)
			snapshot.AppendTexture (texture, bounds);
		else
			snapshot.AppendScaledTexture (texture, filter, bounds);
	}

	private static void SetViewBounds (Graphene.Rect target, RectangleI imageBounds, Graphene.Rect viewBounds, float scaleX, float scaleY)
	{
		target.Init (
			viewBounds.GetX () + imageBounds.X * scaleX,
			viewBounds.GetY () + imageBounds.Y * scaleY,
			imageBounds.Width * scaleX,
			imageBounds.Height * scaleY);
	}

	private RectangleI TileBounds (int column, int row)
	{
		int x = column * tile_size;
		int y = row * tile_size;
		return new RectangleI (
			x,
			y,
			Math.Min (tile_size, ImageSize.Width - x),
			Math.Min (tile_size, ImageSize.Height - y));
	}

	private RectangleI TextureBounds (int column, int row)
		=> TileBounds (column, row).Inflated (OVERLAP, OVERLAP).Intersect (new RectangleI (PointI.Zero, ImageSize));

	private Gdk.Texture CreateTexture (ReadOnlySpan<byte> pixels, int stride, RectangleI bounds)
	{
		int rowBytes = bounds.Width * 4;
		for (int y = 0; y < bounds.Height; y++) {
			ReadOnlySpan<byte> sourceRow = pixels.Slice ((bounds.Y + y) * stride + bounds.X * 4, rowBytes);
			sourceRow.CopyTo (tile_bytes.AsSpan (y * rowBytes, rowBytes));
		}

		Gdk.MemoryTextureBuilder builder = Gdk.MemoryTextureBuilder.New ();
		builder.Bytes = GLib.Bytes.New (tile_bytes.AsSpan (0, rowBytes * bounds.Height));
		builder.Stride = (ulong) rowBytes;
		builder.Width = bounds.Width;
		builder.Height = bounds.Height;
		builder.Format = Gdk.MemoryFormat.B8g8r8a8Premultiplied;
		return builder.Build ();
	}
}
