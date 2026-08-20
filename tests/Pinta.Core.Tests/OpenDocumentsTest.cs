using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The workspace tracks which of the open documents is active by its index in the list, so anything
/// that shifts the list has to move that index with it. Closing a document is the one operation that
/// does, and closing one the user is not looking at is the case that gets missed.
/// </summary>
[TestFixture]
internal sealed class OpenDocumentsTest : DocumentHarness
{
	private readonly List<Document> extra_documents = [];

	[TearDown]
	public void CloseRemainingDocuments ()
	{
		// Newest first, so each close is of the active document - the case that already worked.
		extra_documents.Reverse ();

		try {
			foreach (Document document in extra_documents)
				if (PintaCore.Workspace.OpenDocuments.Contains (document))
					PintaCore.Workspace.CloseDocument (document);
		} finally {
			extra_documents.Clear ();
		}
	}

	/// <summary>
	/// Closing a document below the active one used to leave the index where it was, so it named
	/// the wrong document afterwards - and threw outright when the active document had been last,
	/// because the index then pointed past the end of a list that had just got shorter.
	/// </summary>
	[Test]
	public void ClosingADocumentBelowTheActiveOneKeepsTheActiveOne ()
	{
		Document first = Document;
		Document second = OpenAnotherDocument ();
		Document third = OpenAnotherDocument ();

		Assert.That (PintaCore.Workspace.ActiveDocument, Is.SameAs (third), "the newest is active");

		PintaCore.Workspace.CloseDocument (first);

		Assert.Multiple (() => {
			Assert.That (PintaCore.Workspace.ActiveDocument, Is.SameAs (third));
			Assert.That (PintaCore.Workspace.OpenDocuments, Is.EqualTo (new[] { second, third }));
		});
	}

	/// <summary>
	/// Closing above the active one moves nothing, so the index must be left alone rather than
	/// decremented on every close.
	/// </summary>
	[Test]
	public void ClosingADocumentAboveTheActiveOneKeepsTheActiveOne ()
	{
		Document first = Document;
		Document second = OpenAnotherDocument ();
		Document third = OpenAnotherDocument ();

		PintaCore.Workspace.ActivateDocument (second);
		PintaCore.Workspace.CloseDocument (third);

		Assert.That (PintaCore.Workspace.ActiveDocument, Is.SameAs (second));
		Assert.That (PintaCore.Workspace.OpenDocuments, Is.EqualTo (new[] { first, second }));
	}

	/// <summary>
	/// Activating is how a document is opened, and it was also the only way to switch to one that is
	/// already open - which listed it a second time, subscribed to its events a second time so every
	/// one of them fired twice, and gave it a second entry in the Window menu.
	/// </summary>
	[Test]
	public void ActivatingAnOpenDocumentSwitchesToItRatherThanOpeningItAgain ()
	{
		Document first = Document;
		Document second = OpenAnotherDocument ();

		int layerEvents = 0;
		void Count (object? sender, System.EventArgs e) => layerEvents++;
		PintaCore.Workspace.LayerAdded += Count;

		try {
			PintaCore.Workspace.ActivateDocument (first);

			Assert.Multiple (() => {
				Assert.That (PintaCore.Workspace.ActiveDocument, Is.SameAs (first));
				Assert.That (PintaCore.Workspace.OpenDocuments, Is.EqualTo (new[] { first, second }));
			});

			first.Layers.AddNewLayer (string.Empty);

			Assert.That (layerEvents, Is.EqualTo (1), "the document is subscribed to once, not twice");
		} finally {
			PintaCore.Workspace.LayerAdded -= Count;
		}
	}

	private Document OpenAnotherDocument ()
	{
		Document document = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));

		document.Layers.AddNewLayer (string.Empty);
		PintaCore.Workspace.ActivateDocument (document);

		extra_documents.Add (document);

		return document;
	}
}
