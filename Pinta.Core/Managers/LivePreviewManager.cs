//
// LivePreviewManager.cs
//
// Author:
//       Greg Lowe <greg@vis.net.nz>
//
// Copyright (c) 2010 Greg Lowe
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

#if (!LIVE_PREVIEW_DEBUG && DEBUG)
#undef DEBUG
#endif

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Debug = System.Diagnostics.Debug;

namespace Pinta.Core;

public interface ILivePreview
{
	RectangleI RenderBounds { get; }
	bool IsEnabled { get; }
	ImageSurface LivePreviewSurface { get; }
}

public sealed class LivePreviewManager : ILivePreview
{
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	private readonly SystemManager system;
	private readonly ChromeManager chrome;

	internal LivePreviewManager (
		WorkspaceManager workspaceManager,
		ToolManager toolManager,
		SystemManager systemManager,
		ChromeManager chromeManager)
	{
		IsEnabled = false;

		workspace = workspaceManager;
		tools = toolManager;
		system = systemManager;
		chrome = chromeManager;
	}

	public Cairo.ImageSurface LivePreviewSurface { get; private set; } = null!;
	public RectangleI RenderBounds { get; private set; }
	public bool IsEnabled { get; private set; }

	// Covers the whole session, including while its non-modal configuration dialog is open on a
	// document that is not the active one (IsEnabled cleared).
	private bool is_running;

	/// <summary>The whole preview session; completes once the effect is applied (true) or dropped.</summary>
	internal async Task<bool> Run (BaseEffect effect)
	{
		// Effect menus stay live while a non-modal effect dialog is open; ignore a second effect.
		if (is_running)
			return false;
		is_running = true;
		try {
			return await RunSession (effect);
		} finally {
			is_running = false;
		}
	}

	/// <summary>
	/// Impasto: applies <paramref name="effect"/> with its current settings to the active layer and
	/// selection, with no dialog or preview — the Repeat commands. Ignored while a session is open.
	/// </summary>
	public void ApplyWithoutPreview (BaseEffect effect)
	{
		if (is_running)
			return;

		tools.Commit ();

		Document doc = workspace.ActiveDocument;
		AddEffectNode (doc, doc.Layers.CurrentUserLayer, effect, doc.Selection);
	}

	private async Task<bool> RunSession (BaseEffect effect)
	{
		tools.Commit ();

		Document doc = workspace.ActiveDocument;
		DocumentSelection selection = doc.Selection;

		IsEnabled = true;

		//TODO Use the current tool layer instead.
		LivePreviewSurface = CairoExtensions.CreateImageSurface (
			Cairo.Format.Argb32,
			workspace.ImageSize.Width,
			workspace.ImageSize.Height);

		RenderBounds = selection.Visible ? selection.GetBounds ().ToInt () : LivePreviewSurface.GetBounds ();
		RenderBounds = workspace.ClampToImageSize (RenderBounds);

		const uint UPDATE_MILLISECONDS = 100;

		AsyncEffectRenderer.Settings settings = new (
			threadCount: system.RenderThreads,
			renderBounds: RenderBounds,
			effectIsTileable: effect.IsTileable);

		int handlersInQueue = 0;
		UserLayer layer = doc.Layers.CurrentUserLayer;
		// Preview reads what the canvas currently shows for this layer — its accumulated composite
		// when it already carries modifier nodes, otherwise its base raster. Owned copy: history
		// pushes while the dialog is open replace (and dispose) the layer's composite.
		Cairo.ImageSurface renderSource = (layer.Composite ?? layer.Surface).Clone ();
		Task pendingRestart = Task.CompletedTask;
		bool abandoned = false;
		string effectName = effect.Name;

		RenderHandle renderHandle = null!; // NRT: Assigned before first use

		IProgressDialog dialog = chrome.ProgressDialog;
		dialog.Title = Translations.GetString ("Rendering Effect");
		dialog.Text = effect.Name;
		dialog.Progress = 0;
		dialog.Canceled += HandleProgressDialogCancel;

		bool renderAlive = true;

		workspace.ActiveDocumentChanged += HandleActiveDocumentChanged;
		workspace.DocumentClosed += HandleDocumentClosed;

		try {
			// Paint the pre-effect layer surface into into the working surface.
			using Cairo.Context ctx = new (LivePreviewSurface);
			layer.Draw (ctx, renderSource, 1);

			Debug.WriteLine (DateTime.Now.ToString ("HH:mm:ss:ffff") + "Start Live preview.");

			if (effect.EffectData != null)
				effect.EffectData.PropertyChanged += EffectData_PropertyChanged;

			renderHandle = AsyncEffectRenderer.Start (
				settings,
				effect,
				renderSource,
				LivePreviewSurface);

			using GLibTimer _ = GLib.Functions.TimeoutAdd (
				0,
				UPDATE_MILLISECONDS,
				() => {
					if (!renderAlive || abandoned) return false;
					PollForUpdate (renderHandle);
					return true; // Keep ticking as long as the effect is active.
				}
			);

			bool userConfirmed = !effect.IsConfigurable || await effect.LaunchConfiguration ();

			if (abandoned)
				return false;

			chrome.MainWindowBusy = true;

			if (!userConfirmed) {
				Debug.WriteLine ("User decided not to proceed with the render");
				renderHandle.Cancel ();
				await renderHandle.Task;
				return false;
			}

			// The user confirmed, so show progress dialog
			Debug.WriteLine (DateTime.Now.ToString ("HH:mm:ss:ffff") + "LivePreviewManager.Apply()");

			dialog.Show ();

			await WaitForSettledRender ();
			var result = await renderHandle.Task;

			if (abandoned)
				return false;

			// Final poll after the renderer finishes to ensure the last-rendered tiles are displayed.
			PollForUpdate (renderHandle);

			foreach (var ex in result.Errors)
				Debug.WriteLine ("AsyncEffectRenderer Error while rendering effect: " + effectName + " exception: " + ex.Message + "\n" + ex.StackTrace);

			if (result.WasCanceled) {
				Debug.WriteLine ("User decided to cancel the render");
				renderHandle.Cancel ();
				await renderHandle.Task;
				return false;
			}

			// Was not canceled, so finally apply
			Debug.WriteLine ("Render completed without the user canceling");

			// Impasto: applying an effect records an editable node rather than writing pixels, so the
			// user can reopen its settings later. The rendered preview is discarded — the node's own
			// Apply reproduces it inside the layer's accumulator. See
			// docs-private/layer-effects-model.md.
			// The layer may have been deleted while the dialog was open; there is nothing to apply to.
			if (!doc.Layers.UserLayers.Contains (layer))
				return false;

			AddEffectNode (doc, layer, effect, selection);
			return true;

		} finally {

			IsEnabled = false;
			workspace.ActiveDocumentChanged -= HandleActiveDocumentChanged;
			workspace.DocumentClosed -= HandleDocumentClosed;
			LivePreviewSurface = null!;
			workspace.Invalidate ();

			if (effect.EffectData != null)
				effect.EffectData.PropertyChanged -= EffectData_PropertyChanged;

			chrome.MainWindowBusy = false;

			dialog.Canceled -= HandleProgressDialogCancel;

			dialog.Hide ();

			renderAlive = false;

			// No render may still be reading the snapshot when it is freed.
			await pendingRestart;
			if (renderHandle is not null) {
				renderHandle.Cancel ();
				await renderHandle.Task;
			}
			renderSource.Dispose ();
		}

		// === Methods ===

		void HandleProgressDialogCancel (object? o, EventArgs e)
		{
			renderHandle.Cancel ();
		}

		// Another document's tab hides this preview; the dialog keeps editing and applying to doc.
		void HandleActiveDocumentChanged (object? o, EventArgs e)
		{
			IsEnabled = !abandoned && workspace.ActiveDocumentOrDefault == doc;
			workspace.Invalidate ();
		}

		void HandleDocumentClosed (object? o, DocumentEventArgs e)
		{
			if (e.Document == doc)
				Abandon ();
		}

		// Closing the previewed document ends the preview as if the user pressed Cancel.
		void Abandon ()
		{
			if (abandoned) return;
			abandoned = true;
			IsEnabled = false; // stop painting this document's preview over another one
			renderHandle?.Cancel ();
			workspace.Invalidate ();
		}

		// A slider change cancels the render and restarts it; wait until no restart is pending so
		// the apply path reads the render that reflects the final settings.
		async Task WaitForSettledRender ()
		{
			RenderHandle awaited;
			do {
				awaited = renderHandle;
				await awaited.Task;
				await pendingRestart;
			} while (awaited != renderHandle);
		}

		void EffectData_PropertyChanged (object? sender, PropertyChangedEventArgs e)
		{
			if (abandoned) return;
			pendingRestart = RestartRender ();
		}

		async Task RestartRender ()
		{
			// TODO: calculate bounds
			handlersInQueue++;
			renderHandle.Cancel ();
			await renderHandle.Task;
			handlersInQueue--;
			if (handlersInQueue > 0 || abandoned) return;
			renderHandle = AsyncEffectRenderer.Start (settings, effect, renderSource, LivePreviewSurface);
		}

		// This method now polls the renderer for its state instead of being a passive event handler.
		void PollForUpdate (RenderHandle renderTask)
		{
			Debug.WriteLine (DateTime.Now.ToString ("HH:mm:ss:ffff") + " Polling for update.");

			chrome.ProgressDialog.Progress = renderTask.Progress;

			if (!IsEnabled || !renderTask.TryConsumeBounds (out RectangleI updatedBounds))
				return;

			double scale = workspace.Scale;

			// Transform bounds (Image -> Canvas -> Window)

			// Calculate canvas bounds.
			PointD bounds1 = new (
				X: updatedBounds.Left * scale,
				Y: updatedBounds.Top * scale);

			PointD bounds2 = new (
				X: (updatedBounds.Right + 1) * scale,
				Y: (updatedBounds.Bottom + 1) * scale);

			// TODO Figure out why when scale > 1 that I need add on an
			// extra pixel of padding.
			// I must being doing something wrong here.
			if (scale > 1.0) {
				//x1 = (bounds.Left-1) * scale;
				bounds1 = bounds1 with { Y = (updatedBounds.Top - 1) * scale };
				//x2 = (bounds.Right+1) * scale;
				//y2 = (bounds.Bottom+1) * scale;
			}

			// Convert to integer, carefully not to miss partially covered
			// pixels by rounding incorrectly.
			int x = (int) Math.Floor (bounds1.X);
			int y = (int) Math.Floor (bounds1.Y);
			RectangleI areaToInvalidate = new (
				X: x,
				Y: y,
				Width: (int) Math.Ceiling (bounds2.X) - x,
				Height: (int) Math.Ceiling (bounds2.Y) - y);

			// Tell GTK to expose the drawing area.
			workspace.ActiveWorkspace.InvalidateWindowRect (areaToInvalidate);
		}
	}

	private void AddEffectNode (Document doc, UserLayer layer, BaseEffect effect, DocumentSelection selection)
	{
		List<ILayerObject> objectsBefore = ObjectOpacity.CloneAll (layer.Objects);

		layer.AddModifierNode (EffectModifierNode.FromEffect (effect, selection.Visible ? selection.Clone () : null, PintaCore.Services));
		ObjectOpacity.RefreshLayer (workspace, chrome, layer);

		doc.History.PushNewItem (
			new LayerObjectsHistoryItem (workspace, chrome, effect.Icon, effect.Name, layer, objectsBefore));
	}
}
