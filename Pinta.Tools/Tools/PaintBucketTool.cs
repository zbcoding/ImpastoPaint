//
// PaintBucketTool.cs
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
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class PaintBucketTool : FloodTool
{
	private readonly IPaletteService palette;
	private Color fill_color;

	public PaintBucketTool (IServiceProvider services) : base (services)
	{
		palette = services.GetService<IPaletteService> ();
	}

	public override bool UsesPaintColors => true;
	public override string Name => Translations.GetString ("Paint Bucket");
	public override string Icon => Pinta.Resources.Icons.ToolPaintBucket;
	public override string StatusBarText => Translations.GetString (
		"Left click to fill a region with the primary color, right click to fill with the secondary color." +
		"\nHold Shift to use Global mode."
	);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.PaintBucket.png"), 21, 21, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_F);
	public override int Priority => 29;
	protected override bool CalculatePolygonSet => false;

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		fill_color = e.MouseButton switch {
			MouseButton.Left => palette.PrimaryColor,
			_ => palette.SecondaryColor,
		};
		last_click_secondary = e.MouseButton != MouseButton.Left;

		base.OnMouseDown (document, e);
	}

	protected override void OnFillRegionComputed (Document document, BitMask stencil)
	{
		var surf = document.Layers.ToolLayer.Surface;

		using Context tool_layer_ctx = new (surf) {
			Operator = Operator.Source
		};
		tool_layer_ctx.SetSourceSurface (document.Layers.CurrentPaintSurface, 0, 0);
		tool_layer_ctx.Paint ();

		var hist = new SimpleHistoryItem (Icon, Name);
		hist.TakeSnapshotOfLayer (document.Layers.CurrentUserLayer, document.Layers.CurrentMaskIsTarget);

		var color = fill_color.ToColorBgra ();
		var width = surf.Width;
		surf.Flush ();

		// Color in any pixel that the stencil says we need to fill
		Parallel.For (0, stencil.Height, y => {
			var stencil_width = stencil.Width;
			var dst_data = surf.GetPixelData ();

			for (var x = 0; x < stencil_width; ++x) {
				if (stencil.Get (x, y))
					dst_data[y * width + x] = color;
			}
		});

		surf.MarkDirty ();

		// Transfer the temp layer to the real one,
		// respecting any selection area
		using Context layer_ctx = document.CreateClippedContext ();
		layer_ctx.Operator = Operator.Source;
		layer_ctx.SetSourceSurface (surf, 0, 0);
		layer_ctx.Paint ();

		document.Layers.ToolLayer.Clear ();
		document.History.PushNewItem (hist);
		document.Workspace.Invalidate ();
	}

	private bool last_click_secondary;

	protected override bool TryRecolorObjectAt (Document document, PointI pos)
	{
		// While a mask is the paint target there are no live objects in play on this layer.
		if (document.Layers.CurrentMaskIsTarget)
			return false;

		UserLayer layer = document.Layers.CurrentUserLayer;
		if (!layer.ObjectLayer.IsLayerSetup)
			return false;

		// Topmost object first, so the one whose ink the user actually sees and clicked wins.
		for (int i = layer.Objects.Count - 1; i >= 0; --i) {
			ILayerObject obj = layer.Objects[i];

			// Hit-test against just this object's ink, rendered by the real renderers so the
			// pixels match what the canvas paints.
			using ImageSurface probe = CairoExtensions.CreateImageSurface (Format.Argb32, layer.Surface.Width, layer.Surface.Height);
			switch (obj) {
				case ShapeObject { Hidden: false } shape:
					LayerObjectSelection.RenderShape (probe, layer, shape);
					break;
				case TextObject { Hidden: false } text:
					TextObjectRenderer.Render (probe, text, PintaCore.Chrome, antialias: true);
					break;
				default:
					continue; // modifier nodes carry no clickable ink
			}
			probe.Flush ();

			if (probe.GetColorBgra (pos).A == 0)
				continue;

			// Recolor with the app's own palette mapping (see BaseEditEngine.Palette_PrimaryColorChanged):
			// primary -> outline / text ink, secondary -> shape fill. Left click = primary,
			// right click = secondary. Fill-only shapes draw their body from OutlineColor (see
			// ShapeObjectRenderer.RenderOpaque's FillStyle parity), so both slots get it.
			switch (obj) {
				case ShapeObject shape when last_click_secondary:
					shape.FillColor = fill_color;
					break;
				case ShapeObject shape:
					shape.OutlineColor = fill_color;
					shape.FillColor = fill_color;
					break;
				case TextObject text:
					text.Engine.PrimaryColor = fill_color;
					break;
				default:
					continue;
			}

			PushRecolorHistory (document, layer);
			return true;
		}

		return false;
	}

	private void PushRecolorHistory (Document document, UserLayer layer)
	{
		List<ILayerObject> before = ObjectOpacity.CloneAll (layer.Objects);
		ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, layer);
		document.History.PushNewItem (
			new LayerObjectsHistoryItem (PintaCore.Workspace, PintaCore.Chrome, Icon, Name, layer, before));
	}
}
