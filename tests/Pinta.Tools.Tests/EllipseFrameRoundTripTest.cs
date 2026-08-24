using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The frozen frame of a custom (segmented) ellipse belongs to the shape everywhere the shape
/// goes - including every engine rebuild. Engines are rebuilt from <see cref="ShapeObject"/> far
/// more often than files are saved: each shape-tool gesture's Store, undo of a bake, selection
/// reloads. Going red on the gap left after the drag fix: the frame's orientation gained by
/// rotating the whole shape lived only on the engine, so the next rebuild dropped it and the
/// points stopped fitting their own ellipse - the reloaded shape was a spline approximation of
/// what was on screen.
/// </summary>
[TestFixture]
internal sealed class EllipseFrameRoundTripTest : ToolsTestHarness
{
	private static readonly RectangleI DragBox = new (4, 4, 20, 14);

	private EllipseEngine SegmentedEllipse (UserLayer layer)
	{
		EllipseEngine engine = new (
			layer,
			drawingLayer: null,
			antialiasing: false,
			new Color (0, 0, 1),
			new Color (0, 0, 1),
			brushWidth: 2,
			lineCap: LineCap.Butt);

		PointD[] corners = [
			new (DragBox.Left, DragBox.Top),
			new (DragBox.Right + 1, DragBox.Top),
			new (DragBox.Right + 1, DragBox.Bottom + 1),
			new (DragBox.Left, DragBox.Bottom + 1),
		];
		foreach (PointD c in corners)
			engine.ControlPoints.Add (new ControlPoint (c, 0.25d));

		Assert.That (engine.TryGetEllipseGeometry (out _, out _, out PointD center, out _, out _), Is.True);
		int inserted = engine.ConvertToSegmentedEllipseAndInsert (new PointD (center.X + 6, center.Y - 1), 0.25d);
		Assert.That (inserted, Is.Not.EqualTo (-1));
		return engine;
	}

	private static List<PointD> Outline (ShapeEngine engine)
	{
		engine.GeneratePoints (brush_width: 2);
		return [.. engine.GeneratedPoints.Select (gp => gp.Position)];
	}

	[Test]
	public void RotatedCustomEllipseSurvivesAPersistRebuildCycle ()
	{
		UserLayer layer = Layer (0);
		EllipseEngine engine = SegmentedEllipse (layer);

		// One whole-shape rotate, exactly what BaseEditEngine's rotate branch performs.
		PointD pivot = new (DragBox.Left + DragBox.Width / 2d, DragBox.Top + DragBox.Height / 2d);
		engine.RotateWholeShape (pivot, Math.PI / 5d);

		List<PointD> before = Outline (engine);

		// The cycle every real gesture runs: persist to objects, rebuild the engine from them.
		ShapeEngineCollection.Store (layer, [engine]);
		ShapeEngine rebuilt = ShapeEngineCollection.Create (layer, layer.ShapeObjects[0]);
		List<PointD> after = Outline (rebuilt);

		Assert.That (after.Count, Is.EqualTo (before.Count), "the outline keeps its point count across the rebuild");

		double worst = before.Zip (after, (b, a) => b.Distance (a)).Max ();
		Assert.That (worst, Is.LessThan (0.5),
			"the rebuilt ellipse has to render the same outline - its frozen frame must survive the round trip");
	}
}
