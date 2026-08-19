using System;
using System.Collections.Generic;
using Cairo;
using ClipperLib;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// What happens when a selection meets a layer's sublayer children. Three separate things can live on
/// one layer — a base raster, additive objects (shapes and text), and modifier nodes (effects,
/// adjustments, transforms) — and the canvas shows the accumulated composite of all of them. A
/// selection-driven raster operation reads and writes only the base raster, so every case below is
/// about keeping those two views of the layer from disagreeing.
///
/// The rules these pin, in one place:
/// <list type="bullet">
/// <item>A clipped node renders over its clip's bounds, which is the region the live preview used.</item>
/// <item>A clip is frozen against the live selection, but not against the layer moving underneath it.</item>
/// <item>Modifier nodes cannot be baked per-region; reaching one bakes the whole stack.</item>
/// <item>Shapes and text stay separable, so only the ones a selection overlaps get baked.</item>
/// <item>List order is the composition order, for modifiers among themselves and against objects.</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class SelectionNodeInteractionsTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	private const int Width = 16;
	private const int Height = 8;

	// Records every region it is handed, so a test can assert on what the node decided to render
	// rather than having to infer it from pixels.
	private sealed class RegionRecordingEffect : BaseEffect
	{
		public List<RectangleI> Regions { get; } = [];
		public override bool IsTileable => false;
		public override string Name => "Region recorder (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			foreach (RectangleI roi in rois)
				Regions.Add (roi);
		}
	}

	// Paints every pixel it is given opaque red, ignoring the input. A generative effect rather than a
	// filter, because that is the case where a node still contributes pixels over an emptied region.
	private sealed class FillRedEffect : BaseEffect
	{
		public override bool IsTileable => false;
		public override string Name => "Fill red (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> data = dst.GetPixelData ();
			foreach (RectangleI roi in rois) {
				for (int y = roi.Top; y <= roi.Bottom; ++y)
					for (int x = roi.Left; x <= roi.Right; ++x)
						data[(y * dst.Width) + x] = ColorBgra.FromBgra (0, 0, 255, 255);
			}
		}
	}

	// Halves every channel. Paired with an inversion below to show that two modifiers do not commute,
	// which is what makes their order in the list observable.
	private sealed class HalveEffect : BaseEffect
	{
		public override bool IsTileable => false;
		public override string Name => "Halve (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> dstData = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
			for (int i = 0; i < dstData.Length; ++i) {
				ColorBgra c = srcData[i];
				dstData[i] = ColorBgra.FromBgra ((byte) (c.B / 2), (byte) (c.G / 2), (byte) (c.R / 2), c.A);
			}
		}
	}

	private sealed class InvertEffect : BaseEffect
	{
		public override bool IsTileable => false;
		public override string Name => "Invert (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> dstData = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
			for (int i = 0; i < dstData.Length; ++i) {
				ColorBgra c = srcData[i];
				dstData[i] = ColorBgra.FromBgra ((byte) (255 - c.B), (byte) (255 - c.G), (byte) (255 - c.R), c.A);
			}
		}
	}

	private static UserLayer LayerFilledWith (double gray)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, Width, Height);
		using (Context g = new (surface)) {
			g.SetSourceRgba (gray, gray, gray, 1);
			g.Operator = Operator.Source;
			g.Paint ();
		}
		surface.MarkDirty ();
		return new UserLayer (surface);
	}

	private static DocumentSelection RectangleSelection (RectangleD r)
	{
		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (r);
		return selection;
	}

	private static DocumentSelection EllipseSelection (RectangleD r)
	{
		DocumentSelection selection = new ();
		selection.CreateEllipseSelection (r);
		return selection;
	}

	private static DocumentSelection PolygonSelection (params IntPoint[] points)
	{
		DocumentSelection selection = new ();
		selection.SelectionPolygons.Add ([.. points]);
		return selection;
	}

	private static ColorBgra CompositeAt (UserLayer layer, int x, int y)
	{
		Assert.That (layer.Composite, Is.Not.Null, "a layer carrying modifier nodes renders from its composite");
		return layer.Composite!.GetColorBgra (new PointI (x, y));
	}

	// ---- The clip decides the render region -------------------------------------------------------

	// LivePreviewManager runs the effect over the selection's bounding box. A node that rendered the
	// whole canvas instead would hand a region-dependent effect (a twist's centre, a polar inversion's
	// origin, a gradient's span) a different region than the dialog previewed, and clipping the output
	// afterwards cannot put those pixels back where the user saw them.
	[Test]
	public void AClippedNodeRendersTheRegionTheLivePreviewShowed ()
	{
		UserLayer layer = LayerFilledWith (0);
		RegionRecordingEffect effect = new ();
		DocumentSelection clip = RectangleSelection (new RectangleD (2, 1, 6, 4));
		layer.Objects.Add (new EffectModifierNode (effect, clip));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (effect.Regions, Has.Count.EqualTo (1));
		Assert.That (effect.Regions[0], Is.EqualTo (clip.GetBounds ().ToInt ()),
			"the committed render has to use the same region the preview did");
	}

	[Test]
	public void AnUnclippedNodeStillRendersTheWholeCanvas ()
	{
		UserLayer layer = LayerFilledWith (0);
		RegionRecordingEffect effect = new ();
		layer.Objects.Add (new EffectModifierNode (effect));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (effect.Regions, Has.Count.EqualTo (1));
		Assert.That (effect.Regions[0], Is.EqualTo (new RectangleI (0, 0, Width, Height)),
			"no selection when the effect was applied means the whole layer");
	}

	// A clip whose bounds run past the canvas (a selection dragged off-canvas, or one that survived a
	// crop) must not hand the effect coordinates outside the surface it is writing to.
	[Test]
	public void AClipReachingOffCanvasIsIntersectedWithTheSurface ()
	{
		UserLayer layer = LayerFilledWith (0);
		RegionRecordingEffect effect = new ();
		layer.Objects.Add (new EffectModifierNode (effect, RectangleSelection (new RectangleD (-20, -20, 100, 100))));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.That (effect.Regions[0], Is.EqualTo (new RectangleI (0, 0, Width, Height)));
	}

	// Rendering a smaller region does not widen where the node's output lands: the clip still gates the
	// blend, so a pixel outside it keeps the value it had beneath the node.
	[Test]
	public void PixelsOutsideTheClipAreUntouchedByTheNode ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (new FillRedEffect (), RectangleSelection (new RectangleD (0, 0, 4, Height))));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.Multiple (() => {
			Assert.That (CompositeAt (layer, 1, 1).R, Is.EqualTo (255), "inside the clip");
			Assert.That (CompositeAt (layer, 12, 1).R, Is.EqualTo (0), "outside the clip");
		});
	}

	// ---- A clip travels with the pixels it was drawn against --------------------------------------

	// "Frozen" means the clip does not follow the live selection. It does not mean the clip survives
	// the layer's own pixels moving out from under it: a flipped layer whose effect zone stayed put
	// leaves the effect over content it was never applied to.
	[Test]
	public void FlippingALayerMirrorsAClippedNodeAlongWithTheRaster ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (new FillRedEffect (), RectangleSelection (new RectangleD (0, 0, 4, Height))));

		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);
		Assert.That (CompositeAt (layer, 1, 1).R, Is.EqualTo (255), "the zone starts on the left");

		layer.FlipContents (horizontal: true);
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		Assert.Multiple (() => {
			Assert.That (CompositeAt (layer, Width - 2, 1).R, Is.EqualTo (255), "the zone mirrored to the right");
			Assert.That (CompositeAt (layer, 1, 1).R, Is.EqualTo (0), "and left where it was");
		});
	}

	// Two flips are an involution for the raster, and the clip has to round-trip with it or a
	// flip-and-flip-back would walk the effect zone across the layer.
	[Test]
	public void FlippingTwiceRestoresTheClipExactly ()
	{
		UserLayer layer = LayerFilledWith (0);
		RectangleD before = new (2, 1, 5, 3);
		layer.Objects.Add (new EffectModifierNode (new FillRedEffect (), RectangleSelection (before)));

		layer.FlipContents (horizontal: true);
		layer.FlipContents (horizontal: true);

		RectangleD after = ((ILayerModifierNode) layer.Objects[0]).Clip!.GetBounds ();
		Assert.Multiple (() => {
			Assert.That (after.X, Is.EqualTo (before.X).Within (0.001));
			Assert.That (after.Width, Is.EqualTo (before.Width).Within (0.001));
		});
	}

	// A transform node's clip is the same kind of frozen selection and moves under the same rule.
	[Test]
	public void ATransformNodesClipMovesWithTheLayerToo ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new LayerTransformNode (
			new LayerTransformData { FlipHorizontal = true },
			RectangleSelection (new RectangleD (0, 0, 4, Height))));

		layer.FlipContents (horizontal: true);

		// RectangleD.Right is the last pixel, so a zone touching the right edge ends at Width - 1.
		RectangleD clip = ((ILayerModifierNode) layer.Objects[0]).Clip!.GetBounds ();
		Assert.That (clip.Right, Is.EqualTo (Width - 1).Within (0.001), "the zone mirrored to the right edge");
	}

	// ---- What a selection-driven raster op has to bake first --------------------------------------

	// The decision behind the "rasterize these first" prompt that Cut, Erase and Move Selected Pixels
	// put up. A node with no clip modifies the whole layer, so every selection reaches it.
	[Test]
	public void EverySelectionReachesAnUnclippedNode ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (new InvertEffect ()));

		Assert.That (
			ObjectRasterizer.SelectionReachesAnyModifier (layer, RectangleSelection (new RectangleD (0, 0, 1, 1))),
			Is.True);
	}

	[Test]
	public void ASelectionAwayFromEveryClippedNodeReachesNone ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (new InvertEffect (), RectangleSelection (new RectangleD (0, 0, 4, 4))));

		Assert.Multiple (() => {
			Assert.That (
				ObjectRasterizer.SelectionReachesAnyModifier (layer, RectangleSelection (new RectangleD (8, 0, 4, 4))),
				Is.False,
				"nothing to bake, so no prompt and the raster op runs untouched");
			Assert.That (
				ObjectRasterizer.SelectionReachesAnyModifier (layer, RectangleSelection (new RectangleD (3, 3, 4, 4))),
				Is.True,
				"a one-pixel overlap is still an overlap");
		});
	}

	[Test]
	public void EllipseSelectionBoundsDoNotReachAClippedNodeOutsideTheEllipse ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (
			new InvertEffect (),
			RectangleSelection (new RectangleD (0, 0, 1, 1))));

		DocumentSelection ellipse = EllipseSelection (new RectangleD (0, 0, 10, 8));

		Assert.That (
			ObjectRasterizer.SelectionReachesAnyModifier (layer, ellipse),
			Is.False,
			"the selection's rectangular bounds overlap the node, but its selected pixels do not");
	}

	[Test]
	public void CustomPolygonSelectionBoundsDoNotReachAClippedNodeOutsideThePolygon ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (new EffectModifierNode (
			new InvertEffect (),
			RectangleSelection (new RectangleD (7, 0, 1, 1))));

		DocumentSelection triangle = PolygonSelection (
			new (0, 0),
			new (8, 8),
			new (0, 8),
			new (0, 0));

		Assert.That (
			ObjectRasterizer.SelectionReachesAnyModifier (layer, triangle),
			Is.False,
			"arbitrary selection polygons use their selected area rather than their rectangular bounds");
	}

	// Several small zones from repeated select-and-apply is the normal way this list grows. Reaching
	// any one of them bakes the whole stack, because the accumulator has already fused their output.
	[Test]
	public void ReachingOneOfManySmallZonesIsEnough ()
	{
		UserLayer layer = LayerFilledWith (0);
		for (int x = 0; x < Width; x += 4)
			layer.Objects.Add (new EffectModifierNode (new InvertEffect (), RectangleSelection (new RectangleD (x, 0, 2, 2))));

		Assert.Multiple (() => {
			Assert.That (layer.ModifierNodes, Has.Count.EqualTo (4));
			Assert.That (ObjectRasterizer.SelectionReachesAnyModifier (layer, RectangleSelection (new RectangleD (12, 0, 2, 2))), Is.True);
			Assert.That (ObjectRasterizer.SelectionReachesAnyModifier (layer, RectangleSelection (new RectangleD (2, 4, 2, 2))), Is.False);
		});
	}

	// Shapes and text are separable in a way modifier output is not, so a selection that misses them
	// bakes nothing and the user sees no prompt at all.
	[Test]
	public void OnlyTheShapesASelectionOverlapsAreListedForBaking ()
	{
		UserLayer layer = LayerFilledWith (0);
		layer.Objects.Add (ShapeAt (new PointD (1, 1), new PointD (3, 3)));
		layer.Objects.Add (ShapeAt (new PointD (12, 4), new PointD (14, 6)));

		ObjectRasterizer.FindIntersecting (
			layer, new RectangleD (0, 0, 5, 5),
			out List<int> shapeIndices, out List<int> textIndices);

		Assert.Multiple (() => {
			Assert.That (shapeIndices, Is.EqualTo (new[] { 0 }), "the far shape stays editable");
			Assert.That (textIndices, Is.Empty);
		});
	}

	// Move Selected Pixels lifts pixels by clearing them out of the base raster. When the selection
	// misses every node nothing is baked, so the layer keeps rendering from its composite — and the
	// lift has to fold or the emptied region goes on showing the pixels the drag carried away. That
	// was the bug: dragging a region outside the effect zone rendered nothing until the mouse was
	// released and the history push rebuilt the composite.
	[Test]
	public void LiftingOutsideEveryClippedNodeShowsTheHoleImmediately ()
	{
		UserLayer layer = LayerFilledWith (1.0);
		layer.Objects.Add (new EffectModifierNode (new InvertEffect (), RectangleSelection (new RectangleD (0, 0, 4, Height))));
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		DocumentSelection lifted = RectangleSelection (new RectangleD (8, 0, 4, 4));

		Assert.That (
			ObjectRasterizer.PrepareForSelectionRasterOp (doc: null!, workspace: null!, chrome: null!, layer, lifted),
			Is.True,
			"a selection clear of every node needs no bake");
		Assert.That (layer.NeedsComposite, Is.True,
			"the node survived, so the canvas is still painting the composite rather than the raster");

		ObjectOpacity.LiftSelectionFromRaster (chrome: null!, layer, lifted);

		Assert.Multiple (() => {
			Assert.That (CompositeAt (layer, 9, 1).A, Is.EqualTo (0),
				"the lifted region has to read as empty mid-drag, not after the mouse is released");
			Assert.That (CompositeAt (layer, 1, 1).R, Is.EqualTo (0), "and the node still inverts its own zone");
			Assert.That (CompositeAt (layer, 13, 1).A, Is.EqualTo (255), "pixels outside the selection stay put");
		});
	}

	// The reported Move Selected Pixels case: one rectangle spans pixels inside a clipped effect and
	// plain pixels outside it. Accepting the rasterize warning must bake before the tool copies from
	// the base raster; otherwise the lifted rectangle is uniformly un-effected, and clearing it pulls
	// the apparent effect away from the source region.
	[Test]
	public void LiftingAcrossAClippedNodeCarriesEffectedAndPlainPixels ()
	{
		UserLayer layer = LayerFilledWith (1.0);
		layer.Objects.Add (new EffectModifierNode (
			new InvertEffect (),
			RectangleSelection (new RectangleD (0, 0, 4, Height))));
		ObjectOpacity.RenderLayerObjects (chrome: null!, layer);

		DocumentSelection lifted = RectangleSelection (new RectangleD (2, 0, 6, 4));
		Assert.That (
			ObjectRasterizer.SelectionReachesAnyModifier (layer, lifted),
			Is.True,
			"the rasterize warning is required when any part of the move reaches the clipped node");

		// The accepted-warning branch of PrepareForSelectionRasterOp bakes the whole modifier stack
		// before MoveSelectedTool copies these pixels to SelectionLayer.
		Assert.That (layer.RasterizeModifierStack (), Is.True);
		Assert.That (layer.HasModifiers, Is.False);

		using ImageSurface liftedPixels = CairoExtensions.CreateImageSurface (Format.Argb32, Width, Height);
		using (Context selectionContext = new (liftedPixels)) {
			selectionContext.AppendPath (lifted.SelectionPath);
			selectionContext.FillRule = FillRule.EvenOdd;
			selectionContext.SetSourceSurface (layer.Surface, 0, 0);
			selectionContext.Clip ();
			selectionContext.Paint ();
		}

		ObjectOpacity.LiftSelectionFromRaster (chrome: null!, layer, lifted);

		Assert.Multiple (() => {
			Assert.That (liftedPixels.GetColorBgra (new PointI (3, 1)).R, Is.EqualTo (0),
				"the lifted portion inside the effect zone carries the baked effect");
			Assert.That (liftedPixels.GetColorBgra (new PointI (6, 1)).R, Is.EqualTo (255),
				"the same lifted rectangle also carries its plain pixels");
			Assert.That (layer.Surface.GetColorBgra (new PointI (3, 1)).A, Is.EqualTo (0),
				"the affected portion is removed from the source during the drag");
			Assert.That (layer.Surface.GetColorBgra (new PointI (6, 1)).A, Is.EqualTo (0),
				"the plain portion is removed from the source during the drag");
			Assert.That (layer.Surface.GetColorBgra (new PointI (1, 1)).R, Is.EqualTo (0),
				"baked effect pixels outside the moved rectangle stay behind");
		});
	}

	// The same lift on a layer the canvas paints directly must not conjure a composite: one there
	// would make GetLayersToPaint skip the layer's own raster.
	[Test]
	public void LiftingFromALayerWithoutNodesLeavesItOnTheDirectRasterPath ()
	{
		UserLayer layer = LayerFilledWith (1.0);

		ObjectOpacity.LiftSelectionFromRaster (chrome: null!, layer, RectangleSelection (new RectangleD (8, 0, 4, 4)));

		Assert.Multiple (() => {
			Assert.That (layer.Composite, Is.Null);
			Assert.That (layer.Surface.GetColorBgra (new PointI (9, 1)).A, Is.EqualTo (0), "the pixels were still lifted");
		});
	}

	private static ShapeObject ShapeAt (PointD from, PointD to)
	{
		ShapeObject shape = new () { ShapeType = ShapeObjectType.OpenLineCurveSeries, BrushWidth = 1 };
		shape.ControlPoints.Add (new ShapeControlPoint { Position = from });
		shape.ControlPoints.Add (new ShapeControlPoint { Position = to });
		return shape;
	}

	// ---- Order in the list is the composition order -----------------------------------------------

	// The design's central rule: a modifier applies to everything beneath it. Halve-then-invert and
	// invert-then-halve give different pixels, so the order the list stores is observable and any
	// operation that rewrites the list (undo, duplicate layer, a load that restores saved positions)
	// has to preserve it.
	[Test]
	public void ModifierOrderInTheListChangesTheResult ()
	{
		UserLayer halveFirst = LayerFilledWith (1.0);
		halveFirst.Objects.Add (new EffectModifierNode (new HalveEffect ()));
		halveFirst.Objects.Add (new EffectModifierNode (new InvertEffect ()));
		ObjectOpacity.RenderLayerObjects (chrome: null!, halveFirst);

		UserLayer invertFirst = LayerFilledWith (1.0);
		invertFirst.Objects.Add (new EffectModifierNode (new InvertEffect ()));
		invertFirst.Objects.Add (new EffectModifierNode (new HalveEffect ()));
		ObjectOpacity.RenderLayerObjects (chrome: null!, invertFirst);

		Assert.Multiple (() => {
			Assert.That (CompositeAt (halveFirst, 1, 1).R, Is.EqualTo (128), "white halved to 127, then inverted");
			Assert.That (CompositeAt (invertFirst, 1, 1).R, Is.EqualTo (0), "white inverted to 0, then halved");
		});
	}

	// Duplicating a layer deep-copies the stack. If the copies aliased, editing one document's node
	// would silently rewrite the other's.
	[Test]
	public void CloningANodeCopiesItsClipRatherThanSharingIt ()
	{
		EffectModifierNode source = new (new InvertEffect (), RectangleSelection (new RectangleD (0, 0, 4, 4)));
		EffectModifierNode clone = source.Clone ();

		clone.Clip = RectangleSelection (new RectangleD (8, 0, 4, 4));

		Assert.That (source.Clip!.GetBounds ().X, Is.EqualTo (0), "the original kept its own zone");
	}
}
