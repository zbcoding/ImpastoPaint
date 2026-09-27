using System;
using System.IO;
using System.Linq;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Photoshop import, checked against files Photoshop wrote. Each fixture carries the merged image
/// Photoshop rendered when it saved; flattening what Impasto imported must reproduce it, which
/// holds only if layer order, placement, alpha, visibility, groups and masks all came through.
/// </summary>
[TestFixture]
internal sealed class PsdFormatTest : DocumentHarness
{
	// Rounding in Photoshop's own compositing puts a pixel off by up to 2 in places.
	private const int Tolerance = 2;

	[TestCase ("psd-hidden-groups", Description = "Groups flattened; a hidden group hides its children")]
	[TestCase ("psd-masks3", Description = "A group's mask applies to the layers inside it")]
	[TestCase ("psd-mask-density-layermask", Description = "Layer masks with partial density")]
	[TestCase ("psd-opacity-fill", Description = "Fill opacity")]
	[TestCase ("psd-transparentbg-gimp", Description = "Written by GIMP rather than Photoshop")]
	[TestCase ("psd-slices", Description = "No layers: the merged image is the picture")]
	public void FlatteningTheImportReproducesPhotoshopsRender (string name)
	{
		Document imported = Import (name + ".psd");
		using ImageSurface flattened = imported.GetFlattenedImage ();
		using ImageSurface expected = Utilities.LoadImage (Utilities.GetAssetPath (name + ".expected.png"));

		Assert.That ((flattened.Width, flattened.Height), Is.EqualTo ((expected.Width, expected.Height)));

		ReadOnlySpan<ColorBgra> actualPixels = flattened.GetReadOnlyPixelData ();
		ReadOnlySpan<ColorBgra> expectedPixels = expected.GetReadOnlyPixelData ();
		for (int i = 0; i < expectedPixels.Length; i++) {
			ColorBgra a = actualPixels[i];
			ColorBgra e = expectedPixels[i];
			int difference = new[] { a.B - e.B, a.G - e.G, a.R - e.R, a.A - e.A }.Max (Math.Abs);
			if (difference > Tolerance)
				Assert.Fail ($"Pixel ({i % expected.Width}, {i / expected.Width}) is {a}, Photoshop rendered {e}");
		}
	}

	[Test]
	public void GroupsFlattenIntoTheirLayersWithTheGroupsVisibility ()
	{
		// Photoshop's panel, top to bottom: Group 2 > Shape 2, Group 1 (hidden) > Shape 1, Background.
		Document imported = Import ("psd-hidden-groups.psd");

		Assert.That (
			imported.Layers.UserLayers.Select (l => (l.Name, l.Hidden)),
			Is.EqualTo (new[] { ("Background", false), ("Shape 1", true), ("Shape 2", false) }));
	}

	[Test]
	public void UnicodeLayerNamesSurviveIncludingCharactersOutsideTheBasicPlane ()
	{
		Document imported = Import ("psd-layer-name-emoji.psd");

		Assert.That (imported.Layers.UserLayers.Single ().Name, Is.EqualTo ("👽"));
	}

	[Test]
	public void AGroupMaskBecomesAnEditableMaskOnItsLayers ()
	{
		Document imported = Import ("psd-masks3.psd");

		UserLayer insideGroup = imported.Layers.UserLayers.Single (l => l.Name == "Rounded Rectangle 1");
		Assert.That (insideGroup.Mask, Is.Not.Null);
		Assert.That (insideGroup.Mask!.Hidden, Is.False);
		Assert.That (imported.Layers.UserLayers.Single (l => l.Name == "Background").Mask, Is.Null);
	}

	[TestCase (16, 3, Description = "16 bits per channel")]
	[TestCase (8, 4, Description = "CMYK")]
	public void UnsupportedDocumentsAreRefusedWithAReason (int depth, int colorMode)
	{
		byte[] header = [
			.. "8BPS"u8, 0, 1, 0, 0, 0, 0, 0, 0,
			0, 3, // channels
			0, 0, 0, 4, 0, 0, 0, 4, // 4x4
			0, (byte) depth,
			0, (byte) colorMode,
		];

		Assert.That (() => PsdFormat.Import (header, null), Throws.TypeOf<NotSupportedException> ());
	}

	[Test]
	public void ATruncatedFileIsRejectedAsCorrupt ()
	{
		byte[] whole = File.ReadAllBytes (Utilities.GetAssetPath ("psd-hidden-groups.psd"));

		Assert.That (() => PsdFormat.Import (whole[..(whole.Length / 2)], null), Throws.InstanceOf<InvalidDataException> ());
	}

	[Test]
	public void OpenFindsThePsdImporterByExtension ()
	{
		FormatDescriptor? format = PintaCore.ImageFormats.GetFormatByFile ("drawing.psd");

		Assert.That (format?.Importer, Is.InstanceOf<PsdFormat> ());
		Assert.That (format!.IsExportAvailable (), Is.False);
	}

	private static Document Import (string fileName)
		=> PsdFormat.Import (File.ReadAllBytes (Utilities.GetAssetPath (fileName)), null);
}
