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
