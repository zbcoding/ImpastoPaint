using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Where a freshly added object lands in <see cref="UserLayer.Objects"/>. The list is bottom-to-top:
/// <see cref="Pinta.Core.ObjectOpacity.RenderLayerObjects"/> walks it from index 0, and a modifier node
/// only reaches what sits at a lower index than itself. Stacking rule: an object goes directly above
/// the previous topmost object - the last thing drawn is seen on top among objects and heads the
/// sub-layer menu - while an effect already on the layer stays above everything drawn afterwards and
/// keeps applying to it.
/// </summary>
[TestFixture]
internal sealed class LayerObjectInsertionOrderTest : DocumentHarness
{
	[Test]
	public void EachAdditionLandsAboveThePreviousObjects ()
	{
		UserLayer layer = Layer (0);

		ShapeObject first = Box (new Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (first);

		TextObject second = Text ("A", new PointI (0, 8));
		layer.AddText (second);

		ShapeObject third = Box (new Color (0, 0, 1, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (third);

		Assert.That (layer.Objects, Is.EqualTo (new ILayerObject[] { first, second, third }),
			"each addition goes above the previous objects, so the last thing drawn is on top");
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

	/// <summary>
	/// The real drawing flow calls <c>ShapeEngineCollection.Store</c> (and so
	/// <see cref="UserLayer.ReplaceShapes"/>) once per shape, handing it the *entire* live engine list
	/// every time - each call's list is the previous one's plus one new entry appended at the end, in
	/// draw order. Regression coverage for a bug where such repeated calls silently swapped
	/// already-persisted shapes' geometry between each other's z-slots (without moving anything in the
	/// layers dock): pairing the incoming draw-order list against the existing slots by raw position
	/// must line each persisted shape back up with its own geometry, whatever stacking rule inserts
	/// the new entries.
	/// </summary>
	[Test]
	public void SequentialReplaceShapesCallsDoNotSwapAlreadyPersistedShapes ()
	{
		UserLayer layer = Layer (0);

		ShapeObject first = Box (new Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3));
		ShapeObject second = Box (new Color (0, 1, 0, 1), new RectangleI (0, 0, 3, 3));
		ShapeObject third = Box (new Color (0, 0, 1, 1), new RectangleI (0, 0, 3, 3));

		layer.ReplaceShapes ([first]);
		layer.ReplaceShapes ([first, second]);
		layer.ReplaceShapes ([first, second, third]);

		Assert.That (layer.Objects, Is.EqualTo (new ILayerObject[] { first, second, third }),
			"each shape keeps its own geometry, and each lands above everything drawn before it");
	}
}
