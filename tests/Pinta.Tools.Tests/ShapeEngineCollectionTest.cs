using System.Collections.Generic;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class ShapeEngineCollectionTest : ToolsTestHarness
{
	private static readonly Color ShapeFill = new (0, 1, 0, 1);

	// The bug this pins: Store used to reimplement the same "existing shapes keep position, new
	// shapes insert at the bottom" merge UserLayer.ReplaceShapes already did, fed from ShapeEngine
	// instead of a plain shape list. Only one of the two copies got fixed when the ordering rule
	// changed, so drawing with an actual shape tool kept landing new shapes above an effect that was
	// already on the layer even after the Core-side bug was fixed. Store now delegates to
	// ReplaceShapes; this exercises the real Tools-side entry point through
	// ShapeEngineCollection.Create, the way a shape tool actually builds its live engines - the path
	// Pinta.Core.Tests cannot reach at all.
	[Test]
	public void StoreInsertsNewShapesBelowAnExistingEffect ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		EffectModifierNode invert = Invert ();
		AddObject (layer, invert, "Invert");

		ShapeObject first = Box (ShapeFill, new RectangleI (0, 0, CanvasSize - 1, CanvasSize - 1));
		ShapeObject second = Box (ShapeFill, new RectangleI (0, 0, CanvasSize - 1, CanvasSize - 1));

		List<ShapeEngine> engines = [LiveEngine (layer, first), LiveEngine (layer, second)];

		ShapeEngineCollection.Store (layer, engines);

		Assert.Multiple (() => {
			Assert.That (layer.Objects.Count, Is.EqualTo (3), "both shapes and the effect have to survive the store");
			Assert.That (layer.Objects[0], Is.InstanceOf<ShapeObject> (), "shapes drawn after it insert below the effect");
			Assert.That (layer.Objects[1], Is.InstanceOf<ShapeObject> ());
			Assert.That (layer.Objects[2], Is.SameAs (invert), "the effect has to stay on top");
		});

		Refresh (layer);
		ColorBgra shown = Shown (layer, 4, 4);
		Assert.That ((shown.R, shown.G, shown.B), Is.EqualTo (((byte) 255, (byte) 0, (byte) 255)),
			"the effect above them should still be reaching what they drew");
	}
}
