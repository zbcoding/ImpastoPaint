//
// Author:
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2020 Cameron White
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
using Pinta.Core;

namespace Pinta.Docking;

[GObject.Subclass<Gtk.Box>]
public sealed partial class DockPanel
{
	internal sealed class DockPanelItem
	{
		public DockItem Item { get; }
		public Gtk.Paned Pane { get; }
		public Gtk.ToggleButton ReopenButton { get; }
		private readonly Gtk.Popover popover;
		private Gtk.Window? float_window;
		public DockPanelItem (DockItem item)
		{
			Gtk.Paned pane = Gtk.Paned.New (Gtk.Orientation.Vertical);

			Gtk.Image icon = Gtk.Image.NewFromIconName (item.IconName);

			Gtk.ToggleButton reopenButton = Gtk.ToggleButton.New ();
			reopenButton.TooltipText = item.Label;
			reopenButton.SetChild (icon);
			reopenButton.OnToggled += ReopenButton_OnToggled;

			// Autohide is set to false since it seems to cause the popover to close even when clicking inside it, on macOS at least
			// Instead, the reopen button is a toggle button to close the popover.
			Gtk.Popover popover = Gtk.Popover.New ();
			popover.Autohide = false;
			popover.Position = Gtk.PositionType.Left;
			popover.SetParent (reopenButton);

			// --- References to keep

			this.popover = popover;
			Pane = pane;
			ReopenButton = reopenButton;
			Item = item;
		}

		private void ReopenButton_OnToggled (Gtk.ToggleButton _, EventArgs __)
		{
			if (ReopenButton.Active)
				popover.Popup ();
			else
				popover.Popdown ();
		}

		public bool IsMinimized
			=> popover.Child is not null;

		public void UpdateOnMaximize (Gtk.Box dockBar)
		{
			// Remove the reopen button from the dock bar.
			// Note that it might not already be in the dock bar, e.g. on startup.
			dockBar.RemoveIfChild (ReopenButton);

			popover.Popdown ();
			popover.Child = null;

			Pane.StartChild = Item;
			Pane.ResizeStartChild = true;
			Pane.ShrinkStartChild = false;
		}

		public void UpdateOnMinimize (Gtk.Box dock_bar)
		{
			Pane.StartChild = null;
			popover.Child = Item;

			dock_bar.Append (ReopenButton);
			ReopenButton.Active = false;
		}

		public void Float (Gtk.Box dockBar)
		{
			redock_bar = dockBar;

			Gtk.Window? parent = Item.GetRoot () as Gtk.Window;

			// Detach from wherever the item currently lives (pane or popover).
			dockBar.RemoveIfChild (ReopenButton);
			popover.Popdown ();
			if (popover.Child == Item)
				popover.Child = null;
			if (Pane.StartChild == Item)
				Pane.StartChild = null;

			Item.SetFloating (true);

			if (float_window is null) {
				float_window = Gtk.Window.New ();
				float_window.Title = Item.Label;
				float_window.DestroyWithParent = true;
				float_window.SetDefaultSize (250, 350);

				// Only a close button - closing re-docks the item maximized.
				Gtk.HeaderBar header = Gtk.HeaderBar.New ();
				header.DecorationLayout = ":close";
				float_window.Titlebar = header;
				// Closing the floating window re-docks the item.
				float_window.OnCloseRequest += (_, _) => {
					Redock ();
					return true;
				};
			}

			float_window.TransientFor = parent;
			// Without an application, "app." actions (e.g. the layer toolbar
			// buttons) can't resolve inside the floating window.
			float_window.Application = parent?.Application;
			float_window.SetChild (Item);
			float_window.Present ();
		}

		private Gtk.Box? redock_bar;

		public void Redock ()
		{
			if (float_window is null || redock_bar is null)
				return;

			if (float_window.Child == Item)
				float_window.Child = null;
			float_window.SetVisible (false);

			Item.SetFloating (false);
			// Flips the header back to the minimize button if the item was
			// floated from the minimized state; no-op (and no event) otherwise.
			Item.Maximize ();
			UpdateOnMaximize (redock_bar);
		}
	}

	/// <summary>
	/// Contains the buttons to re-open any minimized dock items.
	/// </summary>
	private readonly Gtk.Box dock_bar = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

	/// <summary>
	/// List of the items in this panel, which may be minimized or maximized.
	/// </summary>
	private readonly List<DockPanelItem> items = [];

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Horizontal);
		Append (dock_bar);
	}

	public static DockPanel New () => NewWithProperties ([]);

	public void AddItem (DockItem item)
	{
		DockPanelItem panelItem = new (item);

		// Connect to the previous pane in the list.
		if (items.Count > 0) {
			Gtk.Paned pane = items.Last ().Pane;
			pane.EndChild = panelItem.Pane;
		} else {
			panelItem.Pane.Hexpand = true;
			panelItem.Pane.Halign = Gtk.Align.Fill;
			Prepend (panelItem.Pane);
		}

		items.Add (panelItem);
		panelItem.UpdateOnMaximize (dock_bar);

		item.MinimizeClicked += (_, _) => {
			panelItem.UpdateOnMinimize (dock_bar);

			int index = items.IndexOf (panelItem);
			if (index > 0)
				items[index - 1].Pane.PositionSet = false;
		};
		item.MaximizeClicked += (_, _) => panelItem.UpdateOnMaximize (dock_bar);

		// Defer to idle: floating reparents the item itself, which must not happen
		// while GTK is still dispatching the click on a button inside that item.
		item.FloatClicked += (_, _) => GLib.Functions.IdleAdd (
			GLib.Constants.PRIORITY_DEFAULT_IDLE,
			() => {
				panelItem.Float (dock_bar);

				int index = items.IndexOf (panelItem);
				if (index > 0)
					items[index - 1].Pane.PositionSet = false;

				return false;
			});
	}

	public void SaveSettings (ISettingsService settings)
	{
		foreach (var panel_item in items) {
			settings.PutSetting (SettingNames.MinimizeKey (panel_item), panel_item.IsMinimized);
#if false
			settings.PutSetting (SplitPosKey (panel_item), panel_item.Pane.Position);
#endif
		}
	}

	public void LoadSettings (ISettingsService settings)
	{
		foreach (var panel_item in items) {

			if (settings.GetSetting<bool> (SettingNames.MinimizeKey (panel_item), false)) {
				panel_item.Item.Minimize ();
			}

#if false
			panel_item.Pane.Position = settings.GetSetting<int> (
				SplitPosKey (panel_item), panel_item.Pane.Position);
#endif
		}
	}
}
