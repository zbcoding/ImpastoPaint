// PdnFormat.cs
// Implements import of PDN v3 files (Paint.NET's native format).
// Based on open specifications:
// - pypdn (MIT) https://github.com/addisonelliott/pypdn – NRBF parsing and chunked gzip storage
// - OpenPDN MemoryBlock.cs (MIT) – chunk format documentation (formatVersion, chunkSize, chunkNumber, dataSize)
// File format:
//  - "PDN3" magic
//  - 3-byte little-endian header size + 0 byte
//  - UTF-8 header XML (contains thumbnail, not needed for raster)
//  - 0x00 0x01 marker
//  - NRBF payload (MS-NRBF) containing PaintDotNet.Document with LayerList and MemoryBlock deferred placeholders
//  - For each layer: 1-byte formatVersion (0=gzip, 1=raw), BE uint32 chunkSize, then chunkCount * (BE uint32 chunkNumber, BE uint32 dataSize, data)
//  - Layer pixel data is BGRA (32bpp) or BGR (24bpp) with stride = width * bpp/8, decompressed and laid out row-major.
// This importer uses System.Formats.Nrbf (safe decoder) to avoid BinaryFormatter.

using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;
using System.IO.Compression;
using System.Text;
using Cairo;

namespace Pinta.Core;

public sealed class PdnFormat : IImageImporter
{
	public Document Import (Gio.File file)
	{
		// Read entire Gio file into MemoryStream for random access
		using GioStream gioStream = new (file.Read (cancellable: null));
		using MemoryStream msFull = new ();
		gioStream.CopyTo (msFull);
		msFull.Position = 0;
		return ImportCore (msFull, file);
	}

	// Internal core that works with any seekable stream – useful for tests without Gio
	public Document ImportCore (Stream inputStream, Gio.File? file = null)
	{
		MemoryStream msFull = EnsureSeekableCopy (inputStream);

		using (BinaryReader reader = new (msFull, Encoding.UTF8, leaveOpen: true))
			ReadHeader (reader);

		(ClassRecord docRecord, long afterNrbfPos) = DecodeNrbf (msFull);

		int docWidth = docRecord.GetInt32 ("width");
		int docHeight = docRecord.GetInt32 ("height");

		if (docWidth <= 0 || docHeight <= 0 || docWidth > 20000 || docHeight > 20000)
			throw new InvalidDataException ($"Invalid PDN dimensions {docWidth}x{docHeight}");

		List<LayerInfo> layerInfos = ReadLayerInfos (docRecord);

		msFull.Position = afterNrbfPos;
		List<byte[]> layerPixelDatas = ReadAllLayerPixelData (msFull, layerInfos);

		return BuildDocument (docWidth, docHeight, file, layerInfos, layerPixelDatas);
	}

	private static MemoryStream EnsureSeekableCopy (Stream inputStream)
	{
		if (inputStream is MemoryStream mem && mem.CanSeek) {
			mem.Position = 0;
			return mem;
		}

		MemoryStream copy = new ();
		inputStream.CopyTo (copy);
		copy.Position = 0;
		return copy;
	}

	private static void ReadHeader (BinaryReader reader)
	{
		byte[] magic = reader.ReadBytes (4);
		if (magic.Length != 4 || Encoding.ASCII.GetString (magic) != "PDN3")
			throw new InvalidDataException ("Invalid PDN file magic");

		byte[] hdrSize3 = reader.ReadBytes (3);
		if (hdrSize3.Length != 3)
			throw new InvalidDataException ("Truncated PDN header size");
		byte[] hdrSize4 = new byte[4];
		Array.Copy (hdrSize3, hdrSize4, 3);
		hdrSize4[3] = 0;
		int headerSize = BitConverter.ToInt32 (hdrSize4, 0);
		if (headerSize < 0 || headerSize > 20_000_000)
			throw new InvalidDataException ("Invalid PDN header size");

		byte[] headerBytes = reader.ReadBytes (headerSize);
		if (headerBytes.Length != headerSize)
			throw new InvalidDataException ("Truncated PDN header XML");

		byte[] marker = reader.ReadBytes (2);
		if (marker.Length != 2 || marker[0] != 0x00 || marker[1] != 0x01)
			throw new InvalidDataException ("Invalid PDN marker after header");
	}

	private static (ClassRecord DocRecord, long AfterNrbfPosition) DecodeNrbf (MemoryStream msFull)
	{
		long nrbfStartPos = msFull.Position;

		// Extract remaining bytes (NRBF + chunk data) into a separate array
		// to avoid issues with GetBuffer() on non-expandable streams and to keep msFull intact
		byte[] fullArray = msFull.ToArray ();
		int remLen = fullArray.Length - (int) nrbfStartPos;
		byte[] remBytes = new byte[remLen];
		Array.Copy (fullArray, nrbfStartPos, remBytes, 0, remLen);

		SerializationRecord rootRecord;
		long afterNrbfPos;
		using (MemoryStream nrbfSlice = new (remBytes, writable: false)) {
			rootRecord = NrbfDecoder.Decode (nrbfSlice, leaveOpen: true);
			afterNrbfPos = nrbfStartPos + nrbfSlice.Position;
		}

		ClassRecord docRecord = rootRecord as ClassRecord
			?? throw new InvalidDataException ("PDN root is not a class record");

		return (docRecord, afterNrbfPos);
	}

	private static List<LayerInfo> ReadLayerInfos (ClassRecord docRecord)
	{
		ClassRecord layersRec = docRecord.GetSerializationRecord ("layers") as ClassRecord
			?? throw new InvalidDataException ("Missing layers");
		int layersSize = layersRec.GetInt32 ("ArrayList+_size");

		ArrayRecord itemsRec = layersRec.GetSerializationRecord ("ArrayList+_items") as ArrayRecord
			?? throw new InvalidDataException ("Missing layer items array");

		SerializationRecord[] arr = itemsRec.GetArray (typeof (object[]), allowNulls: true) as SerializationRecord[]
			?? throw new InvalidDataException ("Failed to get layer array");

		List<LayerInfo> layerInfos = new (layersSize);

		for (int i = 0; i < layersSize; i++) {
			if (i >= arr.Length) break;
			ClassRecord? bm = arr[i] as ClassRecord;
			if (bm == null) continue;

			ClassRecord surfRec = bm.GetSerializationRecord ("surface") as ClassRecord
				?? throw new InvalidDataException ($"Missing surface for layer {i}");
			ClassRecord scan0Rec = surfRec.GetSerializationRecord ("scan0") as ClassRecord
				?? throw new InvalidDataException ($"Missing scan0 for layer {i}");

			long length;
			try {
				length = scan0Rec.GetInt64 ("length64");
			} catch {
				length = scan0Rec.GetInt32 ("length");
			}
			int stride = surfRec.GetInt32 ("stride");
			int layerWidth = bm.GetInt32 ("Layer+width");
			int layerHeight = bm.GetInt32 ("Layer+height");

			ValidateLayerGeometry (layerWidth, layerHeight, stride, i);

			ClassRecord lpRec = bm.GetSerializationRecord ("Layer+properties") as ClassRecord
				?? throw new InvalidDataException ($"Missing Layer+properties for layer {i}");

			string name = lpRec.GetString ("name") ?? $"Layer {i}";
			bool visible = lpRec.GetBoolean ("visible");
			bool isBackground = false;
			try { isBackground = lpRec.GetBoolean ("isBackground"); } catch { }

			byte opacity = 255;
			try { opacity = lpRec.GetByte ("opacity"); } catch { }

			BlendMode blendMode = BlendMode.Normal;

			// Try blendMode enum (LayerBlendMode) first – new files
			try {
				SerializationRecord? blendModeRec = lpRec.GetSerializationRecord ("blendMode");
				if (blendModeRec is ClassRecord bmRec2) {
					int v = bmRec2.GetInt32 ("value__");
					blendMode = BlendTypeToBlendMode (v);
				}
			} catch {
				// Fallback to old blendOp class name
				try {
					ClassRecord propsRec = bm.GetSerializationRecord ("properties") as ClassRecord
						?? throw new Exception ();
					ClassRecord blendOpRec = propsRec.GetSerializationRecord ("blendOp") as ClassRecord
						?? throw new Exception ();
					string fullName = blendOpRec.TypeName.FullName;
					blendMode = BlendOpNameToBlendMode (fullName);
				} catch { }
			}

			layerInfos.Add (new LayerInfo {
				Name = name,
				Visible = visible,
				IsBackground = isBackground,
				Opacity = opacity,
				BlendMode = blendMode,
				Width = layerWidth,
				Height = layerHeight,
				Stride = stride,
				Length = length
			});
		}

		return layerInfos;
	}

	// Unlike docWidth/docHeight, these come from the per-layer NRBF record with no
	// framework-level bounds check. CopyPixels divides by width to recover bpp, so a crafted 0
	// throws DivideByZeroException instead of the InvalidDataException every other malformed
	// field in this importer produces, and a bogus stride can overflow the bpp calculation into
	// a value that still passes the 24/32 check there.
	internal static void ValidateLayerGeometry (int width, int height, int stride, int layerIndex)
	{
		if (width <= 0 || height <= 0 || width > 20000 || height > 20000)
			throw new InvalidDataException ($"Invalid PDN layer dimensions {width}x{height} for layer {layerIndex}");
		if (stride <= 0 || stride > 20000 * 4)
			throw new InvalidDataException ($"Invalid PDN layer stride {stride} for layer {layerIndex}");
	}

	private static List<byte[]> ReadAllLayerPixelData (MemoryStream msFull, List<LayerInfo> layerInfos)
	{
		List<byte[]> layerPixelDatas = new (layerInfos.Count);
		foreach (LayerInfo info in layerInfos)
			layerPixelDatas.Add (ReadLayerPixelData (msFull, info));
		return layerPixelDatas;
	}

	private static byte[] ReadLayerPixelData (Stream stream, LayerInfo info)
	{
		int fmt = stream.ReadByte ();
		if (fmt == -1)
			throw new EndOfStreamException ("Unexpected EOF reading formatVersion");

		uint chunkSize = ReadUInt32BigEndian (stream);
		if (chunkSize == 0 || chunkSize > 10_000_000)
			throw new InvalidDataException ($"Invalid chunkSize {chunkSize}");

		long length = info.Length;
		uint chunkCount = (uint) ((length + chunkSize - 1) / chunkSize);
		byte[] data = new byte[length];

		for (uint c = 0; c < chunkCount; c++)
			ReadChunkInto (stream, data, chunkSize, chunkCount, length, gzipCompressed: fmt == 0);

		return data;
	}

	private static uint ReadUInt32BigEndian (Stream stream)
	{
		byte[] bytes = new byte[4];
		if (stream.Read (bytes, 0, 4) != 4)
			throw new EndOfStreamException ();
		if (BitConverter.IsLittleEndian) Array.Reverse (bytes);
		return BitConverter.ToUInt32 (bytes, 0);
	}

	private static void ReadChunkInto (Stream stream, byte[] data, uint chunkSize, uint chunkCount, long length, bool gzipCompressed)
	{
		uint chunkNumber = ReadUInt32BigEndian (stream);
		uint dataSize = ReadUInt32BigEndian (stream);

		if (chunkNumber >= chunkCount)
			throw new InvalidDataException ($"Chunk number {chunkNumber} out of bounds {chunkCount}");
		if (dataSize > 20_000_000)
			throw new InvalidDataException ($"Invalid dataSize {dataSize}");

		byte[] raw = new byte[dataSize];
		int readTotal = 0;
		while (readTotal < dataSize) {
			int r = stream.Read (raw, readTotal, (int) dataSize - readTotal);
			if (r == 0) throw new EndOfStreamException ();
			readTotal += r;
		}

		uint actualChunkSize = Math.Min (chunkSize, (uint) (length - (long) chunkNumber * chunkSize));
		long offset = (long) chunkNumber * chunkSize;

		if (gzipCompressed) {
			using MemoryStream comp = new (raw, writable: false);
			using GZipStream gzip = new (comp, CompressionMode.Decompress);
			int off = 0;
			while (off < actualChunkSize) {
				int toRead = (int) actualChunkSize - off;
				int got = gzip.Read (data, (int) (offset + off), toRead);
				if (got == 0) break;
				off += got;
			}
			if (off != actualChunkSize)
				throw new InvalidDataException ($"Decompressed size mismatch {off} vs {actualChunkSize}");
		} else {
			Array.Copy (raw, 0, data, offset, actualChunkSize);
		}
	}

	private static Document BuildDocument (
		int docWidth,
		int docHeight,
		Gio.File? file,
		List<LayerInfo> layerInfos,
		List<byte[]> layerPixelDatas)
	{
		Document newDoc = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (docWidth, docHeight),
			file,
			"pdn");

		// PDN stores bottom to top – insert in same order
		for (int i = 0; i < layerInfos.Count; i++) {
			LayerInfo info = layerInfos[i];

			UserLayer layer = newDoc.Layers.CreateLayer (info.Name);
			layer.Opacity = info.Opacity / 255.0;
			layer.Hidden = !info.Visible;
			layer.BlendMode = info.BlendMode;

			CopyPixels (info, layerPixelDatas[i], layer.Surface.GetPixelData ());

			layer.Surface.MarkDirty ();
			newDoc.Layers.Insert (layer, i);
		}

		return newDoc;
	}

	private static void CopyPixels (LayerInfo info, byte[] pixelData, Span<ColorBgra> dest)
	{
		int bpp = info.Stride * 8 / info.Width;

		if (bpp == 32) {
			// Fast path when stride == width*4 – bulk copy via bytes is okay because BGRA layout matches
			// But we still do row-aware copy to handle potential stride padding
			for (int y = 0; y < info.Height; y++) {
				int srcRow = y * info.Stride;
				int dstRow = y * info.Width;
				for (int x = 0; x < info.Width; x++) {
					int srcOff = srcRow + x * 4;
					byte b = pixelData[srcOff];
					byte g = pixelData[srcOff + 1];
					byte r = pixelData[srcOff + 2];
					byte a = pixelData[srcOff + 3];
					dest[dstRow + x] = ColorBgra.FromBgra (b, g, r, a);
				}
			}
		} else if (bpp == 24) {
			for (int y = 0; y < info.Height; y++) {
				int srcRow = y * info.Stride;
				int dstRow = y * info.Width;
				for (int x = 0; x < info.Width; x++) {
					int srcOff = srcRow + x * 3;
					byte b = pixelData[srcOff];
					byte g = pixelData[srcOff + 1];
					byte r = pixelData[srcOff + 2];
					dest[dstRow + x] = ColorBgra.FromBgra (b, g, r, 255);
				}
			}
		} else {
			throw new InvalidDataException ($"Unsupported bpp {bpp}");
		}
	}

	private sealed class LayerInfo
	{
		public required string Name { get; init; }
		public required bool Visible { get; init; }
		public required bool IsBackground { get; init; }
		public required byte Opacity { get; init; }
		public required BlendMode BlendMode { get; init; }
		public required int Width { get; init; }
		public required int Height { get; init; }
		public required int Stride { get; init; }
		public required long Length { get; init; }
	}

	// BlendType as defined in pypdn / Paint.NET UserBlendOps
	private static BlendMode BlendTypeToBlendMode (int type)
		=> type switch {
			0 => BlendMode.Normal,        // Normal
			1 => BlendMode.Multiply,      // Multiply
			3 => BlendMode.ColorBurn,     // ColorBurn
			4 => BlendMode.ColorDodge,    // ColorDodge
			7 => BlendMode.Overlay,       // Overlay
			8 => BlendMode.Difference,    // Difference
			10 => BlendMode.Lighten,      // Lighten
			11 => BlendMode.Darken,       // Darken
			12 => BlendMode.Screen,       // Screen
			13 => BlendMode.Xor,          // XOR
			9 => BlendMode.Difference,    // Negation -> Difference
			2 => BlendMode.HardLight,     // Additive approx
			5 => BlendMode.HardLight,     // Reflect approx
			6 => BlendMode.SoftLight,     // Glow approx
			_ => BlendMode.Normal
		};

	private static BlendMode BlendOpNameToBlendMode (string fullName)
	{
		if (fullName.Contains ("Multiply", StringComparison.OrdinalIgnoreCase)) return BlendMode.Multiply;
		if (fullName.Contains ("ColorBurn", StringComparison.OrdinalIgnoreCase)) return BlendMode.ColorBurn;
		if (fullName.Contains ("ColorDodge", StringComparison.OrdinalIgnoreCase)) return BlendMode.ColorDodge;
		if (fullName.Contains ("Overlay", StringComparison.OrdinalIgnoreCase)) return BlendMode.Overlay;
		if (fullName.Contains ("Difference", StringComparison.OrdinalIgnoreCase)) return BlendMode.Difference;
		if (fullName.Contains ("Lighten", StringComparison.OrdinalIgnoreCase)) return BlendMode.Lighten;
		if (fullName.Contains ("Darken", StringComparison.OrdinalIgnoreCase)) return BlendMode.Darken;
		if (fullName.Contains ("Screen", StringComparison.OrdinalIgnoreCase)) return BlendMode.Screen;
		if (fullName.Contains ("Xor", StringComparison.OrdinalIgnoreCase)) return BlendMode.Xor;
		if (fullName.Contains ("Negation", StringComparison.OrdinalIgnoreCase)) return BlendMode.Difference;
		if (fullName.Contains ("Additive", StringComparison.OrdinalIgnoreCase)) return BlendMode.HardLight;
		if (fullName.Contains ("Reflect", StringComparison.OrdinalIgnoreCase)) return BlendMode.HardLight;
		if (fullName.Contains ("Glow", StringComparison.OrdinalIgnoreCase)) return BlendMode.SoftLight;
		return BlendMode.Normal;
	}
}
