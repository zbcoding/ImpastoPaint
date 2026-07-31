//
// OraFormat.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva <sikon@ubuntu.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using Cairo;
using GdkPixbuf;

namespace Pinta.Core;

public sealed class OraFormat : IImageImporter, IImageExporter
{
	private const int THUMB_MAX_SIZE = 256;
	// Sidecar entry that keeps text objects editable when round-tripping .ora files.
	// Standard OpenRaster readers ignore it; Impasto uses it to restore text layers.
	private const string TEXT_ENTRY_PATH = "data/impasto-text.xml";

	public Document Import (Gio.File file)
	{
		using GioStream stream = new (file.Read (cancellable: null));
		using ZipArchive zipfile = new (stream);

		XmlDocument stackXml = new ();

		ZipArchiveEntry stackXmlEntry = zipfile.GetEntry ("stack.xml") ?? throw new XmlException ("No 'stack.xml' found in OpenRaster file");
		stackXml.Load (stackXmlEntry.Open ());

		// NRT - This makes a lot of assumptions that the file will be perfectly
		// valid that we need to guard against.
		XmlElement imageElement = stackXml.DocumentElement!;

		Size imageSize = new (
			Width: int.Parse (imageElement.GetAttribute ("w")),
			Height: int.Parse (imageElement.GetAttribute ("h"))
		);

		Document newDocument = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			imageSize,
			file,
			"ora");

		XmlElement stackElement = (XmlElement) stackXml.GetElementsByTagName ("stack")[0]!;
		XmlNodeList layerElements = stackElement.GetElementsByTagName ("layer");

		if (layerElements.Count == 0)
			throw new XmlException ("No layers found in OpenRaster file");

		for (int i = 0; i < layerElements.Count; i++) {

			XmlElement layerElement = (XmlElement) layerElements[i]!;

			PointI position = new (
				X: int.Parse (GetAttribute (layerElement, "x", "0")),
				Y: int.Parse (GetAttribute (layerElement, "y", "0"))
			);

			string name = GetAttribute (layerElement, "name", $"Layer {i}");

			try {
				// Write the file to a temporary file first
				// Fixes a bug when running on .Net
				ZipArchiveEntry? zf = zipfile.GetEntry (layerElement.GetAttribute ("src")) ?? throw new XmlException ("Missing layer in OpenRaster file");
				using Stream s = zf.Open ();
				string tmp_file = System.IO.Path.GetTempFileName ();

				using (Stream stream_out = File.Open (tmp_file, FileMode.OpenOrCreate)) {
					byte[] buffer = new byte[2048];
					while (true) {
						int len = s.Read (buffer, 0, buffer.Length);

						if (len > 0)
							stream_out.Write (buffer, 0, len);
						else
							break;
					}
				}

				UserLayer layer = newDocument.Layers.CreateLayer (name);
				newDocument.Layers.Insert (layer, 0);

				string visibility = GetAttribute (layerElement, "visibility", "visible");
				if (visibility == "hidden") {
					layer.Hidden = true;
				}

				layer.Opacity = double.Parse (GetAttribute (layerElement, "opacity", "1"), GetFormat ());
				layer.BlendMode = StandardToBlendMode (GetAttribute (layerElement, "composite-op", "svg:src-over"));

				using Pixbuf pb = Pixbuf.NewFromFile (tmp_file)!; // NRT: only nullable when an error is thrown
				using Context g = new (layer.Surface);
				g.DrawPixbuf (pb, (PointD) position);

				try {
					File.Delete (tmp_file);
				} catch { }
			} catch {
				// Translators: {0} is the name of a layer, and {1} is the path to a .ora file.
				string details = Translations.GetString ("Could not import layer \"{0}\" from {1}", name, zipfile);
				PintaCore.Chrome.ShowMessageDialog (PintaCore.Chrome.MainWindow, Translations.GetString ("Error"), details);
			}
		}

		// Restore any editable text objects saved in the sidecar entry.
		LoadTextEntry (zipfile, newDocument);

		return newDocument;
	}

	private static CultureInfo GetFormat ()
		=> CultureInfo.CreateSpecificCulture ("en");

	private static string GetAttribute (XmlElement element, string attribute, string defValue)
	{
		string ret = element.GetAttribute (attribute);
		return string.IsNullOrEmpty (ret) ? defValue : ret;
	}

	private static Size GetThumbDimensions (int width, int height)
	{
		if (width <= THUMB_MAX_SIZE && height <= THUMB_MAX_SIZE)
			return new (width, height);
		else if (width > height)
			return new (THUMB_MAX_SIZE, (int) ((double) height / width * THUMB_MAX_SIZE));
		else
			return new ((int) ((double) width / height * THUMB_MAX_SIZE), THUMB_MAX_SIZE);
	}

	private static byte[] GetLayerXmlData (IReadOnlyList<UserLayer> layers)
	{
		using MemoryStream ms = new ();
		using XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) {
			Formatting = Formatting.Indented,
		};

		writer.WriteStartElement ("image");
		writer.WriteAttributeString ("w", layers[0].Surface.Width.ToString ());
		writer.WriteAttributeString ("h", layers[0].Surface.Height.ToString ());
		writer.WriteAttributeString ("version", "0.0.5"); // Current version of the spec.

		writer.WriteStartElement ("stack");

		// ORA stores layers top to bottom
		for (int i = layers.Count - 1; i >= 0; i--) {
			var layer = layers[i];
			writer.WriteStartElement ("layer");
			writer.WriteAttributeString ("opacity", string.Format (GetFormat (), "{0:0.00}", layer.Opacity));
			writer.WriteAttributeString ("name", layer.Name);
			writer.WriteAttributeString ("composite-op", BlendModeToStandard (layer.BlendMode));
			writer.WriteAttributeString ("src", "data/layer" + i.ToString () + ".png");

			if (layer.Hidden)
				writer.WriteAttributeString ("visibility", "hidden");

			writer.WriteEndElement ();
		}

		writer.WriteEndElement (); // stack
		writer.WriteEndElement (); // image

		writer.Close ();

		return ms.ToArray ();
	}

	public void Export (Document document, Gio.File file, Gtk.Window parent)
	{
		using GioStream file_stream = new (file.Replace ());
		using ZipArchive archive = new (file_stream, ZipArchiveMode.Create);
		using Pixbuf flattened = document.GetFlattenedImage ().ToPixbuf ();
		AddMimeEntry (archive);
		AddLayerEntries (archive, document);
		AddStackEntry (archive, document);
		AddTextEntry (archive, document);
		AddMergedImage (archive, flattened);
		AddThumbnail (archive, flattened);
	}

	private static void AddMimeEntry (ZipArchive archive)
	{
		byte[] mimeBytes = System.Text.Encoding.ASCII.GetBytes ("image/openraster");
		ZipArchiveEntry mimeEntry = archive.CreateEntry ("mimetype", CompressionLevel.NoCompression);
		using Stream mimeStream = mimeEntry.Open ();
		mimeStream.Write (mimeBytes, 0, mimeBytes.Length);
	}

	private static void AddLayerEntries (ZipArchive archive, Document document)
	{
		for (int i = 0; i < document.Layers.UserLayers.Count; i++) {
			using Pixbuf pb = document.Layers.UserLayers[i].Surface.ToPixbuf ();
			byte[] buf = pb.SaveToBuffer ("png");
			ZipArchiveEntry layerEntry = archive.CreateEntry ($"data/layer{i}.png");
			using Stream layerStream = layerEntry.Open ();
			layerStream.Write (buf, 0, buf.Length);
		}
	}

	private static void AddStackEntry (ZipArchive archive, Document document)
	{
		byte[] userLayerBytes = GetLayerXmlData (document.Layers.UserLayers);
		ZipArchiveEntry stackEntry = archive.CreateEntry ("stack.xml");
		using Stream stackStream = stackEntry.Open ();
		stackStream.Write (userLayerBytes, 0, userLayerBytes.Length);
	}

	private static void AddTextEntry (ZipArchive archive, Document document)
	{
		IReadOnlyList<UserLayer> layers = document.Layers.UserLayers;
		if (!layers.Any (layer => layer.TextObjects.Count > 0))
			return;

		using MemoryStream ms = new ();
		using (XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) {
			Formatting = Formatting.Indented,
		}) {
			writer.WriteStartElement ("impasto-text");
			writer.WriteAttributeString ("version", "1");

			for (int i = 0; i < layers.Count; i++) {
				UserLayer layer = layers[i];
				if (layer.TextObjects.Count == 0)
					continue;

				writer.WriteStartElement ("layer");
				writer.WriteAttributeString ("index", i.ToString ());

				foreach (TextObject obj in layer.TextObjects) {
					writer.WriteStartElement ("text");
					WriteTextObject (writer, obj);
					writer.WriteEndElement ();
				}

				writer.WriteEndElement (); // layer
			}

			writer.WriteEndElement (); // impasto-text
		}

		ms.Position = 0;
		ZipArchiveEntry textEntry = archive.CreateEntry (TEXT_ENTRY_PATH);
		using Stream textStream = textEntry.Open ();
		ms.CopyTo (textStream);
	}

	private static void WriteTextObject (XmlTextWriter writer, TextObject obj)
	{
		TextEngine engine = obj.Engine;

		writer.WriteAttributeString ("origin-x", engine.Origin.X.ToString ());
		writer.WriteAttributeString ("origin-y", engine.Origin.Y.ToString ());
		writer.WriteAttributeString ("bounds-x", obj.TextBounds.X.ToString ());
		writer.WriteAttributeString ("bounds-y", obj.TextBounds.Y.ToString ());
		writer.WriteAttributeString ("bounds-w", obj.TextBounds.Width.ToString ());
		writer.WriteAttributeString ("bounds-h", obj.TextBounds.Height.ToString ());
		writer.WriteAttributeString ("font", engine.Font.ToString ());
		writer.WriteAttributeString ("color", engine.PrimaryColor.ToHex (addAlpha: true));
		writer.WriteAttributeString ("color2", engine.SecondaryColor.ToHex (addAlpha: true));
		writer.WriteAttributeString ("align", engine.Alignment.ToString ());
		writer.WriteAttributeString ("underline", engine.Underline ? "1" : "0");
		writer.WriteAttributeString ("fill", "0");
		writer.WriteAttributeString ("outline", "2");
		writer.WriteAttributeString ("join", "0");

		foreach (string line in engine.Lines)
			writer.WriteElementString ("line", line);
	}
	private static void LoadTextEntry (ZipArchive zipfile, Document document)
	{
		ZipArchiveEntry? entry = zipfile.GetEntry (TEXT_ENTRY_PATH);
		if (entry is null)
			return;

		XmlDocument xml = new ();
		xml.Load (entry.Open ());

		XmlElement root = xml.DocumentElement!;

		foreach (XmlElement layerElement in root.GetElementsByTagName ("layer")) {
			if (!int.TryParse (GetAttribute (layerElement, "index", "-1"), out int index) ||
				index < 0 || index >= document.Layers.UserLayers.Count)
				continue;

			UserLayer layer = document.Layers.UserLayers[index];

			int fillStyle = 0;
			int outlineWidth = 2;
			Cairo.LineJoin lineJoin = Cairo.LineJoin.Miter;

			foreach (XmlElement textElement in layerElement.GetElementsByTagName ("text")) {
				(TextObject? obj, int fill, int outline, Cairo.LineJoin join) = ReadTextObject (textElement);
				if (obj is null)
					continue;

				layer.TextObjects.Add (obj);

				//Use the style of the first restored object to re-render the layer.
				if (layer.TextObjects.Count == 1) {
					fillStyle = fill;
					outlineWidth = outline;
					lineJoin = join;
				}
			}

			//Render the restored objects so they are visible before the text tool ever activates.
			if (layer.TextObjects.Count > 0) {
				TextObjectRenderer.RenderAll (
					layer.TextLayer.Layer.Surface,
					layer.TextObjects,
					PintaCore.Chrome,
					antialias: true,
					fillStyle: fillStyle,
					outlineWidth: outlineWidth,
					lineJoin: lineJoin);
			}
		}
	}

	private static (TextObject? obj, int fill, int outline, Cairo.LineJoin join) ReadTextObject (XmlElement element)
	{
		try {
			List<string> lines = [];
			foreach (XmlElement lineElement in element.GetElementsByTagName ("line"))
				lines.Add (lineElement.InnerText);
			if (lines.Count == 0)
				lines.Add (string.Empty);

			string alignName = GetAttribute (element, "align", "Left");
			TextAlignment alignment = Enum.TryParse (alignName, out TextAlignment parsed)
				? parsed
				: TextAlignment.Left;

			bool underline = GetAttribute (element, "underline", "0") == "1";

			string fontName = GetAttribute (element, "font", "");
			if (string.IsNullOrEmpty (fontName))
				fontName = "Sans 12";

			TextEngine engine = new (lines) {
				Origin = new PointI (
					int.Parse (GetAttribute (element, "origin-x", "0")),
					int.Parse (GetAttribute (element, "origin-y", "0"))),
				PrimaryColor = Color.FromHex (GetAttribute (element, "color", "#000000ff")) ?? Color.Black,
				SecondaryColor = Color.FromHex (GetAttribute (element, "color2", "#ffffffff")) ?? Color.White,
			};

			engine.SetFont (Pango.FontDescription.FromString (fontName), alignment, underline);

			RectangleI bounds = new (
				int.Parse (GetAttribute (element, "bounds-x", "0")),
				int.Parse (GetAttribute (element, "bounds-y", "0")),
				int.Parse (GetAttribute (element, "bounds-w", "0")),
				int.Parse (GetAttribute (element, "bounds-h", "0")));

			TextObject obj = new (engine) {
				TextBounds = bounds,
				PreviousTextBounds = bounds
			};

			int fill = int.Parse (GetAttribute (element, "fill", "0"));
			int outline = int.Parse (GetAttribute (element, "outline", "2"));
			Cairo.LineJoin join = (Cairo.LineJoin) int.Parse (GetAttribute (element, "join", "0"));

			return (obj, fill, outline, join);
		} catch {
			// A malformed text entry shouldn't prevent the layer from loading.
			return (null, 0, 2, Cairo.LineJoin.Miter);
		}
	}

	private static void AddMergedImage (ZipArchive archive, Pixbuf flattened)
	{
		byte[] mergedImageBytes = flattened.SaveToBuffer ("png");
		ZipArchiveEntry mergedImageEntry = archive.CreateEntry ("mergedimage.png");
		using Stream mergedImageStream = mergedImageEntry.Open ();
		mergedImageStream.Write (mergedImageBytes, 0, mergedImageBytes.Length);
	}

	private static void AddThumbnail (ZipArchive archive, Pixbuf flattened)
	{
		Size newSize = GetThumbDimensions (flattened.Width, flattened.Height);
		using Pixbuf thumb = flattened.ScaleSimple (newSize.Width, newSize.Height, InterpType.Bilinear)!; // Creates new Pixbuf
		byte[] thumbnailBytes = thumb.SaveToBuffer ("png");
		ZipArchiveEntry thumbnailEntry = archive.CreateEntry ("Thumbnails/thumbnail.png");
		using Stream thumbnailStream = thumbnailEntry.Open ();
		thumbnailStream.Write (thumbnailBytes, 0, thumbnailBytes.Length);
	}

	private static string BlendModeToStandard (BlendMode mode)
		=> mode switch {
			BlendMode.Multiply => "svg:multiply",
			BlendMode.ColorBurn => "svg:color-burn",
			BlendMode.ColorDodge => "svg:color-dodge",
			BlendMode.Overlay => "svg:overlay",
			BlendMode.Difference => "svg:difference",
			BlendMode.Lighten => "svg:lighten",
			BlendMode.Darken => "svg:darken",
			BlendMode.Screen => "svg:screen",
			BlendMode.Xor => "svg:xor",
			BlendMode.HardLight => "svg:hard-light",
			BlendMode.SoftLight => "svg:soft-light",
			BlendMode.Color => "svg:color",
			BlendMode.Luminosity => "svg:luminosity",
			BlendMode.Hue => "svg:hue",
			BlendMode.Saturation => "svg:saturation",
			_ => "svg:src-over",
		};

	private static BlendMode StandardToBlendMode (string mode)
	{
		switch (mode) {
			case "svg:src-over":
				return BlendMode.Normal;
			case "svg:multiply":
				return BlendMode.Multiply;
			case "svg:color-burn":
				return BlendMode.ColorBurn;
			case "svg:color-dodge":
				return BlendMode.ColorDodge;
			case "svg:overlay":
				return BlendMode.Overlay;
			case "svg:difference":
				return BlendMode.Difference;
			case "svg:lighten":
				return BlendMode.Lighten;
			case "svg:darken":
				return BlendMode.Darken;
			case "svg:screen":
				return BlendMode.Screen;
			case "svg:xor":
				return BlendMode.Xor;
			case "svg:hard-light":
				return BlendMode.HardLight;
			case "svg:soft-light":
				return BlendMode.SoftLight;
			case "svg:color":
				return BlendMode.Color;
			case "svg:luminosity":
				return BlendMode.Luminosity;
			case "svg:hue":
				return BlendMode.Hue;
			case "svg:saturation":
				return BlendMode.Saturation;
			default:
				Console.WriteLine ("Unrecognized composite-op: {0}, using Normal.", mode);
				return BlendMode.Normal;
		}
	}
}
