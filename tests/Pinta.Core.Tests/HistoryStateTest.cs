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
}
