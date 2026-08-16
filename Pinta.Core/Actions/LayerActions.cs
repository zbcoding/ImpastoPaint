//
// LayerActions.cs
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
using System.Linq;

namespace Pinta.Core;

public sealed class LayerActions
{
	public Command AddNewLayer { get; }
	public Command DeleteLayer { get; }
	public Command DuplicateLayer { get; }
	public Command MergeLayerDown { get; }
	public Command ImportFromFile { get; }
	public Command FlipHorizontal { get; }
	public Command FlipVertical { get; }
	public Command RotateZoom { get; }
	public Command MoveLayerUp { get; }
	public Command MoveLayerDown { get; }
	public Command Properties { get; }
	public Command RasterizeAllObjects { get; }
	public Command SoloLayer1 { get; }
	public Command SoloLayer2 { get; }
	public Command SoloLayer3 { get; }
	public Command SoloLayer4 { get; }
	public Command SoloLayer5 { get; }

	private readonly Command solo_layer;
	private readonly Command[] solo_layer_commands;

	private readonly ChromeManager chrome;
	private readonly ImageConverterManager image_formats;
	private readonly RecentFileManager recent_files;
	private readonly ToolManager tools;
	private readonly WorkspaceManager workspace;
	private readonly ImageActions image;
	public LayerActions (
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		RecentFileManager recentFiles,
		ToolManager tools,
		WorkspaceManager workspace,
		ImageActions image)
	{
		AddNewLayer = new Command (
			"addnewlayer",
			Translations.GetString ("Add New Layer"),
			null,
			Resources.Icons.LayerNew,
			shortcuts: ["<Primary><Shift>N"]);

		DeleteLayer = new Command (
			"deletelayer",
			Translations.GetString ("Delete Layer"),
			null,
			Resources.Icons.LayerDelete,
			shortcuts: ["<Primary><Shift>Delete"]);

		DuplicateLayer = new Command (
			"duplicatelayer",
			Translations.GetString ("Duplicate Layer"),
			null,
			Resources.Icons.LayerDuplicate,
			shortcuts: ["<Primary><Shift>D"]);

		MergeLayerDown = new Command (
			"mergelayerdown",
			Translations.GetString ("Merge Layer Down"),
			null,
			Resources.Icons.LayerMergeDown,
			shortcuts: ["<Primary>M"]);

		ImportFromFile = new Command (
			"importfromfile",
			Translations.GetString ("Import from File..."),
			null,
			Resources.Icons.LayerImport);

		FlipHorizontal = new Command (
			"fliplayerhorizontal",
			Translations.GetString ("Flip Horizontal"),
			null,
			Resources.Icons.LayerFlipHorizontal);

		FlipVertical = new Command (
			"fliplayervertical",
			Translations.GetString ("Flip Vertical"),
			null,
			Resources.Icons.LayerFlipVertical);

		RotateZoom = new Command (
			"RotateZoom",
			Translations.GetString ("Rotate / Zoom Layer..."),
			null,
			Resources.Icons.LayerRotateZoom);

		MoveLayerUp = new Command (
			"movelayerup",
			Translations.GetString ("Move Layer Up"),
			null,
			Resources.StandardIcons.LayerMoveUp);

		MoveLayerDown = new Command (
			"movelayerdown",
			Translations.GetString ("Move Layer Down"),
			null,
			Resources.StandardIcons.LayerMoveDown);

		Properties = new Command (
			"properties",
			Translations.GetString ("Layer Properties..."),
			null,
			Resources.Icons.LayerProperties,
			shortcuts: ["F2"]);

		RasterizeAllObjects = new Command (
			"rasterizeallobjects",
			Translations.GetString ("Rasterize All Objects"),
			null,
			Resources.Icons.ImageFlatten);

		solo_layer = new Command (
			"sololayer",
			Translations.GetString ("Solo Layer"),
			null,
			Resources.StandardIcons.ViewReveal);

		SoloLayer1 = CreateSoloLayerCommand (1);
		SoloLayer2 = CreateSoloLayerCommand (2);
		SoloLayer3 = CreateSoloLayerCommand (3);
		SoloLayer4 = CreateSoloLayerCommand (4);
		SoloLayer5 = CreateSoloLayerCommand (5);
		solo_layer_commands = [SoloLayer1, SoloLayer2, SoloLayer3, SoloLayer4, SoloLayer5];

		this.chrome = chrome;
		image_formats = imageFormats;
		recent_files = recentFiles;
		this.tools = tools;
		this.workspace = workspace;
		this.image = image;
	}

	private static Command CreateSoloLayerCommand (int layerNumber)
		=> new (
			$"sololayer{layerNumber}",
			// Translators: {0} is a layer number from 1 to 5, counting up from the bottom layer.
			Translations.GetString ("Solo Layer {0}", layerNumber),
			null,
			Resources.StandardIcons.ViewReveal,
			shortcuts: [$"<Primary>{layerNumber}"]);

	public Gio.MenuItem CreateSoloLayerMenuItem (UserLayer layer)
	{
		Document document = workspace.ActiveDocument;
		int layerIndex = document.Layers.IndexOf (layer);
		Command command = layerIndex >= 0 && layerIndex < solo_layer_commands.Length
			? solo_layer_commands[layerIndex]
			: solo_layer;
		string label = SoloLayerHistoryItem.IsSoloState (document.Layers.UserLayers, layer)
			? Translations.GetString ("Show All Layers")
			: solo_layer.Label;

		// The row identifies the layer; the indexed action supplies its current shortcut.
		return Gio.MenuItem.New (label, command.FullName);
	}

	public void RegisterActions (Gtk.Application app)
	{
		app.AddCommands ([
			AddNewLayer,
			DeleteLayer,
			DuplicateLayer,
			MergeLayerDown,
			ImportFromFile,

			FlipHorizontal,
			FlipVertical,
			RotateZoom,

			Properties,

			RasterizeAllObjects,
			solo_layer,
			SoloLayer1,
			SoloLayer2,
			SoloLayer3,
			SoloLayer4,
			SoloLayer5,

			MoveLayerDown,
			MoveLayerUp]);
	}

	public void RegisterHandlers ()
	{
		AddNewLayer.Activated += HandlePintaCoreActionsLayersAddNewLayerActivated;
		DeleteLayer.Activated += HandlePintaCoreActionsLayersDeleteLayerActivated;
		DuplicateLayer.Activated += HandlePintaCoreActionsLayersDuplicateLayerActivated;
		MergeLayerDown.Activated += HandlePintaCoreActionsLayersMergeLayerDownActivated;
		MoveLayerDown.Activated += HandlePintaCoreActionsLayersMoveLayerDownActivated;
		MoveLayerUp.Activated += HandlePintaCoreActionsLayersMoveLayerUpActivated;
		FlipHorizontal.Activated += HandlePintaCoreActionsLayersFlipHorizontalActivated;
		FlipVertical.Activated += HandlePintaCoreActionsLayersFlipVerticalActivated;
		ImportFromFile.Activated += HandlePintaCoreActionsLayersImportFromFileActivated;
		RasterizeAllObjects.Activated += HandleRasterizeAllObjectsActivated;
		solo_layer.Activated += HandleSoloLayerActivated;
		for (int i = 0; i < solo_layer_commands.Length; ++i) {
			int layerIndex = i;
			solo_layer_commands[i].Activated += (_, _) => HandleSoloLayerActivated (layerIndex);
		}

		workspace.LayerAdded += EnableOrDisableLayerActions;
		workspace.LayerRemoved += EnableOrDisableLayerActions;
		workspace.SelectedLayerChanged += EnableOrDisableLayerActions;
		workspace.ActiveDocumentChanged += EnableOrDisableLayerActions;
		LayerObjectSelection.ObjectsChanged += () => EnableOrDisableLayerActions (null, EventArgs.Empty);
		LayerObjectSelection.ObjectSelectionChanged += () => EnableOrDisableLayerActions (null, EventArgs.Empty);

		EnableOrDisableLayerActions (null, EventArgs.Empty);
	}

	// Bakes every live editable object on the current layer into its base raster (after confirmation),
	// dropping them as objects. Wired to the layers dock right-click menu (shown only for layers that
	// have objects). Object-mode shapes/text elsewhere are the point of this: it fuses them all at once.
	private void HandleRasterizeAllObjectsActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		ObjectRasterizer.RasterizeAllObjects (doc, workspace, chrome, doc.Layers.CurrentUserLayer);
	}

	private void EnableOrDisableLayerActions (object? sender, EventArgs e)
	{
		Document? activeDoc = workspace.ActiveDocumentOrDefault;

		solo_layer.Sensitive = activeDoc is not null;
		for (int i = 0; i < solo_layer_commands.Length; ++i)
			solo_layer_commands[i].Sensitive = activeDoc?.Layers.UserLayers.Count > i;

		bool hasMultipleLayers = activeDoc?.Layers.UserLayers.Count > 1;
		DeleteLayer.Sensitive = hasMultipleLayers;

		// A single layer still has something to flatten if it holds live shape/text objects to bake.
		bool hasLiveObjects = activeDoc?.Layers.UserLayers.Any (l => l.HasObjectSubNodes) ?? false;
		image.Flatten.Sensitive = hasMultipleLayers || hasLiveObjects;

		bool canMergeDown = activeDoc?.Layers.CurrentUserLayerIndex > 0;
		MergeLayerDown.Sensitive = canMergeDown;

		// With an object sub-row selected, Move Up/Down reorders that object instead of the layer,
		// so their sensitivity follows the object's room to move.
		MoveLayerDown.Sensitive = canMergeDown
			|| LayerObjectSelection.MoveSelectedObject?.Invoke (-1, true) == true;

		MoveLayerUp.Sensitive = (activeDoc != null
				&& activeDoc.Layers.CurrentUserLayerIndex < activeDoc.Layers.UserLayers.Count - 1)
			|| LayerObjectSelection.MoveSelectedObject?.Invoke (1, true) == true;
	}

	private void HandleSoloLayerActivated (object sender, EventArgs e)
	{
		if (workspace.ActiveDocumentOrDefault is not { } document)
			return;

		SoloLayer (document, document.Layers.CurrentUserLayer);
	}

	private void HandleSoloLayerActivated (int layerIndex)
	{
		if (workspace.ActiveDocumentOrDefault is not { } document
			|| layerIndex >= document.Layers.UserLayers.Count)
			return;

		SoloLayer (document, document.Layers.UserLayers[layerIndex]);
	}

	private void SoloLayer (Document document, UserLayer layer)
	{
		tools.Commit ();

		if (!ReferenceEquals (document.Layers.CurrentUserLayer, layer))
			document.Layers.SetCurrentUserLayer (layer);

		SoloLayerHistoryItem historyItem = new (
			document.Layers.UserLayers,
			layer);

		if (!historyItem.HasChanges)
			return;

		document.History.PushNewItem (historyItem);
		historyItem.Redo ();
	}

	private Gtk.FileFilter CreateImagesFileFilter ()
	{
		Gtk.FileFilter imagesFilter = Gtk.FileFilter.New ();
		foreach (var format in image_formats.Formats) {
			if (!format.IsImportAvailable ()) continue;
			foreach (string ext in format.Extensions)
				imagesFilter.AddPattern ($"*.{ext}");
		}

		// On Unix-like systems, file extensions are often considered optional.
		// Files can often also be identified by their MIME types.
		// Windows does not understand MIME types natively.
		// Adding a MIME filter on Windows would break the native file picker and force a GTK file picker instead.
		if (SystemManager.GetOperatingSystem () != OS.Windows)
			foreach (var format in image_formats.Formats)
				foreach (var mime in format.Mimes)
					imagesFilter.AddMimeType (mime);

		imagesFilter.Name = Translations.GetString ("Image files");

		return imagesFilter;
	}

	private async void HandlePintaCoreActionsLayersImportFromFileActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		// Add image files filter
		using Gtk.FileFilter imagesFilter = CreateImagesFileFilter ();

		using Gio.ListStore fileFilters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		fileFilters.Append (imagesFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Open Image File"));
		fileDialog.SetFilters (fileFilters);
		if (recent_files.GetDialogDirectory () is Gio.File dir && dir.QueryExists (null))
			fileDialog.SetInitialFolder (dir);

		Gio.File? choice = await fileDialog.OpenFileAsync (chrome.MainWindow);

		if (choice is null) return;

		Gio.File? directory = choice.GetParent ();

		if (directory is not null)
			recent_files.LastDialogDirectory = directory;

		// Open the image and add it to the layers
		UserLayer layer = doc.Layers.AddNewLayer (choice.GetDisplayName ());

		using (Gio.FileInputStream fs = choice.Read (null)) {
			try {
				using GdkPixbuf.Pixbuf bg = GdkPixbuf.Pixbuf.NewFromStream (fs, cancellable: null)!; // NRT: only nullable when an error is thrown
				using Cairo.Context context = new (layer.Surface);
				context.DrawPixbuf (bg, PointD.Zero);
			} finally {
				fs.Close (null);
			}
		}

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerImport,
			Translations.GetString ("Import From File"),
			doc.Layers.IndexOf (layer));

		// --- Changes to document go after everything else is completed successfully

		doc.Layers.SetCurrentUserLayer (layer);
		doc.History.PushNewItem (hist);
		doc.Workspace.Invalidate ();
	}

	private void HandlePintaCoreActionsLayersFlipVerticalActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		// Flipping only mirrors raster pixels, so live objects must become pixels first (or the user
		// cancels and nothing happens).
		if (!ObjectRasterizer.RasterizeAllObjects (doc, workspace, chrome, doc.Layers.CurrentUserLayer))
			return;

		doc.Layers.CurrentUserLayer.FlipVertical ();
		doc.Workspace.Invalidate ();
		doc.History.PushNewItem (new InvertHistoryItem (InvertType.FlipLayerVertical, doc.Layers.CurrentUserLayerIndex));
	}

	private void HandlePintaCoreActionsLayersFlipHorizontalActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		if (!ObjectRasterizer.RasterizeAllObjects (doc, workspace, chrome, doc.Layers.CurrentUserLayer))
			return;

		doc.Layers.CurrentUserLayer.FlipHorizontal ();
		doc.Workspace.Invalidate ();
		doc.History.PushNewItem (new InvertHistoryItem (InvertType.FlipLayerHorizontal, doc.Layers.CurrentUserLayerIndex));
	}

	private void HandlePintaCoreActionsLayersMoveLayerUpActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		// An object sub-row is selected: move that object up in z-order, not the whole layer.
		if (LayerObjectSelection.MoveSelectedObject?.Invoke (1, false) == true)
			return;

		// The button can be sensitive purely because an object row was selected, and that selection
		// can be gone by the time the command runs (commit rebuilds the dock rows). Don't fall
		// through into an impossible layer move.
		if (doc.Layers.CurrentUserLayerIndex >= doc.Layers.UserLayers.Count - 1)
			return;

		SwapLayersHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveUp,
			Translations.GetString ("Move Layer Up"),
			doc.Layers.CurrentUserLayerIndex,
			doc.Layers.CurrentUserLayerIndex + 1);

		doc.Layers.MoveCurrentLayerUp ();
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersMoveLayerDownActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		if (LayerObjectSelection.MoveSelectedObject?.Invoke (-1, false) == true)
			return;

		if (doc.Layers.CurrentUserLayerIndex <= 0)
			return;

		SwapLayersHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveDown,
			Translations.GetString ("Move Layer Down"),
			doc.Layers.CurrentUserLayerIndex,
			doc.Layers.CurrentUserLayerIndex - 1);

		doc.Layers.MoveCurrentLayerDown ();
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersMergeLayerDownActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		int bottomLayerIndex = doc.Layers.CurrentUserLayerIndex - 1;
		UserLayer bottomLayer = doc.Layers.UserLayers[bottomLayerIndex];
		Cairo.ImageSurface oldBottomSurface = bottomLayer.Surface.Clone ();
		Cairo.ImageSurface oldBottomObjectSurface = bottomLayer.ObjectLayer.Layer.Surface.Clone ();
		var oldBottomObjects = ObjectOpacity.CloneAll (bottomLayer.Objects);
		bool mergedObjects = doc.Layers.CurrentUserLayer.HasAnyObjects;

		CompoundHistoryItem hist = new (
			Resources.Icons.LayerMergeDown,
			Translations.GetString ("Merge Layer Down"));

		DeleteLayerHistoryItem h1 = new (
			string.Empty,
			string.Empty,
			doc.Layers.CurrentUserLayer,
			doc.Layers.CurrentUserLayerIndex);

		doc.Layers.MergeCurrentLayerDown ();

		// The objects that came down need painting into the destination's object surface.
		if (mergedObjects) {
			ObjectOpacity.RefreshLayer (workspace, chrome, bottomLayer);
			LayerObjectSelection.RaiseObjectsChanged ();
		}

		// The bottom layer's object list and object surface changed too, so undo has to restore all
		// three (base raster included) — a plain surface swap would leave the merged objects behind.
		BaseHistoryItem h2 = mergedObjects
			? new RasterizeObjectsHistoryItem (
				workspace,
				string.Empty,
				string.Empty,
				oldBottomSurface,
				oldBottomObjectSurface,
				oldBottomObjects,
				bottomLayer)
			: new SimpleHistoryItem (
				string.Empty,
				string.Empty,
				oldBottomSurface,
				bottomLayerIndex);
		hist.Push (h1);
		hist.Push (h2);

		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersDuplicateLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		UserLayer l = doc.Layers.DuplicateCurrentLayer ();

		// Paint the copied objects onto the new layer's object surface.
		if (l.HasAnyObjects) {
			ObjectOpacity.RefreshLayer (workspace, chrome, l);
			LayerObjectSelection.RaiseObjectsChanged ();
		}

		// Make new layer the current layer
		doc.Layers.SetCurrentUserLayer (l);

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerDuplicate,
			Translations.GetString ("Duplicate Layer"),
			doc.Layers.IndexOf (l));
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersDeleteLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		DeleteLayerHistoryItem hist = new (
			Resources.Icons.LayerDelete,
			Translations.GetString ("Delete Layer"),
			doc.Layers.CurrentUserLayer,
			doc.Layers.CurrentUserLayerIndex);

		doc.Layers.DeleteLayer (doc.Layers.CurrentUserLayerIndex);

		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersAddNewLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;
		tools.Commit ();

		UserLayer l = doc.Layers.AddNewLayer (string.Empty);

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerNew,
			Translations.GetString ("Add New Layer"),
			doc.Layers.IndexOf (l));
		doc.History.PushNewItem (hist);
	}
}
