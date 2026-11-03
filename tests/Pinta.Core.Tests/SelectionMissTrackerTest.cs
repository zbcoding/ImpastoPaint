using System;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The selection outline pulses to tell a user their painting is being clipped away. It must not
/// nag after a single stray stroke, must not fire while strokes are actually landing, and must not
/// keep flashing while the user goes on painting outside the selection.
/// </summary>
[TestFixture]
internal sealed class SelectionMissTrackerTest
{
	private static bool Stroke (SelectionMissTracker tracker, double atSeconds, params bool[] pointsInside)
	{
		tracker.BeginStroke ();
		foreach (bool inside in pointsInside)
			tracker.AddPoint (inside);
		return tracker.EndStroke (TimeSpan.FromSeconds (atSeconds));
	}

	[Test]
	public void OneMissIsForgivenTheSecondInARowPulses ()
	{
		SelectionMissTracker tracker = new ();

		Assert.Multiple (() => {
			Assert.That (Stroke (tracker, 0, false, false), Is.False, "a single stray stroke should not pulse");
			Assert.That (Stroke (tracker, 0.5, false), Is.True, "the second miss in a row should pulse");
		});
	}

	[Test]
	public void MissesRightAfterAPulseStayQuietUntilTheCooldownPasses ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, 0, false);
		Stroke (tracker, 0.5, false); // pulses

		Assert.Multiple (() => {
			Assert.That (Stroke (tracker, 1.0, false), Is.False, "the outline only just pulsed");
			Assert.That (Stroke (tracker, 2.4, false), Is.False, "still inside the cooldown");
			Assert.That (Stroke (tracker, 2.5, false), Is.True, "the cooldown has passed and the user is still missing");
		});
	}

	[Test]
	public void AStrokeThatEntersTheSelectionIsNotAMiss ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, 0, false);

		Assert.That (Stroke (tracker, 1, false, true, false), Is.False, "the stroke painted where it crossed the selection");
	}

	[Test]
	public void ALandedStrokeStartsTheCountOver ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, 0, false);
		Stroke (tracker, 1, true);

		Assert.That (Stroke (tracker, 2, false), Is.False, "misses separated by a landed stroke are not repeated misses");
	}

	[Test]
	public void ChangingTheSelectionForgetsEarlierMissesAndTheCooldown ()
	{
		SelectionMissTracker tracker = new ();

		Stroke (tracker, 0, false);
		Stroke (tracker, 0.5, false); // pulses
		tracker.Reset ();

		Assert.Multiple (() => {
			Assert.That (Stroke (tracker, 0.6, false), Is.False, "a miss against the old selection says nothing about the new one");
			Assert.That (Stroke (tracker, 0.7, false), Is.True, "the new selection gets its own pulse without waiting out the old cooldown");
		});
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
