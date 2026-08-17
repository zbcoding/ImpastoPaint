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
using ClipperLib;
using GdkPixbuf;

namespace Pinta.Core;

public sealed class OraFormat : IImageImporter, IImageExporter
{
	private const int THUMB_MAX_SIZE = 256;
	// Sidecar entry that keeps text objects editable when round-tripping .ora files.
	// Standard OpenRaster readers ignore it; Impasto uses it to restore text layers.
	private const string TEXT_ENTRY_PATH = "data/impasto-text.xml";
	private const string SHAPE_ENTRY_PATH = "data/impasto-shapes.xml";
	private const string SHAPE_IMAGE_PREFIX = "data/impasto-shapes-layer";
	private const string EFFECT_ENTRY_PATH = "data/impasto-effects.xml";
	private const string MANIFEST_ENTRY_PATH = "impasto/manifest.xml";

	// Version of Impasto's own sidecar layout, bumped when an entry's shape changes in a way an older
	// build cannot read. Independent of the OpenRaster spec version written into stack.xml.
	private const string IMPASTO_FORMAT_VERSION = "1";

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
		LoadShapeEntry (zipfile, newDocument);
		LoadEffectEntry (zipfile, newDocument);

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

		// Identifies what produced the file. Readers ignore attributes they don't know, and Impasto's
		// own sidecar entries are described in impasto/manifest.xml.
		writer.WriteAttributeString ("generator", $"Impasto {PintaCore.ApplicationVersion}");

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
		AddShapeEntry (archive, document);
		AddEffectEntry (archive, document);
		AddManifestEntry (archive, document);
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
			// The base raster, so the modifier nodes written alongside it (AddEffectEntry) apply once
			// on load rather than twice. A layer whose nodes cannot round-trip is the exception: its
			// effects are baked into the saved raster instead, and no nodes are written for it.
			UserLayer userLayer = document.Layers.UserLayers[i];
			ImageSurface saved = NodesRoundTrip (userLayer) ? userLayer.Surface : (userLayer.Composite ?? userLayer.Surface);
			using Pixbuf pb = saved.ToPixbuf ();
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

		// ToArray, not Position/CopyTo: disposing the writer above also closed the stream
		// it was wrapping, and a closed MemoryStream can still be read this way.
		byte[] textXml = ms.ToArray ();
		ZipArchiveEntry textEntry = archive.CreateEntry (TEXT_ENTRY_PATH);
		using Stream textStream = textEntry.Open ();
		textStream.Write (textXml, 0, textXml.Length);
	}

	private static void AddShapeEntry (ZipArchive archive, Document document)
	{
		IReadOnlyList<UserLayer> layers = document.Layers.UserLayers;
		if (!layers.Any (layer => layer.ShapeObjects.Count > 0))
			return;

		using MemoryStream ms = new ();
		using (XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) { Formatting = Formatting.Indented }) {
			writer.WriteStartElement ("impasto-shapes");
			writer.WriteAttributeString ("version", "1");

			for (int i = 0; i < layers.Count; i++) {
				UserLayer layer = layers[i];
				if (layer.ShapeObjects.Count == 0)
					continue;

				writer.WriteStartElement ("layer");
				writer.WriteAttributeString ("index", i.ToString ());
				foreach (ShapeObject shape in layer.ShapeObjects)
					WriteShapeObject (writer, shape);
				writer.WriteEndElement ();

				using ImageSurface shapeOverlay = layer.CreateShapeOverlay ();
				using Pixbuf overlay = shapeOverlay.ToPixbuf ();
				byte[] bytes = overlay.SaveToBuffer ("png");
				ZipArchiveEntry imageEntry = archive.CreateEntry ($"{SHAPE_IMAGE_PREFIX}{i}.png");
				using Stream imageStream = imageEntry.Open ();
				imageStream.Write (bytes, 0, bytes.Length);
			}

			writer.WriteEndElement ();
		}

		// See AddTextEntry: the writer closed this stream when it was disposed.
		byte[] shapeXml = ms.ToArray ();
		ZipArchiveEntry shapeEntry = archive.CreateEntry (SHAPE_ENTRY_PATH);
		using Stream shapeStream = shapeEntry.Open ();
		shapeStream.Write (shapeXml, 0, shapeXml.Length);
	}

	/// <summary>
	/// Whether <paramref name="layer"/>'s modifier nodes can be written as editable nodes. An add-in
	/// effect cannot: nothing guarantees the add-in is installed when the file is opened again, so the
	/// layer is saved with its effects baked in. <see cref="EffectNodesToBake"/> is what the save path
	/// warns about before it comes to that.
	/// </summary>
	public static bool NodesRoundTrip (UserLayer layer)
		=> !layer.ModifierNodes.Any (node => AddinMenu.AddinNameOf (node.Effect.GetType ()) is not null);

	/// <summary>
	/// The effect names, in layer order, that saving <paramref name="document"/> as ORA would flatten
	/// because they came from an add-in. Empty when everything round-trips.
	/// </summary>
	public static IReadOnlyList<string> EffectNodesToBake (Document document)
	{
		List<string> names = [];
		foreach (UserLayer layer in document.Layers.UserLayers) {
			foreach (EffectModifierNode node in layer.ModifierNodes) {
				if (AddinMenu.AddinNameOf (node.Effect.GetType ()) is not null)
					names.Add (node.DisplayName);
			}
		}
		return names;
	}

	private static void AddEffectEntry (ZipArchive archive, Document document)
	{
		IReadOnlyList<UserLayer> layers = document.Layers.UserLayers;
		if (!layers.Any (layer => layer.HasModifiers && NodesRoundTrip (layer)))
			return;

		using MemoryStream ms = new ();
		using (XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) { Formatting = Formatting.Indented }) {
			writer.WriteStartElement ("impasto-effects");
			writer.WriteAttributeString ("version", IMPASTO_FORMAT_VERSION);

			for (int i = 0; i < layers.Count; i++) {
				UserLayer layer = layers[i];
				if (!layer.HasModifiers || !NodesRoundTrip (layer))
					continue;

				writer.WriteStartElement ("layer");
				writer.WriteAttributeString ("index", i.ToString ());

				// The position in the layer's unified object list, not among the nodes: a modifier
				// applies to what is below it, so its place relative to text and shapes is meaningful.
				for (int position = 0; position < layer.Objects.Count; position++) {
					if (layer.Objects[position] is not EffectModifierNode node)
						continue;

					writer.WriteStartElement ("effect");
					writer.WriteAttributeString ("id", node.Effect.EffectId);
					writer.WriteAttributeString ("position", position.ToString ());
					writer.WriteAttributeString ("effect-name", node.Effect.Name);
					WriteObjectCommon (writer, node);

					foreach ((string property, string value) in EffectParameters (node.Effect)) {
						writer.WriteStartElement ("param");
						writer.WriteAttributeString ("name", property);
						writer.WriteAttributeString ("value", value);
						writer.WriteEndElement ();
					}

					WriteClip (writer, node.Clip);
					writer.WriteEndElement (); // effect
				}

				writer.WriteEndElement (); // layer
			}

			writer.WriteEndElement (); // impasto-effects
		}

		// See AddTextEntry: the writer closed this stream when it was disposed.
		byte[] effectXml = ms.ToArray ();
		ZipArchiveEntry effectEntry = archive.CreateEntry (EFFECT_ENTRY_PATH);
		using Stream effectStream = effectEntry.Open ();
		effectStream.Write (effectXml, 0, effectXml.Length);
	}

	// An effect this build cannot run keeps the parameter text it was loaded with, so re-saving a
	// document written by a newer Impasto does not strip settings this one never understood.
	private static IReadOnlyDictionary<string, string> EffectParameters (BaseEffect effect)
		=> effect is UnavailableEffect unavailable
			? unavailable.SavedParameters
			: EffectDataSerializer.ToText (effect.EffectData);

	private static void AddManifestEntry (ZipArchive archive, Document document)
	{
		bool hasEffectNodes = document.Layers.UserLayers.Any (layer => layer.HasModifiers && NodesRoundTrip (layer));

		using MemoryStream ms = new ();
		using (XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) { Formatting = Formatting.Indented }) {
			writer.WriteStartElement ("impasto-manifest");
			writer.WriteAttributeString ("version", IMPASTO_FORMAT_VERSION);
			writer.WriteAttributeString ("app", "Impasto");
			writer.WriteAttributeString ("app-version", PintaCore.ApplicationVersion);

			// Space-separated feature names, so a reader can tell what a file uses without parsing
			// every sidecar entry. Absent features are simply not listed.
			if (hasEffectNodes)
				writer.WriteAttributeString ("features", "layer-effects");

			writer.WriteEndElement ();
		}

		byte[] manifestXml = ms.ToArray ();
		ZipArchiveEntry manifestEntry = archive.CreateEntry (MANIFEST_ENTRY_PATH);
		using Stream manifestStream = manifestEntry.Open ();
		manifestStream.Write (manifestXml, 0, manifestXml.Length);
	}

	private static void LoadEffectEntry (ZipArchive zipfile, Document document)
	{
		ZipArchiveEntry? entry = zipfile.GetEntry (EFFECT_ENTRY_PATH);
		if (entry is null)
			return;

		XmlDocument xml = new ();
		xml.Load (entry.Open ());
		XmlElement root = xml.DocumentElement!;

		foreach (XmlElement layerElement in root.GetElementsByTagName ("layer")) {
			if (!int.TryParse (GetAttribute (layerElement, "index", "-1"), out int index)
				|| index < 0 || index >= document.Layers.UserLayers.Count)
				continue;

			UserLayer layer = document.Layers.UserLayers[index];

			foreach (XmlElement effectElement in layerElement.GetElementsByTagName ("effect")) {
				EffectModifierNode? node = ReadEffectNode (effectElement);
				if (node is null)
					continue;

				// The saved position counts every object on the layer. Text and shapes are restored
				// first, so it usually lands where it was; a stale index clamps to the end rather than
				// throwing. ponytail: good enough while text and shapes each load as their own pass. If
				// the three entries ever merge into one ordered list, insert straight from that order.
				int position = int.TryParse (GetAttribute (effectElement, "position", "-1"), out int saved) ? saved : -1;
				if (position < 0 || position > layer.Objects.Count)
					position = layer.Objects.Count;

				layer.Objects.Insert (position, node);
			}

			if (layer.HasModifiers)
				ObjectOpacity.RefreshLayerNoInvalidate (PintaCore.Chrome, layer);
		}
	}

	private static EffectModifierNode? ReadEffectNode (XmlElement element)
	{
		try {
			string effectId = GetAttribute (element, "id", "");
			if (effectId.Length == 0)
				return null;

			Dictionary<string, string> parameters = [];
			foreach (XmlElement paramElement in element.GetElementsByTagName ("param"))
				parameters[GetAttribute (paramElement, "name", "")] = GetAttribute (paramElement, "value", "");

			BaseEffect? registered = PintaCore.Effects.FindEffectById (effectId);

			EffectModifierNode node;
			if (registered is null) {
				// An effect this build does not supply: the node stays, inert, carrying its identifier
				// and parameters so a re-save preserves them.
				node = new (new UnavailableEffect (effectId, GetAttribute (element, "effect-name", ""), parameters));
			} else {
				node = EffectModifierNode.FromEffect (registered, clip: null, PintaCore.Services);
				EffectDataSerializer.ApplyText (node.Effect.EffectData, parameters);
			}

			node.Clip = ReadClip (element);
			ReadObjectCommon (element, node);

			return node;
		} catch {
			// A malformed node shouldn't prevent the document from loading.
			return null;
		}
	}

	// The per-object sub-node properties every object kind shares (see ILayerObject), written and
	// read in one place so shapes and text can't drift apart on the round-trip.
	private static void WriteObjectCommon (XmlTextWriter writer, ILayerObject obj)
	{
		writer.WriteAttributeString ("name", obj.Name);
		writer.WriteAttributeString ("hidden", obj.Hidden ? "1" : "0");
		writer.WriteAttributeString ("object-opacity", obj.Opacity.ToString (GetFormat ()));
		writer.WriteAttributeString ("object-blend", ((int) obj.BlendMode).ToString ());
	}

	private static void ReadObjectCommon (XmlElement element, ILayerObject obj)
	{
		obj.Name = GetAttribute (element, "name", "");
		obj.Hidden = GetAttribute (element, "hidden", "0") == "1";
		obj.Opacity = double.Parse (GetAttribute (element, "object-opacity", "1"), GetFormat ());
		obj.BlendMode = (BlendMode) int.Parse (GetAttribute (element, "object-blend", "0"));
	}

	private static void WriteShapeObject (XmlTextWriter writer, ShapeObject shape)
	{
		writer.WriteStartElement ("shape");
		writer.WriteAttributeString ("type", ((int) shape.ShapeType).ToString ());
		WriteObjectCommon (writer, shape);
		writer.WriteAttributeString ("antialias", shape.AntiAliasing ? "1" : "0");
		writer.WriteAttributeString ("closed", shape.Closed ? "1" : "0");
		writer.WriteAttributeString ("outline", shape.OutlineColor.ToHex (addAlpha: true));
		writer.WriteAttributeString ("fill", shape.FillColor.ToHex (addAlpha: true));
		writer.WriteAttributeString ("width", shape.BrushWidth.ToString ());
		writer.WriteAttributeString ("cap", ((int) shape.LineCap).ToString ());
		writer.WriteAttributeString ("dash", shape.DashPattern);
		writer.WriteAttributeString ("spacing", shape.DashSpacing.ToString ());
		writer.WriteAttributeString ("style", shape.FillStyle.ToString ());
		writer.WriteAttributeString ("radius", shape.RoundedRadius.ToString (GetFormat ()));
		writer.WriteAttributeString ("triangle", shape.TriangleType.ToString ());
		writer.WriteAttributeString ("partial", shape.IsPartialEllipse ? "1" : "0");
		writer.WriteAttributeString ("partial-cx", shape.PartialEllipseCenter.X.ToString (GetFormat ()));
		writer.WriteAttributeString ("partial-cy", shape.PartialEllipseCenter.Y.ToString (GetFormat ()));
		writer.WriteAttributeString ("partial-rx", shape.PartialEllipseRadiusX.ToString (GetFormat ()));
		writer.WriteAttributeString ("partial-ry", shape.PartialEllipseRadiusY.ToString (GetFormat ()));

		WriteArrow (writer, "arrow1", shape.Arrow1);
		WriteArrow (writer, "arrow2", shape.Arrow2);
		foreach (ShapeControlPoint point in shape.ControlPoints) {
			writer.WriteStartElement ("point");
			writer.WriteAttributeString ("x", point.Position.X.ToString (GetFormat ()));
			writer.WriteAttributeString ("y", point.Position.Y.ToString (GetFormat ()));
			writer.WriteAttributeString ("tension", point.Tension.ToString (GetFormat ()));
			writer.WriteEndElement ();
		}

		WriteClip (writer, shape.Clip);

		writer.WriteEndElement ();
	}

	// The frozen draw-time selection clip (integer polygon rings). Persisting it keeps a partially
	// clipped shape - or a modifier node applied to a selection - covering the same area after
	// save/reopen instead of applying to the whole layer. Writes nothing when there was no clip.
	private static void WriteClip (XmlTextWriter writer, DocumentSelection? clip)
	{
		if (clip is null)
			return;

		writer.WriteStartElement ("clip");
		foreach (List<IntPoint> polygon in clip.SelectionPolygons) {
			writer.WriteStartElement ("poly");
			foreach (IntPoint pt in polygon) {
				writer.WriteStartElement ("pt");
				writer.WriteAttributeString ("x", pt.X.ToString ());
				writer.WriteAttributeString ("y", pt.Y.ToString ());
				writer.WriteEndElement ();
			}
			writer.WriteEndElement ();
		}
		writer.WriteEndElement ();
	}

	private static void WriteArrow (XmlTextWriter writer, string name, ShapeArrow arrow)
	{
		writer.WriteStartElement (name);
		writer.WriteAttributeString ("show", arrow.Show ? "1" : "0");
		writer.WriteAttributeString ("size", arrow.Size.ToString (GetFormat ()));
		writer.WriteAttributeString ("angle", arrow.AngleOffset.ToString (GetFormat ()));
		writer.WriteAttributeString ("length", arrow.LengthOffset.ToString (GetFormat ()));
		writer.WriteEndElement ();
	}

	private static void LoadShapeEntry (ZipArchive zipfile, Document document)
	{
		ZipArchiveEntry? entry = zipfile.GetEntry (SHAPE_ENTRY_PATH);
		if (entry is null)
			return;

		XmlDocument xml = new ();
		xml.Load (entry.Open ());
		XmlElement root = xml.DocumentElement!;
		foreach (XmlElement layerElement in root.GetElementsByTagName ("layer")) {
			if (!int.TryParse (GetAttribute (layerElement, "index", "-1"), out int index) || index < 0 || index >= document.Layers.UserLayers.Count)
				continue;

			UserLayer layer = document.Layers.UserLayers[index];
			foreach (XmlElement shapeElement in layerElement.GetElementsByTagName ("shape")) {
				ShapeObject? shape = ReadShapeObject (shapeElement);
				if (shape is not null)
					layer.AddShape (shape);
			}

			ZipArchiveEntry? imageEntry = zipfile.GetEntry ($"{SHAPE_IMAGE_PREFIX}{index}.png");
			if (imageEntry is not null) {
				string temporaryFile = System.IO.Path.GetTempFileName ();
				using (Stream input = imageEntry.Open ())
				using (Stream output = File.Open (temporaryFile, FileMode.Create, FileAccess.Write))
					input.CopyTo (output);

				using Pixbuf overlay = Pixbuf.NewFromFile (temporaryFile)!;
				using Context g = new (layer.ObjectLayer.Layer.Surface);
				g.DrawPixbuf (overlay, PointD.Zero);
				try { File.Delete (temporaryFile); } catch { }
			}
		}
	}

	private static ShapeObject? ReadShapeObject (XmlElement element)
	{
		try {
			ShapeObject shape = new () {
				ShapeType = (ShapeObjectType) int.Parse (GetAttribute (element, "type", "0")),
				AntiAliasing = GetAttribute (element, "antialias", "1") == "1",
				Closed = GetAttribute (element, "closed", "0") == "1",
				OutlineColor = Color.FromHex (GetAttribute (element, "outline", "#000000ff")) ?? Color.Black,
				FillColor = Color.FromHex (GetAttribute (element, "fill", "#ffffffff")) ?? Color.White,
				BrushWidth = int.Parse (GetAttribute (element, "width", "2")),
				LineCap = (LineCap) int.Parse (GetAttribute (element, "cap", "0")),
				DashPattern = GetAttribute (element, "dash", "-"),
				DashSpacing = int.Parse (GetAttribute (element, "spacing", "1")),
				FillStyle = int.Parse (GetAttribute (element, "style", "0")),
				RoundedRadius = double.Parse (GetAttribute (element, "radius", "0"), GetFormat ()),
				TriangleType = int.Parse (GetAttribute (element, "triangle", "0")),
				IsPartialEllipse = GetAttribute (element, "partial", "0") == "1",
				PartialEllipseCenter = new PointD (double.Parse (GetAttribute (element, "partial-cx", "0"), GetFormat ()), double.Parse (GetAttribute (element, "partial-cy", "0"), GetFormat ())),
				PartialEllipseRadiusX = double.Parse (GetAttribute (element, "partial-rx", "0"), GetFormat ()),
				PartialEllipseRadiusY = double.Parse (GetAttribute (element, "partial-ry", "0"), GetFormat ()),
			};

			ReadObjectCommon (element, shape);

			ReadArrow (element, "arrow1", shape.Arrow1);
			ReadArrow (element, "arrow2", shape.Arrow2);
			foreach (XmlElement pointElement in element.GetElementsByTagName ("point"))
				shape.ControlPoints.Add (new ShapeControlPoint {
					Position = new PointD (double.Parse (GetAttribute (pointElement, "x", "0"), GetFormat ()), double.Parse (GetAttribute (pointElement, "y", "0"), GetFormat ())),
					Tension = double.Parse (GetAttribute (pointElement, "tension", "0"), GetFormat ()),
				});

			shape.Clip = ReadClip (element);

			return shape;
		} catch {
			return null;
		}
	}

	// Rebuilds a shape's frozen selection clip from its <clip> element, or null if it had none.
	private static DocumentSelection? ReadClip (XmlElement shapeElement)
	{
		XmlNodeList clipNodes = shapeElement.GetElementsByTagName ("clip");
		if (clipNodes.Count == 0)
			return null;

		List<List<IntPoint>> polygons = [];
		foreach (XmlElement polyElement in ((XmlElement) clipNodes[0]!).GetElementsByTagName ("poly")) {
			List<IntPoint> polygon = [];
			foreach (XmlElement ptElement in polyElement.GetElementsByTagName ("pt"))
				polygon.Add (new IntPoint (
					long.Parse (GetAttribute (ptElement, "x", "0")),
					long.Parse (GetAttribute (ptElement, "y", "0"))));
			polygons.Add (polygon);
		}

		if (polygons.Count == 0)
			return null;

		return new DocumentSelection { SelectionPolygons = polygons };
	}

	private static void ReadArrow (XmlElement shape, string name, ShapeArrow arrow)
	{
		XmlNodeList nodes = shape.GetElementsByTagName (name);
		if (nodes.Count == 0)
			return;

		XmlElement element = (XmlElement) nodes[0]!;
		arrow.Show = GetAttribute (element, "show", "0") == "1";
		arrow.Size = double.Parse (GetAttribute (element, "size", "10"), GetFormat ());
		arrow.AngleOffset = double.Parse (GetAttribute (element, "angle", "15"), GetFormat ());
		arrow.LengthOffset = double.Parse (GetAttribute (element, "length", "10"), GetFormat ());
	}

	private static void WriteTextObject (XmlTextWriter writer, TextObject obj)
	{
		TextEngine engine = obj.Engine;

		WriteObjectCommon (writer, obj);
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
		writer.WriteAttributeString ("fill", obj.FillStyle.ToString ());
		writer.WriteAttributeString ("outline", obj.OutlineWidth.ToString ());
		writer.WriteAttributeString ("join", ((int) obj.LineJoin).ToString ());
		writer.WriteAttributeString ("wrap-width", engine.WrapWidth.ToString ());

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

			foreach (XmlElement textElement in layerElement.GetElementsByTagName ("text")) {
				TextObject? obj = ReadTextObject (textElement);
				if (obj is null)
					continue;

				layer.AddText (obj);
			}

			//Render the restored objects so they are visible before the text tool ever activates.
			//Each object carries its own fill style, outline width, and line join.
			if (layer.TextObjects.Count > 0)
				ObjectOpacity.RefreshLayerNoInvalidate (PintaCore.Chrome, layer);
		}
	}

	private static TextObject? ReadTextObject (XmlElement element)
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
			engine.WrapWidth = int.Parse (GetAttribute (element, "wrap-width", "0"));

			RectangleI bounds = new (
				int.Parse (GetAttribute (element, "bounds-x", "0")),
				int.Parse (GetAttribute (element, "bounds-y", "0")),
				int.Parse (GetAttribute (element, "bounds-w", "0")),
				int.Parse (GetAttribute (element, "bounds-h", "0")));

			TextObject obj = new (engine) {
				TextBounds = bounds,
				PreviousTextBounds = bounds,
				FillStyle = int.Parse (GetAttribute (element, "fill", "0")),
				OutlineWidth = int.Parse (GetAttribute (element, "outline", "2")),
				LineJoin = (Cairo.LineJoin) int.Parse (GetAttribute (element, "join", "0")),
			};

			ReadObjectCommon (element, obj);

			return obj;
		} catch {
			// A malformed text entry shouldn't prevent the layer from loading.
			return null;
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
