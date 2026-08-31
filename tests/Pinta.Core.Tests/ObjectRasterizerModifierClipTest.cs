using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// PrepareForSelectionRasterOp routes a selection-driven raster op to RasterizeSubset (bakes only the
/// objects the selection overlaps, at their raw geometry - ObjectLayerRenderWalk has no arm for
/// modifier nodes) unless SelectionReachesAnyModifier says the SELECTION itself reaches a clipped
/// modifier's region. That misses the case where the clip doesn't reach the selection but does reach
/// one of the objects the subset is about to bake: RasterizeSubset then commits that object's raw,
/// un-modified pixels straight into the base raster - silently different from what
/// ObjectOpacity.RenderLayerObjects was showing through the modifier a frame earlier. The live
/// composite can look right afterwards purely because the modifier node itself is never removed and
/// re-applies on the next render; what's actually wrong is what got committed to layer.Surface.
/// </summary>
[TestFixture]
internal sealed class ObjectRasterizerModifierClipTest : DocumentHarness
{
	private static readonly Color FillColor = new (1, 0, 0, 1);

	// Shape A sits under a clipped Invert node whose clip covers only part of it.
	private static (ShapeObject shapeA, EffectModifierNode invert) Scene ()
		=> (
			Box (FillColor, new RectangleI (0, 0, 4, 4)),
			Invert (SelectionOf (new RectangleI (0, 0, 2, 2))));

	[Test]
	public void AClipThatMissesTheSelectionButReachesTheBakedObjectStillApplies ()
	{
		UserLayer live = Layer (0);
		Fill (live.Surface, Transparent);
		(ShapeObject liveShapeA, EffectModifierNode liveInvert) = Scene ();
		live.Objects.Add (liveShapeA);
		live.Objects.Add (liveInvert);

		// Overlaps shape A's bounds (0,0)-(4,4) but not the clip (0,0)-(2,2).
		DocumentSelection selection = SelectionOf (new RectangleI (3, 3, 2, 2));
		Assert.That (ObjectRasterizer.SelectionReachesAnyModifier (live, selection), Is.False,
			"setup: the selection itself must not reach the clip - that path is covered elsewhere");

		Assert.That (
			ObjectRasterizer.PrepareForSelectionRasterOp (Document, PintaCore.Workspace, PintaCore.Chrome, live, selection),
			Is.True);

		// Reference: the identical scene, composited the normal way and never baked.
		UserLayer reference = new (CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize));
		(ShapeObject refShapeA, EffectModifierNode refInvert) = Scene ();
		reference.Objects.Add (refShapeA);
		reference.Objects.Add (refInvert);
		ObjectOpacity.RenderLayerObjects (PintaCore.Chrome, reference);

		Assert.Multiple (() => {
			Assert.That (live.HasModifiers, Is.False,
				"reaching the object through the clip has to force the whole-stack bake, dropping the modifier");
			Assert.That (live.Surface.GetColorBgra (new PointI (1, 1)),
				Is.EqualTo (reference.Composite!.GetColorBgra (new PointI (1, 1))),
				"the object's pixels under the clip must be committed already-inverted, matching what the " +
				"composite showed - not baked raw with the modifier left to reapply and hide the mistake");
		});
	}
}
