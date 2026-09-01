using System;
using System.IO;
using System.Text;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// PdnFormatLayerGeometryTest exercises ValidateLayerGeometry and SafeLayerListCapacity in
/// isolation, which proves those two functions are individually correct but not that ImportCore
/// actually calls them with the right arguments in the right order. This drives the real entry
/// point (ImportCore) end to end against a hand-built byte stream, so a wiring mistake - a swapped
/// argument, a skipped call - would fail here even though every isolated unit test still passes.
/// <para>
/// There is no NRBF *writer* in the BCL - System.Formats.Nrbf is a decode-only "safe" replacement
/// for BinaryFormatter, by design, so PdnBytes hand-encodes the minimal MS-NRBF byte layout
/// PdnFormat's reader actually walks (see its own field-by-field comments) rather than going through
/// a serializer. No real Paint.NET file is shipped as a fixture; the bytes below are self-contained.
/// </para>
/// </summary>
internal sealed class PdnFormatRoundTripTest : DocumentHarness
{
	[Test]
	public void SingleLayerDocumentRoundTripsExactPixels ()
	{
		// 2x2, BGRA32, stride 8: red, green / blue, white - four distinct colors so a channel or
		// row/column transposition bug in CopyPixels would show up as a mismatch, not a coincidence.
		byte[] pixels = [
			0, 0, 255, 255, 0, 255, 0, 255, // row 0: red, green
			255, 0, 0, 255, 255, 255, 255, 255, // row 1: blue, white
		];
		byte[] bytes = PdnBytes.BuildSingleLayer (
			docWidth: 2, docHeight: 2,
			layerWidth: 2, layerHeight: 2, stride: 8,
			pixelDataBgra: pixels,
			layerName: "Background", visible: true);

		Document doc = new PdnFormat ().ImportCore (new MemoryStream (bytes));

		Assert.Multiple (() => {
			Assert.That (doc.ImageSize.Width, Is.EqualTo (2));
			Assert.That (doc.ImageSize.Height, Is.EqualTo (2));
			Assert.That (doc.Layers.UserLayers, Has.Count.EqualTo (1));
		});

		UserLayer layer = doc.Layers[0];
		Assert.Multiple (() => {
			Assert.That (layer.Name, Is.EqualTo ("Background"));
			Assert.That (layer.Hidden, Is.False);
		});

		ColorBgra[] data = layer.Surface.GetPixelData ().ToArray ();
		Assert.Multiple (() => {
			Assert.That (data[0], Is.EqualTo (ColorBgra.FromBgra (0, 0, 255, 255)), "top-left: red");
			Assert.That (data[1], Is.EqualTo (ColorBgra.FromBgra (0, 255, 0, 255)), "top-right: green");
			Assert.That (data[2], Is.EqualTo (ColorBgra.FromBgra (255, 0, 0, 255)), "bottom-left: blue");
			Assert.That (data[3], Is.EqualTo (ColorBgra.FromBgra (255, 255, 255, 255)), "bottom-right: white");
		});
	}

	// The isolated ValidateLayerGeometry tests (PdnFormatLayerGeometryTest) prove the check itself
	// is correct; this proves ReadLayerInfos actually threads a mismatched scan0 length64 into it
	// through the real decode path, rather than the huge claim reaching ReadLayerPixelData's
	// `new byte[length]` unchecked. No large allocation is attempted: ValidateLayerGeometry throws
	// before any buffer is sized.
	[Test]
	public void MismatchedScanZeroLengthIsRejectedBeforeAnyAllocation ()
	{
		byte[] pixels = new byte[16];
		byte[] bytes = PdnBytes.BuildSingleLayer (
			docWidth: 2, docHeight: 2,
			layerWidth: 2, layerHeight: 2, stride: 8,
			pixelDataBgra: pixels,
			layerName: "Background", visible: true,
			length64Override: 5_000_000_000L);

		Assert.Throws<InvalidDataException> (() => new PdnFormat ().ImportCore (new MemoryStream (bytes)));
	}

	// Same wiring question as above, for the layer-vs-document dimension check.
	[Test]
	public void LayerDimensionsMismatchingTheDocumentAreRejectedThroughTheRealImportPath ()
	{
		byte[] pixels = new byte[16];
		byte[] bytes = PdnBytes.BuildSingleLayer (
			docWidth: 5, docHeight: 5,
			layerWidth: 2, layerHeight: 2, stride: 8,
			pixelDataBgra: pixels,
			layerName: "Background", visible: true);

		Assert.Throws<InvalidDataException> (() => new PdnFormat ().ImportCore (new MemoryStream (bytes)));
	}

	/// <summary>
	/// Hand-encodes exactly the PDN3 byte layout PdnFormat.ImportCore reads: the "PDN3" header, an
	/// MS-NRBF payload built from SystemClassWithMembersAndTypes/ArraySingleObject/BinaryObjectString
	/// records with no BinaryLibrary reference (the decoder does not resolve class names to real
	/// types, so an arbitrary name is fine), and a single raw (uncompressed) pixel chunk.
	/// </summary>
	private static class PdnBytes
	{
		private const byte SerializedStreamHeader = 0;
		private const byte SystemClassWithMembersAndTypes = 4;
		private const byte BinaryObjectString = 6;
		private const byte ArraySingleObject = 16;
		private const byte MessageEnd = 11;

		private const byte BinaryTypePrimitive = 0;
		private const byte BinaryTypeString = 1;
		private const byte BinaryTypeObject = 2;

		private const byte PrimitiveBoolean = 1;
		private const byte PrimitiveInt32 = 8;
		private const byte PrimitiveInt64 = 9;

		public static byte[] BuildSingleLayer (
			int docWidth,
			int docHeight,
			int layerWidth,
			int layerHeight,
			int stride,
			byte[] pixelDataBgra,
			string layerName,
			bool visible,
			long? length64Override = null)
		{
			long length64 = length64Override ?? pixelDataBgra.Length;

			using MemoryStream ms = new ();
			using (BinaryWriter w = new (ms, Encoding.UTF8, leaveOpen: true)) {
				int nextId = 1;

				void WriteClassStart (string className, string[] memberNames)
				{
					w.Write (SystemClassWithMembersAndTypes);
					w.Write (nextId++);
					w.Write (className);
					w.Write (memberNames.Length);
					foreach (string name in memberNames)
						w.Write (name);
				}

				void WriteMemberTypes (params (byte type, byte prim)[] members)
				{
					foreach (var m in members) w.Write (m.type);
					foreach (var m in members)
						if (m.type == BinaryTypePrimitive) w.Write (m.prim);
				}

				void WriteStringRecord (string value)
				{
					w.Write (BinaryObjectString);
					w.Write (nextId++);
					w.Write (value);
				}

				// --- PDN3 header: magic, 3-byte header size (0 - no XML needed), then the 0x00 0x01
				// marker ReadHeader checks for. ---
				w.Write (Encoding.ASCII.GetBytes ("PDN3"));
				w.Write ((byte) 0); w.Write ((byte) 0); w.Write ((byte) 0);
				w.Write ((byte) 0x00); w.Write ((byte) 0x01);

				// --- NRBF payload ---
				w.Write (SerializedStreamHeader);
				w.Write (1);  // RootId - the document class below takes id 1
				w.Write (-1); // HeaderId
				w.Write (1);  // MajorVersion
				w.Write (0);  // MinorVersion

				WriteClassStart ("Document", ["width", "height", "layers"]);
				WriteMemberTypes ((BinaryTypePrimitive, PrimitiveInt32), (BinaryTypePrimitive, PrimitiveInt32), (BinaryTypeObject, 0));
				w.Write (docWidth);
				w.Write (docHeight);

				WriteClassStart ("ArrayList", ["ArrayList+_size", "ArrayList+_items"]);
				WriteMemberTypes ((BinaryTypePrimitive, PrimitiveInt32), (BinaryTypeObject, 0));
				w.Write (1); // _size

				w.Write (ArraySingleObject);
				w.Write (nextId++);
				w.Write (1); // one element

				WriteClassStart ("BitmapLayer", ["surface", "Layer+width", "Layer+height", "Layer+properties"]);
				WriteMemberTypes (
					(BinaryTypeObject, 0),
					(BinaryTypePrimitive, PrimitiveInt32),
					(BinaryTypePrimitive, PrimitiveInt32),
					(BinaryTypeObject, 0));

				WriteClassStart ("Surface", ["scan0", "stride"]);
				WriteMemberTypes ((BinaryTypeObject, 0), (BinaryTypePrimitive, PrimitiveInt32));

				WriteClassStart ("MemoryBlock", ["length64"]);
				WriteMemberTypes ((BinaryTypePrimitive, PrimitiveInt64));
				w.Write (length64);

				w.Write (stride); // Surface.stride

				w.Write (layerWidth);  // Layer+width
				w.Write (layerHeight); // Layer+height

				WriteClassStart ("LayerProperties", ["name", "visible"]);
				WriteMemberTypes ((BinaryTypeString, 0), (BinaryTypePrimitive, PrimitiveBoolean));
				WriteStringRecord (layerName);
				w.Write (visible);

				w.Write (MessageEnd);

				// --- Pixel data: formatVersion=1 (raw), one chunk covering the whole buffer. ---
				w.Write ((byte) 1);
				WriteUInt32BigEndian (w, (uint) pixelDataBgra.Length); // chunkSize
				WriteUInt32BigEndian (w, 0); // chunkNumber
				WriteUInt32BigEndian (w, (uint) pixelDataBgra.Length); // dataSize
				w.Write (pixelDataBgra);
			}

			return ms.ToArray ();
		}

		private static void WriteUInt32BigEndian (BinaryWriter w, uint value)
		{
			byte[] bytes = BitConverter.GetBytes (value);
			if (BitConverter.IsLittleEndian) Array.Reverse (bytes);
			w.Write (bytes);
		}
	}
}
