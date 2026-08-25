using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// Two callers ask the ellipse where its frame is, and they want different answers for a rectangle
// that has been flattened to a line - the state every ellipse passes through at the start of a
// drag. Rendering still draws that degenerate ellipse, so it must not be turned away; node
// placement and the partial-arc conversion have nowhere to put a point on a zero radius, so they
// must be. Both used to resolve the corners in their own copy of the same block.
[TestFixture]
internal sealed class EllipseFrameTest : ToolsTestHarness
{
	private EllipseEngine Rect (params PointD[] corners)
	{
		EllipseEngine engine = new (
			Layer (0), null, antialiasing: true,
			new Color (1, 0, 0), new Color (0, 1, 0), brushWidth: 2, LineCap.Butt);
		engine.ControlPoints = [.. corners.Select (p => new ControlPoint (p, 0.25d))];
		return engine;
	}

	[Test]
	public void EveryCornerOrderResolvesToTheSameEllipse ()
	{
		PointD topLeft = new (2, 3);
		PointD topRight = new (22, 3);
		PointD bottomRight = new (22, 17);
		PointD bottomLeft = new (2, 17);

		EllipseEngine[] rotations = [
			Rect (topLeft, topRight, bottomRight, bottomLeft),
			Rect (topRight, bottomRight, bottomLeft, topLeft),
			Rect (bottomRight, bottomLeft, topLeft, topRight),
			Rect (bottomLeft, topLeft, topRight, bottomRight),
		];

		Assert.Multiple (() => {
			foreach (EllipseEngine engine in rotations) {
				Assert.That (engine.TryGetEllipseGeometry (out PointD tl, out PointD br, out PointD center, out double rx, out double ry), Is.True);
				Assert.That (tl, Is.EqualTo (topLeft));
				Assert.That (br, Is.EqualTo (bottomRight));
				Assert.That (center, Is.EqualTo (new PointD (12, 10)));
				Assert.That (rx, Is.EqualTo (10d));
				Assert.That (ry, Is.EqualTo (7d));
			}
		});
	}

	[Test]
	public void AFlattenedRectangleStillRendersButCarriesNoEllipseToPlaceNodesOn ()
	{
		EllipseEngine flat = Rect (new PointD (2, 9), new PointD (22, 9), new PointD (22, 9), new PointD (2, 9));

		Assert.That (flat.TryGetEllipseGeometry (out _, out _, out _, out _, out _), Is.False,
			"a zero-height rectangle has no ellipse for a node to land on");
		Assert.That (flat.ConvertToSegmentedEllipseAndInsert (new PointD (12, 9), 0.25d), Is.EqualTo (-1),
			"so the partial-arc conversion has to decline it");

		flat.GeneratePoints (brush_width: 2);

		Assert.That (flat.GeneratedPoints.Length, Is.GreaterThan (0), "it still has to render");
		// The arcs are still evaluated, so the flattened Y carries the polynomial's rounding.
		Assert.That (flat.GeneratedPoints.Max (gp => System.Math.Abs (gp.Position.Y - 9d)), Is.LessThan (1e-9),
			"as the collapsed ellipse it is - every point on the line, not a spline bulging off it");
	}
}
