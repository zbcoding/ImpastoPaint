//
// UserLayer.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2013 Andrew Davis, GSoC 2012 and GSoC 2013
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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// A UserLayer is a Layer that the user interacts with directly. Each UserLayer contains special layers
/// and some other special variables that allow for re-editability of various things.
/// </summary>
public sealed class UserLayer : Layer
{
	//Special layers to be drawn on to keep things editable by drawing them separately from the UserLayers.
	internal Collection<ReEditableLayer> ReEditableLayers { get; } = [];
	public ReEditableLayer TextLayer { get; }
	public ReEditableLayer ShapeLayer { get; }

	//Call the base class constructor and setup the engines.
	public UserLayer (ImageSurface surface)
		: this (surface, false, 1f, "")
	{ }

	//Call the base class constructor and setup the engines.
	public UserLayer (
		ImageSurface surface,
		bool hidden,
		double opacity,
		string name
	)
		: base (surface, hidden, opacity, name)
	{
		TextLayer = new ReEditableLayer (this);
		ShapeLayer = new ReEditableLayer (this);
	}

	//The re-editable text objects on this layer.
	public List<TextObject> TextObjects { get; } = [];

	/// <summary>
	/// The source-of-truth state for editable shapes on this layer. Tool-specific
	/// engines are adapters over this collection and are not persisted here.
	/// </summary>
	public List<ShapeObject> ShapeObjects { get; } = [];

	/// <summary>
	/// Shapes in Rasterize-on-finalize mode are transient — they fuse into the base raster the
	/// moment you move on and must never show as a persistent sub-node. Only Object-mode shapes
	/// (and any text) count as sub-layer objects the layers dock shows.
	/// </summary>
	/// <summary>
	/// The shape or text object at <paramref name="index"/>, or null if the index is stale (object
	/// lists are rebuilt on persist, so callers hold positions rather than references).
	/// </summary>
	public ILayerObject? FindObject (bool isText, int index)
	{
		IReadOnlyList<ILayerObject> objects = isText ? TextObjects : ShapeObjects;
		return index >= 0 && index < objects.Count ? objects[index] : null;
	}

	public bool HasObjectSubNodes
		=> TextObjects.Count > 0 || ShapeObjects.Exists (s => !s.RasterizeOnFinalize);

	/// <summary>
	/// Any live object at all, including transient rasterize-on-finalize shapes — the test for "is
	/// there anything to bake", as opposed to <see cref="HasObjectSubNodes"/>'s "anything to show".
	/// </summary>
	public bool HasAnyObjects => ShapeObjects.Count > 0 || TextObjects.Count > 0;

	/// <summary>
	/// Creates a raster fallback for editable shape overlays. It is used by ORA
	/// import before the shape tool has hydrated its engines.
	/// </summary>
	public ImageSurface CreateShapeOverlay ()
	{
		ImageSurface overlay = CairoExtensions.CreateImageSurface (Surface.Format, Surface.Width, Surface.Height);
		using Context g = new (overlay);
		foreach (ReEditableLayer layer in ReEditableLayers) {
			if (layer == TextLayer || !layer.IsLayerSetup)
				continue;

			g.SetSourceSurface (layer.Layer.Surface, 0, 0);
			g.Paint ();
		}

		return overlay;
	}

	public override void ApplyTransform (
		Matrix xform,
		Size old_size,
		Size new_size)
	{
		base.ApplyTransform (xform, old_size, new_size);

		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (rel.IsLayerSetup)
				rel.Layer.ApplyTransform (xform, old_size, new_size);
		}

		// Shapes are stored as vector control points (the source of truth) that get
		// redrawn from scratch, so transforming only the raster above isn't enough —
		// the next redraw would clobber it with the un-transformed points.
		// ponytail: rotate/flip only; radii stay valid since those keep aspect. If a
		// non-uniform scale ever routes through here, the partial-ellipse radii need scaling too.
		foreach (ShapeObject shape in ShapeObjects) {
			foreach (ShapeControlPoint cp in shape.ControlPoints)
				cp.Position = xform.TransformPoint (cp.Position);

			if (shape.IsPartialEllipse)
				shape.PartialEllipseCenter = xform.TransformPoint (shape.PartialEllipseCenter);
		}
	}

	public void Rotate (
		DegreesAngle angle,
		Size old_size,
		Size new_size)
	{
		RadiansAngle radians = angle.ToRadians ();

		Matrix xform = CairoExtensions.CreateIdentityMatrix ();
		xform.Translate (new_size.Width / 2.0, new_size.Height / 2.0);
		xform.Rotate (radians.Radians);
		xform.Translate (-old_size.Width / 2.0, -old_size.Height / 2.0);

		ApplyTransform (xform, old_size, new_size);
	}

	public override void Crop (RectangleI rect, Path? selection)
	{
		base.Crop (rect, selection);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.Crop (rect, selection);
	}

	public override void ResizeCanvas (Size newSize, Anchor anchor)
	{
		base.ResizeCanvas (newSize, anchor);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.ResizeCanvas (newSize, anchor);
	}

	public override void Resize (Size newSize, ResamplingMode resamplingMode)
	{
		base.Resize (newSize, resamplingMode);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.Resize (newSize, resamplingMode);
	}

	/// <summary>
	/// Bakes this layer's live editable objects (shapes + text) into its base raster and drops them
	/// as objects. The object surfaces already equal the render of the object lists (the object-layer
	/// invariant), so baking is just compositing those surfaces onto the base raster. Called before a
	/// destructive raster op (cut/erase) so it actually touches the objects' pixels. Returns true if
	/// anything was baked.
	/// </summary>
	public bool RasterizeObjects ()
	{
		if (!HasAnyObjects)
			return false;

		using Context g = new (Surface);
		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (!rel.IsLayerSetup)
				continue;
			rel.Layer.Draw (g);
			rel.Layer.Surface.Clear ();
		}

		ShapeObjects.Clear ();
		TextObjects.Clear ();
		return true;
	}

	/// <summary>
	/// Returns a list of the layers to paint for this top-level layer.
	/// This includes the primary layer and any active re-editable layers.
	/// </summary>
	public IEnumerable<Layer> GetLayersToPaint ()
	{
		yield return this;

		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (rel.IsLayerSetup)
				yield return rel.Layer;
		}
	}
}
