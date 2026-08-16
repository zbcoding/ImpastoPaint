using System;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

internal static class PaletteWidget
{
	// Two rows for the standard palette, three once the user opts into the extra
	// darker row (see PaletteHelper.GetPaletteRowCount ()).
	internal static int PALETTE_ROWS => PaletteHelper.GetPaletteRowCount ();
	internal const int SWATCH_SIZE = 19;
	// The status bar palette shrinks its tiles rather than folding a whole section
	// away, down to this floor. Kept close to the full size on purpose: folding the
	// quick colors away early and leaving the recent colors legible beats squeezing
	// both grids down to specks.
	internal const int MIN_SWATCH_SIZE = 15;
	internal static int WIDGET_HEIGHT => 4 + SWATCH_SIZE * PALETTE_ROWS;
	internal const int PALETTE_MARGIN = 10;

	// The recently-used palette has a fixed color count that may not divide evenly
	// into PALETTE_ROWS, so round up the column count.
	internal static int GetRecentColorColumns (int maxRecentlyUsedColor)
		=> (maxRecentlyUsedColor + PALETTE_ROWS - 1) / PALETTE_ROWS;

	public static int GetSwatchAtLocation (
		IPaletteService palette,
		PointD point,
		RectangleD palette_bounds,
		bool recentColorPalette = false,
		int swatchSize = SWATCH_SIZE)
	{
		int max =
			recentColorPalette
			? Math.Min (palette.RecentlyUsedColors.Count, palette.MaxRecentlyUsedColor)
			: palette.CurrentPalette.Colors.Count;

		// This could be more efficient, but is good enough for now
		for (int i = 0; i < max; i++)
			if (GetSwatchBounds (palette, i, palette_bounds, recentColorPalette, swatchSize).ContainsPoint (point))
				return i;

		return -1;
	}

	public static RectangleD GetSwatchBounds (
		IPaletteService palette,
		int index,
		RectangleD palette_bounds,
		bool recentColorPalette = false,
		int swatchSize = SWATCH_SIZE)
	{
		// Normal swatches are laid out like this:
		// 0 | 2 | 4 | 6
		// 1 | 3 | 5 | 7
		// Recent swatches are laid out like this (it's less visually jarring as they change):
		// 0 | 1 | 2 | 3
		// 4 | 5 | 6 | 7

		// First we need to figure out what row and column the color is
		int recent_cols = GetRecentColorColumns (palette.MaxRecentlyUsedColor);
		int row = recentColorPalette ? index / recent_cols : index % PALETTE_ROWS;
		int col = recentColorPalette ? index % recent_cols : index / PALETTE_ROWS;

		// Now we need to construct the bounds of that row/column
		double x = palette_bounds.X + (col * swatchSize);
		double y = palette_bounds.Y + (row * swatchSize);

		return new (x, y, swatchSize, swatchSize);
	}

	// Wrapped variant for contexts too narrow to grow the grid rightward forever
	// (the floating Colors panel, the color-wheel popover's mini grids) - unlike the
	// footer bar, which folds sections away instead of wrapping. Once the natural
	// column count exceeds maxColumns, colors continue in a new row-band below.
	public static RectangleD GetWrappedSwatchBounds (
		IPaletteService palette,
		int index,
		RectangleD palette_bounds,
		int maxColumns,
		int rowCount,
		bool recentColorPalette = false,
		int swatchSize = SWATCH_SIZE)
	{
		int recent_cols = (palette.MaxRecentlyUsedColor + rowCount - 1) / rowCount;
		int row = recentColorPalette ? index / recent_cols : index % rowCount;
		int col = recentColorPalette ? index % recent_cols : index / rowCount;

		int band = col / maxColumns;
		int wrappedCol = col % maxColumns;
		int wrappedRow = row + band * rowCount;

		double x = palette_bounds.X + (wrappedCol * swatchSize);
		double y = palette_bounds.Y + (wrappedRow * swatchSize);

		return new (x, y, swatchSize, swatchSize);
	}

	public static int GetWrappedSwatchAtLocation (
		IPaletteService palette,
		PointD point,
		RectangleD palette_bounds,
		int maxColumns,
		int rowCount,
		bool recentColorPalette = false,
		int swatchSize = SWATCH_SIZE)
	{
		int max =
			recentColorPalette
			? Math.Min (palette.RecentlyUsedColors.Count, palette.MaxRecentlyUsedColor)
			: palette.CurrentPalette.Colors.Count;

		for (int i = 0; i < max; i++)
			if (GetWrappedSwatchBounds (palette, i, palette_bounds, maxColumns, rowCount, recentColorPalette, swatchSize).ContainsPoint (point))
				return i;

		return -1;
	}

	// How many row-bands a grid of naturalColumns columns folds into once capped at
	// maxColumns - used to size the containing widget.
	public static int GetWrappedBandCount (int naturalColumns, int maxColumns)
		=> Math.Max (1, (naturalColumns + maxColumns - 1) / maxColumns);
}
