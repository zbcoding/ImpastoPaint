//
// UpdateImpastoAction.cs
//
// Author:
//       zbcoding
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

internal sealed class UpdateImpastoAction : IActionHandler
{
	private readonly FileActions file;
	private readonly SystemManager system;
	private string? release_url;

	internal UpdateImpastoAction (FileActions file, SystemManager system)
	{
		this.file = file;
		this.system = system;
	}

	void IActionHandler.Initialize ()
	{
		file.UpdateImpasto.Activated += UpdateImpasto_Activated;

		// Fire-and-forget: enable the menu item when a newer release is found.
		_ = CheckForUpdatesAsync ();
	}

	void IActionHandler.Uninitialize ()
	{
		file.UpdateImpasto.Activated -= UpdateImpasto_Activated;
	}

	private async void UpdateImpasto_Activated (object sender, EventArgs e)
	{
		if (!string.IsNullOrEmpty (release_url))
			await system.LaunchUri (release_url);
	}

	private async System.Threading.Tasks.Task CheckForUpdatesAsync ()
	{
		ReleaseInfo? latest = await UpdateChecker.GetLatestReleaseAsync ();
		if (latest is null)
			return;

		// The app's version is the assembly informational version (e.g. "0.0.1f").
		string current = PintaCore.ApplicationVersion;

		if (UpdateChecker.IsNewer (latest.TagName, current)) {
			release_url = latest.HtmlUrl;

			// The awaiting continuation runs on the UI thread (GLib SynchronizationContext),
			// so it's safe to touch the menu here.
			file.ShowUpdateCommand ();
		}
	}
}
