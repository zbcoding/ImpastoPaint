using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Escape is the quick-deselect command: one press has to clear the selection. It used to wait for a
/// second press inside the double-click interval, which left a single Escape doing nothing at all.
/// </summary>
[TestFixture]
internal sealed class EscapeDeselectTest : DocumentHarness
{
	private static bool edit_handlers_registered;

	[OneTimeSetUp]
	public void RegisterEditHandlers ()
	{
		// The harness wires the Image menu only; PintaCore.Initialize, which wires the rest, is not run.
		if (edit_handlers_registered)
			return;

		edit_handlers_registered = true;
		PintaCore.Actions.Edit.RegisterHandlers ();
	}

	// The handler is invoked directly: reaching it through the Gio action needs the app-level
	// RegisterActions this headless harness does not run.
	private static void PressEscape ()
		=> typeof (EditActions)
			.GetMethod ("HandlePintaCoreActionsEditDeselectSelectionActivated", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke (PintaCore.Actions.Edit, [PintaCore.Actions.Edit.DeselectSelection, EventArgs.Empty]);

	[Test]
	public void ASingleEscapeClearsTheSelection ()
	{
		Document.Selection.CreateRectangleSelection (new RectangleD (4, 4, 8, 8));
		Document.Selection.Visible = true;

		PressEscape ();

		Assert.That (Document.Selection.Visible, Is.False);
	}

	[Test]
	public void EscapeWithNothingSelectedLeavesNoUndoStep ()
	{
		int before = Document.History.Items.Count ();

		PressEscape ();

		Assert.That (Document.History.Items.Count (), Is.EqualTo (before));
	}

	// Two commands on one accelerator make GTK fire only one of them and the shortcuts dialog flag a
	// clash. Ctrl+Shift+A sat on both Normal Size and another command until one was dropped.
	[Test]
	public void NoAcceleratorIsTheDefaultOfTwoCommands ()
	{
		IEnumerable<string> clashes =
			PintaCore.Shortcuts.AllCommands ()
			.SelectMany (command => command.DefaultShortcuts.Select (accel => (accel, command.Name)))
			.GroupBy (entry => entry.accel)
			.Where (group => group.Count () > 1)
			.Select (group => $"{group.Key}: {string.Join (", ", group.Select (entry => entry.Name))}");

		Assert.That (clashes, Is.Empty);
	}
}
