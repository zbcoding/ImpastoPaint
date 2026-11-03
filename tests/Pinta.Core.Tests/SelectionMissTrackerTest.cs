using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The selection outline pulses to tell a user their painting is being clipped away. It must not
/// nag after a single stray stroke, and must not fire while strokes are actually landing.
/// </summary>
[TestFixture]
internal sealed class SelectionMissTrackerTest
{
	private static bool Stroke (SelectionMissTracker tracker, params bool[] pointsInside)
	{
		tracker.BeginStroke ();
		foreach (bool inside in pointsInside)
			tracker.AddPoint (inside);
		return tracker.EndStroke ();
	}

	[Test]
	public void OneMissIsForgivenTheSecondInARowPulses ()
	{
		SelectionMissTracker tracker = new ();

		Assert.Multiple (() => {
			Assert.That (Stroke (tracker, false, false), Is.False, "a single stray stroke should not pulse");
			Assert.That (Stroke (tracker, false), Is.True, "the second miss in a row should pulse");
			Assert.That (Stroke (tracker, false), Is.True, "further misses keep pointing at the selection");
		});
	}

	[Test]
	public void AStrokeThatEntersTheSelectionIsNotAMiss ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, false);

		Assert.That (Stroke (tracker, false, true, false), Is.False, "the stroke painted where it crossed the selection");
	}

	[Test]
	public void ALandedStrokeStartsTheCountOver ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, false);
		Stroke (tracker, true);

		Assert.That (Stroke (tracker, false), Is.False, "misses separated by a landed stroke are not repeated misses");
	}

	[Test]
	public void ChangingTheSelectionForgetsEarlierMisses ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, false);
		tracker.Reset ();

		Assert.That (Stroke (tracker, false), Is.False, "a miss against the old selection says nothing about the new one");
	}

	[Test]
	public void OnlyAStrokeStillMissingNeedsItsPointsTested ()
	{
		SelectionMissTracker tracker = new ();

		Assert.That (tracker.NeedsPointTest, Is.False, "no stroke is being watched");

		tracker.BeginStroke ();
		tracker.AddPoint (true);

		Assert.That (tracker.NeedsPointTest, Is.False, "once a stroke lands, the rest of it needs no selection queries");
	}
}
