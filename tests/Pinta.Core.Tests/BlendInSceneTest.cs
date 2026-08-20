using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Blend modes composed with the rest of the object model. <see cref="BlendOpTests"/> proves the
/// per-pixel arithmetic and <see cref="ObjectBlendTest"/> proves a blended object leaves uncovered
/// pixels alone; what these add is what each blend is handed as its destination — a node's output
/// rather than the raster underneath it, a mask applied after the blend rather than before, and a
/// layer blending against the composite of the modifier-carrying layer below it.
/// </summary>
[TestFixture]
internal sealed class BlendInSceneTest : DocumentHarness
{
	// Multiply against a mid-grey halves each channel, which makes the destination legible in the
	// result: halved cyan is (0, 128, 128) and halved red is (128, 0, 0), so a blend given the wrong
	// destination cannot land on the expected numbers by coincidence.
	private static readonly Color GrayFill = new (0.5, 0.5, 0.5, 1);
	private static readonly ColorBgra Gray = ColorBgra.FromBgra (128, 128, 128, 255);

	private UserLayer Bottom => Layer (0);

	// The node stack and the objects share one list, accumulated in order, so a shape added above a
	// node has to blend against what the node produced. Blending against the base raster instead
	// would show through as soon as the two differ.
	[Test]
	public void ABlendedShapeCompositesAgainstTheNodeBeneathIt ()
	{
		Fill (Bottom.Surface, Red);
		AddObject (Bottom, Invert (), "Invert");

		ShapeObject shape = Box (GrayFill, new RectangleI (0, 0, 16, 16));
		shape.BlendMode = BlendMode.Multiply;
		AddObject (Bottom, shape, "Box");

		Assert.Multiple (() => {
			Assert.That (Shown (Bottom, 4, 4).G, Is.EqualTo (128).Within (2), "cyan halved, not red halved");
			Assert.That (Shown (Bottom, 4, 4).R, Is.EqualTo (0), "red would have left 128 here");
			Assert.That (Shown (Bottom, 20, 20).G, Is.EqualTo (255), "outside the shape the node's output is untouched");
		});
	}

	// The mask multiplies the layer's accumulated alpha last of all. A blended shape is part of that
	// accumulation, so the mask has to scale the blend's result rather than one of its inputs.
	[Test]
	public void AMaskScalesTheResultOfABlendedShape ()
	{
		Fill (Bottom.Surface, Red);

		ShapeObject shape = Box (GrayFill, new RectangleI (0, 0, 16, 16));
		shape.BlendMode = BlendMode.Multiply;
		AddObject (Bottom, shape, "Box");

		LayerMask mask = Bottom.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (Bottom);

		Assert.Multiple (() => {
			Assert.That (Shown (Bottom, 4, 4).A, Is.EqualTo (128), "the mask halves the alpha under the shape too");
			Assert.That (Shown (Bottom, 4, 4).R, Is.EqualTo (64).Within (2),
				"red halved by the blend, then premultiplied by the half mask");
			Assert.That (Shown (Bottom, 20, 20).R, Is.EqualTo (128).Within (2),
				"outside the shape only the mask applies");
		});
	}

	// Flattening walks the layers bottom-up and blends each one's rendered result. A layer carrying
	// modifiers renders as its composite, so the layer above has to blend against that composite —
	// against the base raster it would multiply into the un-effected pixels nobody is looking at.
	[Test]
	public void ABlendedLayerCompositesAgainstTheModifierOutputBelowIt ()
	{
		Fill (Bottom.Surface, Red);
		AddObject (Bottom, Invert (), "Invert");

		UserLayer top = AddLayer (Gray);
		top.BlendMode = BlendMode.Multiply;

		using ImageSurface flattened = Document.GetFlattenedImage ();
		ColorBgra blended = flattened.GetColorBgra (new PointI (4, 4));

		Assert.Multiple (() => {
			Assert.That (blended.G, Is.EqualTo (128).Within (2), "cyan halved, not red halved");
			Assert.That (blended.R, Is.EqualTo (0), "red would have left 128 here");
		});
	}
}
