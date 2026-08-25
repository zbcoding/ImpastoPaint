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
using System.Linq;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// A UserLayer is a Layer that the user interacts with directly. Each UserLayer contains special layers
/// and some other special variables that allow for re-editability of various things.
/// </summary>
public sealed class UserLayer : Layer
{
	//Call the base class constructor and set up the object layer.
	public UserLayer (ImageSurface surface)
		: this (surface, false, 1f, "")
	{ }

	//Call the base class constructor and set up the object layer.
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

	/// <summary>
	/// Where the next drawn object goes: directly above the topmost existing object, or at index 0
	/// when none exist yet - so an effect already on the layer keeps reaching everything drawn
	/// after it. Coverage is positional and decided at draw time; drag-reorder changes it after.
	/// </summary>
	private int NextObjectInsertIndex {
		get {
			for (int i = Objects.Count - 1; i >= 0; --i)
				if (Objects[i] is ShapeObject or TextObject)
					return i + 1;
			return 0;
		}
	}

	/// <summary>Adds a text object just above the topmost existing object (below any effect above
	/// it), so the last thing drawn is the one seen on top among objects.</summary>
	public void AddText (TextObject text)
	{
		Objects.Insert (NextObjectInsertIndex, text);
	}

	/// <summary>Adds a shape object just above the topmost existing object (below any effect above
	/// it), so the last thing drawn is the one seen on top among objects.</summary>
	public void AddShape (ShapeObject shape)
	{
		Objects.Insert (NextObjectInsertIndex, shape);
	}

	/// <summary>Adds an effect/modifier node at the very top of the stack, so it reaches everything
	/// already on the layer - objects included. A newly applied effect grades last; drag-reorder
	/// changes that after the fact.</summary>
	public void AddModifierNode (ILayerModifierNode node) => Objects.Add (node);

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
	/// brand new (freshly drawn), not reordered survivors, so they insert just above the topmost
	/// existing object -- below any modifier above it -- like any other new addition.
	/// </summary>
	/// <remarks>
	/// <paramref name="shapes"/> arrives oldest-first (the caller's own creation order) and never gets
	/// reordered on its end between calls, and under the top-insert stacking rule
	/// <see cref="Objects"/>' existing slots read in that same oldest-first order (index 0 is still
	/// the bottom). Pairing the incoming prefix against the existing slots in their given order lines
	/// each persisted shape back up with its own geometry; walking either side in reverse instead
	/// reassigns each slot's geometry to a different shape as soon as a third shape is added,
	/// silently swapping shapes' z-order without moving anything in the layers dock.
	/// </remarks>
	public void ReplaceShapes (IReadOnlyList<ShapeObject> shapes)
	{
		int existingShapeCount = Objects.Count (o => o is ShapeObject);
		Queue<ShapeObject> existingShapesInDrawOrder = new (shapes.Take (existingShapeCount));

		List<ILayerObject> rebuilt = [];
		foreach (ILayerObject o in Objects) {
			if (o is ShapeObject) {
				if (existingShapesInDrawOrder.Count > 0)
					rebuilt.Add (existingShapesInDrawOrder.Dequeue ());
			} else {
				rebuilt.Add (o);
			}
		}
		rebuilt.InsertRange (InsertIndexForNewObjects (rebuilt), shapes.Skip (existingShapeCount));

		Objects.Clear ();
		Objects.AddRange (rebuilt);
	}

	/// <summary>Replaces every text object on the layer with <paramref name="texts"/>, in place. New
	/// text beyond the old count inserts just above the topmost existing object, same as
	/// <see cref="ReplaceShapes"/> -- see its remarks for how the existing prefix pairs up.</summary>
	public void ReplaceText (IReadOnlyList<TextObject> texts)
	{
		int existingTextCount = Objects.Count (o => o is TextObject);
		Queue<TextObject> existingTextInDrawOrder = new (texts.Take (existingTextCount));

		List<ILayerObject> rebuilt = [];
		foreach (ILayerObject o in Objects) {
			if (o is TextObject) {
				if (existingTextInDrawOrder.Count > 0)
					rebuilt.Add (existingTextInDrawOrder.Dequeue ());
			} else {
				rebuilt.Add (o);
			}
		}
		rebuilt.InsertRange (InsertIndexForNewObjects (rebuilt), texts.Skip (existingTextCount));

		Objects.Clear ();
		Objects.AddRange (rebuilt);
	}

	/// <summary>
	/// Index within <paramref name="rebuilt"/> where newly drawn objects go: just above the
	/// topmost existing object (below any modifier above it) - the same rule
	/// <see cref="AddShape"/>/<see cref="AddText"/> apply, so both paths stack identically.
	/// </summary>
	private int InsertIndexForNewObjects (List<ILayerObject> rebuilt)
	{
		for (int i = rebuilt.Count - 1; i >= 0; --i)
			if (rebuilt[i] is ShapeObject or TextObject)
				return i + 1;
		return 0;
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
	public IReadOnlyList<ILayerModifierNode> ModifierNodes => Objects.OfType<ILayerModifierNode> ().ToList ();

	/// <summary>Whether this layer contains an enabled, non-identity transform.</summary>
	public bool HasActiveTransform => Objects.OfType<LayerTransformNode> ().Any (node => node.IsActive);

	/// <summary>
	/// Whether this layer renders through the accumulator path. False keeps the original two-surface
	/// composite (base raster + object surface) so a layer without modifiers renders exactly as before.
	/// </summary>
	public bool HasModifiers => Objects.Any (o => o is ILayerModifierNode);

	/// <summary>The layer's mask slot, or null when the layer has no mask.</summary>
	public LayerMask? Mask { get; internal set; }

	/// <summary>Whether this layer has a mask slot at all (whether or not it is hidden).</summary>
	public bool HasMask => Mask is not null;

	/// <summary>
	/// Whether this layer renders through the accumulator path: it has modifier nodes, or an enabled
	/// mask. Either one composites all of the layer's content into one surface, which is where the
	/// mask is applied last.
	/// </summary>
	public bool NeedsComposite => HasModifiers || (Mask is not null && !Mask.Hidden);

	/// <summary>
	/// The surface raster paint tools draw into: the layer's own raster, or its mask's surface when
	/// the mask row is the active edit target. Reads the current mask-selection state so a tool that
	/// calls this per stroke retargets live as the user picks between the layer and its mask.
	/// </summary>
	public ImageSurface PaintSurface
		=> LayerMaskSelection.IsActiveMaskLayer (this) && Mask is not null
			? Mask.Surface
			: Surface;

	/// <summary>Creates (or returns) the layer's mask: a fresh, fully transparent surface sized to the layer.</summary>
	public LayerMask CreateMask ()
	{
		if (Mask is not null)
			return Mask;

		ImageSurface surface = CairoExtensions.CreateImageSurface (Surface.Format, Surface.Width, Surface.Height);
		Mask = new LayerMask (surface);
		return Mask;
	}

	/// <summary>Removes the layer's mask slot entirely, so the layer renders unmasked.</summary>
	public void DropMask ()
		=> Mask = null;

	/// <summary>Replaces the mask's surface with <paramref name="surface"/>, creating the mask if absent.</summary>
	internal void ReplaceMaskSurface (ImageSurface surface)
	{
		if (Mask is null)
			Mask = new LayerMask (surface);
		else
			Mask.Surface = surface;
	}

	/// <summary>
	/// The accumulated composite for a layer with modifiers: the base raster with every child applied
	/// bottom-up. Built by <see cref="ObjectOpacity.RenderLayerObjects"/>, the single chokepoint that
	/// already re-runs after any object change. Null when the layer has no modifiers, in which case
	/// the original two-surface path renders the layer.
	/// </summary>
	public ImageSurface? Composite { get; private set; }

	/// <summary>
	/// Replaces the composite, disposing the surface it replaces. This runs per mouse move over a
	/// layer with nodes, so leaving the old surface to the finalizer orphans Cairo memory fast.
	/// Callers only ever borrow the composite for the duration of a paint, which cannot overlap this.
	/// </summary>
	internal void SetComposite (ImageSurface? composite)
	{
		if (ReferenceEquals (Composite, composite))
			return;

		Composite?.Dispose ();
		Composite = composite;
	}

	/// <summary>
	/// A fresh, caller-owned snapshot of what this layer alone shows on screen: <see cref="Composite"/>
	/// when the layer has modifiers or an applying mask, otherwise the base raster with any live
	/// shape/text objects blended on top — the same two-surface picture <see cref="GetLayersToPaint"/>
	/// paints for display. Sampling tools (paint bucket, colour picker) need this instead of
	/// <see cref="Surface"/> alone, which excludes live objects entirely.
	/// </summary>
	public ImageSurface CreateVisibleSnapshot ()
	{
		ImageSurface snapshot = (Composite ?? Surface).Clone ();

		if (Composite is null && ObjectLayer.IsLayerSetup) {
			using Context g = new (snapshot);
			ObjectLayer.Layer.Draw (g);
			snapshot.MarkDirty ();
		}

		return snapshot;
	}

	public bool HasObjectSubNodes
		=> Objects.Any (GetsSubRow);

	/// <summary>
	/// Whether <paramref name="obj"/> gets its own row in the layers dock. A rasterize-on-finalize
	/// shape or text object is transient — it fuses into the layer's raster the moment editing moves
	/// on, so showing it as a sub-node that then vanishes is confusing. Shared by the dock's row
	/// builder (Pinta.Gui.Widgets) and this "is there anything to show" check so they never disagree.
	/// </summary>
	public static bool GetsSubRow (ILayerObject o) => o switch {
		ShapeObject s => !s.RasterizeOnFinalize,
		TextObject t => !t.RasterizeOnFinalize,
		_ => true,
	};

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
		// ObjectLayer is excluded here (it renders through the normal object-layer path), and it was
		// the only member of the old ReEditableLayers collection, so this has always returned a blank
		// transparent overlay. Kept as-is: not this task's job to decide what CreateShapeOverlay should
		// draw, only to remove the now-empty collection it used to iterate.
		return CairoExtensions.CreateImageSurface (Surface.Format, Surface.Width, Surface.Height);
	}

	public override void ApplyTransform (
		Matrix xform,
		Size old_size,
		Size new_size)
	{
		base.ApplyTransform (xform, old_size, new_size);

		if (ObjectLayer.IsLayerSetup)
			ObjectLayer.Layer.ApplyTransform (xform, old_size, new_size);

		// The mask is part of the layer's raster geometry, so a destructive transform moves it too —
		// otherwise it would stay glued to the old pixel positions. (A non-destructive transform node
		// is not routed here; the mask applies last, after the node, and is left in place.)
		if (Mask is { } mask)
			mask.ApplyTransform (xform, old_size, new_size);

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

		// A modifier node's clip is the selection the user drew against the pixels being transformed
		// here, so it has to travel with them — a flipped layer whose effect zone stayed put leaves the
		// effect over content it was never applied to. Frozen means "does not follow the live
		// selection", not "immune to the layer moving underneath it".
		foreach (ILayerModifierNode node in ModifierNodes) {
			if (node.Clip is null)
				continue;

			node.Clip = node.Clip.Transform (xform);
			node.InvalidateCache ();
		}
	}

	/// <summary>
	/// Mirrors everything the layer owns — base raster, mask, live shapes and text — about the
	/// canvas centre. Destructive for the raster, but the live objects keep their identity: their
	/// geometry is mirrored rather than baked, so they stay editable and a second flip puts them
	/// back exactly. That involution is what lets the flip's history item undo by re-flipping,
	/// with no stored positions. Callers re-render the object surface afterwards.
	/// </summary>
	public void FlipContents (bool horizontal)
	{
		Size size = new (Surface.Width, Surface.Height);

		// Handles raster, re-editable sublayer surfaces, the mask and shape control points.
		ApplyTransform (
			horizontal
				? CairoExtensions.CreateMatrix (-1, 0, 0, 1, size.Width, 0)
				: CairoExtensions.CreateMatrix (1, 0, 0, -1, 0, size.Height),
			size,
			size);

		// Text is mirrored by position, not by glyph: mirroring the rendering would leave the text
		// unreadable while still claiming to be editable. The anchor moves so the block lands where
		// its mirror image would, and the rotation flips sign so a rotated block stays consistent.
		foreach (TextObject text in TextObjects) {
			PointI origin = text.Engine.Origin;
			text.Engine.Origin = horizontal
				? new PointI (size.Width - origin.X - text.TextBounds.Width, origin.Y)
				: new PointI (origin.X, size.Height - origin.Y - text.TextBounds.Height);
			text.Rotation = -text.Rotation;
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

		if (ObjectLayer.IsLayerSetup)
			ObjectLayer.Layer.Crop (rect, selection);

		Mask?.Crop (rect);
	}

	public override void ResizeCanvas (Size newSize, Anchor anchor)
	{
		base.ResizeCanvas (newSize, anchor);

		if (ObjectLayer.IsLayerSetup)
			ObjectLayer.Layer.ResizeCanvas (newSize, anchor);

		Mask?.ResizeCanvas (newSize, anchor);
	}

	/// <summary>
	/// Shifts every live shape/text object's coordinates by <paramref name="delta"/>, the same anchor
	/// offset <see cref="ResizeCanvas"/> paints the raster and re-editable surfaces at. Used when a
	/// canvas resize only grows the canvas: nothing is cropped away, so the objects can keep their
	/// identity and stay editable instead of being baked to pixels first. Only valid on a layer with
	/// no modifier nodes (<see cref="HasModifiers"/>) — a node's Apply can depend on the layer's
	/// current size (e.g. a transform node's pivot), which a coordinate shift alone cannot preserve.
	/// </summary>
	public void TranslateObjects (PointD delta)
	{
		PointI deltaInt = delta.ToInt ();

		foreach (ShapeObject shape in ShapeObjects) {
			foreach (ShapeControlPoint cp in shape.ControlPoints)
				cp.Position += delta;

			if (shape.IsPartialEllipse)
				shape.PartialEllipseCenter += delta;

			if (shape.Clip is { } clip)
				shape.Clip = clip.Transform (TranslationMatrix (delta));
		}

		foreach (TextObject text in TextObjects) {
			text.Engine.Origin += deltaInt;
			text.TextBounds = Translated (text.TextBounds, deltaInt);
			text.PreviousTextBounds = Translated (text.PreviousTextBounds, deltaInt);
		}
	}

	private static Matrix TranslationMatrix (PointD delta)
	{
		Matrix xform = CairoExtensions.CreateIdentityMatrix ();
		xform.Translate (delta.X, delta.Y);
		return xform;
	}

	private static RectangleI Translated (RectangleI rect, PointI delta)
		=> new (rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height);

	public override void Resize (Size newSize, ResamplingMode resamplingMode)
	{
		base.Resize (newSize, resamplingMode);

		if (ObjectLayer.IsLayerSetup)
			ObjectLayer.Layer.Resize (newSize, resamplingMode);

		Mask?.Resize (newSize, resamplingMode);
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
		if (ObjectLayer.IsLayerSetup) {
			ObjectLayer.Layer.Draw (g);
			ObjectLayer.Layer.Surface.Clear ();
		}

		Objects.Clear ();
		return true;
	}

	/// <summary>
	/// Bakes a modifier-carrying layer's whole stack into its base raster and drops every child. A
	/// single modifier cannot be baked on its own: once the accumulator has run, the effect's pixels
	/// are not separable from the objects beneath it, so rasterizing one node rasterizes the stack.
	/// Returns false when the layer has no modifiers (the object-only path handles that case).
	/// </summary>
	public bool RasterizeModifierStack ()
	{
		if (Composite is null)
			return false;

		using (Context g = new (Surface)) {
			g.Operator = Operator.Source;
			g.SetSourceSurface (Composite, 0, 0);
			g.Paint ();
		}
		Surface.MarkDirty ();

		Objects.Clear ();
		SetComposite (null);

		// An applying mask is part of the composite just baked, so it has to go or it would be
		// re-applied on the next render. A hidden mask contributed nothing, so dropping it would
		// only destroy pixels the user is holding on to — it survives the bake.
		if (Mask is { Hidden: false })
			DropMask ();

		if (ObjectLayer.IsLayerSetup)
			ObjectLayer.Layer.Surface.Clear ();

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

		if (ObjectLayer.IsLayerSetup)
			yield return ObjectLayer.Layer;
	}
}
