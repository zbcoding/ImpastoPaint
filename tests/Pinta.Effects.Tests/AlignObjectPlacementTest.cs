using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

/// <summary>
/// Where Align Object actually lands the object, asserted against the region's edges rather than a
/// reference image: the right/bottom cases used to be one pixel short because they aligned to the
/// inclusive Right/Bottom instead of the far edge, which also made a full-width object land at -1.
/// </summary>
[TestFixture]
[Category ("Object")]
internal sealed class AlignObjectPlacementTest
{
	private const int Size = 16;
	private static readonly ColorBgra background = ColorBgra.FromBgra (255, 255, 255, 255);
	private static readonly ColorBgra content = ColorBgra.FromBgra (0, 0, 255, 255);

	[OneTimeSetUp]
	public void LoadNativeLibraries ()
		=> Utilities.EnsureNativeLibraries ();

	private static ImageSurface CreateSource (RectangleI contentArea)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, Size, Size);
		var data = surface.GetPixelData ();
		data.Fill (background);

		for (int y = contentArea.Top; y <= contentArea.Bottom; ++y)
			for (int x = contentArea.Left; x <= contentArea.Right; ++x)
				data[y * Size + x] = content;

		return surface;
	}

	private static ImageSurface Align (ImageSurface source, AlignPosition position, RectangleI region)
	{
		AlignObjectEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Position = position;
		ImageSurface result = CairoExtensions.CreateImageSurface (Format.Argb32, Size, Size);
		effect.Render (source, result, [region]);
		return result;
	}

	/// <returns>The bounds of every pixel that is not the background color.</returns>
	private static RectangleI ContentBounds (ImageSurface surface)
	{
		var data = surface.GetReadOnlyPixelData ();
		int left = Size, top = Size, right = -1, bottom = -1;

		for (int y = 0; y < Size; ++y) {
			for (int x = 0; x < Size; ++x) {
				if (data[y * Size + x] == background)
					continue;
				left = System.Math.Min (left, x);
				top = System.Math.Min (top, y);
				right = System.Math.Max (right, x);
				bottom = System.Math.Max (bottom, y);
			}
		}

		return right < left || bottom < top
			? RectangleI.Zero
			: RectangleI.FromLTRB (left, top, right, bottom);
	}

	[Test]
	public void BottomRightPutsTheObjectFlushWithBothFarEdges ()
	{
		using ImageSurface source = CreateSource (new RectangleI (1, 2, 4, 5));
		using ImageSurface result = Align (source, AlignPosition.BottomRight, source.GetBounds ());

		Assert.That (ContentBounds (result), Is.EqualTo (new RectangleI (Size - 4, Size - 5, 4, 5)));
	}

	[Test]
	public void TopLeftPutsTheObjectAtTheOrigin ()
	{
		using ImageSurface source = CreateSource (new RectangleI (6, 7, 4, 5));
		using ImageSurface result = Align (source, AlignPosition.TopLeft, source.GetBounds ());

		Assert.That (ContentBounds (result), Is.EqualTo (new RectangleI (0, 0, 4, 5)));
	}

	/// <summary>
	/// An object as wide as the region has nowhere to move; the old arithmetic put it at -1 and the
	/// copy threw an <see cref="System.ArgumentOutOfRangeException"/> on the render thread, where
	/// it was swallowed and the effect silently did nothing.
	/// </summary>
	[Test]
	public void ObjectSpanningTheFullWidthStaysInPlace ()
	{
		using ImageSurface source = CreateSource (new RectangleI (0, 4, Size, 4));
		using ImageSurface result = Align (source, AlignPosition.BottomRight, source.GetBounds ());

		Assert.That (ContentBounds (result), Is.EqualTo (new RectangleI (0, Size - 4, Size, 4)));
	}

	/// <summary>
	/// A selection dragged fully off the canvas clamps to zero size on the far edge, whose origin is
	/// not a pixel; sampling it for the background color threw.
	/// </summary>
	[Test]
	public void EmptyRegionOnTheFarEdgeRendersNothing ()
	{
		using ImageSurface source = CreateSource (new RectangleI (2, 2, 4, 4));
		using ImageSurface result = Align (source, AlignPosition.Center, new RectangleI (Size, Size, 0, 0));

		var pixels = result.GetReadOnlyPixelData ();
		bool untouched = true;
		foreach (ColorBgra pixel in pixels)
			untouched &= pixel.A == 0;

		Assert.That (untouched, Is.True, "the destination was left as it came in");
	}
}
