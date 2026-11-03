namespace Pinta.Gui.Widgets;

/// <summary>
/// Spots the user painting outside the selection. A stroke whose sampled points all miss the
/// selection paints nothing; once that happens twice in a row the selection outline should pulse.
/// A stroke that reaches the selection starts the count over.
/// </summary>
public sealed class SelectionMissTracker
{
	private const int MISSES_BEFORE_PULSE = 2;

	private bool stroke_active;
	private bool stroke_reached_selection;
	private int consecutive_misses;

	/// <summary>
	/// Whether the stroke's next point still has to be tested against the selection. False once the
	/// stroke has reached the selection, so the caller can skip the hit test for the rest of it.
	/// </summary>
	public bool NeedsPointTest => stroke_active && !stroke_reached_selection;

	public void BeginStroke ()
	{
		stroke_active = true;
		stroke_reached_selection = false;
	}

	public void AddPoint (bool insideSelection)
	{
		if (insideSelection)
			stroke_reached_selection = true;
	}

	/// <summary>
	/// Ends the stroke. Returns whether the selection outline should pulse.
	/// </summary>
	public bool EndStroke ()
	{
		if (!stroke_active)
			return false;

		stroke_active = false;

		if (stroke_reached_selection) {
			consecutive_misses = 0;
			return false;
		}

		consecutive_misses++;
		return consecutive_misses >= MISSES_BEFORE_PULSE;
	}

	/// <summary>
	/// Forgets earlier misses, e.g. after the selection changes.
	/// </summary>
	public void Reset ()
	{
		stroke_active = false;
		consecutive_misses = 0;
	}
}
