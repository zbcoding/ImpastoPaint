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
		// Ensure seekable
		MemoryStream msFull;
		if (inputStream is MemoryStream mem && mem.CanSeek) {
			msFull = mem;
			msFull.Position = 0;
		} else {
			msFull = new MemoryStream ();
			inputStream.CopyTo (msFull);
			msFull.Position = 0;
		}

		using BinaryReader reader = new (msFull, Encoding.UTF8, leaveOpen: true);

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

		long nrbfStartPos = msFull.Position;
		SerializationRecord rootRecord;
		long afterNrbfPos;

		// Extract remaining bytes (NRBF + chunk data) into a separate array
		// to avoid issues with GetBuffer() on non-expandable streams and to keep msFull intact
		byte[] fullArray = msFull.ToArray ();
		int remLen = fullArray.Length - (int) nrbfStartPos;
		byte[] remBytes = new byte[remLen];
		Array.Copy (fullArray, nrbfStartPos, remBytes, 0, remLen);

		using (MemoryStream nrbfSlice = new (remBytes, writable: false)) {
			rootRecord = NrbfDecoder.Decode (nrbfSlice, leaveOpen: true);
			afterNrbfPos = nrbfStartPos + nrbfSlice.Position;
		}

		ClassRecord docRecord = rootRecord as ClassRecord
			?? throw new InvalidDataException ("PDN root is not a class record");

		int docWidth = docRecord.GetInt32 ("width");
		int docHeight = docRecord.GetInt32 ("height");

		if (docWidth <= 0 || docHeight <= 0 || docWidth > 20000 || docHeight > 20000)
			throw new InvalidDataException ($"Invalid PDN dimensions {docWidth}x{docHeight}");

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

		// Now read chunked pixel data
		msFull.Position = afterNrbfPos;
		List<byte[]> layerPixelDatas = new (layerInfos.Count);

		foreach (LayerInfo info in layerInfos) {
			int fmt = msFull.ReadByte ();
			if (fmt == -1)
				throw new EndOfStreamException ("Unexpected EOF reading formatVersion");

			byte[] chunkSizeBytes = new byte[4];
			if (msFull.Read (chunkSizeBytes, 0, 4) != 4)
				throw new EndOfStreamException ();
			if (BitConverter.IsLittleEndian) Array.Reverse (chunkSizeBytes);
			uint chunkSize = BitConverter.ToUInt32 (chunkSizeBytes, 0);
			if (chunkSize == 0 || chunkSize > 10_000_000)
				throw new InvalidDataException ($"Invalid chunkSize {chunkSize}");

			long length = info.Length;
			uint chunkCount = (uint) ((length + chunkSize - 1) / chunkSize);
			byte[] data = new byte[length];

			for (uint c = 0; c < chunkCount; c++) {
				byte[] cnBytes = new byte[4];
				if (msFull.Read (cnBytes, 0, 4) != 4) throw new EndOfStreamException ();
				if (BitConverter.IsLittleEndian) Array.Reverse (cnBytes);
				uint chunkNumber = BitConverter.ToUInt32 (cnBytes, 0);

				byte[] dsBytes = new byte[4];
				if (msFull.Read (dsBytes, 0, 4) != 4) throw new EndOfStreamException ();
				if (BitConverter.IsLittleEndian) Array.Reverse (dsBytes);
				uint dataSize = BitConverter.ToUInt32 (dsBytes, 0);

				if (chunkNumber >= chunkCount)
					throw new InvalidDataException ($"Chunk number {chunkNumber} out of bounds {chunkCount}");
				if (dataSize > 20_000_000)
					throw new InvalidDataException ($"Invalid dataSize {dataSize}");

				byte[] raw = new byte[dataSize];
				int readTotal = 0;
				while (readTotal < dataSize) {
					int r = msFull.Read (raw, readTotal, (int) dataSize - readTotal);
					if (r == 0) throw new EndOfStreamException ();
					readTotal += r;
				}

				uint actualChunkSize = Math.Min (chunkSize, (uint) (length - (long) chunkNumber * chunkSize));
				long offset = (long) chunkNumber * chunkSize;

				if (fmt == 0) {
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

			layerPixelDatas.Add (data);
		}

		// Create Pinta document
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
			byte[] pixelData = layerPixelDatas[i];

			UserLayer layer = newDoc.Layers.CreateLayer (info.Name);
			layer.Opacity = info.Opacity / 255.0;
			layer.Hidden = !info.Visible;
			layer.BlendMode = info.BlendMode;

			Span<ColorBgra> dest = layer.Surface.GetPixelData ();
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

			layer.Surface.MarkDirty ();
			newDoc.Layers.Insert (layer, i);
		}

		return newDoc;
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
