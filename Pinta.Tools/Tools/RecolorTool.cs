//
// RecolorTool.cs
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

// Some methods from Paint.Net:

/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public class RecolorTool : BaseBrushTool
{
	private readonly IWorkspaceService workspace;

	private PointI? last_point = null;
	private BitMask? stencil;
	// Whether the stroke reverses direction (canvas pixels near the primary color are repainted
	// with the secondary). Fixed at stroke start so holding the reverse gesture (default Alt) and
	// releasing it mid-stroke can't make one drag repaint in both directions.
	private bool reversed_stroke;

	public RecolorTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
	}

	public override bool UsesPaintColors => true;
	// Its own down-point guard (TryRasterizeObjectAtStrokeStart) handles live objects with an
	// ink-accurate probe of just the topmost clicked object; the ToolManager chokepoint must
	// not pre-empt it with a coarser bbox prompt over every intersecting object.
	public override bool HandlesLiveObjectsItself => true;
	public override string Name => Translations.GetString ("Recolor");
	public override string Icon => Pinta.Resources.Icons.ToolRecolor;
	public override string StatusBarText {
		get {
			string reverse = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.RecolorReverseStroke).ClickBindingLabel ();

			return
				// Translators: {0} is the click gesture that reverses a stroke (default Alt+Click).
				Translations.GetString (
					"Left click to replace the secondary color on the canvas with the primary color." +
					"\n{0} or right click to replace the primary color with the secondary color.",
					reverse);
		}
	}
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.Recolor.png"), 9, 18, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_R);
	protected float Tolerance => (float) (ToleranceSlider.GetValue () / 100);
	public override int Priority => 49;

	protected override void OnBuildToolBar (Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (Separator);

		tb.Append (ToleranceLabel);
		tb.Append (ToleranceSlider);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// Mirrors BaseBrushTool's own re-entrancy guard: a second mouse button pressed while a
		// stroke is already in progress must be ignored outright, not just prevented from
		// restarting the stroke below - otherwise it still flips reversed_stroke and swaps the
		// stencil out from under the drag that's still running.
		if (mouse_button != MouseButton.None)
			return;

		document.Layers.ToolLayer.Clear ();

		reversed_stroke = IsReverseStroke (e);

		// Recolor replaces pixels, so a stroke starting on a live object's ink first offers to bake
		// that object (the same prompt cut/erase uses). Declining aborts the whole click — carrying
		// on would paint the raster underneath the ink, where the change is invisible and the
		// object keeps its color anyway.
		if (!TryRasterizeObjectAtStrokeStart (document, e.Point))
			return;

		stencil = new BitMask (document.ImageSize.Width, document.ImageSize.Height);

		base.OnMouseDown (document, e);
	}

	// The reverse direction is right click, or the user-configured reverse gesture (default
	// Alt + left click) held during a left click stroke.
	private bool IsReverseStroke (ToolMouseEventArgs e)
		=> e.MouseButton == MouseButton.Right
			|| (e.MouseButton == MouseButton.Left && PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.RecolorReverseStroke).MatchesClick (e));

	/// <summary>
	/// Called before the stroke starts: if <paramref name="pos"/> lands on a live shape/text
	/// object's ink (hit-tested against just that object, rendered by the real renderers), prompts
	/// once and bakes it so the rest of the stroke edits real pixels. Returns whether the stroke
	/// may go ahead. Strokes on bare ground - or while the mask is the paint target, where no
	/// objects are in play - pass through unchanged.
	/// </summary>
	private bool TryRasterizeObjectAtStrokeStart (Document document, PointI pos)
	{
		if (document.Layers.CurrentMaskIsTarget)
			return true;

		UserLayer layer = document.Layers.CurrentUserLayer;
		if (!layer.ObjectLayer.IsLayerSetup || layer.Objects.Count == 0)
			return true;

		for (int i = layer.Objects.Count - 1; i >= 0; --i) {
			ILayerObject obj = layer.Objects[i];

			switch (obj) {
				// Topmost visible ink wins, mirroring PaintBucketTool's recolor hit-test. Modifier
				// nodes carry no clickable ink of their own.
				case ShapeObject { Hidden: false }:
				case TextObject { Hidden: false }:
					break;
				default:
					continue;
			}

			using ImageSurface probe = CairoExtensions.CreateImageSurface (Format.Argb32, layer.Surface.Width, layer.Surface.Height);
			switch (obj) {
				case ShapeObject shape:
					LayerObjectSelection.RenderShape (probe, layer, shape);
					break;
				case TextObject text:
					TextObjectRenderer.Render (probe, text, PintaCore.Chrome, antialias: true);
					break;
			}
			probe.Flush ();

			if (probe.GetColorBgra (pos).A == 0)
				continue;

			// RasterizeSubset reads by kind-scoped index, so translate the unified list position.
			int shape_index = -1;
			int text_index = -1;
			int shapes_seen = 0;
			int texts_seen = 0;
			for (int k = 0; k <= i; ++k) {
				if (layer.Objects[k] is ShapeObject)
					shapes_seen++;
				else if (layer.Objects[k] is TextObject)
					texts_seen++;
			}
			if (obj is ShapeObject)
				shape_index = shapes_seen - 1;
			else
				text_index = texts_seen - 1;

			List<int> shape_indices = shape_index >= 0 ? [shape_index] : [];
			List<int> text_indices = text_index >= 0 ? [text_index] : [];

			List<string> labels = [.. ObjectRasterizer.Describe (layer, shape_indices, text_indices)];
			if (!ObjectRasterizer.Confirm (PintaCore.Chrome, labels))
				return false;

			ObjectRasterizer.RasterizeSubset (document, workspace, PintaCore.Chrome, layer, shape_indices, text_indices);
			return true;
		}

		return true;
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		ColorBgra old_color;
		ColorBgra new_color;

		// This should have been created in OnMouseDown
		if (stencil is null)
			return;

		// Only a stroke in progress paints; after mouse-up a hovering move (no button held)
		// would otherwise repaint at the cursor.
		if (mouse_button is not (MouseButton.Left or MouseButton.Right)) {
			last_point = null;
			return;
		}

		// Pixels within tolerance of new_color are repainted toward old_color (names kept from the
		// ported Paint.NET code). A normal stroke finds secondary-colored pixels and paints them
		// with the primary color; a reversed stroke does the opposite.
		if (reversed_stroke) {
			old_color = Palette.SecondaryColor.ToColorBgra ();
			new_color = Palette.PrimaryColor.ToColorBgra ();
		} else {
			old_color = Palette.PrimaryColor.ToColorBgra ();
			new_color = Palette.SecondaryColor.ToColorBgra ();
		}

		var x = e.Point.X;
		var y = e.Point.Y;

		if (!last_point.HasValue)
			last_point = new PointI (x, y);

		if (document.Workspace.PointInCanvas (e.PointDouble))
			surface_modified = true;

		var surf = document.Layers.CurrentPaintSurface;
		var tmp_layer = document.Layers.ToolLayer.Surface;

		int roiPadding = BrushWidth + 2;
		RectangleI roi = RectangleI.FromPoints (last_point.Value, new PointI (x, y)).Inflated (roiPadding, roiPadding);

		roi = workspace.ClampToImageSize (roi);
		var myTolerance = (int) (Tolerance * 256);

		tmp_layer.Flush ();

		var tmp_data = tmp_layer.GetPixelData ();
		var tmp_width = tmp_layer.Width;
		var surf_data = surf.GetReadOnlyPixelData ();
		var surf_width = surf.Width;

		// The stencil lets us know if we've already checked this
		// pixel, providing a nice perf boost
		// Maybe this should be changed to a BitVector2DSurfaceAdapter?
		for (var i = roi.X; i <= roi.Right; i++)
			for (var j = roi.Y; j <= roi.Bottom; j++) {
				if (stencil[i, j])
					continue;

				ColorBgra surf_color = surf_data[j * surf_width + i];
				if (ColorBgra.ColorsWithinTolerance (new_color, surf_color, myTolerance))
					tmp_data[j * tmp_width + i] = AdjustColorDifference (new_color, old_color, surf_color);

				stencil[i, j] = true;
			}

		tmp_layer.MarkDirty ();

		using Context g = document.CreateClippedContext ();
		g.Antialias = UseAntialiasing ? Antialias.Subpixel : Antialias.None;

		g.MoveTo (last_point.Value.X, last_point.Value.Y);
		g.LineTo (x, y);

		g.LineWidth = BrushWidth;
		g.LineJoin = LineJoin.Round;
		g.LineCap = LineCap.Round;

		g.SetSourceSurface (tmp_layer, 0, 0);

		g.Stroke ();

		document.Workspace.Invalidate (roi);

		// See FoldRasterIntoComposite: a layer with effect nodes is painted from its accumulated
		// surface, so a live stroke on the raster alone would not appear until it was committed.
		if (ObjectOpacity.FoldRasterIntoComposite (PintaCore.Chrome, document.Layers.CurrentUserLayer))
			document.Workspace.Invalidate ();

		last_point = new PointI (x, y);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (tolerance_slider is not null)
			settings.PutSetting (SettingNames.RECOLOR_TOLERANCE, (int) tolerance_slider.GetValue ());
	}

	#region Private PDN Methods
	private static ColorBgra AdjustColorDifference (ColorBgra oldColor, ColorBgra newColor, ColorBgra basisColor)
	{
		return ColorBgra.FromBgra (
			b: AdjustColorByte (oldColor.B, newColor.B, basisColor.B),
			g: AdjustColorByte (oldColor.G, newColor.G, basisColor.G),
			r: AdjustColorByte (oldColor.R, newColor.R, basisColor.R),
			a: basisColor.A
		);
	}

	private static byte AdjustColorByte (byte oldByte, byte newByte, byte basisByte)
	{
		if (oldByte > newByte)
			return Utility.ClampToByte (basisByte - (oldByte - newByte));
		else
			return Utility.ClampToByte (basisByte + (newByte - oldByte));
	}
	#endregion

	private Label? tolerance_label;
	private Scale? tolerance_slider;
	private Separator? separator;

	private Label ToleranceLabel => tolerance_label ??= Label.New (string.Format ("  {0}: ", Translations.GetString ("Tolerance")));
	private Scale ToleranceSlider => tolerance_slider ??= GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.RECOLOR_TOLERANCE, 50));
	private Separator Separator => separator ??= GtkExtensions.CreateToolBarSeparator ();
}
