using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class LayerTransformNodeTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	private static void RequireCairo ()
	{
		try {
			using ImageSurface _ = CairoExtensions.CreateImageSurface (Format.Argb32, 1, 1);
		} catch (DllNotFoundException e) {
			Assert.Ignore ($"Native cairo-graphics unavailable: {e.Message}");
		}
	}

	private static ImageSurface SurfaceWithPixel (int width, int height, PointI point)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		surface.GetPixelData ()[(point.Y * width) + point.X] = ColorBgra.FromBgra (0, 0, 255, 255);
		surface.MarkDirty ();
		return surface;
	}

	[Test]
	public void TranslationMovesPixelsAndClampsAtCanvasBounds ()
	{
		RequireCairo ();
		using ImageSurface surface = SurfaceWithPixel (5, 3, new PointI (1, 1));
		LayerTransformNode node = new (new LayerTransformData {
			TranslateHorizontal = 2,
		});

		node.Apply (surface);

		Assert.Multiple (() => {
			Assert.That (surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (0));
			Assert.That (surface.GetColorBgra (new PointI (3, 1)).R, Is.EqualTo (255));
		});
	}

	[Test]
	public void HorizontalFlipUsesTheCanvasCenter ()
	{
		RequireCairo ();
		using ImageSurface surface = SurfaceWithPixel (5, 3, new PointI (0, 1));
		LayerTransformNode node = new (new LayerTransformData {
			FlipHorizontal = true,
		});

		node.Apply (surface);

		Assert.That (surface.GetColorBgra (new PointI (4, 1)).R, Is.EqualTo (255));
	}

	[Test]
	public void PerspectiveCornerOffsetsWarpTheLayer ()
	{
		RequireCairo ();
		using ImageSurface surface = SurfaceWithPixel (5, 5, new PointI (0, 0));
		LayerTransformNode node = new (new LayerTransformData {
			PerspectiveTopLeftHorizontal = 1,
			Resampling = ResamplingMode.NearestNeighbor,
		});

		node.Apply (surface);

		Assert.That (surface.GetColorBgra (new PointI (1, 0)).R, Is.EqualTo (255));
	}

	[Test]
	public void HiddenAndZeroStrengthTransformsLeavePixelsUnchanged ()
	{
		RequireCairo ();
		using ImageSurface hiddenSurface = SurfaceWithPixel (5, 3, new PointI (1, 1));
		using ImageSurface zeroSurface = SurfaceWithPixel (5, 3, new PointI (1, 1));
		LayerTransformData data = new () { TranslateHorizontal = 2 };

		new LayerTransformNode (data) { Hidden = true }.Apply (hiddenSurface);
		new LayerTransformNode ((LayerTransformData) data.Clone ()) { Opacity = 0 }.Apply (zeroSurface);

		Assert.Multiple (() => {
			Assert.That (hiddenSurface.GetColorBgra (new PointI (1, 1)).R, Is.EqualTo (255));
			Assert.That (zeroSurface.GetColorBgra (new PointI (1, 1)).R, Is.EqualTo (255));
		});
	}

	[Test]
	public void CloneDoesNotAliasTransformSettings ()
	{
		LayerTransformNode original = new (new LayerTransformData { ScaleHorizontal = 2 }) { Name = "mine" };
		LayerTransformNode copy = original.Clone ();

		copy.Data.ScaleHorizontal = 3;

		Assert.Multiple (() => {
			Assert.That (original.Data.ScaleHorizontal, Is.EqualTo (2));
			Assert.That (copy.Data.ScaleHorizontal, Is.EqualTo (3));
			Assert.That (copy.Name, Is.EqualTo ("mine"));
		});
	}

	[Test]
	public void TransformSettingsSurviveSerializerRoundTrip ()
	{
		LayerTransformData original = new () {
			Angle = new DegreesAngle (17.5),
			TranslateHorizontal = 12.25,
			TranslateVertical = -8.5,
			ScaleHorizontal = 1.25,
			ScaleVertical = 0.75,
			ShearHorizontal = 11,
			ShearVertical = -7,
			FlipHorizontal = true,
			PerspectiveTopLeftHorizontal = 2,
			PerspectiveTopLeftVertical = 3,
			PerspectiveTopRightHorizontal = 4,
			PerspectiveTopRightVertical = 5,
			PerspectiveBottomRightHorizontal = 6,
			PerspectiveBottomRightVertical = 7,
			PerspectiveBottomLeftHorizontal = 8,
			PerspectiveBottomLeftVertical = 9,
			Resampling = ResamplingMode.NearestNeighbor,
		};
		LayerTransformData restored = new ();

		EffectDataSerializer.ApplyText (restored, EffectDataSerializer.ToText (original));

		Assert.That (restored, Is.EqualTo (original));
	}

	[Test]
	public void LayerReportsOnlyEnabledNonIdentityTransformsAsActive ()
	{
		RequireCairo ();
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 2, 2);
		UserLayer layer = new (surface);
		LayerTransformNode node = new (new LayerTransformData ());
		layer.Objects.Add (node);

		Assert.That (layer.HasActiveTransform, Is.False);

		node.Data.TranslateHorizontal = 1;
		Assert.That (layer.HasActiveTransform, Is.True);

		node.Hidden = true;
		Assert.That (layer.HasActiveTransform, Is.False);
	}
}
