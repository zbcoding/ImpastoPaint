//
// LayersPad.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2011 Jonathan Pobst
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

using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;

namespace Pinta;

internal sealed class LayersPad : IDockPad
{
	private readonly LayerActions layer_actions;
	internal LayersPad (LayerActions layerActions)
	{
		layer_actions = layerActions;
	}

	public void Initialize (Dock workspace)
	{
		LayersListView layers = LayersListView.New ();
		DockItem layers_item = DockItem.New (
			child: layers,
			uniqueName: "Layers",
			iconName: Resources.Icons.Layers
		);
		layers_item.Label = Translations.GetString ("Layers");

		Gio.Menu hamburger_menu = Gio.Menu.New ();

		Gio.Menu flip_section = Gio.Menu.New ();
		flip_section.AppendItem (layer_actions.FlipHorizontal.CreateMenuItem ());
		flip_section.AppendItem (layer_actions.FlipVertical.CreateMenuItem ());
		flip_section.AppendItem (layer_actions.RotateZoom.CreateMenuItem ());

		Gio.Menu prop_section = Gio.Menu.New ();
		prop_section.AppendItem (layer_actions.Properties.CreateMenuItem ());

		hamburger_menu.AppendItem (layer_actions.ImportFromFile.CreateMenuItem ());
		hamburger_menu.AppendSection (null, flip_section);
		hamburger_menu.AppendSection (null, prop_section);

		// Impasto: thumbnail size is a setting for the whole list, not an operation on one layer,
		// so it gets its own section (a separator above it) and a slider rather than a menu item.
		Gio.Menu thumbnail_section = Gio.Menu.New ();
		Gio.MenuItem thumbnail_item = Gio.MenuItem.New (null, null);
		thumbnail_item.SetAttributeValue ("custom", GLib.Variant.NewString (ThumbnailSliderId));
		thumbnail_section.AppendItem (thumbnail_item);
		hamburger_menu.AppendSection (null, thumbnail_section);

		Gtk.MenuButton hamburger_button = GtkExtensions.CreateMenuButton (
			hamburger_menu, Resources.StandardIcons.OpenMenu);

		hamburger_button.Direction = Gtk.ArrowType.Up;
		((Gtk.PopoverMenu) hamburger_button.Popover!).AddChild (CreateThumbnailSlider (), ThumbnailSliderId);

		// ponytail: symbolic icons render at 16px, so 24px = 1.5x bigger
		Gtk.Button move_up = layer_actions.MoveLayerUp.CreateDockToolBarItem ();
		Gtk.Button move_down = layer_actions.MoveLayerDown.CreateDockToolBarItem ();
		move_up.Child = Gtk.Image.NewFromIconName (Resources.StandardIcons.LayerMoveUp);
		((Gtk.Image) move_up.Child).PixelSize = 24;
		move_down.Child = Gtk.Image.NewFromIconName (Resources.StandardIcons.LayerMoveDown);
		((Gtk.Image) move_down.Child).PixelSize = 24;

		Gtk.Box layers_tb = layers_item.AddToolBar ();
		layers_tb.AppendMultiple ([
			layer_actions.AddNewLayer.CreateDockToolBarItem (),
			layer_actions.DeleteLayer.CreateDockToolBarItem (),
			layer_actions.DuplicateLayer.CreateDockToolBarItem (),
			layer_actions.MergeLayerDown.CreateDockToolBarItem (),
			move_up,
			move_down,
			hamburger_button
		]);

		workspace.AddItem (layers_item, DockPlacement.Right);
	}

	// Id linking the "custom" menu item above to the widget added to the popover.
	private const string ThumbnailSliderId = "layer-thumbnail-size";

	/// <summary>
	/// Impasto: the layer thumbnail size slider. Its lowest step turns thumbnails off; the rest
	/// grow them, and the list reflows the layer names under the thumbnails when they stop fitting
	/// beside them.
	/// </summary>
	private static Gtk.Widget CreateThumbnailSlider ()
	{
		Gtk.Label title = Gtk.Label.New (Translations.GetString ("Thumbnail Size"));
		title.Halign = Gtk.Align.Start;

		Gtk.Scale slider = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, LayerThumbnailScale.MaxStep, 1);
		slider.WidthRequest = 180;
		slider.DrawValue = false;
		slider.RoundDigits = 0;
		slider.Digits = 0;
		slider.SetValue (LayerThumbnailScale.Step);
		slider.AddMark (0, Gtk.PositionType.Bottom, Translations.GetString ("Off"));
		slider.TooltipText = Translations.GetString ("Size of the layer thumbnails; the lowest setting hides them");
		slider.OnValueChanged += (_, _) => LayerThumbnailScale.Step = (int) slider.GetValue ();

		Gtk.Box layout = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		layout.SetAllMargins (6);
		layout.Append (title);
		layout.Append (slider);
		return layout;
	}
}
