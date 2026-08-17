using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// History items operate on the document they were stamped with when pushed, so that undoing on a
/// background tab cannot corrupt the focused one. An item that owns another item is never pushed
/// itself, so nothing stamps it — the gradient tool's undo dereferenced that null and threw. Any item
/// that delegates to one it owns has to hand its document down first.
/// </summary>
[TestFixture]
internal sealed class OwnedHistoryItemTest
{
	private sealed class OwnedItem : BaseHistoryItem
	{
		public int Undos { get; private set; }

		public override void Undo ()
		{
			// What SimpleHistoryItem.Swap does first, and what threw when nothing had stamped this.
			Assert.That (Document, Is.Not.Null, "an owned item ran without a document");
			Undos++;
		}
	}

	private sealed class OwnerItem : BaseHistoryItem
	{
		public OwnedItem Owned { get; } = new ();

		public override void Undo ()
		{
			AdoptChild (Owned);
			Owned.Undo ();
		}
	}

	[Test]
	public void AnOwnedItemRunsAgainstItsOwnersDocument ()
	{
		// A Document needs the GTK-backed managers to construct, and this test only ever compares the
		// reference — never touches a member of it — so an uninitialized instance stands in.
		Document document = (Document) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject (typeof (Document));

		OwnerItem owner = new ();
		owner.Document = document;

		Assert.DoesNotThrow (owner.Undo);
		Assert.That (owner.Owned.Document, Is.SameAs (document));
		Assert.That (owner.Owned.Undos, Is.EqualTo (1));
	}
}
