using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pinta.Core;

/// <summary>
/// Runs at most one instance of an async flow per key: a caller that arrives while the key's flow
/// is still in flight gets that flow's task instead of starting a second one. Closing a document is
/// the motivating case — its save prompt runs nested main loops, during which a window close or Quit
/// can ask to close the same document again, and a second flow would prompt twice and then act on a
/// document the first one already closed.
/// <para>
/// The key is registered before the flow starts, so a re-entrant request made from inside the flow's
/// synchronous prefix (e.g. a blocking dialog's nested main loop) joins it too.
/// </para>
/// </summary>
public sealed class InFlightGate<TKey, TResult> where TKey : notnull
{
	private readonly Dictionary<TKey, Task<TResult>> in_flight = [];

	public Task<TResult> Run (TKey key, Func<Task<TResult>> start)
	{
		if (in_flight.TryGetValue (key, out Task<TResult>? running))
			return running;

		TaskCompletionSource<TResult> completion = new ();
		in_flight[key] = completion.Task;
		_ = Complete (key, start, completion);
		return completion.Task;
	}

	// The key is released before the task completes, so a caller resuming on completion that asks
	// again for the same key starts a fresh flow instead of being handed this finished one.
	private async Task Complete (TKey key, Func<Task<TResult>> start, TaskCompletionSource<TResult> completion)
	{
		try {
			TResult result = await start ();
			in_flight.Remove (key);
			completion.SetResult (result);
		} catch (Exception e) {
			in_flight.Remove (key);
			completion.SetException (e);
		}
	}
}
