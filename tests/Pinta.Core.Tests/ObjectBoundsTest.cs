using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ObjectBoundsTest
{
	private static readonly ColorBgra background = ColorBgra.FromBgra (255, 255, 255, 255);
	private static readonly ColorBgra content = ColorBgra.FromBgra (0, 0, 255, 255);

	/// <param name="content_area">Filled with a color that differs from the background.</param>
	private static ImageSurface CreateSurface (int width, int height, RectangleI? content_area = null)
	{
		Utilities.EnsureNativeLibraries ();
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		var data = surface.GetPixelData ();
		data.Fill (background);

		if (content_area is not { } area)
			return surface;

		for (int y = area.Top; y <= area.Bottom; ++y)
			for (int x = area.Left; x <= area.Right; ++x)
				data[y * width + x] = content;

		return surface;
	}

	[Test]
	public void InteriorContentIsBoundedExactly ()
	{
		using ImageSurface surface = CreateSurface (16, 16, new RectangleI (3, 4, 5, 6));

		Assert.That (Utility.GetObjectBounds (surface), Is.EqualTo (new RectangleI (3, 4, 5, 6)));
	}

	/// <summary>
	/// The trim loops used to stop one short of the inclusive Right/Bottom edge, so content that
	/// reached the last column or row lost it - and a uniform last column or row was never trimmed.
	/// </summary>
	[Test]
	public void ContentReachingTheFarEdgeKeepsItsLastRowAndColumn ()
	{
		using ImageSurface surface = CreateSurface (16, 16, new RectangleI (10, 9, 6, 7));

		Assert.That (Utility.GetObjectBounds (surface), Is.EqualTo (new RectangleI (10, 9, 6, 7)));
	}

	/// <summary>
	/// The background is whatever the search area's top-left pixel is, so a surface with content in
	/// that corner has nothing to distinguish an object from.
	/// </summary>
	[Test]
	public void ContentFillingTheSurfaceLeavesNoObject ()
	{
		using ImageSurface surface = CreateSurface (8, 8, new RectangleI (0, 0, 8, 8));

		Assert.That (Utility.GetObjectBounds (surface).IsEmpty, Is.True);
	}

	[Test]
	public void UniformSurfaceHasNoObject ()
	{
		using ImageSurface surface = CreateSurface (8, 8);

		Assert.That (Utility.GetObjectBounds (surface).IsEmpty, Is.True);
	}

	[Test]
	public void SearchAreaRestrictsTheResult ()
	{
		using ImageSurface surface = CreateSurface (16, 16, new RectangleI (2, 2, 12, 12));

		RectangleI bounds = Utility.GetObjectBounds (surface, new RectangleI (6, 6, 4, 4));

		Assert.That (bounds.IsEmpty, Is.True, "the whole search area is one uniform color");
	}

	/// <summary>
	/// <see cref="Document.ClampToImageSize"/> clamps a fully off-canvas selection to zero size on
	/// the far edge, where the origin is not a pixel of the surface. Reading that corner threw.
	/// </summary>
	[Test]
	public void EmptySearchAreaOnTheFarEdgeIsReturnedUnchanged ()
	{
		using ImageSurface surface = CreateSurface (8, 8, new RectangleI (2, 2, 4, 4));
		RectangleI offCanvas = new (8, 8, 0, 0);

		Assert.That (Utility.GetObjectBounds (surface, offCanvas), Is.EqualTo (offCanvas));
	}
}
