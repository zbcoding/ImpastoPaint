using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

/// <summary>
/// Painting tools fold each mouse move into a layer's composite through
/// <see cref="ObjectOpacity.FoldRasterRegionIntoComposite"/>, which re-renders only the area the edit
/// can reach through the effect nodes' sample radii. What the user sees has to be exactly what a full
/// rebuild shows: a radius that understates a blur's reach, or a render window that is not grown
/// enough to feed the edge of that area, leaves seams along every stroke.
/// </summary>
[TestFixture]
[Category ("Blurs")]
internal sealed class ModifierRegionFoldTest
{
	private const int Width = 300;
	private const int Height = 200;

	// The sum of the radii of the nodes ModifierStack adds.
	private const int StackRadius = 7;

	[OneTimeSetUp]
	public void LoadNativeLibraries ()
		=> Utilities.EnsureNativeLibraries ();

	private static ImageSurface RandomSurface (int seed)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, Width, Height);
		Random random = new (seed);
		Span<ColorBgra> pixels = surface.GetPixelData ();
		for (int i = 0; i < pixels.Length; i++) {
			ColorBgra straight = ColorBgra.FromBgra (
				(byte) random.Next (256), (byte) random.Next (256), (byte) random.Next (256), (byte) random.Next (256));
			pixels[i] = straight.ToPremultipliedAlpha ();
		}
		surface.MarkDirty ();
		return surface;
	}

	// Two blurs, one blended, around a per-pixel adjustment clipped to an ellipse (an antialiased clip
	// edge), with a random mask applied last.
	private static UserLayer LayerWithModifierStack ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		UserLayer layer = new (RandomSurface (seed: 1));

		GaussianBlurEffect wideBlur = new (services);
		wideBlur.Data.Radius = 5;
		layer.Objects.Add (new EffectModifierNode (wideBlur) { Opacity = 0.8 });

		HueSaturationEffect hue = new (services);
		hue.Data.Hue = 40;
		DocumentSelection ellipse = new ();
		ellipse.CreateEllipseSelection (new RectangleD (30, 20, 150, 120));
		layer.Objects.Add (new EffectModifierNode (hue, ellipse));

		GaussianBlurEffect narrowBlur = new (services);
		narrowBlur.Data.Radius = 2;
		layer.Objects.Add (new EffectModifierNode (narrowBlur) { BlendMode = BlendMode.Multiply });

		layer.CreateMask ();
		layer.ReplaceMaskSurface (RandomSurface (seed: 2));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		return layer;
	}

	private static void PaintStroke (ImageSurface target, RectangleI area, double red)
	{
		using Context g = new (target);
		g.SetSourceRgba (red, 0.3, 0.9, 0.7);
		g.Operator = Operator.Source;
		g.Rectangle (area.ToDouble ());
		g.Fill ();
	}

	private static readonly RectangleI[] strokes = [
		new (40, 30, 20, 10), // inside the ellipse clip
		new (170, 60, 30, 30), // across the clip's edge
		new (290, 192, 10, 8), // against the canvas corner
	];

	[Test]
	public void StrokesFoldedByRegionMatchAFullRebuild ()
	{
		UserLayer regionFolded = LayerWithModifierStack ();
		UserLayer rebuilt = LayerWithModifierStack ();

		for (int i = 0; i < strokes.Length; i++) {
			RectangleI stroke = strokes[i];
			PaintStroke (regionFolded.Surface, stroke, red: i / 3.0);
			PaintStroke (rebuilt.Surface, stroke, red: i / 3.0);

			RectangleI? folded = ObjectOpacity.FoldRasterRegionIntoComposite (chrome: null!, regionFolded, stroke);
			ObjectOpacity.FoldRasterIntoComposite (chrome: null!, rebuilt);

			RectangleI expected = stroke.Inflated (StackRadius, StackRadius).Intersect (new RectangleI (0, 0, Width, Height));
			Assert.That (folded, Is.EqualTo (expected), "only the area the stroke can reach is re-rendered");
			Utilities.CompareImages (regionFolded.Composite!, rebuilt.Composite!, tolerance: 0);
		}
	}

	// The mask applies last and per pixel, so a stroke on it folds the same way a raster stroke does.
	[Test]
	public void MaskStrokeFoldedByRegionMatchesAFullRebuild ()
	{
		UserLayer regionFolded = LayerWithModifierStack ();
		UserLayer rebuilt = LayerWithModifierStack ();
		RectangleI stroke = new (100, 50, 40, 25);

		PaintStroke (regionFolded.Mask!.Surface, stroke, red: 0.5);
		PaintStroke (rebuilt.Mask!.Surface, stroke, red: 0.5);

		ObjectOpacity.FoldRasterRegionIntoComposite (chrome: null!, regionFolded, stroke);
		ObjectOpacity.FoldRasterIntoComposite (chrome: null!, rebuilt);

		Utilities.CompareImages (regionFolded.Composite!, rebuilt.Composite!, tolerance: 0);
	}

	// An effect that reads the whole image (here a histogram) can change every pixel from a one-pixel
	// edit, so the fold has to rebuild everything and say so.
	[Test]
	public void NonLocalEffectRebuildsTheWholeCanvas ()
	{
		UserLayer layer = new (RandomSurface (seed: 3));
		layer.Objects.Add (new EffectModifierNode (new AutoLevelEffect (Utilities.CreateMockServices ())));
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		RectangleI? folded = ObjectOpacity.FoldRasterRegionIntoComposite (chrome: null!, layer, new RectangleI (10, 10, 4, 4));

		Assert.That (folded, Is.EqualTo (new RectangleI (0, 0, Width, Height)));
	}

	// A node's settings dialog is non-modal and edits the settings in place, rebuilding the layer only
	// when it closes, so a stroke can land while the composite still shows the old settings. Patching
	// just the stroke would leave a window of new settings in a canvas of old ones.
	[Test]
	public void SettingsEditedSinceTheLastRebuildForceAWholeCanvasFold ()
	{
		UserLayer layer = new (RandomSurface (seed: 4));
		GaussianBlurEffect blur = new (Utilities.CreateMockServices ());
		blur.Data.Radius = 2;
		layer.Objects.Add (new EffectModifierNode (blur));
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		blur.Data.Radius = 6;
		blur.Data.FirePropertyChanged (nameof (GaussianBlurEffect.GaussianBlurData.Radius));
		RectangleI stroke = new (50, 50, 10, 10);
		PaintStroke (layer.Surface, stroke, red: 1);

		RectangleI? folded = ObjectOpacity.FoldRasterRegionIntoComposite (chrome: null!, layer, stroke);
		Assert.That (folded, Is.EqualTo (new RectangleI (0, 0, Width, Height)), "stale settings: rebuild everything");

		// Once rebuilt with the new settings, the next stroke can be folded by region again.
		PaintStroke (layer.Surface, stroke, red: 0);
		folded = ObjectOpacity.FoldRasterRegionIntoComposite (chrome: null!, layer, stroke);
		Assert.That (folded, Is.EqualTo (stroke.Inflated (6, 6)));
	}
}
