//
// LayerPropertiesAction.cs
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
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class LayerPropertiesAction : IActionHandler
{
	private readonly ChromeManager chrome;
	private readonly LayerActions layers;
	private readonly WorkspaceManager workspace;
	private LayerPropertiesDialog? active_dialog;
	internal LayerPropertiesAction (
		ChromeManager chrome,
		LayerActions layers,
		WorkspaceManager workspace)
	{
		this.chrome = chrome;
		this.layers = layers;
		this.workspace = workspace;
	}

	void IActionHandler.Initialize ()
	{
		layers.Properties.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		layers.Properties.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		if (active_dialog is not null) {
			active_dialog.Response ((int) Gtk.ResponseType.Ok);
			return;
		}

		if (workspace.ActiveDocumentOrDefault is not Document active)
			return;

		using LayerPropertiesDialog dialog = LayerPropertiesDialog.New (chrome, workspace, active);
		active_dialog = dialog;
		workspace.DocumentClosed += CancelDialogOnDocumentClosed;
		try {
			Gtk.ResponseType response = await dialog.RunAsync ();
			FinishDialog (dialog, response);
		} finally {
			workspace.DocumentClosed -= CancelDialogOnDocumentClosed;
			active_dialog = null;
			dialog.Destroy ();
		}
	}

	private void CancelDialogOnDocumentClosed (object? sender, DocumentEventArgs e)
	{
		if (active_dialog is LayerPropertiesDialog dialog && dialog.Document == e.Document)
			dialog.Response ((int) Gtk.ResponseType.Cancel);
	}

	private static void FinishDialog (LayerPropertiesDialog dialog, Gtk.ResponseType response)
	{
		Document document = dialog.Document;
		LayerProperties initial = dialog.InitialLayerProperties;

		if (!dialog.IsLayerAttached) {
			// The layer was deleted or its document closed: leave it as it was before the dialog opened.
			dialog.SetLayerProperties (initial);
			return;
		}

		if (response == Gtk.ResponseType.Ok && dialog.AreLayerPropertiesUpdated) {
			UpdateLayerPropertiesHistoryItem historyItem = GetLayerUpdateHistoryItem (
				document.Layers.IndexOf (dialog.Layer),
				initial,
				dialog.UpdatedLayerProperties);

			document.History.PushNewItem (historyItem);
			document.Workspace.Invalidate ();
			return;
		}

		dialog.SetLayerProperties (initial);
		document.Workspace.Invalidate ();
	}

	private static UpdateLayerPropertiesHistoryItem GetLayerUpdateHistoryItem (
		int layer,
		LayerProperties initial,
		LayerProperties updated)
	{

		string? message = null;
		string icon = Resources.Icons.LayerProperties;
		int count = 0;

		if (updated.Opacity != initial.Opacity) {
			message = Translations.GetString ("Layer Opacity");
			icon = Resources.Icons.LayerProperties;
			count++;
		}

		if (updated.Name != initial.Name) {
			message = Translations.GetString ("Rename Layer");
			icon = Resources.Icons.LayerProperties;
			count++;
		}

		if (updated.Hidden != initial.Hidden) {
			message = initial.Hidden ? Translations.GetString ("Show Layer") : Translations.GetString ("Hide Layer");
			icon = initial.Hidden ? Resources.StandardIcons.ViewReveal : Resources.StandardIcons.ViewConceal;
			count++;
		}

		if (message == null || count > 1) {
			message = Translations.GetString ("Layer Properties");
			icon = Resources.Icons.LayerProperties;
		}

		return new UpdateLayerPropertiesHistoryItem (icon, message, layer, initial, updated);
	}
}
