using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The layers dock's drag-reorder (<see cref="RowReorderTest"/> covers its index math) depends on
/// its mutation running after GTK has finished with the drop, not inside the handler - running it
/// inline is what crashed GTK on a re-entrant drop assertion (c3962d95). That property lives in
/// <see cref="DeferredAction"/>; this is what actually guards it, since no Pinta.Gui.Widgets test
/// project exists to drive the real GTK drop handler headlessly.
/// </summary>
[TestFixture]
internal sealed class DeferredActionTest
{
	[TearDown]
	public void RestoreRealScheduler () => DeferredAction.ResetScheduler ();

	[Test]
	public void RunHandsTheActionToTheSchedulerInsteadOfRunningItInline ()
	{
		List<System.Action> captured = [];
		DeferredAction.Scheduler = captured.Add;

		bool ran = false;
		DeferredAction.Run (() => ran = true);

		Assert.Multiple (() => {
			Assert.That (ran, Is.False, "Run() must defer through the scheduler, not execute the action inline");
			Assert.That (captured, Has.Count.EqualTo (1));
		});
	}

	[Test]
	public void TheDeferredActionRunsOnceTheSchedulerFiresIt ()
	{
		List<System.Action> captured = [];
		DeferredAction.Scheduler = captured.Add;

		bool ran = false;
		DeferredAction.Run (() => ran = true);
		captured[0] ();

		Assert.That (ran, Is.True);
	}
}
