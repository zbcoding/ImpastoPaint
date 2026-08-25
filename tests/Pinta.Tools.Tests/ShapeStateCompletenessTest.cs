using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The companion to ShapeStateRoundTripTest, which pins the fields that exist today. This one is
// about the fields that do not exist yet: ShapeEngineCollection.Create and ToShapeObject copy
// ShapeObject's state in two hand-written parallel legs, so a field added to one leg only - or to
// ShapeObject and neither leg - is silently dropped, which is exactly what happened to
// PartialEllipseRotation in 4f86b3dc. Every public ShapeObject field is walked by reflection here
// and has to be classified below as common state or as one kind's own; an unclassified field fails
// the run the moment it is added.
[TestFixture]
internal sealed class ShapeStateCompletenessTest : ToolsTestHarness
{
	private static readonly ShapeObjectType[] LineKinds = [
		ShapeObjectType.OpenLineCurveSeries,
		ShapeObjectType.ClosedLineCurveSeries,
		ShapeObjectType.Triangle,
	];

	// Fields only one engine kind can hold. Everything else is common state that every kind must
	// carry across the round trip; a kind that does not own a field must give it back at its
	// default, because Create builds an engine with nowhere to keep it.
	private static readonly Dictionary<string, ShapeObjectType[]> per_type_state = new () {
		[nameof (ShapeObject.RoundedRadius)] = [ShapeObjectType.RoundedLineSeries],
		[nameof (ShapeObject.TriangleType)] = LineKinds,
		[nameof (ShapeObject.Arrow1)] = LineKinds,
		[nameof (ShapeObject.Arrow2)] = LineKinds,
		[nameof (ShapeObject.IsPartialEllipse)] = [ShapeObjectType.Ellipse],
		[nameof (ShapeObject.PartialEllipseCenter)] = [ShapeObjectType.Ellipse],
		[nameof (ShapeObject.PartialEllipseRadiusX)] = [ShapeObjectType.Ellipse],
		[nameof (ShapeObject.PartialEllipseRadiusY)] = [ShapeObjectType.Ellipse],
		[nameof (ShapeObject.PartialEllipseRotation)] = [ShapeObjectType.Ellipse],
	};

	private static IEnumerable<PropertyInfo> Fields
		=> typeof (ShapeObject).GetProperties (BindingFlags.Public | BindingFlags.Instance);

	[TestCase (ShapeObjectType.OpenLineCurveSeries)]
	[TestCase (ShapeObjectType.ClosedLineCurveSeries)]
	[TestCase (ShapeObjectType.Ellipse)]
	[TestCase (ShapeObjectType.RoundedLineSeries)]
	[TestCase (ShapeObjectType.Triangle)]
	public void EveryFieldEitherSurvivesTheRoundTripOrIsOneKindsOwn (ShapeObjectType kind)
	{
		ShapeObject source = Populated (kind);
		ShapeObject dropped = new ();

		ShapeObject rebuilt = ShapeEngineCollection.Create (Layer (0), source).ToShapeObject ();

		Assert.Multiple (() => {
			foreach (PropertyInfo field in Fields) {
				bool owned = !per_type_state.TryGetValue (field.Name, out ShapeObjectType[]? owners) || owners.Contains (kind);
				ShapeObject expected = owned ? source : dropped;

				Assert.That (
					Same (field.GetValue (rebuilt), field.GetValue (expected)), Is.True,
					owned
						? $"{kind}.{field.Name} is common (or this kind's own) state and has to survive Create -> ToShapeObject"
						: $"{kind}.{field.Name} belongs to another kind, so it has to come back at its default rather than a stale value");
			}
		});
	}

	// Guards the test above: it only proves a field round-trips if the field was actually given a
	// non-default value to carry. A field added to ShapeObject and not populated here would pass
	// vacuously, which is the failure mode this whole fixture exists to prevent.
	[Test]
	public void ThePopulatedShapeSetsEveryField ()
	{
		// Ellipse rather than the first kind: ShapeType's own default is OpenLineCurveSeries, so
		// only a non-default kind makes every single field differ from a fresh ShapeObject.
		ShapeObject populated = Populated (ShapeObjectType.Ellipse);
		ShapeObject defaults = new ();

		Assert.Multiple (() => {
			foreach (PropertyInfo field in Fields)
				Assert.That (
					Same (field.GetValue (populated), field.GetValue (defaults)), Is.False,
					$"{field.Name} is still at its default - give it a distinct value in Populated and classify it in per_type_state");
		});
	}

	// --- Helpers --------------------------------------------------------------------------------

	// Every field set away from its default, so a dropped one is visible as a default coming back.
	private ShapeObject Populated (ShapeObjectType kind)
	{
		ShapeObject shape = new () {
			ShapeType = kind,
			Name = "populated",
			RasterizeOnFinalize = true,
			Clip = SelectionOf (new RectangleI (1, 1, CanvasSize / 2, CanvasSize / 2)),
			Opacity = 0.4,
			Hidden = true,
			BlendMode = BlendMode.Multiply,
			AntiAliasing = false,
			// Ellipse and RoundedLine force Closed on in their constructors, so only true can round
			// trip on all five kinds.
			Closed = true,
			OutlineColor = new Color (0.1, 0.2, 0.3, 0.4),
			FillColor = new Color (0.5, 0.6, 0.7, 0.8),
			BrushWidth = 7,
			LineCap = LineCap.Round,
			DashPattern = "-.",
			DashSpacing = 3,
			FillStyle = 2,
			RoundedRadius = 6.5,
			TriangleType = 2,
			Arrow1 = new ShapeArrow { Show = true, Size = 12d, AngleOffset = 20d, LengthOffset = 14d },
			Arrow2 = new ShapeArrow { Show = true, Size = 13d, AngleOffset = 21d, LengthOffset = 15d },
			IsPartialEllipse = true,
			PartialEllipseCenter = new PointD (11, 12),
			PartialEllipseRadiusX = 5.5,
			PartialEllipseRadiusY = 4.25,
			PartialEllipseRotation = 0.3,
		};

		shape.ControlPoints.AddRange ([
			new ShapeControlPoint { Position = new PointD (2, 3), Tension = 0.25 },
			new ShapeControlPoint { Position = new PointD (18, 3), Tension = 0.5 },
			new ShapeControlPoint { Position = new PointD (18, 15), Tension = 0.75 },
			new ShapeControlPoint { Position = new PointD (2, 15), Tension = 1.0 },
		]);

		return shape;
	}

	// ShapeArrow and the control points are mutable classes without value equality of their own.
	private static bool Same (object? left, object? right)
		=> (left, right) switch {
			(ShapeArrow a, ShapeArrow b)
				=> a.Show == b.Show && a.Size == b.Size && a.AngleOffset == b.AngleOffset && a.LengthOffset == b.LengthOffset,
			(List<ShapeControlPoint> a, List<ShapeControlPoint> b)
				=> a.Count == b.Count && a.Zip (b).All (pair => pair.First.Position == pair.Second.Position && pair.First.Tension == pair.Second.Tension),
			_ => Equals (left, right),
		};
}
