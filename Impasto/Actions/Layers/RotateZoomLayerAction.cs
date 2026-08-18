//
// RotateZoomLayerAction.cs
//
// Author:
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2012 Cameron White
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
using Pinta.Gui.Widgets;

namespace Pinta.Actions;

public sealed class RotateZoomLayerAction : IActionHandler
{
	private readonly ChromeManager chrome;
	private readonly LayerActions layers;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	internal RotateZoomLayerAction (
		ChromeManager chrome,
		LayerActions layers,
		WorkspaceManager workspace,
		ToolManager tools)
	{
		this.chrome = chrome;
		this.layers = layers;
		this.workspace = workspace;
		this.tools = tools;
	}

	void IActionHandler.Initialize ()
	{
		layers.RotateZoom.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		layers.RotateZoom.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		if (workspace.ActiveDocumentOrDefault is not { } doc)
			return;

		tools.Commit ();

		// Rotate / Zoom becomes a non-destructive transform modifier node, created through the numeric
		// dialog — the entry point the design routes the layer transforms through. The node stays
		// editable after OK; nothing is baked.
		await TransformNodeDialog.Create (
			chrome,
			workspace,
			doc.Layers.CurrentUserLayer,
			Resources.Icons.LayerRotateZoom);
	}
}
