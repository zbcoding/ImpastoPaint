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

	// How much of each pixel the current stroke has erased so far, 0-255. A stroke erases a pixel
	// to at most the Opacity setting however often it passes over it, so each dab raises this
	// (never adds to it) and the pixel is recomputed from undo_surface, its state before the stroke.
	private byte[]? stroke_erased;
	private EraserType eraser_type = EraserType.Normal;

	private const int LUT_Resolution = 256;
	private const int DefaultFeatherPercent = 100;
	private const int DefaultOpacityPercent = 100;
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

		tb.Append (OpacityLabel);
		tb.Append (OpacitySlider);

		tb.Append (FeatherLabel);
		tb.Append (FeatherSlider);
		UpdateFeatherVisibility ();
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		PointI newPoint = e.Point;

		if (mouse_button == MouseButton.None || undo_surface is null) {
			last_point = null;
			return;
		}

		if (!last_point.HasValue)
			last_point = newPoint;

		// Both erase modes reach half the brush width out from the segment, rounded up. That reach,
		// not the pointer's position, is what tells us pixels changed: a wide eraser scrubbed along
		// an edge never puts the pointer on the canvas but clears a good strip of it.
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

		EraseSegment (
			document.Layers.CurrentPaintSurface,
			undo_surface,
			g,
			last_point.Value,
			newPoint);

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

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		stroke_erased = null;
		base.OnMouseUp (document, e);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (type_combobox is not null)
			settings.PutSetting (SettingNames.ERASER_ERASE_TYPE, type_combobox.ComboBox.Active);

		if (opacity_slider is not null)
			settings.PutSetting (SettingNames.ERASER_OPACITY, (int) opacity_slider.GetValue ());

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

	/// <summary>
	/// Erases the capsule of <paramref name="brushWidth"/> pixels around the segment between two pixel
	/// centres. Each pixel's share of the erase is raised to this dab's value if that is higher, and
	/// the pixel is rebuilt from <paramref name="before"/>, the surface as the stroke found it.
	/// </summary>
	private void EraseSegment (ImageSurface surface, ImageSurface before, Context g, PointI start, PointI end)
	{
		double brushRadius = BrushWidth / 2.0;
		int reach = (int) Math.Ceiling (brushRadius) + 1;
		RectangleI surfaceBounds = new (0, 0, surface.Width, surface.Height);
		RectangleI destinationBounds = RectangleI.Intersect (
			surfaceBounds,
			RectangleI.FromPoints (start, end).Inflated (reach, reach));

		if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
			return;

		stroke_erased ??= new byte[surface.Width * surface.Height];

		byte[] lut = GetLookupTable ();
		int opacityPercent = OpacityPercent;
		ColorBgra background = PremultipliedSecondaryColor ();
		PointD from = new (start.X + 0.5, start.Y + 0.5);
		PointD to = new (end.X + 0.5, end.Y + 0.5);

		// Allow clipping through a temporary surface
		using ImageSurface temporarySurface = CopySurfacePart (surface, destinationBounds);
		Span<ColorBgra> temporaryData = temporarySurface.GetPixelData ();
		ReadOnlySpan<ColorBgra> beforeData = before.GetReadOnlyPixelData ();

		// Right/Bottom are the last pixel inside destinationBounds; stopping short of them left
		// the canvas's final column and row un-erasable, since that is where the clip lands.
		for (int iy = destinationBounds.Top; iy <= destinationBounds.Bottom; iy++) {
			for (int ix = destinationBounds.Left; ix <= destinationBounds.Right; ix++) {

				double distance = DistanceToSegment (ix + 0.5, iy + 0.5, from, to);
				int erased = EraseCoverage (distance, brushRadius, lut) * opacityPercent / 100;
				int surfaceIndex = iy * surface.Width + ix;

				if (erased <= stroke_erased[surfaceIndex])
					continue;

				stroke_erased[surfaceIndex] = (byte) erased;

				temporaryData[(iy - destinationBounds.Top) * temporarySurface.Width + ix - destinationBounds.Left] =
					ErasedPixel (beforeData[surfaceIndex], background, (byte) (255 - erased));
			}
		}

		// Draw the final result on the surface
		PasteSurfacePart (g, temporarySurface, destinationBounds);
	}

	/// <summary>How much of a pixel the eraser reaches, 0-255, when its centre is <paramref name="distance"/> pixels from the stroke.</summary>
	private int EraseCoverage (double distance, double brushRadius, byte[] lut)
	{
		if (eraser_type == EraserType.Smooth) {
			// Checked exactly: a table index would round a pixel on the rim inside the circle.
			return distance >= brushRadius ? 0 : 255 - lut[(int) (distance / brushRadius * LUT_Resolution)];
		}

		if (!UseAntialiasing)
			return distance < brushRadius ? 255 : 0;

		return (int) (Math.Clamp (brushRadius - distance + 0.5, 0.0, 1.0) * 255.0);
	}

	/// <summary>
	/// Left-click erases to transparent; right-click erases toward the secondary colour.
	/// <paramref name="surviving"/> is how much of <paramref name="original"/> is left. Premultiplied alpha is used.
	/// </summary>
	private ColorBgra ErasedPixel (ColorBgra original, ColorBgra background, byte surviving)
	{
		if (mouse_button != MouseButton.Right) {
			return ColorBgra.FromBgra (
				b: (byte) (original.B * surviving / 255),
				g: (byte) (original.G * surviving / 255),
				r: (byte) (original.R * surviving / 255),
				a: (byte) (original.A * surviving / 255));
		}

		return ColorBgra.FromBgra (
			b: (byte) ((original.B * surviving + background.B * (255 - surviving)) / 255),
			g: (byte) ((original.G * surviving + background.G * (255 - surviving)) / 255),
			r: (byte) ((original.R * surviving + background.R * (255 - surviving)) / 255),
			a: (byte) ((original.A * surviving + background.A * (255 - surviving)) / 255));
	}

	private ColorBgra PremultipliedSecondaryColor ()
	{
		byte a = (byte) (Palette.SecondaryColor.A * 255.0);

		return ColorBgra.FromBgra (
			b: (byte) (Palette.SecondaryColor.B * a),
			g: (byte) (Palette.SecondaryColor.G * a),
			r: (byte) (Palette.SecondaryColor.R * a),
			a: a);
	}

	private static double DistanceToSegment (double x, double y, PointD from, PointD to)
	{
		double dx = to.X - from.X;
		double dy = to.Y - from.Y;
		double lengthSquared = dx * dx + dy * dy;
		double along = lengthSquared == 0.0
			? 0.0
			: Math.Clamp (((x - from.X) * dx + (y - from.Y) * dy) / lengthSquared, 0.0, 1.0);

		return Math.Sqrt (Math.Pow (x - (from.X + along * dx), 2) + Math.Pow (y - (from.Y + along * dy), 2));
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

	private Label? opacity_label;
	private Scale? opacity_slider;

	private Label OpacityLabel => opacity_label ??= Label.New (string.Format ("  {0}: ", Translations.GetString ("Opacity")));
	private Scale OpacitySlider => opacity_slider ??= GtkExtensions.CreateToolBarSlider (1, 100, 1, Settings.GetSetting (SettingNames.ERASER_OPACITY, DefaultOpacityPercent));

	/// <summary>The most a stroke erases of any pixel, in percent.</summary>
	private int OpacityPercent => (int) OpacitySlider.GetValue ();

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
