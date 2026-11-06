// 
// ExitProgramAction.cs
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

internal sealed class ExitProgramAction : IActionHandler
{
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly CloseDocumentAction close_document;
	private bool is_exiting;
	internal ExitProgramAction (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace,
		CloseDocumentAction closeDocument)
	{
		this.actions = actions;
		this.chrome = chrome;
		this.workspace = workspace;
		close_document = closeDocument;
	}

	void IActionHandler.Initialize ()
	{
		actions.App.Exit.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.App.Exit.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		// A second quit request while the first is still prompting would stack a second prompt.
		if (is_exiting)
			return;

		is_exiting = true;
		try {
			while (workspace.ActiveDocumentOrDefault is Document document) {
				// The user cancelled on a Save prompt.
				if (!await close_document.CloseDocument (document))
					return;
			}
		} finally {
			is_exiting = false;
		}

		// Let everyone know we are quitting
		actions.App.RaiseBeforeQuit ();

		chrome.Application.Quit ();
	}
}
