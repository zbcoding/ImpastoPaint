using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Where a freshly added object or modifier node lands in <see cref="UserLayer.Objects"/>. The list is
/// bottom-to-top: <see cref="Pinta.Core.ObjectOpacity.RenderLayerObjects"/> walks it from index 0, and a
/// modifier node only reaches what sits at a lower index than itself. Regression coverage for a bug
/// where <see cref="UserLayer.AddShape"/>/<see cref="UserLayer.AddText"/> appended to the end of the
/// list (the top) instead: a shape or text object drawn after an effect landed above it and the effect
/// never touched it, contradicting the model's "history order" (a later addition still falls under an
/// earlier effect) and the "a modifier applies to everything below it" rule.
/// </summary>
[TestFixture]
internal sealed class LayerObjectInsertionOrderTest : DocumentHarness
{
	[Test]
	public void EachAdditionLandsBelowEverythingAlreadyOnTheLayer ()
	{
		UserLayer layer = Layer (0);

		ShapeObject first = Box (new Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (first);

		TextObject second = Text ("A", new PointI (0, 8));
		layer.AddText (second);

		ShapeObject third = Box (new Color (0, 0, 1, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (third);

		Assert.That (layer.Objects, Is.EqualTo (new ILayerObject[] { third, second, first }),
			"each addition should insert at index 0, so the newest object is always the new bottom of the stack");
	}

	[Test]
	public void AnEffectAddedFirstKeepsApplyingToAShapeDrawnAfterwards ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		// The effect is the only thing on the layer when it is added, so its own position does not
		// depend on the fix under test - only the shape's does.
		AddObject (layer, Invert (), "Invert");

		ShapeObject box = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, CanvasSize - 1, CanvasSize - 1));
		layer.AddShape (box);
		Refresh (layer);

		// Green (0,255,0) drawn under the Invert node comes out as (255,0,255). Before the fix, AddShape
		// appended above the node, so the shape would show its own colour untouched.
		ColorBgra shown = Shown (layer, 4, 4);
		Assert.That (shown.R, Is.EqualTo (255));
		Assert.That (shown.G, Is.EqualTo (0));
		Assert.That (shown.B, Is.EqualTo (255));
	}

	/// <summary>
	/// The path a shape tool actually uses to persist a freshly drawn shape is
	/// <c>ShapeEngineCollection.Store</c> (in Pinta.Tools, not reachable from this project), not
	/// <see cref="UserLayer.AddShape"/> - that only covers a caller adding one shape directly. Store
	/// shares its "new entries beyond the old count" rebuild with <see cref="UserLayer.ReplaceShapes"/>
	/// here in Core, which is what this pins: drawing two shapes (an old count of 0, then 2) after an
	/// effect must not leave them appended above it.
	/// </summary>
	[Test]
	public void ShapesBeyondTheOldCountInsertBelowAnExistingEffectOnReplace ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		EffectModifierNode invert = Invert ();
		AddObject (layer, invert, "Invert");

		// What ShapeEngineCollection.Store hands ReplaceShapes after two ellipses are drawn: the old
		// shape list (empty) plus the newly drawn ones, in draw order.
		ShapeObject first = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, CanvasSize - 1, CanvasSize - 1));
		ShapeObject second = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, CanvasSize - 1, CanvasSize - 1));
		layer.ReplaceShapes ([first, second]);

		Assert.That (layer.Objects, Is.EqualTo (new ILayerObject[] { first, second, invert }),
			"newly drawn shapes should land below the effect that was already there, in draw order");

		Refresh (layer);
		ColorBgra shown = Shown (layer, 4, 4);
		Assert.That ((shown.R, shown.G, shown.B), Is.EqualTo (((byte) 255, (byte) 0, (byte) 255)),
			"the effect above them should still be reaching what they drew");
	}
}
