using System.ComponentModel;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// State that must stay true across a sequence of history steps, not just after one.
/// </summary>
[TestFixture]
internal sealed class HistoryStateTest : DocumentHarness
{
	/// <summary>
	/// Saving at A, undoing and drawing B truncates A from the stack. Redoing back to B lands on
	/// the index A was saved at, which used to count as clean: closing then lost B without a prompt.
	/// </summary>
	[Test]
	public void RedoOntoTheIndexOfATruncatedSavedStepStaysDirty ()
	{
		PaintRaster (Layer (0), s => Fill (s, Red), "A");
		Document.History.SetClean ();

		Document.History.Undo ();
		PaintRaster (Layer (0), s => Fill (s, Blue), "B");
		Document.History.Undo ();
		Document.History.Redo ();

		Assert.That (Document.IsDirty, Is.True, "B was never saved");

		Document.IsDirty = false;
	}

	/// <summary>
	/// Undo then a new step restores the same (pointer, count), so a fingerprint built from those
	/// cannot tell the undone content from the new one.
	/// </summary>
	[Test]
	public void ChangeCountDiffersAfterUndoAndANewStep ()
	{
		PaintRaster (Layer (0), s => Fill (s, Red), "A");
		int afterA = Document.History.ChangeCount;

		Document.History.Undo ();
		PaintRaster (Layer (0), s => Fill (s, Blue), "B");

		Assert.That (Document.History.ChangeCount, Is.Not.EqualTo (afterA));

		Document.IsDirty = false;
	}

	/// <summary>
	/// A cloned EffectData is committed into a modifier node; it must not carry the original's
	/// subscribers (a live preview's closure over a document and preview surface).
	/// </summary>
	[Test]
	public void ClonedEffectDataDoesNotNotifyTheOriginalsSubscribers ()
	{
		SampleData original = new ();
		int notified = 0;
		original.PropertyChanged += (object? _, PropertyChangedEventArgs _) => notified++;

		EffectData clone = original.Clone ();
		clone.FirePropertyChanged ("Value");

		Assert.That (notified, Is.Zero);
	}

	private sealed class SampleData : EffectData
	{
	}
}
