using System;

namespace Pinta.Core;

/// <summary>
/// Runs an action once the current GTK event has finished, rather than inside it. Needed by any
/// handler whose own action can destroy the very widget invoking it - the layers dock's drag-reorder
/// is the motivating case: reordering rebuilds the dock's rows, which destroys the widget whose drop
/// handler is running, and GTK aborts the process when a second drop begins while the first is still
/// active. Deferring past the current event lets GTK finish with it first.
/// <para>
/// The real scheduler is the GLib idle queue, which needs a live main loop and so cannot run in a
/// headless test. <see cref="Scheduler"/> is the test seam: substitute it to capture the action
/// instead of running it, so a test can assert a caller truly defers rather than running inline -
/// exactly the property a re-entrant-drop crash depends on.
/// </para>
/// </summary>
public static class DeferredAction
{
	private static readonly Action<Action> real_scheduler = RunOnIdle;

	internal static Action<Action> Scheduler { get; set; } = real_scheduler;

	public static void Run (Action action) => Scheduler (action);

	/// <summary>Test hook: restores the real GLib-idle scheduler.</summary>
	internal static void ResetScheduler () => Scheduler = real_scheduler;

	private static void RunOnIdle (Action action)
		=> GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
			action ();
			return false;
		});
}
