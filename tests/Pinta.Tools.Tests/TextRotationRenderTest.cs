using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The bug this pins: TextObject.Rotation was applied only by the text tool's own draw path, so a
// rotated text object came back upright the moment anything else rendered it - leaving the tool
// (the layer composite), baking it, or the paint bucket probing where its ink is. The tool and
// TextObjectRenderer held two copies of the same draw sequence and only the tool's copy rotated;
// they are one routine now, so these tests exercise the shared one through the paths a rotated
// object actually travels.
[TestFixture]
internal sealed class TextRotationRenderTest : ToolsTestHarness
{
	private static TextObject Text (double rotation)
	{
		TextEngine engine = new (["IMPASTO"]) {
			Origin = new PointI (4, 4),
			PrimaryColor = new Color (0, 0, 0, 1),
		};
		return new TextObject (engine) { Rotation = rotation };
	}

	private static ImageSurface Rendered (TextObject obj)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);
		TextObjectRenderer.Render (surface, obj, PintaCore.Chrome, antialias: true);
		surface.MarkDirty ();
		return surface;
	}

	private static int DifferingPixels (ImageSurface left, ImageSurface right)
	{
		var a = left.GetReadOnlyPixelData ();
		var b = right.GetReadOnlyPixelData ();
		int differing = 0;
		for (int i = 0; i < a.Length; ++i)
			if (!a[i].Equals (b[i]))
				++differing;
		return differing;
	}

	private static int InkPixels (ImageSurface surface)
	{
		var data = surface.GetReadOnlyPixelData ();
		int ink = 0;
		for (int i = 0; i < data.Length; ++i)
			if (data[i].A > 0)
				++ink;
		return ink;
	}

	[Test]
	public void TheSharedRendererDrawsRotatedTextRotated ()
	{
		using ImageSurface upright = Rendered (Text (0));
		using ImageSurface turned = Rendered (Text (45));

		Assert.That (InkPixels (upright), Is.GreaterThan (0), "the upright text has to render at all");
		Assert.That (DifferingPixels (upright, turned), Is.GreaterThan (0),
			"a 45 degree object rendered identically to an upright one means the rotation was dropped");
	}

	// The pivot is the object's origin, so a full turn has to land back where it started - this is
	// what keeps rotated text in place as the user types more of it.
	[Test]
	public void AFullTurnRendersWhereItStarted ()
	{
		using ImageSurface upright = Rendered (Text (0));
		using ImageSurface turned = Rendered (Text (360));

		Assert.That (DifferingPixels (upright, turned), Is.Zero, "360 degrees about the origin is no rotation");
	}

	// The path a rotated object takes once the text tool is no longer the one drawing it.
	[Test]
	public void RotationSurvivesTheLayerComposite ()
	{
		UserLayer layer = Layer (0);
		TextObject obj = Text (45);
		AddObject (layer, obj, "Rotated text");
		Refresh (layer);

		using ImageSurface uprightAlone = Rendered (Text (0));

		int matchesUpright = 0;
		var upright = uprightAlone.GetReadOnlyPixelData ();
		for (int y = 0; y < CanvasSize; ++y)
			for (int x = 0; x < CanvasSize; ++x)
				if (Shown (layer, x, y).Equals (upright[(y * CanvasSize) + x]))
					++matchesUpright;

		Assert.That (matchesUpright, Is.LessThan (CanvasSize * CanvasSize),
			"the composited layer must not look like the same text drawn upright");
	}

	// The bake path: a rotated object has to fuse into the raster rotated.
	[Test]
	public void RotationSurvivesABake ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, Text (45), "Rotated text");
		Refresh (layer);
		Assert.That (
			ObjectRasterizer.RasterizeSubset (Document, PintaCore.Workspace, PintaCore.Chrome, layer, [], [0]),
			Is.True, "the object has to actually bake");

		using ImageSurface uprightAlone = Rendered (Text (0));
		var upright = uprightAlone.GetReadOnlyPixelData ();
		var baked = layer.Surface.GetReadOnlyPixelData ();

		int differing = 0;
		for (int i = 0; i < baked.Length; ++i)
			if (!baked[i].Equals (upright[i]))
				++differing;

		Assert.That (differing, Is.GreaterThan (0), "the baked raster must not be the upright text");
	}
}
