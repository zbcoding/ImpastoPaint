using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Where an effect/modifier node goes when the user applies it to a layer that already carries
/// shape/text objects. An effect is meant to grade everything the user sees on the layer, so it
/// has to land on top - above the objects - and the sub-layer menu must show it there too. Going
// red on the reported symptom: an effect added after some shapes sat beneath them (raw
/// <c>Objects.Insert (0)</c>), filtering only the bare raster and leaving every object untouched.
/// </summary>
[TestFixture]
internal sealed class EffectInsertionOrderTest : DocumentHarness
{
	[Test]
	public void AnEffectAddedAfterShapesLandsOnTopAndGradesTheirInk ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Box (new Color (0, 0, 1, 1), new RectangleI (4, 4, 12, 12)), "Box");

		Assert.That (Shown (layer, 8, 8).B, Is.EqualTo (255),
			"the scene has to start with the blue shape visible, or this test proves nothing");

		// What LivePreviewManager does when the user applies an effect - the same seam, so this
		// pins the real insertion rule rather than a copy of it.
		layer.AddModifierNode (Invert ());
		Refresh (layer);

		Assert.Multiple (() => {
			Assert.That (Shown (layer, 8, 8), Is.EqualTo (ColorBgra.FromBgra (0, 255, 255, 255)),
				"the effect was applied last, so the shape ink the user sees has to come out inverted");
			Assert.That (Shown (layer, CanvasSize - 2, CanvasSize - 2).R, Is.EqualTo (0),
				"the bare raster away from the shape has to be inverted red, i.e. not stay red");
		});
	}
}
