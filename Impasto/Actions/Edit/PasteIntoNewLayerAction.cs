// 
// PasteIntoNewLayerAction.cs
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

internal sealed class PasteIntoNewLayerAction : IActionHandler
{
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	private readonly SettingsManager settings;
	internal PasteIntoNewLayerAction (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace,
		ToolManager tools,
		SettingsManager settings)
	{
		this.actions = actions;
		this.chrome = chrome;
		this.workspace = workspace;
		this.tools = tools;
		this.settings = settings;
	}

	void IActionHandler.Initialize ()
	{
		actions.Edit.PasteAlternate.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.Edit.PasteAlternate.Activated -= Activated;
	}

	private void Activated (object sender, EventArgs e)
	{
		if (PasteAction.TryGetPasteTarget (actions, workspace) is not (Document doc, PointI canvasPos))
			return;

		// Paste into the active document.
		// The 'true' argument indicates that paste should be
		// performed into a new layer.
		PasteAction.Paste (
			actions: actions,
			chrome: chrome,
			workspace: workspace,
			tools: tools,
			doc: doc,
			destination: settings.GetSetting (SettingNames.PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER, false) ? PasteDestination.ActiveLayer : PasteDestination.NewLayer,
			pastePosition: canvasPos
		);
	}
}
