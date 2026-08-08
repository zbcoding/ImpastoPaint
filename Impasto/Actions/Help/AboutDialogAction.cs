//
// AboutDialogAction.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Actions;

internal sealed class AboutDialogAction : IActionHandler
{
	private readonly AppActions app;
	private readonly ChromeManager chrome;
	private readonly string application_version;

	internal AboutDialogAction (
		AppActions app,
		ChromeManager chrome,
		string applicationVersion)
	{
		this.app = app;
		this.chrome = chrome;
		application_version = applicationVersion;
	}

	void IActionHandler.Initialize ()
	{
		app.About.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		app.About.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		using Adw.AboutWindow dialog = Adw.AboutWindow.New ();
		dialog.TransientFor = chrome.MainWindow;
		dialog.Title = Translations.GetString ("About Impasto");
		dialog.ApplicationName = Translations.GetString ("Impasto");
		dialog.ApplicationIcon = Icons.Pinta;
		dialog.Version = application_version;
		dialog.DefaultWidth = 800;
		dialog.DefaultHeight = 700;
		dialog.Website = "https://github.com/zbcoding/Impasto";
		dialog.Comments = Translations.GetString ("Easily create and edit images");
		dialog.Copyright = BuildCopyrightText ();
		dialog.License = string.Empty;

		// The Legal and Credits sections below are populated verbatim from the
		// corresponding files (embedded as resources in Pinta.csproj). To update what
		// users see in the app, edit these files — don't hardcode text here:
		//   license-mit.txt        -> MIT License (the Impasto application license)
		//   license-mit-pinta.txt  -> MIT License (upstream Pinta Project)
		//   THIRD-PARTY-NOTICES.md  -> Third-Party Notices (icons + component licenses)
		//   license-pdn.txt        -> Paint.NET reference license
		//   CONTRIBUTORS.md         -> Credits (contributors)
		dialog.AddLegalSection (
			Translations.GetString ("Third-Party Notices"),
			string.Empty,
			Gtk.License.Custom,
			EscapeMarkup (LoadEmbeddedText ("THIRD-PARTY-NOTICES.md")));
		dialog.AddLegalSection (
			Translations.GetString ("Paint.NET Reference License"),
			string.Empty,
			Gtk.License.Custom,
			EscapeMarkup (LoadEmbeddedText ("LICENSE-PDN.txt")));
		ReplaceLegalSections (dialog);
		dialog.AddCreditSection (
			Translations.GetString ("Pinta Project code by"),
			LoadContributors (LoadEmbeddedText ("CONTRIBUTORS.md")));
		dialog.TranslatorCredits = Translations.GetString ("translator-credits");
		dialog.IssueUrl = "https://github.com/zbcoding/Impasto/issues";
		dialog.SupportUrl = "https://github.com/zbcoding/Impasto/issues";
		await dialog.PresentAsync ();
	}

	private static string BuildCopyrightText ()
	{
		string copyrightText = Translations.GetString ("Copyright");
		string contributorsText = Translations.GetString ("zbcoding — based on Pinta, (c) 2010-2026 the Pinta Project contributors");
		return $"{copyrightText} (c) 2026 {contributorsText}";
	}

	private static void ReplaceLegalSections (Adw.AboutWindow dialog)
	{
		// ponytail: keep the stock AboutWindow until libadwaita exposes a public legal
		// content widget; replace this tree lookup when that API becomes available.
		Gtk.Box? legalBox = FindLegalBox (dialog, Translations.GetString ("Third-Party Notices"));
		if (legalBox is null)
			return;

		while (legalBox.GetFirstChild () is Gtk.Widget child)
			legalBox.Remove (child);

		AppendCopyrightRow (legalBox, Translations.GetString ("Impasto contributions - Copyright by the Impasto contributors"));
		AppendCopyrightRow (legalBox, Translations.GetString ("Pinta Project contributions - Copyright (c) 2010-2026 by the Pinta Project contributors"));
		AppendLegalExpander (
			legalBox,
			Translations.GetString ("MIT License - Impasto"),
			LoadEmbeddedText ("LICENSE-MIT.txt"));
		AppendLegalExpander (
			legalBox,
			Translations.GetString ("MIT License - Pinta Project"),
			LoadEmbeddedText ("LICENSE-MIT-PINTA.txt"));
		AppendLegalExpander (
			legalBox,
			Translations.GetString ("Third-Party Notices"),
			LoadEmbeddedText ("THIRD-PARTY-NOTICES.md"));
		AppendLegalExpander (
			legalBox,
			Translations.GetString ("Paint.NET Reference License"),
			LoadEmbeddedText ("LICENSE-PDN.txt"));
	}

	private static void AppendCopyrightRow (Gtk.Box legalBox, string text)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.AddCssClass ("body");
		label.SetWrap (true);
		label.SetXalign (0);
		legalBox.Append (label);
	}

	private static void AppendLegalExpander (Gtk.Box legalBox, string title, string text)
	{
		Gtk.TextView textView = Gtk.TextView.New ();
		textView.Buffer!.SetText (text, -1);
		textView.Editable = false;
		textView.CursorVisible = false;
		textView.WrapMode = Gtk.WrapMode.WordChar;
		textView.TopMargin = 8;
		textView.BottomMargin = 8;
		textView.LeftMargin = 8;
		textView.RightMargin = 8;

		Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New ();
		scroll.SetChild (textView);
		scroll.HeightRequest = 250;
		scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		scroll.VscrollbarPolicy = Gtk.PolicyType.Automatic;

		Gtk.Expander expander = Gtk.Expander.New (title);
		expander.SetChild (scroll);
		legalBox.Append (expander);
	}

	private static Gtk.Box? FindLegalBox (Gtk.Widget widget, string sectionTitle)
	{
		if (widget is Gtk.Box box && HasDirectLabel (box, sectionTitle))
			return box;

		for (Gtk.Widget? child = widget.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
			Gtk.Box? legalBox = FindLegalBox (child, sectionTitle);
			if (legalBox is not null)
				return legalBox;
		}

		return null;
	}

	private static bool HasDirectLabel (Gtk.Box box, string text)
	{
		for (Gtk.Widget? child = box.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
			if (child is Gtk.Label label && label.GetLabel () == text)
				return true;
		}

		return false;
	}

	// The About dialog interprets the license/legal text as Pango markup, so escape it.
	// Return the raw text on failure so a missing resource never crashes the dialog.
	private static string EscapeMarkup (string text)
	{
		if (string.IsNullOrEmpty (text))
			return string.Empty;

		try {
			return GLib.Markup.EscapeText (text);
		} catch (Exception ex) {
			Console.Error.WriteLine ($"Failed to escape About text: {ex.Message}");
			return text;
		}
	}

	// Parse CONTRIBUTORS.md into the developer/contributor credits list.
	// Lines use either "- Name — [@handle](url)" or "- Name".
	private static string[] LoadContributors (string markdown)
	{
		List<string> contributors = new ();

		if (string.IsNullOrEmpty (markdown))
			return contributors.ToArray ();

		foreach (string rawLine in markdown.Split ('\n')) {
			string line = rawLine.Trim ();
			if (!line.StartsWith ("- ", StringComparison.Ordinal))
				continue;

			string entry = line.Substring (2).Trim ();

			// Format is either "Name — [@handle](url)" or "Name".
			int handleStart = entry.IndexOf ("[", StringComparison.Ordinal);
			string name;
			if (handleStart >= 0) {
				string beforeHandle = entry.Substring (0, handleStart).Trim ();
				name = beforeHandle.Split ("—", 2)[0].Trim ();
			} else {
				name = entry;
			}

			contributors.Add (name);
		}

		return contributors.ToArray ();
	}

	private static string LoadEmbeddedText (string logicalName)
	{
		using Stream stream = Assembly.GetExecutingAssembly ().GetManifestResourceStream (logicalName)!;
		using StreamReader reader = new (stream);
		return reader.ReadToEnd ();
	}
}
