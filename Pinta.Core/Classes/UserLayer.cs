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
using System.Linq;
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
		ObjectLayer = new ReEditableLayer (this);
	}

	/// <summary>
	/// The single surface every live object (shape or text) renders into, in z-order. Replaces the
	/// old per-kind ShapeLayer/TextLayer pair so blend modes composite across kinds.
	/// </summary>
	public ReEditableLayer ObjectLayer { get; }

	/// <summary>
	/// The single source-of-truth, z-ordered (bottom-to-top) list of every live object on this
	/// layer — shapes and text unified. Tool-specific engines are adapters over this collection and
	/// are not persisted here. Reordering this list (or calling <see cref="MoveObject"/>) changes an
	/// object's z-order freely across kinds, so a text can sit beneath a shape.
	/// </summary>
	public List<ILayerObject> Objects { get; } = [];

	/// <summary>
	/// Converts a position in the unified <see cref="Objects"/> list into the index of that object
	/// *within its kind* (the position among shapes, or among text). Used when a dock row (addressed
	/// by unified z-index) must select an object through a kind-scoped tool seam.
	/// </summary>
	public static int UserLayerIndexOfKind (UserLayer layer, bool isText, int objectIndex)
	{
		int seen = 0;
		for (int i = 0; i <= objectIndex && i < layer.Objects.Count; ++i)
			if (isText ? layer.Objects[i] is TextObject : layer.Objects[i] is ShapeObject)
				seen++;
		return seen - 1;
	}

	/// <summary>The shape objects, in z-order (filtered view of <see cref="Objects"/>).</summary>
	public IReadOnlyList<ShapeObject> ShapeObjects => Objects.OfType<ShapeObject> ().ToList ();

	/// <summary>The text objects, in z-order (filtered view of <see cref="Objects"/>).</summary>
	public IReadOnlyList<TextObject> TextObjects => Objects.OfType<TextObject> ().ToList ();

	/// <summary>Adds a text object on top (end of <see cref="Objects"/>).</summary>
	public void AddText (TextObject text) => Objects.Add (text);

	/// <summary>Adds a shape object on top (end of <see cref="Objects"/>).</summary>
	public void AddShape (ShapeObject shape) => Objects.Add (shape);

	/// <summary>Removes an object; returns whether it was present.</summary>
	public bool RemoveObject (ILayerObject obj) => Objects.Remove (obj);

	/// <summary>Removes the <paramref name="index"/>-th shape (or text, if <paramref name="isText"/>).</summary>
	public bool RemoveObjectAtKind (bool isText, int index)
	{
		int seen = 0;
		for (int i = 0; i < Objects.Count; ++i) {
			if ((isText ? Objects[i] is TextObject : Objects[i] is ShapeObject) && seen++ == index) {
				Objects.RemoveAt (i);
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Replaces every shape object on the layer with <paramref name="shapes"/>, in place: each shape
	/// keeps its position relative to the text objects it was interleaved with (cross-kind z-order
	/// and cross-kind reorder survive a shape only persist/undo). New shapes beyond the old count are
	/// appended on top.
	/// </summary>
	public void ReplaceShapes (IReadOnlyList<ShapeObject> shapes)
	{
		List<ILayerObject> rebuilt = [];
		int shapeIdx = 0;
		foreach (ILayerObject o in Objects) {
			if (o is ShapeObject) {
				if (shapeIdx < shapes.Count)
					rebuilt.Add (shapes[shapeIdx++]);
			} else {
				rebuilt.Add (o);
			}
		}
		for (; shapeIdx < shapes.Count; ++shapeIdx)
			rebuilt.Add (shapes[shapeIdx]);

		Objects.Clear ();
		Objects.AddRange (rebuilt);
	}

	/// <summary>Replaces every text object on the layer with <paramref name="texts"/>, in place.</summary>
	public void ReplaceText (IReadOnlyList<TextObject> texts)
	{
		List<ILayerObject> rebuilt = [];
		int textIdx = 0;
		foreach (ILayerObject o in Objects) {
			if (o is TextObject) {
				if (textIdx < texts.Count)
					rebuilt.Add (texts[textIdx++]);
			} else {
				rebuilt.Add (o);
			}
		}
		for (; textIdx < texts.Count; ++textIdx)
			rebuilt.Add (texts[textIdx]);

		Objects.Clear ();
		Objects.AddRange (rebuilt);
	}

	/// <summary>
	/// The <paramref name="index"/>-th object of the given kind in <see cref="Objects"/>, or null if
	/// out of range. Kind-scoped addressing keeps the tool engines' per-kind index math valid while
	/// the underlying list is a single unified z-order.
	/// </summary>
	public ILayerObject? FindObject (bool isText, int index)
	{
		int seen = 0;
		foreach (ILayerObject o in Objects) {
			if (isText ? o is TextObject : o is ShapeObject) {
				if (seen == index)
					return o;
				seen++;
			}
		}
		return null;
	}

	/// <summary>
	/// Moves an object among its kind, preserving cross-kind position. Returns false when either
	/// index is stale or the move is a no-op. Cross-kind reordering uses <see cref="MoveObjectAt"/>.
	/// </summary>
	public bool MoveObject (bool isText, int from, int to)
	{
		List<ILayerObject> kindObjects = [.. Objects.Where (o => isText ? o is TextObject : o is ShapeObject)];
		if (from < 0 || from >= kindObjects.Count || to < 0 || to >= kindObjects.Count || from == to)
			return false;

		ILayerObject obj = kindObjects[from];
		kindObjects.RemoveAt (from);
		kindObjects.Insert (to, obj);

		// Rebuild the unified list: replace the kind subsequence with the reordered one.
		List<ILayerObject> rebuilt = [];
		int k = 0;
		foreach (ILayerObject o in Objects) {
			if (isText ? o is TextObject : o is ShapeObject)
				rebuilt.Add (kindObjects[k++]);
			else
				rebuilt.Add (o);
		}
		Objects.Clear ();
		Objects.AddRange (rebuilt);
		return true;
	}

	/// <summary>Moves an object within the unified <see cref="Objects"/> list (cross-kind allowed).</summary>
	public bool MoveObjectAt (int from, int to)
	{
		if (from == to || from < 0 || from >= Objects.Count || to < 0 || to >= Objects.Count)
			return false;

		ILayerObject obj = Objects[from];
		Objects.RemoveAt (from);
		Objects.Insert (to, obj);
		return true;
	}

	/// <summary>The index-th object in the unified list, or null if stale.</summary>
	public ILayerObject? FindObjectAt (int index)
		=> index >= 0 && index < Objects.Count ? Objects[index] : null;

	/// <summary>The modifier nodes on this layer, in z-order.</summary>
	public IReadOnlyList<EffectModifierNode> ModifierNodes => Objects.OfType<EffectModifierNode> ().ToList ();

	/// <summary>
	/// Whether this layer renders through the accumulator path. False keeps the original two-surface
	/// composite (base raster + object surface) so a layer without modifiers renders exactly as before.
	/// </summary>
	public bool HasModifiers => Objects.Any (o => o is EffectModifierNode);

	/// <summary>
	/// The accumulated composite for a layer with modifiers: the base raster with every child applied
	/// bottom-up. Built by <see cref="ObjectOpacity.RenderLayerObjects"/>, the single chokepoint that
	/// already re-runs after any object change. Null when the layer has no modifiers, in which case
	/// the original two-surface path renders the layer.
	/// </summary>
	public ImageSurface? Composite { get; internal set; }

	public bool HasObjectSubNodes
		=> Objects.Any (o => o is not ShapeObject s || !s.RasterizeOnFinalize);

	/// <summary>
	/// Any live object at all, including transient rasterize-on-finalize shapes — the test for "is
	/// there anything to bake", as opposed to <see cref="HasObjectSubNodes"/>'s "anything to show".
	/// </summary>
	public bool HasAnyObjects => Objects.Count > 0;

	/// <summary>
	/// Creates a raster fallback for editable shape overlays. It is used by ORA
	/// import before the shape tool has hydrated its engines.
	/// </summary>
	public ImageSurface CreateShapeOverlay ()
	{
		ImageSurface overlay = CairoExtensions.CreateImageSurface (Surface.Format, Surface.Width, Surface.Height);
		using Context g = new (overlay);
		foreach (ReEditableLayer layer in ReEditableLayers) {
			if (layer == ObjectLayer || !layer.IsLayerSetup)
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

		Objects.Clear ();
		return true;
	}

	/// <summary>
	/// Returns a list of the layers to paint for this top-level layer.
	/// This includes the primary layer and any active re-editable layers.
	/// </summary>
	public IEnumerable<Layer> GetLayersToPaint ()
	{
		// A layer carrying modifiers renders as one accumulated surface: its children are already
		// folded in, so the base raster and object surfaces must not be painted a second time.
		if (Composite is not null) {
			yield return new Layer (Composite, Hidden, Opacity, Name) { BlendMode = BlendMode };
			yield break;
		}

		yield return this;

		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (rel.IsLayerSetup)
				yield return rel.Layer;
		}
	}
}
