using System.Threading.Tasks;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Closing a document prompts to save it, and that prompt runs nested main loops during which a
/// window close or Quit asks to close the same document again. Each request used to run its own close
/// flow: the user got a second prompt, and answering Discard there closed the document while the first
/// flow went on to export it. <see cref="InFlightGate{TKey, TResult}"/> is what makes the later
/// request join the close already under way.
/// </summary>
[TestFixture]
internal sealed class CloseDocumentGateTest
{
	[Test]
	public async Task ARequestWhileTheCloseIsInFlightJoinsItInsteadOfPromptingAgain ()
	{
		InFlightGate<string, bool> gate = new ();
		TaskCompletionSource<bool> prompt = new ();
		int prompts = 0;

		Task<bool> Close () => gate.Run ("doc", () => {
			prompts++;
			return prompt.Task;
		});

		Task<bool> fromCloseButton = Close ();
		Task<bool> fromQuit = Close ();
		prompt.SetResult (false); // The user cancels the one prompt they saw.
		bool quitClosed = await fromQuit;
		bool buttonClosed = await fromCloseButton;

		Assert.Multiple (() => {
			Assert.That (prompts, Is.EqualTo (1), "the second request must not prompt again");
			Assert.That (quitClosed, Is.False, "Quit must learn the close was cancelled, or it quits anyway");
			Assert.That (buttonClosed, Is.False);
		});
	}

	[Test]
	public void ARequestFromInsideTheFlowsBlockingPromptJoinsIt ()
	{
		// A blocking dialog's nested main loop runs before the flow ever awaits, so the request made
		// from inside it has to find the flow already registered.
		InFlightGate<string, bool> gate = new ();
		TaskCompletionSource<bool> prompt = new ();
		int prompts = 0;
		Task<bool>? reentrant = null;

		Task<bool> Close () => gate.Run ("doc", () => {
			if (++prompts == 1)
				reentrant = Close ();
			return prompt.Task;
		});

		Task<bool> outer = Close ();

		Assert.Multiple (() => {
			Assert.That (prompts, Is.EqualTo (1));
			Assert.That (reentrant, Is.SameAs (outer));
		});
	}

	[Test]
	public async Task OnceTheCloseFinishesANewRequestRunsAgain ()
	{
		// A cancelled close must not leave the document unclosable.
		InFlightGate<string, bool> gate = new ();
		int prompts = 0;

		Task<bool> Close () => gate.Run ("doc", () => {
			prompts++;
			return Task.FromResult (prompts == 2);
		});

		bool first = await Close ();
		bool second = await Close ();

		Assert.Multiple (() => {
			Assert.That (first, Is.False);
			Assert.That (second, Is.True);
			Assert.That (prompts, Is.EqualTo (2));
		});
	}

	[Test]
	public void ClosingOneDocumentDoesNotHoldBackAnother ()
	{
		InFlightGate<string, bool> gate = new ();
		TaskCompletionSource<bool> firstPrompt = new ();
		int prompts = 0;

		gate.Run ("first", () => {
			prompts++;
			return firstPrompt.Task;
		});
		gate.Run ("second", () => {
			prompts++;
			return Task.FromResult (true);
		});

		Assert.That (prompts, Is.EqualTo (2));
	}
}
