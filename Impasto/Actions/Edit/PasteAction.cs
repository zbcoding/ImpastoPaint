//
// PasteAction.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2012 Jonathan Pobst, Cameron White
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
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

internal enum PasteDestination
{
	ActiveLayer,
	NewLayer
}

internal sealed class PasteAction : IActionHandler
{
	private readonly ChromeManager chrome;
	private readonly ActionManager actions;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	private readonly SettingsManager settings;
	internal PasteAction (
		ChromeManager chrome,
		ActionManager actions,
		WorkspaceManager workspace,
		ToolManager tools,
		SettingsManager settings)
	{
		this.chrome = chrome;
		this.actions = actions;
		this.workspace = workspace;
		this.tools = tools;
		this.settings = settings;
	}

	void IActionHandler.Initialize ()
	{
		actions.Edit.Paste.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.Edit.Paste.Activated -= Activated;
	}

	private void Activated (object sender, EventArgs e)
	{
		if (TryGetPasteTarget (actions, workspace) is not (Document doc, PointI canvasPos))
			return;

		// Paste into the active document.
		// The 'false' argument indicates that paste should be
		// performed into the current (not a new) layer.
		Paste (
			actions: actions,
			chrome: chrome,
			workspace: workspace,
			tools: tools,
			doc: doc,
			destination: settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, false) ? PasteDestination.NewLayer : PasteDestination.ActiveLayer,
			pastePosition: canvasPos
		);
	}

	/// <summary>
	/// The document to paste into and the canvas position to paste at (the current scroll position,
	/// converted from view to canvas coordinates). Null when no document is open, having already
	/// activated <c>PasteIntoNewImage</c> in its place.
	/// </summary>
	internal static (Document Doc, PointI CanvasPosition)? TryGetPasteTarget (
		ActionManager actions,
		WorkspaceManager workspace)
	{
		if (!workspace.HasOpenDocuments) {
			actions.Edit.PasteIntoNewImage.Activate ();
			return null;
		}

		var doc = workspace.ActiveDocument;

		// Get the scroll position in canvas coordinates
		var view = (Gtk.Viewport) doc.Workspace.Canvas.Parent!;

		PointD viewPoint = new (
			X: view.Hadjustment!.Value,
			Y: view.Vadjustment!.Value);

		PointD canvasPos = doc.Workspace.ViewPointToCanvas (viewPoint);

		return (doc, canvasPos.ToInt ());
	}

	/// <summary>
	/// Pastes an image from the clipboard.
	/// </summary>
	/// <param name="destination">The layer destination for the pasted image.</param>
	public static async void Paste (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace,
		ToolManager tools,
		Document doc,
		PasteDestination destination,
		PointI pastePosition = new ())
	{
		// Create a compound history item for recording several
		// operations so that they can all be undone/redone together.
		var history_text = destination == PasteDestination.NewLayer ? Translations.GetString ("Paste Into New Layer") : Translations.GetString ("Paste");
		CompoundHistoryItem paste_action = new (Resources.StandardIcons.EditPaste, history_text);

		var cb = GdkExtensions.GetDefaultClipboard ();

		// See if the current tool wants to handle the paste
		// operation (e.g., the text tool could paste text)
		if (destination == PasteDestination.ActiveLayer) {
			if (await tools.DoHandlePaste (doc, cb))
				return;
		}

		// Commit any unfinished tool actions
		tools.Commit ();

		Gdk.Texture? cb_texture = await cb.ReadTextureAsync ();
		if (cb_texture is null) {
			await ShowClipboardEmptyDialog (chrome);
			return;
		}

		Cairo.ImageSurface cb_image = cb_texture.ToSurface ();

		var canvas_size = workspace.ImageSize;

		// If the image being pasted is larger than the canvas size, allow the user to optionally resize the canvas
		if (cb_image.Width > canvas_size.Width || cb_image.Height > canvas_size.Height) {

			var response = await ShowExpandCanvasDialog (chrome);

			if (response == Gtk.ResponseType.Accept) {

				Size newSize = new (
					Width: Math.Max (canvas_size.Width, cb_image.Width),
					Height: Math.Max (canvas_size.Height, cb_image.Height)
				);

				// False means the user backed out of the rasterize-objects prompt the resize needed -
				// nothing was resized (or baked), so the paste has to abort too rather than land on the
				// still-original-sized canvas as if Preserve had been chosen instead of Expand.
				if (!workspace.ResizeCanvas (newSize, Pinta.Core.Anchor.Center, paste_action))
					return;
				actions.View.UpdateCanvasScale ();

			} else if (response != Gtk.ResponseType.Reject) // cancelled
				return;
		}

		// If the pasted image would fall off bottom- or right-
		// side of image, adjust paste position
		pastePosition = new PointI (
			X: Math.Clamp (pastePosition.X, 0, Math.Max (0, canvas_size.Width - cb_image.Width)),
			Y: Math.Clamp (pastePosition.Y, 0, Math.Max (0, canvas_size.Height - cb_image.Height))
		);

		// If requested, create a new layer, make it the current
		// layer and record it's creation in the history
		if (destination == PasteDestination.NewLayer) {
			var l = doc.Layers.AddNewLayer (string.Empty);
			paste_action.Push (new AddLayerHistoryItem (Resources.Icons.LayerNew, Translations.GetString ("Add New Layer"), doc.Layers.IndexOf (l)));
		}

		// Copy the paste to the temp layer, which should be at least the size of this document.
		doc.Layers.CreateSelectionLayer (
			Math.Max (doc.ImageSize.Width, cb_image.Width),
			Math.Max (doc.ImageSize.Height, cb_image.Height));

		doc.Layers.ShowSelectionLayer = true;

		using Cairo.Context g = new (doc.Layers.SelectionLayer.Surface);

		g.SetSourceSurface (cb_image, 0, 0);
		g.Paint ();

		doc.Layers.SelectionLayer.Transform.InitIdentity ();
		doc.Layers.SelectionLayer.Transform.Translate (pastePosition.X, pastePosition.Y);

		tools.SetCurrentTool ("MoveSelectedTool");

		var old_selection = doc.Selection.Clone ();

		doc.Selection.CreateRectangleSelection (new RectangleD ((PointD) pastePosition, cb_image.Width, cb_image.Height));
		doc.Selection.Visible = true;

		doc.Workspace.Invalidate ();

		paste_action.Push (new PasteHistoryItem (cb_image, old_selection));
		doc.History.PushNewItem (paste_action);
	}

	public static Task ShowClipboardEmptyDialog (ChromeManager chrome)
	{
		string primary = Translations.GetString ("Image cannot be pasted");
		string secondary = Translations.GetString ("The clipboard does not contain an image.");
		return chrome.ShowMessageDialog (chrome.MainWindow, primary, secondary);
	}

	public static async Task<Gtk.ResponseType> ShowExpandCanvasDialog (ChromeManager chrome)
	{
		string primary = Translations.GetString ("Image larger than canvas");
		string secondary = Translations.GetString ("The image being pasted is larger than the canvas. What would you like to do to the canvas size?");

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (chrome.MainWindow, primary, secondary);

		const string cancel_response = "cancel";
		const string reject_response = "reject";
		const string expand_response = "expand";

		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		// Translators: This refers to preserving the current canvas size when pasting a larger image.
		dialog.AddResponse (reject_response, Translations.GetString ("Preserve"));
		// Translators: This refers to expanding the canvas size when pasting a larger image.
		dialog.AddResponse (expand_response, Translations.GetString ("Expand"));

		dialog.SetResponseAppearance (expand_response, Adw.ResponseAppearance.Suggested);
		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = expand_response;

		string response = await dialog.RunAsync ();

		return response switch {
			expand_response => Gtk.ResponseType.Accept,
			reject_response => Gtk.ResponseType.Reject,
			_ => Gtk.ResponseType.Cancel,
		};
	}
}
