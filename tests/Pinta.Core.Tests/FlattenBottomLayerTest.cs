using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Flatten folds every layer into the bottom layer's raster, so the bottom layer must render as a
/// plain, fully opaque, visible layer afterwards. Saving to a format without layers flattens first;
/// a hidden or translucent bottom layer used to survive that and turn the saved file blank or faint.
/// </summary>
[TestFixture]
internal sealed class FlattenBottomLayerTest : DocumentHarness
{
	[Test]
	public void HiddenBottomLayerDoesNotBlankTheFlattenedImage ()
	{
		Fill (Layer (0).Surface, Blue);
		Layer (0).Hidden = true;
		AddLayer (Red);

		Flatten ();

		Assert.That (Document.Layers.Count (), Is.EqualTo (1));
		Assert.That (FlattenedPixel (0, 0), Is.EqualTo (Red));
	}

	[Test]
	public void TranslucentBottomLayerIsNotAppliedTwice ()
	{
		Fill (Layer (0).Surface, Blue);
		Layer (0).Opacity = 0.5;
		AddLayer (Red);

		Flatten ();

		Assert.That (Layer (0).Opacity, Is.EqualTo (1.0));
		Assert.That (FlattenedPixel (0, 0), Is.EqualTo (Red));
	}

	[Test]
	public void UndoRestoresBottomLayerProperties ()
	{
		Fill (Layer (0).Surface, Blue);
		Layer (0).Hidden = true;
		Layer (0).Opacity = 0.5;
		AddLayer (Red);

		Flatten ();
		Document.History.Undo ();

		Assert.That (Document.Layers.Count (), Is.EqualTo (2));
		Assert.That (Layer (0).Hidden, Is.True);
		Assert.That (Layer (0).Opacity, Is.EqualTo (0.5));
	}

	private static void Flatten ()
	{
		PintaCore.Actions.Image.Flatten.Sensitive = true;
		PintaCore.Actions.Image.Flatten.Activate ();
	}

	private ColorBgra FlattenedPixel (int x, int y)
	{
		using Cairo.ImageSurface flat = Document.GetFlattenedImage ();
		return flat.GetColorBgra (new PointI (x, y));
	}
}
