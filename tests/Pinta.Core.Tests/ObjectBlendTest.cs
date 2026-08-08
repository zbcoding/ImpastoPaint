using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// The object-layer compositing invariant: an object's blend mode only affects the pixels it
/// actually covers. <see cref="ObjectOpacity.Draw"/> renders a blended object into an image-sized
/// scratch surface and paints the whole thing over the object surface, so a blend operator that
/// touched destination pixels under a transparent source would recolour every object on the layer —
/// a Color Dodge shape would repaint an entire text object sitting beneath it, not just the overlap.
/// </summary>
[TestFixture]
internal sealed class ObjectBlendTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	[TestCase (BlendMode.ColorDodge)]
	[TestCase (BlendMode.Multiply)]
	[TestCase (BlendMode.Difference)]
	[TestCase (BlendMode.Color)]
	public void BlendedObjectLeavesUncoveredPixelsAlone (BlendMode mode)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 20, 10);

		// Stand-in for the text object: a solid block on the left half.
		using (Context g = new (surface)) {
			g.FillRectangle (new RectangleD (0, 0, 10, 10), new Color (0.2, 0.4, 0.8));
		}

		ColorBgra before = surface.GetColorBgra (new PointI (2, 5));

		// Stand-in for the blended shape above it: covers only the right half.
		ObjectOpacity.Draw (surface, 1.0, mode, target => {
			using Context g = new (target);
			g.FillRectangle (new RectangleD (10, 0, 10, 10), new Color (0.9, 0.5, 0.1));
		});

		Assert.That (surface.GetColorBgra (new PointI (2, 5)), Is.EqualTo (before),
			$"{mode} recoloured a pixel its object never covered");
	}
}
