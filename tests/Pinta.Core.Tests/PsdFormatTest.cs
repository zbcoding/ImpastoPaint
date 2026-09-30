using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

	[Test]
	public void ATruncatedFileIsRejectedAsCorrupt ()
	{
		byte[] whole = File.ReadAllBytes (Utilities.GetAssetPath ("psd-hidden-groups.psd"));

		Assert.That (() => PsdFormat.Import (whole[..(whole.Length / 2)], null), Throws.InstanceOf<InvalidDataException> ());
	}

	[TestCase (false, Description = "A layer's channel")]
	[TestCase (true, Description = "The merged image of a file without layers")]
	public void ATinyFileClaimingAHugeImageIsRejectedBeforeItIsAllocated (bool composite)
	{
		// A 30000x30000 canvas whose channels hold a few bytes, where 900 MB would be needed.
		byte[] psd = composite
			? Psd (30000, 30000, LayerSection ([]), [0, 0, 0, 0, 0, 0])
			: Psd (30000, 30000, LayerSection (LayerRecord (30000, 30000, [(0, [0, 1, 0, 0])], mask: [])), []);

		long before = GC.GetAllocatedBytesForCurrentThread ();
		Assert.That (() => PsdFormat.Import (psd, null), Throws.InstanceOf<InvalidDataException> ());
		Assert.That (GC.GetAllocatedBytesForCurrentThread () - before, Is.LessThan (10_000_000));
	}

	[Test]
	public void ATinyFileListingMoreLayersThanMemoryHoldsIsRejectedBeforeAnyIsCreated ()
	{
		// Layers with no channels cost a few bytes each in the file but a full canvas surface each
		// once imported: 150 at 4000x4000 need 9.6 GB.
		const int count = 150;
		byte[] layer = LayerRecord (4000, 4000, [], mask: [])[2..]; // Without its layer count.
		byte[] layers = [0, count, .. Enumerable.Repeat (layer, count).SelectMany (bytes => bytes)];
		byte[] psd = Psd (4000, 4000, LayerSection (layers), []);

		Assert.That (() => PsdFormat.Import (psd, null), Throws.InstanceOf<InvalidDataException> ());
	}

	[Test]
	public void ZipChannelDataThatEndsEarlyIsRejectedAsCorrupt ()
	{
		// A whole deflate stream, but only one of the 2x2 channel's two rows.
		using MemoryStream compressed = new ();
		using (ZLibStream zlib = new (compressed, CompressionLevel.Optimal, leaveOpen: true))
			zlib.Write ([255, 255]);
		byte[] channel = [0, 2, .. compressed.ToArray ()]; // Compression 2: ZIP.
		byte[] psd = Psd (2, 2, LayerSection (LayerRecord (2, 2, [(0, channel)], mask: [])), []);

		Assert.That (() => PsdFormat.Import (psd, null), Throws.InstanceOf<InvalidDataException> (),
			"the importer's callers treat InvalidDataException as a corrupt file");
	}

	[Test]
	public void MaskDensityIsReadPastTheRealUserMaskOfALayerWithAVectorMask ()
	{
		// With a vector mask (channel -3) too, the real user mask's flags, background and rect
		// (18 bytes) sit between the mask flags and the parameters carrying the density.
		byte[] mask = [
			.. Int32s (0, 0, 1, 1), 0, 0x10, // Rect, default color, flags: parameters follow.
			0, 0, .. Int32s (0, 0, 1, 1), // Real flags, real background, real rect.
			0x01, 128, // Parameters: user mask density follows; density.
			0, 0, // Padding.
		];
		byte[] psd = Psd (1, 1, LayerSection (LayerRecord (1, 1, [(0, [0, 0, 255]), (-2, [0, 0, 0]), (-3, [0, 0, 0])], mask)), []);

		UserLayer layer = PsdFormat.Import (psd, null).Layers.UserLayers.Single ();

		// A fully hiding stored mask at half density reveals about half.
		Assert.That (layer.Mask!.Surface.GetColorBgra (PointI.Zero).A, Is.EqualTo (127));
	}

	[TestCase (255, 0, Description = "Hide All: the whole layer is hidden")]
	[TestCase (128, 127, Description = "At half density, about half shows")]
	public void AMaskWithAnEmptyRectIsItsDefaultColorEverywhere (int density, int expectedAlpha)
	{
		byte[] mask = [
			.. Int32s (0, 0, 0, 0), 0, 0x10, // Empty rect, default color black, flags: parameters follow.
			0x01, (byte) density, // Parameters: user mask density follows; density.
		];
		byte[] psd = Psd (2, 2, LayerSection (LayerRecord (2, 2, [(0, [0, 0, 255, 255, 255, 255]), (-2, [0, 0])], mask)), []);

		UserLayer layer = PsdFormat.Import (psd, null).Layers.UserLayers.Single ();

		Assert.That (layer.Mask, Is.Not.Null, "the mask has no pixels stored, but it still hides");
		Assert.That (layer.Mask!.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (expectedAlpha));
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

	// --- Minimal grayscale PSD writer for crafted files ------------------------------------------

	private static byte[] Psd (int width, int height, byte[] layerSection, byte[] composite)
		=> [
			.. "8BPS"u8, 0, 1, 0, 0, 0, 0, 0, 0, // Signature, version 1, reserved.
			0, 1, .. Int32s (height, width), 0, 8, 0, 1, // One channel, size, 8 bits, grayscale.
			.. Int32s (0, 0), // No color mode data or image resources.
			.. Int32s (layerSection.Length), .. layerSection,
			.. composite,
		];

	private static byte[] LayerSection (byte[] records)
		=> records.Length == 0 ? [] : [.. Int32s (records.Length), .. records];

	/// <summary>Layer info holding one layer at 0,0 and its channel data.</summary>
	private static byte[] LayerRecord (int width, int height, (short Id, byte[] Data)[] channels, byte[] mask)
	{
		List<byte> record = [0, 1, .. Int32s (0, 0, height, width), 0, (byte) channels.Length];
		foreach ((short id, byte[] data) in channels)
			record.AddRange ([(byte) (id >> 8), (byte) id, .. Int32s (data.Length)]);
		record.AddRange ([.. "8BIMnorm"u8, 255, 0, 0, 0]);
		byte[] extra = [.. Int32s (mask.Length), .. mask, .. Int32s (0), 0, 0, 0, 0]; // Mask, no blending ranges, empty name.
		record.AddRange ([.. Int32s (extra.Length), .. extra]);
		foreach ((_, byte[] data) in channels)
			record.AddRange (data);
		return [.. record];
	}

	private static byte[] Int32s (params int[] values)
	{
		byte[] bytes = new byte[values.Length * 4];
		for (int i = 0; i < values.Length; i++)
			BinaryPrimitives.WriteInt32BigEndian (bytes.AsSpan (i * 4), values[i]);
		return bytes;
	}
}
