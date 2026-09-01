namespace Pinta.Core;

/// <summary>
/// Index math shared by the layers dock's two drag-reorder paths (layer rows and object sub-rows).
/// Rows are drawn top-first, so a higher list index sits higher up; a drop on the upper half of a
/// target lands above it. Pulled out of the GTK drop handlers so it can be tested without one.
/// </summary>
public static class RowReorder
{
	/// <summary>
	/// The destination index a drag from <paramref name="from"/> onto <paramref name="target"/>
	/// resolves to, or null when the move is a no-op (the item would not change position).
	/// </summary>
	public static int? ResolveDropIndex (int from, int target, bool dropAbove)
	{
		int insert = dropAbove ? target + 1 : target;

		// Removing the source first shifts everything above it down by one.
		if (from < insert)
			insert--;

		return insert == from ? null : insert;
	}
}
