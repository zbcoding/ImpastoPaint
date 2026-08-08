using System;
using System.Collections.Generic;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ShapeObjectTest
{
	[Test]
	public void ClonePreservesEditableGeometryAndStyle ()
	{
		ShapeObject source = new () {
			ShapeType = ShapeObjectType.RoundedLineSeries,
			AntiAliasing = false,
			OutlineColor = new Color (0.1, 0.2, 0.3, 1),
			FillColor = new Color (0.7, 0.6, 0.5, 1),
			BrushWidth = 7,
			LineCap = LineCap.Round,
			DashPattern = "- .",
			DashSpacing = 4,
			FillStyle = 2,
			RoundedRadius = 23,
			TriangleType = 1,
		};
		source.ControlPoints.Add (new ShapeControlPoint { Position = new PointD (12, 34), Tension = 0.25 });
		source.Arrow1.Show = true;
		source.Arrow1.Size = 18;

		ShapeObject clone = source.Clone ();
		clone.ControlPoints[0].Position = new PointD (99, 100);
		clone.Arrow1.Size = 2;

		Assert.Multiple (() => {
			Assert.That (clone.ShapeType, Is.EqualTo (source.ShapeType));
			Assert.That (clone.FillStyle, Is.EqualTo (2));
			Assert.That (clone.RoundedRadius, Is.EqualTo (23));
			Assert.That (clone.TriangleType, Is.EqualTo (1));
			Assert.That (clone.ControlPoints[0].Tension, Is.EqualTo (0.25));
			Assert.That (source.ControlPoints[0].Position, Is.EqualTo (new PointD (12, 34)));
			Assert.That (source.Arrow1.Size, Is.EqualTo (18));
		});
	}

	// The object-layer history model (ShapesHistoryItem/ShapesModifyHistoryItem) is a pure state
	// machine over the object list: each swap does CloneAll + Clear + AddRange, and the object
	// surface is re-rendered from the list. This asserts that walking a stored snapshot
	// forward -> back -> forward lands on a value-identical object list with no lost data, which is
	// the deterministic, lossless round-trip the design requires.
	[Test]
	public void ObjectListSwap_ForwardBackForward_IsLossless ()
	{
		List<ShapeObject> before = BuildScene ();

		// The layer starts in the "after" state; the history item holds the "before" snapshot.
		List<ShapeObject> layer = BuildScene ();
		layer[0].BrushWidth = 42;                                       // edit 1: restyle
		layer[1].ControlPoints.Add (new ShapeControlPoint { Position = new PointD (5, 6), Tension = 0.5 }); // edit 2: add point
		layer.RemoveAt (2);                                            // edit 3: delete a shape
		List<ShapeObject> after = ShapeObject.CloneAll (layer);

		List<ShapeObject> stored = ShapeObject.CloneAll (before);

		// Undo: swap stored(before) into the layer, stash the live(after).
		List<ShapeObject> live = ShapeObject.CloneAll (layer);
		layer.Clear ();
		layer.AddRange (stored);
		stored = live;
		AssertScenesEqual (before, layer);

		// Redo: swap back.
		live = ShapeObject.CloneAll (layer);
		layer.Clear ();
		layer.AddRange (stored);
		stored = live;
		AssertScenesEqual (after, layer);

		// Undo again: must land byte-identical to the first undo.
		live = ShapeObject.CloneAll (layer);
		layer.Clear ();
		layer.AddRange (stored);
		AssertScenesEqual (before, layer);
	}

	[Test]
	public void CloneAll_ProducesIndependentDeepCopies ()
	{
		List<ShapeObject> source = BuildScene ();
		List<ShapeObject> clone = ShapeObject.CloneAll (source);

		clone[0].BrushWidth = 999;
		clone[1].ControlPoints[0].Position = new PointD (-1, -1);

		Assert.Multiple (() => {
			Assert.That (clone, Has.Count.EqualTo (source.Count));
			Assert.That (source[0].BrushWidth, Is.Not.EqualTo (999));
			Assert.That (source[1].ControlPoints[0].Position, Is.Not.EqualTo (new PointD (-1, -1)));
		});
	}

	private static List<ShapeObject> BuildScene ()
	{
		List<ShapeObject> scene = [];
		for (int i = 0; i < 3; i++) {
			ShapeObject o = new () {
				ShapeType = (ShapeObjectType) (i % 5),
				BrushWidth = 2 + i,
				FillStyle = i,
				OutlineColor = new Color (0.1 * i, 0.2, 0.3, 1),
				DashPattern = i == 1 ? "- ." : "-",
			};
			o.ControlPoints.Add (new ShapeControlPoint { Position = new PointD (i, i * 2), Tension = 0.1 * i });
			o.ControlPoints.Add (new ShapeControlPoint { Position = new PointD (i + 10, i), Tension = 0.5 });
			scene.Add (o);
		}
		return scene;
	}

	// Skips the test (rather than failing it) when the native cairo-graphics library isn't present,
	// so a machine without it doesn't turn the whole suite red and hide real regressions.
	private static void RequireCairo ()
	{
		try {
			using ImageSurface _ = CairoExtensions.CreateImageSurface (Format.Argb32, 1, 1);
		} catch (DllNotFoundException e) {
			Assert.Ignore ($"Native cairo-graphics unavailable: {e.Message}");
		}
	}

	// Per-object opacity: ObjectOpacity.Draw fades the object as a whole (via a scratch surface), so
	// overlapping passes within one object don't compound, and full opacity keeps the direct path.
	[Test]
	public void ObjectOpacityDrawFadesTheWholeObject ()
	{
		RequireCairo ();

		static void DrawTwoOverlappingOpaquePasses (ImageSurface s)
		{
			using Context g = new (s);
			g.SetSourceColor (new Color (1, 0, 0, 1));
			g.Rectangle (0, 0, 4, 4);
			g.Fill ();
			g.Rectangle (0, 0, 4, 4);
			g.Fill ();
		}

		ImageSurface faded = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		ObjectOpacity.Draw (faded, 0.5, DrawTwoOverlappingOpaquePasses);

		ImageSurface opaque = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		ObjectOpacity.Draw (opaque, 1.0, DrawTwoOverlappingOpaquePasses);

		Assert.Multiple (() => {
			Assert.That (faded.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (128).Within (2), "faded to ~50%, not compounded");
			Assert.That (opaque.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (255), "full opacity unchanged");
		});
	}

	// Per-object visibility: the same ObjectOpacity.Draw chokepoint that fades an object skips a
	// hidden one entirely, so no render path has to check Hidden itself.
	[Test]
	public void HiddenObjectDrawsNothing ()
	{
		RequireCairo ();

		static void FillRed (ImageSurface s)
		{
			using Context g = new (s);
			g.SetSourceColor (new Color (1, 0, 0, 1));
			g.Rectangle (0, 0, 4, 4);
			g.Fill ();
		}

		ImageSurface hidden = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		ObjectOpacity.Draw (hidden, new ShapeObject { Hidden = true }, FillRed);

		ImageSurface shown = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		ObjectOpacity.Draw (shown, new ShapeObject (), FillRed);

		Assert.Multiple (() => {
			Assert.That (hidden.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (0), "hidden object draws nothing");
			Assert.That (shown.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (255), "visible object draws");
		});
	}

	// Per-object z-order: MoveObject repositions within the list, and moving back restores the
	// original order (what ObjectReorderHistoryItem relies on for undo).
	[Test]
	public void MoveObjectReordersAndIsReversible ()
	{
		UserLayer layer = new (CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4));
		layer.ShapeObjects.AddRange ([
			new ShapeObject { Name = "a" },
			new ShapeObject { Name = "b" },
			new ShapeObject { Name = "c" },
		]);

		Assert.Multiple (() => {
			Assert.That (layer.MoveObject (isText: false, 0, 2), Is.True);
			Assert.That (layer.ShapeObjects.ConvertAll (o => o.Name), Is.EqualTo (new[] { "b", "c", "a" }));

			Assert.That (layer.MoveObject (isText: false, 2, 0), Is.True);
			Assert.That (layer.ShapeObjects.ConvertAll (o => o.Name), Is.EqualTo (new[] { "a", "b", "c" }));

			Assert.That (layer.MoveObject (isText: false, 0, 0), Is.False, "no-op move");
			Assert.That (layer.MoveObject (isText: false, 0, 9), Is.False, "out of range");
		});
	}

	[Test]
	public void RasterizeObjectsBakesObjectSurfacesIntoBaseRaster ()
	{
		RequireCairo ();

		ImageSurface baseSurface = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		UserLayer layer = new (baseSurface);

		// Simulate the object-layer invariant: a shape object with its rendered pixels sitting in the
		// ShapeLayer surface (not the base raster). RasterizeObjects must fold those pixels down.
		layer.ShapeObjects.Add (new ShapeObject { ShapeType = ShapeObjectType.Ellipse });
		using (Context g = new (layer.ShapeLayer.Layer.Surface)) {
			g.SetSourceColor (new Color (1, 0, 0, 1));
			g.Rectangle (0, 0, 4, 4);
			g.Fill ();
		}

		Assert.That (layer.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (0), "base raster starts empty");

		bool baked = layer.RasterizeObjects ();

		Assert.Multiple (() => {
			Assert.That (baked, Is.True);
			Assert.That (layer.ShapeObjects, Is.Empty, "objects dropped");
			Assert.That (layer.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (255), "pixels now in base raster");
			Assert.That (layer.ShapeLayer.Layer.Surface.GetColorBgra (new PointI (1, 1)).A, Is.EqualTo (0), "object surface cleared");
		});

		Assert.That (layer.RasterizeObjects (), Is.False, "no-op when there are no objects");
	}

	// RasterizeObjects folds down *every* object surface (shapes AND text) and clears both lists —
	// the whole-layer bake behind "Rasterize All Objects" and the resize/crop pre-bake. Covers the
	// text path, which the shape-only test above does not.
	[Test]
	public void RasterizeObjectsBakesShapeAndTextSurfacesAndClearsBothLists ()
	{
		RequireCairo ();

		ImageSurface baseSurface = CairoExtensions.CreateImageSurface (Format.Argb32, 4, 4);
		UserLayer layer = new (baseSurface);

		layer.ShapeObjects.Add (new ShapeObject { ShapeType = ShapeObjectType.Ellipse });
		layer.TextObjects.Add (new TextObject (new TextEngine ()));

		// Paint the shape into its object surface (left half) and the text into its own (right half),
		// so a correct bake leaves both regions opaque in the base raster.
		using (Context g = new (layer.ShapeLayer.Layer.Surface)) {
			g.SetSourceColor (new Color (1, 0, 0, 1));
			g.Rectangle (0, 0, 2, 4);
			g.Fill ();
		}
		using (Context g = new (layer.TextLayer.Layer.Surface)) {
			g.SetSourceColor (new Color (0, 0, 1, 1));
			g.Rectangle (2, 0, 2, 4);
			g.Fill ();
		}

		bool baked = layer.RasterizeObjects ();

		Assert.Multiple (() => {
			Assert.That (baked, Is.True);
			Assert.That (layer.ShapeObjects, Is.Empty, "shape objects dropped");
			Assert.That (layer.TextObjects, Is.Empty, "text objects dropped");
			Assert.That (layer.Surface.GetColorBgra (new PointI (0, 1)).A, Is.EqualTo (255), "shape pixels baked");
			Assert.That (layer.Surface.GetColorBgra (new PointI (3, 1)).A, Is.EqualTo (255), "text pixels baked");
			Assert.That (layer.ShapeLayer.Layer.Surface.GetColorBgra (new PointI (0, 1)).A, Is.EqualTo (0), "shape surface cleared");
			Assert.That (layer.TextLayer.Layer.Surface.GetColorBgra (new PointI (3, 1)).A, Is.EqualTo (0), "text surface cleared");
		});
	}

	private static void AssertScenesEqual (IReadOnlyList<ShapeObject> expected, IReadOnlyList<ShapeObject> actual)
	{
		Assert.That (actual, Has.Count.EqualTo (expected.Count));
		for (int i = 0; i < expected.Count; i++) {
			ShapeObject e = expected[i], a = actual[i];
			Assert.That (a.ShapeType, Is.EqualTo (e.ShapeType));
			Assert.That (a.BrushWidth, Is.EqualTo (e.BrushWidth));
			Assert.That (a.FillStyle, Is.EqualTo (e.FillStyle));
			Assert.That (a.OutlineColor, Is.EqualTo (e.OutlineColor));
			Assert.That (a.DashPattern, Is.EqualTo (e.DashPattern));
			Assert.That (a.ControlPoints, Has.Count.EqualTo (e.ControlPoints.Count));
			for (int j = 0; j < e.ControlPoints.Count; j++) {
				Assert.That (a.ControlPoints[j].Position, Is.EqualTo (e.ControlPoints[j].Position));
				Assert.That (a.ControlPoints[j].Tension, Is.EqualTo (e.ControlPoints[j].Tension));
			}
		}
	}
}
