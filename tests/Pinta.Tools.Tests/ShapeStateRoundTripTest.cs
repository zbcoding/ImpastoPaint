using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// Pins docs-private/refactor.md T15: the per-kind extra state (arrows + TriangleType,
// RoundedRadius, the partial-ellipse frame) is copied in parallel blocks in
// ShapeEngineCollection.Create and ToShapeObject, with no shared code to keep them in sync.
// 4f86b3dc was exactly such a drop - PartialEllipseRotation silently lost for weeks. These
// tests round-trip every kind through the real persist path a shape tool uses
// (engine -> Store -> layer objects -> Create) and assert each extra field survives, so a
// future field added to one leg but not the other fails here instead of in someone's drawing.
[TestFixture]
internal sealed class ShapeStateRoundTripTest : ToolsTestHarness
{
	private static readonly Color ShapeFill = new (0, 1, 0, 1);

	// A four-point closed rectangle, as the rectangle tool would draw it.
	private static LineCurveSeriesEngine RectangleEngine (UserLayer layer)
	{
		LineCurveSeriesEngine engine = new (
			layer, null, BaseEditEngine.ShapeTypes.ClosedLineCurveSeries,
			antialiasing: true, closed: true,
			new Color (1, 0, 0), ShapeFill, brushWidth: 3, LineCap.Square);

		engine.ControlPoints = [
			new (new PointD (2, 2), 0),
			new (new PointD (20, 2), 0),
			new (new PointD (20, 18), 0),
			new (new PointD (2, 18), 0),
		];
		return engine;
	}

	// --- Per-kind fields ------------------------------------------------------------------------

	[Test]
	public void ArrowAndTriangleStateSurvivesThePersistRoundTrip ()
	{
		UserLayer layer = Layer (0);

		LineCurveSeriesEngine original = RectangleEngine (layer);
		original.Arrow1 = new Arrow (Show: true, ArrowSize: 12d, AngleOffset: 20d, LengthOffset: 5d);
		original.Arrow2 = new Arrow (Show: true, ArrowSize: 7d, AngleOffset: 33d, LengthOffset: 9d);
		original.TriangleType = (int) TriangleType.Equilateral;

		ShapeEngineCollection.Store (layer, [original]);
		var reloaded = AssertSingleReloaded<LineCurveSeriesEngine> (layer);

		Assert.Multiple (() => {
			Assert.That (reloaded.TriangleType, Is.EqualTo ((int) TriangleType.Equilateral), "TriangleType");
			AssertArrow (reloaded.Arrow1, original.Arrow1, "Arrow1");
			AssertArrow (reloaded.Arrow2, original.Arrow2, "Arrow2");
		});
	}

	[Test]
	public void RoundedRadiusSurvivesThePersistRoundTrip ()
	{
		UserLayer layer = Layer (0);

		RoundedLineEngine original = new (
			layer, null, radius: 9d,
			antialiasing: true,
			new Color (1, 0, 0), ShapeFill, brushWidth: 3, LineCap.Butt);

		original.ControlPoints = [
			new (new PointD (2, 4), 0),
			new (new PointD (24, 4), 0),
		];

		ShapeEngineCollection.Store (layer, [original]);
		RoundedLineEngine reloaded = AssertSingleReloaded<RoundedLineEngine> (layer);

		Assert.That (reloaded.Radius, Is.EqualTo (9d), "RoundedRadius");
	}

	[Test]
	public void PartialEllipseFrameIncludingRotationSurvivesThePersistRoundTrip ()
	{
		UserLayer layer = Layer (0);
		PointD frameCenter = new (14, 12);
		EllipseEngine original = EllipseWithPartialFrame (layer, frameCenter, radiusX: 12d, radiusY: 10d, rotationDegrees: 30d);

		ShapeEngineCollection.Store (layer, [original]);
		EllipseEngine reloaded = AssertSingleReloaded<EllipseEngine> (layer);

		Assert.That (reloaded.IsPartialEllipse, Is.True, "partial-ellipse flag");
		Assert.That (
			reloaded.TryGetPartialGeometry (out PointD center, out double rx, out double ry, out double rotation),
			Is.True, "partial geometry present");

		Assert.Multiple (() => {
			Assert.That (center, Is.EqualTo (frameCenter), "frame center");
			Assert.That (rx, Is.EqualTo (12d), "frame radius x");
			Assert.That (ry, Is.EqualTo (10d), "frame radius y");
			Assert.That (rotation, Is.EqualTo (30d), "frame rotation (the field 4f86b3dc dropped)");
		});
	}

	// The exact regression from 4f86b3dc: an ellipse converted to a partial one must still be a
	// partial ellipse after persisting and reloading.
	[Test]
	public void FullEllipseThatNeverBecamePartialStaysFullAfterRoundTrip ()
	{
		UserLayer layer = Layer (0);

		EllipseEngine full = new (
			layer, null,
			antialiasing: true,
			new Color (1, 0, 0), ShapeFill, brushWidth: 3, LineCap.Butt);

		full.ControlPoints = [
			new (new PointD (2, 2), 0),
			new (new PointD (22, 2), 0),
			new (new PointD (22, 16), 0),
			new (new PointD (2, 16), 0),
		];

		ShapeEngineCollection.Store (layer, [full]);
		EllipseEngine reloaded = AssertSingleReloaded<EllipseEngine> (layer);

		Assert.Multiple (() => {
			Assert.That (reloaded.IsPartialEllipse, Is.False, "a full ellipse must not gain a frame");
			foreach (var cp in reloaded.ControlPoints.Zip (full.ControlPoints))
				Assert.That (cp.Second.Position, Is.EqualTo (cp.First.Position), "control point");
		});
	}

	// --- Common state ---------------------------------------------------------------------------

	// The T1 contract on every kind at once, via the same path the tool uses.
	[Test]
	public void CommonFieldsSurviveThePersistRoundTripOnEveryKind ()
	{
		DocumentSelection clip = SelectionOf (new RectangleI (0, 0, CanvasSize / 2, CanvasSize / 2));

		LineCurveSeriesEngine line = RectangleEngine (Layer (0));
		line.Arrow1 = new Arrow (true, 11d, 10d, 10d);

		EllipseEngine ellipse = EllipseWithPartialFrame (Layer (0), new PointD (14, 12), 12d, 10d, 15d);

		RoundedLineEngine rounded = new (
			Layer (0), null, radius: 6d, antialiasing: false,
			new Color (1, 0, 0), ShapeFill, brushWidth: 5, LineCap.Butt);
		rounded.ControlPoints = [
			new (new PointD (3, 3), 0),
			new (new PointD (21, 3), 0),
		];

		foreach (ShapeEngine engine in new ShapeEngine[] { line, ellipse, rounded }) {
			engine.Name = $"rt-{engine.ShapeType}";
			engine.RasterizeOnFinalize = false;
			engine.Clip = clip;
			engine.Opacity = 0.75;
			engine.BlendMode = BlendMode.Multiply;
			engine.DashPattern = "-.";
			engine.DashSpacing = 4;
			engine.FillStyle = 2;
		}

		UserLayer layer = Layer (0);
		ShapeEngineCollection.Store (layer, [line, ellipse, rounded]);
		var shapes = layer.Objects.OfType<ShapeObject> ().ToList ();
		Assert.That (shapes.Count, Is.EqualTo (3), "all three kinds persisted");

		foreach (ShapeObject obj in shapes) {
			string id = obj.ShapeType.ToString ();
			Assert.Multiple (() => {
				Assert.That (obj.Name, Does.StartWith ("rt-"), id);
				Assert.That (obj.Clip, Is.Not.Null, id);
				Assert.That (obj.Opacity, Is.EqualTo (0.75), id);
				Assert.That (obj.BlendMode, Is.EqualTo (BlendMode.Multiply), id);
				Assert.That (obj.DashPattern, Is.EqualTo ("-."), id);
				Assert.That (obj.DashSpacing, Is.EqualTo (4), id);
				Assert.That (obj.FillStyle, Is.EqualTo (2), id);
			});
		}
	}

	// --- Helpers --------------------------------------------------------------------------------

	// An ellipse carrying a frozen partial-ellipse frame, as ConvertToSegmented leaves behind.
	private static EllipseEngine EllipseWithPartialFrame (UserLayer layer, PointD center, double radiusX, double radiusY, double rotationDegrees)
	{
		EllipseEngine engine = new (
			layer, null,
			antialiasing: true,
			new Color (1, 0, 0), ShapeFill, brushWidth: 3, LineCap.Butt);

		engine.ControlPoints = [
			new (new PointD (14, 2), 0),   // right anchor
			new (new PointD (8, 8), 0),    // moved node
			new (new PointD (2, 12), 0),   // left anchor
			new (new PointD (26, 12), 0),  // top anchor
		];

		engine.SetPartialGeometry (center, radiusX, radiusY, rotationDegrees);
		return engine;
	}

	private static T AssertSingleReloaded<T> (UserLayer layer) where T : ShapeEngine
	{
		List<ShapeObject> shapes = layer.Objects.OfType<ShapeObject> ().ToList ();
		Assert.That (shapes.Count, Is.EqualTo (1), "one shape persisted");

		T? reloaded = ShapeEngineCollection.Create (layer, shapes[0]) as T;
		Assert.That (reloaded, Is.Not.Null, $"round-tripped engine should be {typeof (T).Name}");
		return reloaded!;
	}

	private static void AssertArrow (Arrow actual, Arrow expected, string label)
	{
		Assert.Multiple (() => {
			Assert.That (actual.Show, Is.EqualTo (expected.Show), $"{label}.Show");
			Assert.That (actual.ArrowSize, Is.EqualTo (expected.ArrowSize), $"{label}.Size");
			Assert.That (actual.AngleOffset, Is.EqualTo (expected.AngleOffset), $"{label}.AngleOffset");
			Assert.That (actual.LengthOffset, Is.EqualTo (expected.LengthOffset), $"{label}.LengthOffset");
		});
	}
}
