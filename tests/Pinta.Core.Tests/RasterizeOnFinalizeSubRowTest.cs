using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A rasterize-on-finalize object is transient - it fuses into the layer's raster the moment editing
/// moves on, so <see cref="UserLayer.GetsSubRow"/> (and the <see cref="UserLayer.HasObjectSubNodes"/>
/// check built on it) must exclude it from the layers dock, matching the sub-row builder in
/// Pinta.Gui.Widgets. Already true for shapes; text objects gained the same rule once the Object/Raster
/// dropdown could flip an existing text object's mode in place.
/// </summary>
[TestFixture]
internal sealed class RasterizeOnFinalizeSubRowTest : DocumentHarness
{
	[Test]
	public void RasterizeOnFinalizeTextObjectGetsNoSubRow ()
	{
		UserLayer layer = Layer (0);
		TextObject text = Text ("hello", new PointI (0, 0));
		text.RasterizeOnFinalize = true;
		layer.AddText (text);

		Assert.That (UserLayer.GetsSubRow (text), Is.False);
		Assert.That (layer.HasObjectSubNodes, Is.False,
			"a layer whose only object is rasterize-on-finalize text has nothing to show");
	}

	[Test]
	public void ObjectModeTextObjectGetsASubRow ()
	{
		UserLayer layer = Layer (0);
		TextObject text = Text ("hello", new PointI (0, 0));
		text.RasterizeOnFinalize = false;
		layer.AddText (text);

		Assert.That (UserLayer.GetsSubRow (text), Is.True);
		Assert.That (layer.HasObjectSubNodes, Is.True);
	}

	[Test]
	public void RasterizeOnFinalizeShapeGetsNoSubRow ()
	{
		UserLayer layer = Layer (0);
		ShapeObject shape = Box (new Cairo.Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3));
		shape.RasterizeOnFinalize = true;
		layer.AddShape (shape);

		Assert.That (UserLayer.GetsSubRow (shape), Is.False);
		Assert.That (layer.HasObjectSubNodes, Is.False);
	}
}
