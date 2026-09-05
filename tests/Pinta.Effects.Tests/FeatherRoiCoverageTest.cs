using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

/// <summary>
/// Feather's row loops are Parallel.For calls, whose upper bound is exclusive, but RectangleI.Bottom
/// is the last row inside the region - so the region's final row used to be neither copied nor
/// feathered, leaving the bottom edge unfeathered and its last row blank.
/// </summary>
[TestFixture]
[Category ("Object")]
internal sealed class FeatherRoiCoverageTest
{
	private const int Size = 12;

	[OneTimeSetUp]
	public void LoadNativeLibraries ()
		=> Utilities.EnsureNativeLibraries ();

	private static ImageSurface CreateOpaqueSurface ()
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, Size, Size);
		surface.GetPixelData ().Fill (ColorBgra.FromBgra (40, 80, 160, 255));
		return surface;
	}

	[Test]
	public void EveryRowOfTheRegionIsWritten ()
	{
		using ImageSurface source = CreateOpaqueSurface ();
		using ImageSurface result = CairoExtensions.CreateImageSurface (Format.Argb32, Size, Size);
		FeatherEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Tolerance = 20;
		effect.Data.Radius = 3;
		// With no transparent pixel and no canvas edge to feather against, the pass has nothing to
		// fade: the result has to come out identical to the source, last row included.
		effect.Data.FeatherCanvasEdge = false;

		effect.Render (source, result, [source.GetBounds ()]);

		Utilities.CompareImages (result, source, tolerance: 0);
	}

	[Test]
	public void BottomCanvasEdgeFadesLikeTheTopOne ()
	{
		using ImageSurface source = CreateOpaqueSurface ();
		using ImageSurface result = CairoExtensions.CreateImageSurface (Format.Argb32, Size, Size);
		FeatherEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Tolerance = 20;
		effect.Data.Radius = 3;
		effect.Data.FeatherCanvasEdge = true;

		effect.Render (source, result, [source.GetBounds ()]);

		var pixels = result.GetReadOnlyPixelData ();
		int column = Size / 2;
		byte[] fromTop = new byte[Size / 2];
		byte[] fromBottom = new byte[Size / 2];
		for (int offset = 0; offset < Size / 2; ++offset) {
			fromTop[offset] = pixels[offset * Size + column].A;
			fromBottom[offset] = pixels[(Size - 1 - offset) * Size + column].A;
		}

		Assert.That (fromBottom, Is.EqualTo (fromTop), "alpha at each distance from the bottom edge matches the top edge");
	}
}
