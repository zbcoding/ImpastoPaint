//
// EffectsActions.cs
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

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Pinta.Core;

public sealed class EffectsActions
{
	/// <summary>
	/// Category menus, keyed by the resolved path rather than the requested one, so an add-in
	/// pack named after a built-in category stays a separate menu.
	/// </summary>
	public Dictionary<string, Gio.Menu> Menus { get; } = [];
	public Collection<Command> Actions { get; } = [];

	// Resolved path per category, so removal knows which menu the entry went into.
	private readonly Dictionary<string, string> resolved_keys = [];

	private readonly AddinActions addins;
	public EffectsActions (AddinActions addins)
	{
		this.addins = addins;
	}

	#region Initialization
	/// <summary>
	/// Adds an effect to the Effects menu. <paramref name="category"/> is a menu path: a plain
	/// name is a category of the Effects menu, and one starting with <see cref="AddinMenu.Root"/>
	/// is placed under the Add-ins container instead.
	/// </summary>
	public void AddEffect (string category, Command action)
	{
		if (!Menus.ContainsKey (category)) {
			Gio.Menu categoryMenu = addins.Menu.ResolvePath (MainMenu.Effects, category, out string resolvedKey);
			Menus.Add (category, categoryMenu);
			resolved_keys.Add (category, resolvedKey);
		}

		Actions.Add (action);
		Menus[category].AppendMenuItemSorted (action.CreateMenuItem ());
	}

	internal void RemoveEffect (string category, Command action)
	{
		if (!Menus.TryGetValue (category, out Gio.Menu? menu))
			return;

		menu.Remove (action);
		Actions.Remove (action);

		if (menu.GetNItems () > 0)
			return;

		// Last effect in this category: drop the now-empty submenu, and the Add-ins container
		// with it if that was the last pack under it.
		Menus.Remove (category);
		addins.Menu.PruneEmpty (MainMenu.Effects, resolved_keys[category]);
		resolved_keys.Remove (category);
	}
	#endregion

	#region Public Methods
	public void ToggleActionsSensitive (bool sensitive)
	{
		foreach (Command a in Actions)
			a.Sensitive = sensitive;
	}
	#endregion
}
