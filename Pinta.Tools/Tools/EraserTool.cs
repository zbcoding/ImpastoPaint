//
// EraserTool.cs
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
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class EraserTool : BaseBrushTool
{
	private enum EraserType
	{
		Normal = 0,
		Smooth = 1,
	}

	private PointI? last_point = null;
	private EraserType eraser_type = EraserType.Normal;

	private const int LUT_Resolution = 256;
	private const int DefaultFeatherPercent = 100;
	private byte[]? lut_factor;
	private int lut_feather_percent;
	private readonly IWorkspaceService workspace;

	public EraserTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};
	}

	public override string Name
		=> Translations.GetString ("Eraser");

	public override string Icon
		=> Pinta.Resources.Icons.ToolEraser;

	public override string StatusBarText
		=> Translations.GetString ("Left click to erase to transparent, right click to erase to secondary color. ");

	public override Gdk.Key ShortcutKey
		=> new (Gdk.Constants.KEY_E);

	public override int Priority => 27;

	public override Gdk.Cursor DefaultCursor {
		get {
			double scale = workspace.GetScale ();
			var icon = GdkExtensions.CreateIconWithShape (
				"Cursor.Eraser.png",
				CursorShape.Ellipse,
				scale,
				BrushWidth,
				8,
				22,
				out int iconOffsetX,
				out int iconOffsetY);

			return Gdk.Cursor.NewFromTexture (icon, iconOffsetX, iconOffsetY, null);
		}
	}

	protected override void OnBuildToolBar (Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (TypeLabel);
		tb.Append (TypeComboBox);

		tb.Append (FeatherLabel);
		tb.Append (FeatherSlider);
		UpdateFeatherVisibility ();
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		PointI newPoint = e.Point;
		PointD newPointD = e.PointDouble;

		if (mouse_button == MouseButton.None) {
			last_point = null;
			return;
		}

		if (!last_point.HasValue)
			last_point = newPoint;

		// Both erase modes reach half the brush width out from the segment - EraseSmooth's own
		// radius, rounded the way it rounds it. That reach, not the pointer's position, is what
		// tells us pixels changed: a wide eraser scrubbed along an edge never puts the pointer on
		// the canvas but clears a good strip of it.
		int erasedReach = (BrushWidth / 2) + 1;

		RectangleI erased =
			RectangleI.FromPoints (
				last_point.Value,
				newPoint)
			.Inflated (
				erasedReach,
				erasedReach);

		if (document.Workspace.RectangleIntersectsCanvas (erased))
			surface_modified = true;

		using Context g = document.CreateClippedContext ();

		PointD lastPointD = (PointD) last_point.Value;

		switch (eraser_type) {

			case EraserType.Normal:

				EraseNormal (
					g,
					lastPointD,
					newPointD);

				break;

			case EraserType.Smooth:

				EraseSmooth (
					document.Layers.CurrentPaintSurface,
					g,
					lastPointD,
					newPointD);

				break;
		}

		int dirtyPadding = BrushWidth + 2;

		RectangleI dirty =
			RectangleI.FromPoints (
				last_point.Value,
				newPoint)
			.Inflated (
				dirtyPadding,
				dirtyPadding);

		if (document.Workspace.IsPartiallyOffscreen (dirty))
			document.Workspace.Invalidate ();
		else
			document.Workspace.Invalidate (document.ClampToImageSize (dirty));

		// See FoldRasterIntoComposite: a layer with effect nodes is painted from its accumulated
		// surface, so a live stroke on the raster alone would not appear until it was committed.
		if (ObjectOpacity.FoldRasterIntoComposite (PintaCore.Chrome, document.Layers.CurrentUserLayer))
			document.Workspace.Invalidate ();

		last_point = newPoint;
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (type_combobox is not null)
			settings.PutSetting (SettingNames.ERASER_ERASE_TYPE, type_combobox.ComboBox.Active);

		if (feather_slider is not null)
			settings.PutSetting (SettingNames.ERASER_FEATHER, (int) feather_slider.GetValue ());
	}

	/// <summary>
	/// How much of a pixel survives, 0-255, at <paramref name="distance"/> - measured in brush radii
	/// from the centre. The brush erases fully out to (1 - feather) of its radius and fades to nothing
	/// at the radius itself, so the erased area never leaves the cursor's circle. A feather of 0 has no
	/// fade at all.
	/// </summary>
	internal static byte SurvivingAlpha (double distance, double feather)
	{
		if (distance >= 1.0)
			return 255;

		if (feather <= 0.0)
			return 0;

		double fade = Math.Max (0.0, (distance - (1.0 - feather)) / feather);

		return (byte) (255.0 - Math.Cos (Math.Sqrt (fade) * Math.PI / 2.0) * 255.0);
	}

	/// <summary>Surviving alpha by distance from the centre, in 1/<see cref="LUT_Resolution"/> brush radii.</summary>
	internal static byte[] CreateLookupTable (int featherPercent)
	{
		double feather = featherPercent / 100.0;
		byte[] result = new byte[LUT_Resolution + 1];
		for (int i = 0; i < result.Length; i++)
			result[i] = SurvivingAlpha ((double) i / LUT_Resolution, feather);
		return result;
	}

	private byte[] GetLookupTable ()
	{
		int featherPercent = FeatherPercent;

		// Built on first use, and again when the slider has moved.
		if (lut_factor is null || lut_feather_percent != featherPercent) {
			lut_factor = CreateLookupTable (featherPercent);
			lut_feather_percent = featherPercent;
		}

		return lut_factor;
	}

	/// <summary>How much of a pixel survives, given its offset in pixels from the brush centre.</summary>
	private static byte SurvivingAlphaAt (byte[] lut, int dx, int dy, double brushRadius)
	{
		double distance = Math.Sqrt (dx * dx + dy * dy) / brushRadius;

		// Checked exactly: a table index would round a pixel on the rim inside the circle.
		return distance >= 1.0 ? (byte) 255 : lut[(int) (distance * LUT_Resolution)];
	}

	private static ImageSurface CopySurfacePart (ImageSurface surface, RectangleI destinationBounds)
	{
		ImageSurface temporarySurface = CairoExtensions.CreateImageSurface (
			Format.Argb32,
			destinationBounds.Width,
			destinationBounds.Height);

		using Context g = new (temporarySurface) { Operator = Operator.Source };

		g.SetSourceSurface (
			surface,
			-destinationBounds.Left,
			-destinationBounds.Top);

		g.Rectangle (
			new RectangleD (
				0,
				0,
				destinationBounds.Width,
				destinationBounds.Height));

		g.Fill ();

		//Flush to make sure all drawing operations are finished
		temporarySurface.Flush ();

		return temporarySurface;
	}

	private static void PasteSurfacePart (Context g, ImageSurface temporarySurface, RectangleI destinationBounds)
	{
		g.Operator = Operator.Source;

		g.SetSourceSurface (
			temporarySurface,
			destinationBounds.Left,
			destinationBounds.Top);

		g.Rectangle (
			new RectangleD (
				destinationBounds.Left,
				destinationBounds.Top,
				destinationBounds.Width,
				destinationBounds.Height));

		g.Fill ();
	}

	private void EraseNormal (Context g, PointD start, PointD end)
	{
		g.Antialias = UseAntialiasing ? Antialias.Subpixel : Antialias.None;

		// Adding 0.5 forces cairo into the correct square:
		// See https://bugs.launchpad.net/bugs/672232
		g.MoveTo (start.X + 0.5, start.Y + 0.5);
		g.LineTo (end.X + 0.5, end.Y + 0.5);

		// Right-click is erase to background color, left-click is transparent
		if (mouse_button == MouseButton.Right) {
			g.Operator = Operator.Source;
			g.SetSourceColor (Palette.SecondaryColor);
		} else
			g.Operator = Operator.Clear;

		g.LineWidth = BrushWidth;
		g.LineJoin = LineJoin.Round;
		g.LineCap = LineCap.Round;

		g.Stroke ();
	}

	private void EraseSmooth (ImageSurface surf, Context g, PointD start, PointD end)
	{
		int rad = (int) (BrushWidth / 2.0) + 1;

		// The cursor's circle: distances are relative to this, not to the padded box, so nothing
		// outside the circle is touched.
		double brushRadius = BrushWidth / 2.0;

		// Premultiply with alpha value
		byte backgroundA = (byte) (Palette.SecondaryColor.A * 255.0);
		byte backgroundR = (byte) (Palette.SecondaryColor.R * backgroundA);
		byte backgroundG = (byte) (Palette.SecondaryColor.G * backgroundA);
		byte backgroundB = (byte) (Palette.SecondaryColor.B * backgroundA);

		int numberOfSteps = (int) start.Distance (end) / rad + 1;

		byte[] lut = GetLookupTable ();

		for (var step = 0; step < numberOfSteps; step++) {

			PointD pt = Utility.Lerp (
				start,
				end,
				(float) step / numberOfSteps);

			int x = (int) pt.X;
			int y = (int) pt.Y;

			RectangleI surfaceBounds = new (0, 0, surf.Width, surf.Height);
			RectangleI brushBounds = new (x - rad, y - rad, 2 * rad, 2 * rad);
			RectangleI destinationBounds = RectangleI.Intersect (surfaceBounds, brushBounds);

			if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
				continue;

			// Allow Clipping through a temporary surface
			ImageSurface temporarySurface = CopySurfacePart (surf, destinationBounds);
			Span<ColorBgra> temporaryData = temporarySurface.GetPixelData ();

			// Right/Bottom are the last pixel inside destinationBounds; stopping short of them left
			// the canvas's final column and row un-erasable, since that is where the clip lands.
			for (int iy = destinationBounds.Top; iy <= destinationBounds.Bottom; iy++) {

				var srcRow = temporaryData[(temporarySurface.Width * (iy - destinationBounds.Top))..];
				int dy = iy - y;

				for (var ix = destinationBounds.Left; ix <= destinationBounds.Right; ix++) {

					byte force = SurvivingAlphaAt (lut, ix - x, dy, brushRadius);

					// Note: premultiplied alpha is used!
					int idx = ix - destinationBounds.Left;

					ColorBgra original = srcRow[idx];

					srcRow[idx] = mouse_button switch {

						MouseButton.Right => ColorBgra.FromBgra (
							b: (byte) ((original.B * force + backgroundB * (255 - force)) / 255),
							g: (byte) ((original.G * force + backgroundG * (255 - force)) / 255),
							r: (byte) ((original.R * force + backgroundR * (255 - force)) / 255),
							a: (byte) ((original.A * force + backgroundA * (255 - force)) / 255)),

						_ => ColorBgra.FromBgra (
							b: (byte) (original.B * force / 255),
							g: (byte) (original.G * force / 255),
							r: (byte) (original.R * force / 255),
							a: (byte) (original.A * force / 255)),
					};
				}
			}

			// Draw the final result on the surface
			PasteSurfacePart (g, temporarySurface, destinationBounds);
		}
	}

	private Label? type_label;
	private ToolBarComboBox? type_combobox;

	private Label TypeLabel => type_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Type")));
	private ToolBarComboBox TypeComboBox {
		get {
			if (type_combobox is null) {
				type_combobox = ToolBarComboBox.New (100, 0, false, Translations.GetString ("Normal"), Translations.GetString ("Smooth"));

				type_combobox.ComboBox.OnChanged += (o, e) => {
					eraser_type = (EraserType) type_combobox.ComboBox.Active;
					UpdateFeatherVisibility ();
				};

				type_combobox.ComboBox.Active = Settings.GetSetting (SettingNames.ERASER_ERASE_TYPE, 0);
			}

			return type_combobox;
		}
	}

	private Label? feather_label;
	private Scale? feather_slider;

	private Label FeatherLabel => feather_label ??= Label.New (string.Format ("  {0}: ", Translations.GetString ("Feather")));
	private Scale FeatherSlider => feather_slider ??= GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.ERASER_FEATHER, DefaultFeatherPercent));

	/// <summary>Softness of the Smooth eraser's edge, in percent of the brush radius.</summary>
	private int FeatherPercent => (int) FeatherSlider.GetValue ();

	// Only the Smooth eraser has a soft edge; Normal is governed by the antialiasing toggle.
	private void UpdateFeatherVisibility ()
	{
		bool smooth = eraser_type == EraserType.Smooth;
		FeatherLabel.Visible = smooth;
		FeatherSlider.Visible = smooth;
	}
}
