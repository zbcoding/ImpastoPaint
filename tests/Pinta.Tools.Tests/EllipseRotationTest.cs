using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The bug this pins: Alt-dragging a plain ellipse turned it into a rectangle. A full ellipse keeps
// its shape in four axis-aligned bounding-box corners, and TryGetEllipseFrame only recognizes an
// ellipse in corners that are still axis-aligned - so the moment the rotate branch turned them, the
// outline regenerated from the fallback spline through four corner points whose tension is zero,
// which is four straight lines. The engine now converts to its segmented form (anchors on the
// ellipse itself, plus the frozen frame) before rotating, the same representation an inserted node
// produces.
[TestFixture]
internal sealed class EllipseRotationTest : ToolsTestHarness
{
	private const double RadiusX = 10d;
	private const double RadiusY = 6d;
	private static readonly PointD Center = new (14, 12);

	private EllipseEngine FullEllipse ()
	{
		EllipseEngine engine = new (
			Layer (0), null, antialiasing: true,
			new Color (1, 0, 0), new Color (0, 1, 0), brushWidth: 2, LineCap.Butt);

		// Exactly what the ellipse tool leaves behind: the four corners of the drag box, tension 0.
		engine.ControlPoints = [
			new (new PointD (Center.X - RadiusX, Center.Y - RadiusY), 0d),
			new (new PointD (Center.X + RadiusX, Center.Y - RadiusY), 0d),
			new (new PointD (Center.X + RadiusX, Center.Y + RadiusY), 0d),
			new (new PointD (Center.X - RadiusX, Center.Y + RadiusY), 0d),
		];
		return engine;
	}

	private static List<PointD> Outline (ShapeEngine engine)
	{
		engine.GeneratePoints (brush_width: 2);
		return [.. engine.GeneratedPoints.Select (gp => gp.Position)];
	}

	[Test]
	public void RotatingAFullEllipseKeepsItElliptical ()
	{
		EllipseEngine engine = FullEllipse ();
		engine.RotateWholeShape (Center, Math.PI / 7d);

		List<PointD> outline = Outline (engine);
		double furthest = outline.Max (p => p.Distance (Center));

		// An ellipse rotated about its own centre never reaches past its long radius. The rectangle
		// this used to become put its corners at sqrt(rx^2 + ry^2), well past that.
		Assert.That (furthest, Is.LessThan (RadiusX + 0.5),
			"a rotated ellipse must not reach its bounding box's corners - that is the rectangle bug");
		Assert.That (furthest, Is.GreaterThan (RadiusX - 0.5),
			"and it still has to reach its own long radius, so the shape was not shrunk either");

		Assert.That (engine.IsPartialEllipse, Is.True,
			"rotating converts the shape to the segmented form that can carry a rotation");
		Assert.That (engine.TryGetPartialGeometry (out PointD center, out double rx, out double ry, out double rotation), Is.True);

		Assert.Multiple (() => {
			Assert.That (center.X, Is.EqualTo (Center.X).Within (1e-9), "the frame stays centred where it was");
			Assert.That (center.Y, Is.EqualTo (Center.Y).Within (1e-9));
			Assert.That (rx, Is.EqualTo (RadiusX).Within (1e-9), "with its radii intact");
			Assert.That (ry, Is.EqualTo (RadiusY).Within (1e-9));
			Assert.That (rotation, Is.EqualTo (Math.PI / 7d).Within (1e-9), "and the angle it was turned by");
		});
	}

	// The conversion has to be a change of representation, not of shape: turning back by the same
	// angle has to give the outline it started with.
	[Test]
	public void RotatingBackRestoresTheOriginalOutline ()
	{
		List<PointD> before = Outline (FullEllipse ());

		EllipseEngine engine = FullEllipse ();
		engine.RotateWholeShape (Center, Math.PI / 5d);
		engine.RotateWholeShape (Center, -Math.PI / 5d);
		List<PointD> after = Outline (engine);

		// The conversion re-samples the outline (true-ellipse arcs give way to partial arcs), so the
		// two runs are compared as curves - every point of each lying on the other - not pairwise.
		double worst = Math.Max (FurthestFrom (after, before), FurthestFrom (before, after));
		Assert.That (worst, Is.LessThan (0.5), "a rotation and its inverse have to leave the shape where it was");
	}

	//The furthest any point of <paramref name="from"/> sits from the nearest point of <paramref name="to"/>.
	private static double FurthestFrom (List<PointD> from, List<PointD> to)
		=> from.Max (p => to.Min (q => p.Distance (q)));

	// Rotation carried by an ellipse that was already segmented (an inserted node) still works -
	// the branch that existed before, kept honest now that the conversion runs ahead of it.
	[Test]
	public void AnAlreadySegmentedEllipseStillCarriesItsFrame ()
	{
		EllipseEngine engine = FullEllipse ();
		Assert.That (engine.ConvertToSegmentedEllipseAndInsert (new PointD (Center.X + RadiusX, Center.Y - 1), 0.25d), Is.Not.EqualTo (-1));

		engine.RotateWholeShape (Center, Math.PI / 6d);

		Assert.That (engine.TryGetPartialGeometry (out _, out _, out _, out double rotation), Is.True);
		Assert.That (rotation, Is.EqualTo (Math.PI / 6d).Within (1e-9));
	}
}
