// PsdFormat.cs
// Imports Photoshop documents (PSD, version 1) with 8 bits per channel, in RGB or grayscale.
// Written from Adobe's published file format specification, and checked against the reference
// readers in ImageMagick (coders/psd.c) and psd-tools for the quirks real files carry.
//
// Layers keep their name, position, opacity, fill opacity, visibility, blend mode and pixel mask.
// Groups are flattened: their layers take on a hidden group's visibility, and the group's
// opacity and mask. A file without layers opens as its merged composite image.
//
// Limits: blend modes Impasto lacks import as Normal, and clipping masks, vector masks,
// adjustment/fill layers and layer effects import as their stored pixels only. 16/32-bit,
// CMYK/Lab/indexed and PSB files are rejected. Each needs a matching Impasto feature first.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Pinta.Core;

public sealed class PsdFormat : IImageImporter
{
	private const int MaxDocumentSide = 30000;
	private const int MaxLayerSide = 300000;
	private const int MaxChannels = 56;

	private const short TransparencyChannel = -1;
	private const short UserMaskChannel = -2;

	public Document Import (Gio.File file)
	{
		using GioStream stream = new (file.Read (cancellable: null));
		using MemoryStream bytes = new ();
		stream.CopyTo (bytes);
		return Import (bytes.ToArray (), file);
	}

	/// <summary>Decodes a whole PSD file already in memory. <paramref name="file"/> may be null in tests.</summary>
	internal static Document Import (byte[] data, Gio.File? file)
	{
		PsdReader reader = new (data);
		Header header = ReadHeader (reader);
		reader.SkipBlock (); // Color mode data
		reader.SkipBlock (); // Image resources

		LayerSection section = ReadLayerSection (reader, header);

		Document document = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (header.Width, header.Height),
			file,
			"psd");

		try {
			if (section.Layers.Count > 0)
				AddLayers (document, header, data, section.Layers);
			else
				AddComposite (document, header, reader, section.CompositeHasAlpha);
		} catch {
			document.Close ();
			throw;
		}

		return document;
	}

	// --- Header -----------------------------------------------------------------------------

	private readonly record struct Header (int Width, int Height, int Channels, bool IsGray)
	{
		public int ColorChannels => IsGray ? 1 : 3;
	}

	private static Header ReadHeader (PsdReader reader)
	{
		if (reader.ReadAscii (4) != "8BPS")
			throw new InvalidDataException ("Not a Photoshop document");

		int version = reader.ReadUInt16 ();
		if (version == 2)
			throw new NotSupportedException ("Large Photoshop documents (PSB) are not supported");
		if (version != 1)
			throw new InvalidDataException ($"Unknown Photoshop document version {version}");

		reader.Skip (6);
		int channels = reader.ReadUInt16 ();
		uint height = reader.ReadUInt32 ();
		uint width = reader.ReadUInt32 ();
		int depth = reader.ReadUInt16 ();
		int colorMode = reader.ReadUInt16 ();

		if (channels < 1 || channels > MaxChannels)
			throw new InvalidDataException ($"Invalid channel count {channels}");
		if (width < 1 || height < 1 || width > MaxDocumentSide || height > MaxDocumentSide)
			throw new InvalidDataException ($"Invalid image size {width}x{height}");
		if (depth != 8)
			throw new NotSupportedException ($"Photoshop documents with {depth} bits per channel are not supported");

		bool isGray = colorMode switch {
			1 => true,
			3 => false,
			_ => throw new NotSupportedException ($"Photoshop color mode {colorMode} is not supported; only RGB and grayscale are"),
		};

		Header header = new ((int) width, (int) height, channels, isGray);
		if (channels < header.ColorChannels)
			throw new InvalidDataException ($"Too few channels ({channels}) for the color mode");

		return header;
	}

	// --- Layer and mask information section -------------------------------------------------

	private sealed record LayerSection (List<LayerRecord> Layers, bool CompositeHasAlpha);

	private static LayerSection ReadLayerSection (PsdReader reader, Header header)
	{
		int sectionLength = reader.ReadLength ();
		int sectionEnd = reader.Position + sectionLength;
		if (sectionLength == 0)
			return new ([], false);

		int layerInfoLength = reader.ReadLength (sectionEnd);
		int layerInfoEnd = reader.Position + layerInfoLength;

		LayerInfo info = layerInfoLength > 0
			? ReadLayerInfo (reader, header, layerInfoEnd)
			: new ([], false);

		// Writers disagree on padding after the layer info, and a few put the layers in a tagged
		// block instead, so the trailing blocks are walked from where the layer info claims to end
		// and the section's own length is what moves the reader on.
		reader.Seek (layerInfoEnd);
		bool mergedTransparency = info.CompositeHasAlpha;
		if (reader.Position + 4 <= sectionEnd) {
			// A length that runs past the section means the layer info end was misreported
			// (seen from some writers); the trailing blocks are then unreadable but unneeded.
			uint globalMaskLength = reader.ReadUInt32 ();
			reader.Skip ((int) Math.Min (globalMaskLength, (uint) (sectionEnd - reader.Position)));
			foreach (TaggedBlock block in ReadTaggedBlocks (reader, sectionEnd)) {
				if (block.Key == "Mtrn")
					mergedTransparency = true;
				else if (block.Key == "Layr" && info.Layers.Count == 0) {
					reader.Seek (block.Start);
					info = ReadLayerInfo (reader, header, block.End);
				}
			}
		}

		reader.Seek (sectionEnd);
		return new (info.Layers, mergedTransparency);
	}

	private sealed record LayerInfo (List<LayerRecord> Layers, bool CompositeHasAlpha);

	private static LayerInfo ReadLayerInfo (PsdReader reader, Header header, int end)
	{
		// A negative count means the composite's first alpha channel is its merged transparency.
		short signedCount = reader.ReadInt16 ();
		int count = Math.Abs ((int) signedCount);

		List<LayerRecord> records = new (count);
		for (int i = 0; i < count; i++)
			records.Add (ReadLayerRecord (reader));

		// Channel image data follows every record, in record order.
		foreach (LayerRecord record in records)
			ReadChannelData (reader, header, record, end);

		return new (ResolveGroups (records), signedCount < 0);
	}

	// --- Layer records ------------------------------------------------------------------------

	private sealed class LayerRecord
	{
		public required Bounds Rect { get; init; }
		public required ChannelInfo[] Channels { get; init; }
		public required string BlendKey { get; init; }
		public required byte Opacity { get; init; }
		public required bool Hidden { get; init; }
		public required string Name { get; set; }
		public required MaskInfo? Mask { get; init; }
		public SectionType Section { get; set; }

		/// <summary>"Fill" opacity: scales the layer's own pixels but not its effects. With effects not imported, it is plain opacity.</summary>
		public byte FillOpacity { get; set; } = 255;

		// Set by ResolveGroups: the layer's own visibility and opacity with its groups' folded in,
		// and the groups around it whose masks it inherits.
		public bool EffectiveHidden { get; set; }
		public double EffectiveOpacity { get; set; }
		public List<LayerRecord> EnclosingGroups { get; set; } = [];

		// Where each channel Impasto uses sits in the file, filled in by ReadChannelData. Decoding
		// waits until the layer is built, so only one layer's channels are in memory at a time.
		public List<ChannelData> ChannelData { get; } = [];
	}

	private readonly record struct ChannelInfo (short Id, uint Length);

	private readonly record struct ChannelData (short Id, Bounds Rect, int Start, int End);

	/// <param name="Density">How strongly the mask hides: 255 fully, 0 not at all.</param>
	private readonly record struct MaskInfo (Bounds Rect, byte DefaultColor, bool Disabled, byte Density);

	private enum SectionType
	{
		Layer = 0,
		OpenFolder = 1,
		ClosedFolder = 2,
		FolderEnd = 3,
	}

	private static LayerRecord ReadLayerRecord (PsdReader reader)
	{
		Bounds rect = ReadBounds (reader);

		int channelCount = reader.ReadUInt16 ();
		if (channelCount > MaxChannels)
			throw new InvalidDataException ($"Invalid layer channel count {channelCount}");

		ChannelInfo[] channels = new ChannelInfo[channelCount];
		for (int i = 0; i < channelCount; i++)
			channels[i] = new (reader.ReadInt16 (), reader.ReadUInt32 ());

		if (reader.ReadAscii (4) != "8BIM")
			throw new InvalidDataException ("Invalid layer blend mode signature");

		string blendKey = reader.ReadAscii (4);
		byte opacity = reader.ReadByte ();
		reader.Skip (1); // Clipping
		byte flags = reader.ReadByte ();
		reader.Skip (1); // Filler

		int extraLength = reader.ReadLength ();
		int extraEnd = reader.Position + extraLength;

		MaskInfo? mask = ReadMaskInfo (reader, extraEnd);
		reader.Skip (reader.ReadLength (extraEnd)); // Blending ranges
		string name = ReadPascalName (reader);

		LayerRecord record = new () {
			Rect = rect,
			Channels = channels,
			BlendKey = blendKey,
			Opacity = opacity,
			Hidden = (flags & 0x02) != 0, // Bit 1 is set when the layer is hidden.
			Name = name,
			Mask = mask,
		};

		foreach (TaggedBlock block in ReadTaggedBlocks (reader, extraEnd))
			ReadLayerBlock (reader, block, record);

		reader.Seek (extraEnd);
		return record;
	}

	private static Bounds ReadBounds (PsdReader reader)
	{
		int top = reader.ReadInt32 ();
		int left = reader.ReadInt32 ();
		int bottom = reader.ReadInt32 ();
		int right = reader.ReadInt32 ();

		// Photoshop writes inverted rects (bottom above top) for masks and layers with no pixels.
		if (right < left || bottom < top)
			return new (left, top, left, top);

		if ((long) right - left > MaxLayerSide || (long) bottom - top > MaxLayerSide)
			throw new InvalidDataException ($"Invalid layer bounds {left},{top} to {right},{bottom}");

		return new (left, top, right, bottom);
	}

	private static MaskInfo? ReadMaskInfo (PsdReader reader, int end)
	{
		int length = reader.ReadLength (end);
		int maskEnd = reader.Position + length;

		// Every non-empty variant (20, 36, or longer with mask parameters) starts with these 18 bytes.
		MaskInfo? mask = null;
		if (length >= 18) {
			Bounds rect = ReadBounds (reader);
			byte defaultColor = reader.ReadByte ();
			byte flags = reader.ReadByte ();

			// Bit 4: a parameters byte follows, whose bit 0 says the user mask density comes first.
			byte density = 255;
			if ((flags & 0x10) != 0 && maskEnd - reader.Position >= 2) {
				byte parameters = reader.ReadByte ();
				if ((parameters & 0x01) != 0)
					density = reader.ReadByte ();
			}

			mask = new (rect, defaultColor, Disabled: (flags & 0x02) != 0, density);
		}

		reader.Seek (maskEnd);
		return mask;
	}

	private static string ReadPascalName (PsdReader reader)
	{
		int length = reader.ReadByte ();
		string name = Encoding.Latin1.GetString (reader.ReadSpan (length));

		// The length byte and the name together are padded to a multiple of 4.
		int padded = (length + 1 + 3) & ~3;
		reader.Skip (padded - (length + 1));
		return name;
	}

	private static void ReadLayerBlock (PsdReader reader, TaggedBlock block, LayerRecord record)
	{
		reader.Seek (block.Start);
		switch (block.Key) {
			case "luni": {
					// UTF-16BE code units; the count may or may not include a trailing NUL.
					uint units = reader.ReadUInt32 ();
					if (units * 2L <= block.End - reader.Position)
						record.Name = Encoding.BigEndianUnicode.GetString (reader.ReadSpan ((int) units * 2)).TrimEnd ('\0');
					break;
				}
			case "lsct":
			case "lsdk":
				if (block.End - block.Start >= 4)
					record.Section = (SectionType) reader.ReadUInt32 ();
				break;
			case "iOpa":
				if (block.End > block.Start)
					record.FillOpacity = reader.ReadByte ();
				break;
		}
	}

	private readonly record struct TaggedBlock (string Key, int Start, int End);

	/// <summary>
	/// Walks "8BIM"/"8B64" tagged blocks up to <paramref name="end"/>, stopping at the first one that
	/// does not fit. Each block's data is [Start, End); the reader is left at the end of the last.
	/// </summary>
	private static IEnumerable<TaggedBlock> ReadTaggedBlocks (PsdReader reader, int end)
	{
		while (end - reader.Position >= 12) {
			string signature = reader.ReadAscii (4);
			if (signature != "8BIM" && signature != "8B64")
				yield break;

			string key = reader.ReadAscii (4);
			uint length = reader.ReadUInt32 ();
			if (length > end - reader.Position)
				yield break;

			int start = reader.Position;
			int blockEnd = start + (int) length;
			yield return new (key, start, blockEnd);
			reader.Seek (blockEnd);
		}
	}

	// --- Groups -----------------------------------------------------------------------------

	/// <summary>
	/// Drops group markers and folds each group's visibility and opacity into its children.
	/// Records run bottom to top: a group's end marker comes first, then its children, then the
	/// folder record carrying the group's own properties. So walk top to bottom.
	/// </summary>
	private static List<LayerRecord> ResolveGroups (List<LayerRecord> records)
	{
		Stack<(bool Hidden, double Opacity)> enclosingGroups = new ();
		List<LayerRecord> openGroups = [];
		bool hidden = false;
		double opacity = 1;
		List<LayerRecord> layers = [];

		for (int i = records.Count - 1; i >= 0; i--) {
			LayerRecord record = records[i];
			switch (record.Section) {
				case SectionType.OpenFolder:
				case SectionType.ClosedFolder:
					enclosingGroups.Push ((hidden, opacity));
					openGroups.Add (record);
					hidden |= record.Hidden;
					opacity *= record.Opacity / 255.0;
					break;
				case SectionType.FolderEnd:
					if (enclosingGroups.Count > 0) {
						(hidden, opacity) = enclosingGroups.Pop ();
						openGroups.RemoveAt (openGroups.Count - 1);
					}
					break;
				default:
					record.EffectiveHidden = hidden || record.Hidden;
					record.EffectiveOpacity = opacity * record.Opacity / 255.0 * record.FillOpacity / 255.0;
					record.EnclosingGroups = [.. openGroups];
					layers.Add (record);
					break;
			}
		}

		layers.Reverse (); // Back to bottom to top.
		return layers;
	}

	// --- Channel image data -----------------------------------------------------------------

	private static void ReadChannelData (PsdReader reader, Header header, LayerRecord record, int end)
	{
		foreach (ChannelInfo channel in record.Channels) {
			if (channel.Length < 2 || channel.Length > end - reader.Position)
				throw new InvalidDataException ($"Invalid layer channel length {channel.Length}");

			int start = reader.Position;
			int channelEnd = start + (int) channel.Length;

			if (ChannelBounds (header, record, channel.Id) is Bounds rect && !rect.IsEmpty)
				record.ChannelData.Add (new (channel.Id, rect, start, channelEnd));

			reader.Seek (channelEnd);
		}
	}

	/// <summary>Decodes a layer's channels, each kept only where it overlaps the canvas.</summary>
	private static Dictionary<short, Plane> DecodePlanes (byte[] data, Header header, LayerRecord record)
	{
		Dictionary<short, Plane> planes = [];
		foreach (ChannelData channel in record.ChannelData) {
			Plane plane = new (channel.Rect.Intersect (header.Width, header.Height));
			int compression = BinaryPrimitives.ReadUInt16BigEndian (data.AsSpan (channel.Start, 2));
			DecodeChannel (data, compression, channel.Start + 2, channel.End, channel.Rect, plane);
			planes[channel.Id] = plane;
		}
		return planes;
	}

	/// <summary>The rectangle a channel's pixels cover, or null for channels Impasto does not use.</summary>
	private static Bounds? ChannelBounds (Header header, LayerRecord record, short id)
	{
		if (id == UserMaskChannel)
			return record.Mask?.Rect;
		if (id == TransparencyChannel || (id >= 0 && id < header.ColorChannels))
			return record.Rect;
		return null; // Real user mask (-3) of a vector mask, or a spot channel.
	}

	private static void DecodeChannel (byte[] data, int compression, int start, int end, Bounds rect, Plane plane)
	{
		switch (compression) {
			case 0:
				DecodeRawRows (data, start, end, rect, plane);
				break;
			case 1:
				DecodeRleRows (data, countsStart: start, rowsStart: start + (2 * rect.Height), end, rect, plane);
				break;
			case 2:
			case 3:
				DecodeZipRows (data, start, end, rect, plane, predicted: compression == 3);
				break;
			default:
				throw new InvalidDataException ($"Unknown channel compression {compression}");
		}
	}

	private static void DecodeRawRows (byte[] data, int start, int end, Bounds rect, Plane plane)
	{
		if ((long) rect.Width * rect.Height > end - start)
			throw new InvalidDataException ("Truncated channel data");

		for (int y = 0; y < rect.Height; y++)
			plane.CopyRow (rect, y, data.AsSpan (start + (y * rect.Width), rect.Width));
	}

	/// <summary>
	/// PackBits rows: a big-endian u16 byte count per row, then the rows. Returns where the last row ends,
	/// so the composite can decode its channels back to back.
	/// </summary>
	private static int DecodeRleRows (byte[] data, int countsStart, int rowsStart, int end, Bounds rect, Plane plane)
	{
		if (rowsStart > end)
			throw new InvalidDataException ("Truncated RLE row counts");

		byte[] row = new byte[rect.Width];
		int position = rowsStart;
		for (int y = 0; y < rect.Height; y++) {
			int count = BinaryPrimitives.ReadUInt16BigEndian (data.AsSpan (countsStart + (2 * y), 2));
			if (count > end - position)
				throw new InvalidDataException ("Truncated RLE row");

			UnpackBits (data.AsSpan (position, count), row);
			plane.CopyRow (rect, y, row);
			position += count;
		}

		return position;
	}

	private static void UnpackBits (ReadOnlySpan<byte> packed, Span<byte> row)
	{
		int written = 0;
		int i = 0;
		while (i < packed.Length) {
			int header = (sbyte) packed[i++];
			if (header == -128)
				continue; // No-op.

			if (header >= 0) {
				int literal = header + 1;
				if (literal > packed.Length - i || literal > row.Length - written)
					throw new InvalidDataException ("Corrupt RLE row");
				packed.Slice (i, literal).CopyTo (row[written..]);
				i += literal;
				written += literal;
			} else {
				int repeat = 1 - header;
				if (i >= packed.Length || repeat > row.Length - written)
					throw new InvalidDataException ("Corrupt RLE row");
				row.Slice (written, repeat).Fill (packed[i++]);
				written += repeat;
			}
		}

		if (written != row.Length)
			throw new InvalidDataException ("Corrupt RLE row");
	}

	private static void DecodeZipRows (byte[] data, int start, int end, Bounds rect, Plane plane, bool predicted)
	{
		using ZLibStream zlib = new (new MemoryStream (data, start, end - start, writable: false), CompressionMode.Decompress);
		byte[] row = new byte[rect.Width];
		for (int y = 0; y < rect.Height; y++) {
			zlib.ReadExactly (row);
			if (predicted) {
				// Each byte was stored as the difference from the one to its left.
				for (int x = 1; x < row.Length; x++)
					row[x] += row[x - 1];
			}
			plane.CopyRow (rect, y, row);
		}
	}

	// --- Building the document --------------------------------------------------------------

	private static void AddLayers (Document document, Header header, byte[] data, List<LayerRecord> records)
	{
		Dictionary<LayerRecord, Plane?> groupMasks = [];
		for (int i = 0; i < records.Count; i++) {
			LayerRecord record = records[i];
			Dictionary<short, Plane> planes = DecodePlanes (data, header, record);

			UserLayer layer = document.Layers.CreateLayer (record.Name);
			layer.Opacity = record.EffectiveOpacity;
			layer.Hidden = record.EffectiveHidden;
			layer.BlendMode = ToBlendMode (record.BlendKey);

			WriteLayerPixels (layer.Surface, header, record.Rect, planes);

			// Impasto layers have one mask, so the layer's own and its groups' masks are multiplied into it.
			List<(MaskInfo Info, Plane Plane)> masks = [];
			if (record.Mask is MaskInfo own && planes.TryGetValue (UserMaskChannel, out Plane? ownPlane))
				masks.Add ((own, ownPlane));
			foreach (LayerRecord group in record.EnclosingGroups) {
				if (!groupMasks.TryGetValue (group, out Plane? groupPlane)) {
					groupPlane = DecodePlanes (data, header, group).GetValueOrDefault (UserMaskChannel);
					groupMasks[group] = groupPlane;
				}
				if (group.Mask is MaskInfo info && groupPlane is not null)
					masks.Add ((info, groupPlane));
			}

			if (masks.Count > 0) {
				// A disabled mask is kept only when there is nothing enabled, so it can be re-enabled.
				bool anyEnabled = masks.Exists (m => !m.Info.Disabled);
				LayerMask layerMask = layer.CreateMask ();
				layerMask.Hidden = !anyEnabled;
				WriteMaskPixels (layerMask.Surface, anyEnabled ? masks.FindAll (m => !m.Info.Disabled) : masks);

				// The mask applies to the layer's rendered result, so render it.
				ObjectOpacity.RefreshLayerNoInvalidate (PintaCore.Chrome, layer);
			}

			document.Layers.Insert (layer, i);
		}
	}

	private static void WriteLayerPixels (Cairo.ImageSurface surface, Header header, Bounds rect, Dictionary<short, Plane> planes)
	{
		Bounds visible = rect.Intersect (header.Width, header.Height);
		Plane? red = planes.GetValueOrDefault ((short) 0);
		Plane? green = header.IsGray ? red : planes.GetValueOrDefault ((short) 1);
		Plane? blue = header.IsGray ? red : planes.GetValueOrDefault ((short) 2);
		Plane? alpha = planes.GetValueOrDefault (TransparencyChannel);

		Span<ColorBgra> pixels = surface.GetPixelData ();
		for (int y = visible.Top; y < visible.Bottom; y++) {
			for (int x = visible.Left; x < visible.Right; x++) {
				pixels[(y * header.Width) + x] = ColorBgra.FromBgra (
					Sample (blue, x, y, 0),
					Sample (green, x, y, 0),
					Sample (red, x, y, 0),
					Sample (alpha, x, y, 255) // No transparency channel means opaque, like a background layer.
				).ToPremultipliedAlpha ();
			}
		}

		surface.MarkDirty ();
	}

	private static byte Sample (Plane? plane, int x, int y, byte missing)
		=> plane is null ? missing : plane.At (x, y);

	private static void WriteMaskPixels (Cairo.ImageSurface surface, List<(MaskInfo Info, Plane Plane)> masks)
	{
		// Each mask's default color covers everything outside its own rectangle. Only a mask
		// surface's alpha matters, so it is stored as premultiplied white.
		Span<ColorBgra> pixels = surface.GetPixelData ();
		int width = surface.Width;
		for (int y = 0; y < surface.Height; y++) {
			for (int x = 0; x < width; x++) {
				int value = 255;
				foreach ((MaskInfo info, Plane plane) in masks) {
					int stored = plane.Bounds.Contains (x, y) ? plane.At (x, y) : info.DefaultColor;
					int reveal = 255 - ((255 - stored) * info.Density / 255);
					value = value * reveal / 255;
				}
				byte b = (byte) value;
				pixels[(y * width) + x] = ColorBgra.FromBgra (b, b, b, b);
			}
		}

		surface.MarkDirty ();
	}

	/// <summary>The merged image section: planar channels, all RLE row counts first when compressed.</summary>
	private static void AddComposite (Document document, Header header, PsdReader reader, bool hasAlpha)
	{
		Bounds canvas = new (0, 0, header.Width, header.Height);
		int channels = header.ColorChannels + (hasAlpha && header.Channels > header.ColorChannels ? 1 : 0);
		int compression = reader.ReadUInt16 ();
		int start = reader.Position;
		int end = reader.Data.Length;

		Plane[] planes = new Plane[channels];
		int rowsStart = start + (2 * header.Height * header.Channels);
		for (int c = 0; c < channels; c++) {
			planes[c] = new Plane (canvas);
			switch (compression) {
				case 0:
					long planeStart = start + ((long) c * header.Width * header.Height);
					if (planeStart > end)
						throw new InvalidDataException ("Truncated composite image");
					DecodeRawRows (reader.Data, (int) planeStart, end, canvas, planes[c]);
					break;
				case 1:
					rowsStart = DecodeRleRows (reader.Data, start + (2 * header.Height * c), rowsStart, end, canvas, planes[c]);
					break;
				default:
					throw new NotSupportedException ($"Composite image compression {compression} is not supported");
			}
		}

		UserLayer layer = document.Layers.CreateLayer ();
		Span<ColorBgra> pixels = layer.Surface.GetPixelData ();
		for (int y = 0; y < header.Height; y++) {
			for (int x = 0; x < header.Width; x++) {
				byte red = planes[0].At (x, y);
				byte green = header.IsGray ? red : planes[1].At (x, y);
				byte blue = header.IsGray ? red : planes[2].At (x, y);
				byte alpha = channels > header.ColorChannels ? planes[^1].At (x, y) : (byte) 255;
				pixels[(y * header.Width) + x] = UnmatteFromWhite (blue, green, red, alpha);
			}
		}

		layer.Surface.MarkDirty ();
		document.Layers.Insert (layer, 0);
	}

	/// <summary>
	/// Photoshop stores the composite's partly transparent pixels blended over white; undo that
	/// and return the premultiplied pixel. Premultiplied, c' * a is simply c - (255 - a).
	/// </summary>
	private static ColorBgra UnmatteFromWhite (byte blue, byte green, byte red, byte alpha)
	{
		int white = 255 - alpha;
		return ColorBgra.FromBgra (
			(byte) Math.Clamp (blue - white, 0, alpha),
			(byte) Math.Clamp (green - white, 0, alpha),
			(byte) Math.Clamp (red - white, 0, alpha),
			alpha);
	}

	private static BlendMode ToBlendMode (string key)
		=> key switch {
			"mul " => BlendMode.Multiply,
			"idiv" => BlendMode.ColorBurn,
			"div " => BlendMode.ColorDodge,
			"over" => BlendMode.Overlay,
			"diff" => BlendMode.Difference,
			"lite" => BlendMode.Lighten,
			"dark" => BlendMode.Darken,
			"scrn" => BlendMode.Screen,
			"hLit" => BlendMode.HardLight,
			"sLit" => BlendMode.SoftLight,
			"colr" => BlendMode.Color,
			"lum " => BlendMode.Luminosity,
			"hue " => BlendMode.Hue,
			"sat " => BlendMode.Saturation,
			_ => BlendMode.Normal,
		};

	// --- Geometry and reading helpers -------------------------------------------------------

	/// <summary>A rectangle in document pixels, right and bottom exclusive.</summary>
	private readonly record struct Bounds (int Left, int Top, int Right, int Bottom)
	{
		public int Width => Right - Left;
		public int Height => Bottom - Top;
		public bool IsEmpty => Width <= 0 || Height <= 0;

		public bool Contains (int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

		public Bounds Intersect (int canvasWidth, int canvasHeight)
		{
			int left = Math.Clamp (Left, 0, canvasWidth);
			int top = Math.Clamp (Top, 0, canvasHeight);
			return new (left, top, Math.Clamp (Right, left, canvasWidth), Math.Clamp (Bottom, top, canvasHeight));
		}
	}

	/// <summary>
	/// One decoded channel, kept only where it overlaps the canvas, so a layer that reaches far
	/// past the edge costs no more memory than the part that shows.
	/// </summary>
	private sealed class Plane (Bounds bounds)
	{
		public Bounds Bounds { get; } = bounds;
		private readonly byte[] pixels = new byte[bounds.Width * bounds.Height];

		public byte At (int x, int y) => pixels[((y - Bounds.Top) * Bounds.Width) + (x - Bounds.Left)];

		/// <summary>Stores row <paramref name="y"/> of a channel covering <paramref name="rect"/>.</summary>
		public void CopyRow (Bounds rect, int y, ReadOnlySpan<byte> row)
		{
			int documentY = rect.Top + y;
			if (documentY < Bounds.Top || documentY >= Bounds.Bottom || Bounds.IsEmpty)
				return;

			row.Slice (Bounds.Left - rect.Left, Bounds.Width)
				.CopyTo (pixels.AsSpan ((documentY - Bounds.Top) * Bounds.Width, Bounds.Width));
		}
	}

	private sealed class PsdReader (byte[] data)
	{
		public byte[] Data { get; } = data;
		public int Position { get; private set; }

		public void Seek (int position)
		{
			if (position < 0 || position > Data.Length)
				throw new InvalidDataException ("Unexpected end of Photoshop document");
			Position = position;
		}

		public void Skip (int count) => Seek (Position + count);

		public ReadOnlySpan<byte> ReadSpan (int count)
		{
			if (count < 0 || count > Data.Length - Position)
				throw new InvalidDataException ("Unexpected end of Photoshop document");
			ReadOnlySpan<byte> span = Data.AsSpan (Position, count);
			Position += count;
			return span;
		}

		public byte ReadByte () => ReadSpan (1)[0];
		public short ReadInt16 () => BinaryPrimitives.ReadInt16BigEndian (ReadSpan (2));
		public ushort ReadUInt16 () => BinaryPrimitives.ReadUInt16BigEndian (ReadSpan (2));
		public int ReadInt32 () => BinaryPrimitives.ReadInt32BigEndian (ReadSpan (4));
		public uint ReadUInt32 () => BinaryPrimitives.ReadUInt32BigEndian (ReadSpan (4));
		public string ReadAscii (int count) => Encoding.ASCII.GetString (ReadSpan (count));

		/// <summary>Reads a u32 length that must fit before <paramref name="end"/> (default: the end of the file).</summary>
		public int ReadLength (int? end = null)
		{
			uint length = ReadUInt32 ();
			if (length > (end ?? Data.Length) - Position)
				throw new InvalidDataException ("Photoshop document section runs past its end");
			return (int) length;
		}

		/// <summary>Skips a section stored as a u32 length followed by that many bytes.</summary>
		public void SkipBlock () => Skip (ReadLength ());
	}
}
