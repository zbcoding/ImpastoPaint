using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Moving a whole custom (segmented) ellipse must be rigid: every frame of the drag shows the
/// same shape, only further along. The engine freezes its original ellipse frame when extra
/// nodes turn a plain ellipse into a custom curve; that frozen frame belongs to the *shape*, so
/// any whole-shape move of the control points has to carry it along. Going red on the reported
/// symptom: mid-drag the frozen frame stayed behind, points stopped counting as on-ellipse and
/// the outline visibly warped until the next full rebuild snapped it back.
/// </summary>
[TestFixture]
internal sealed class EllipseWholeShapeDragTest : ToolsTestHarness
{
	private static readonly RectangleI DragBox = new (4, 4, 20, 14);

	private EllipseEngine SegmentedEllipse ()
	{
		UserLayer layer = Layer (0);
		EllipseEngine engine = new (
			layer,
			drawingLayer: null,
			antialiasing: false,
			new Color (0, 0, 1),
			new Color (0, 0, 1),
			brushWidth: 2,
			lineCap: LineCap.Butt);

		// The four corners a completed ellipse drag leaves behind, in adjacency order.
		PointD[] corners = [
			new (DragBox.Left, DragBox.Top),
			new (DragBox.Right + 1, DragBox.Top),
			new (DragBox.Right + 1, DragBox.Bottom + 1),
			new (DragBox.Left, DragBox.Bottom + 1),
		];
		foreach (PointD c in corners)
			engine.ControlPoints.Add (new ControlPoint (c, 0.25d));

		Assert.That (engine.TryGetEllipseGeometry (out _, out _, out PointD center, out _, out _), Is.True,
			"the four default corners have to form a plain ellipse before segmentation");
		int inserted = engine.ConvertToSegmentedEllipseAndInsert (new PointD (center.X + 6, center.Y - 1), 0.25d);
		Assert.That (inserted, Is.Not.EqualTo (-1), "segmentation has to succeed for this scenario");
		return engine;
	}

	private static List<PointD> Outline (ShapeEngine engine)
	{
		engine.GeneratePoints (brush_width: 2);
		return [.. engine.GeneratedPoints.Select (gp => gp.Position)];
	}

	/// <summary>Max distance between the two outlines after aligning their centroids: zero when
	/// one move is a pure translation of the other, large when the outline warped mid-drag.</summary>
	private static double WarpAfterCentring (List<PointD> before, List<PointD> after)
	{
		Assert.That (after.Count, Is.EqualTo (before.Count), "the outline keeps its point count under a rigid move");
		double bCx = before.Average (p => p.X);
		double bCy = before.Average (p => p.Y);
		double aCx = after.Average (p => p.X);
		double aCy = after.Average (p => p.Y);

		double worst = 0;
		for (int i = 0; i < before.Count; ++i)
			worst = Math.Max (worst, Math.Sqrt (
				Math.Pow (before[i].X - bCx - (after[i].X - aCx), 2) +
				Math.Pow (before[i].Y - bCy - (after[i].Y - aCy), 2)));
		return worst;
	}

	private static void TranslateAll (ShapeEngine engine, double dx, double dy)
		=> engine.TranslateWholeShape (dx, dy);

	private static void RotateAll (ShapeEngine engine, PointD pivot, double radians)
		=> engine.RotateWholeShape (pivot, radians);

	[Test]
	public void DraggingAWholeCustomEllipseDoesNotWarpItsOutline ()
	{
		EllipseEngine engine = SegmentedEllipse ();
		List<PointD> before = Outline (engine);

		// Exactly the pair of operations the whole-shape drag branch performs per frame.
		TranslateAll (engine, 5, 7);
		double warp = WarpAfterCentring (before, Outline (engine));

		Assert.That (warp, Is.LessThan (0.5),
			"a whole-shape drag has to translate the outline verbatim - centred outlines must coincide");
	}

	[Test]
	public void RotatingAWholeCustomEllipseDoesNotWarpItsOutline ()
	{
		EllipseEngine engine = SegmentedEllipse ();
		List<PointD> before = Outline (engine);
		PointD pivot = new (
			before.Average (p => p.X),
			before.Average (p => p.Y));

		// Exactly the pair of operations the whole-shape rotate branch performs per frame.
		RotateAll (engine, pivot, Math.PI / 6d);
		List<PointD> rotated = [.. Outline (engine).Select (p => {
			double x = p.X - pivot.X;
			double y = p.Y - pivot.Y;
			double c = Math.Cos (-Math.PI / 6d);
			double s = Math.Sin (-Math.PI / 6d);
			return new PointD (pivot.X + x * c - y * s, pivot.Y + x * s + y * c);
		})];

		Assert.That (WarpAfterCentring (before, rotated), Is.LessThan (0.5),
			"a whole-shape rotation has to spin the outline verbatim - unrotating it must reproduce the original");
	}
}
