// AutosaveRecoveryDialog.cs
//
// Offers the autosaves left behind by a session that did not exit normally.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta;

internal static class AutosaveRecoveryDialog
{
	private const string RESPONSE_RECOVER = "recover";
	private const string RESPONSE_DISCARD = "discard";
	private const string RESPONSE_LATER = "later";

	/// <summary>
	/// Prompts for any recoverable autosaves and opens the ones the user chooses.
	/// Does nothing when the previous session exited normally.
	/// </summary>
	internal static async Task PromptAsync (Gtk.Window parent, AutosaveManager autosave)
	{
		IReadOnlyList<AutosaveCandidate> candidates = autosave.FindRecoverableDocuments ();

		if (candidates.Count == 0)
			return;

		// Damaged autosaves are shown, but only so the user knows the work wasn't silently
		// dropped - there is nothing to open, so they can't be selected.
		Dictionary<AutosaveCandidate, Gtk.CheckButton> selections = [];

		Gtk.Box list = Gtk.Box.New (Gtk.Orientation.Vertical, 6);

		foreach (AutosaveCandidate candidate in candidates) {

			Gtk.CheckButton check = Gtk.CheckButton.NewWithLabel (candidate.DisplayName);
			check.Active = candidate.IsRecoverable;
			check.Sensitive = candidate.IsRecoverable;

			Gtk.Label detail = Gtk.Label.New (Describe (candidate));
			detail.Halign = Gtk.Align.Start;
			detail.AddCssClass ("dim-label");
			detail.MarginStart = 28;
			detail.Wrap = true;

			list.Append (check);
			list.Append (detail);

			selections[candidate] = check;
		}

		Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New ();

		// Tall enough for a wrapped description to be read in full: a long original path,
		// or the reason a damaged autosave can't be opened, runs to several lines.
		scroll.HeightRequest = Math.Clamp (100 * candidates.Count, 150, 400);
		scroll.SetChild (list);

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (
			parent,
			Translations.GetString ("Recover Unsaved Work?"),
			Translations.GetString ("Impasto did not shut down normally. These documents were automatically saved and can be reopened."));

		dialog.SetExtraChild (scroll);
		dialog.AddResponse (RESPONSE_DISCARD, Translations.GetString ("_Discard"));
		dialog.SetResponseAppearance (RESPONSE_DISCARD, Adw.ResponseAppearance.Destructive);
		dialog.AddResponse (RESPONSE_LATER, Translations.GetString ("_Not Now"));
		dialog.AddResponse (RESPONSE_RECOVER, Translations.GetString ("_Recover"));
		dialog.SetResponseAppearance (RESPONSE_RECOVER, Adw.ResponseAppearance.Suggested);
		dialog.DefaultResponse = RESPONSE_RECOVER;
		dialog.CloseResponse = RESPONSE_LATER;

		string response = await dialog.RunAsync ();

		switch (response) {

			case RESPONSE_DISCARD:
				foreach (AutosaveCandidate candidate in candidates)
					AutosaveManager.Discard (candidate);
				break;

			case RESPONSE_RECOVER:
				foreach (AutosaveCandidate candidate in candidates.Where (c => selections[c].Active))
					await RecoverAsync (parent, candidate);

				// Anything not chosen, or that failed to open, is left on disk so the next
				// launch can offer it again rather than losing it to a stray click.
				break;
		}
	}

	private static string Describe (AutosaveCandidate candidate)
	{
		string when = Translations.GetString (
			"Autosaved {0}",
			candidate.Timestamp.ToString ("g", CultureInfo.CurrentCulture));

		if (!candidate.IsRecoverable)
			return Translations.GetString ("Cannot be recovered: {0}", candidate.Problem!);

		if (candidate.OriginalUri is null)
			return Translations.GetString ("{0} - never saved to a file", when);

		return Translations.GetString ("{0} - from {1}", when, candidate.OriginalUri);
	}

	private static async Task RecoverAsync (Gtk.Window parent, AutosaveCandidate candidate)
	{
		try {
			IImageImporter importer =
				PintaCore.ImageFormats.GetFormatByExtension ("ora")?.Importer
				?? throw new InvalidOperationException ("The OpenRaster format is unavailable.");

			Document document = importer.Import (Gio.FileHelper.NewForPath (candidate.AutosavePath));

			// Import points the document at the autosave file itself. Aim it back at where
			// the user was working, so saving goes to the original file and not into the
			// autosave directory - and demand a Save As when there was no original.
			if (candidate.OriginalUri is not null) {
				Gio.File original = Gio.FileHelper.NewForUri (candidate.OriginalUri);
				document.File = original;
				document.FileType = System.IO.Path.GetExtension (original.GetParseName ()).TrimStart ('.');
			} else {
				document.ClearFileReference ();
				document.DisplayName = candidate.DisplayName;
			}

			PintaCore.Workspace.ActivateDocument (document);

			document.History.PushNewItem (
				new BaseHistoryItem (
					Resources.StandardIcons.DocumentRevert,
					Translations.GetString ("Recover Autosaved Document")));

			// The recovered state was never written to the user's own file.
			document.History.SetDirty ();

			AutosaveManager.Discard (candidate);

		} catch (Exception e) {
			await ErrorDialog.ShowError (
				parent,
				Translations.GetString ("Could not recover \"{0}\"", candidate.DisplayName),
				e.Message,
				e.ToString ());
		}
	}
}
