using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The layers dock addresses sub-rows by unified <see cref="UserLayer.Objects"/> position while the
/// canvas tools select through kind-scoped lists, so both directions of the mapping have to agree:
/// selecting the k-th shape on the canvas must highlight the same row that clicking that row
/// would edit. This pins <see cref="UserLayer.ObjectIndexOfKind"/> as the exact inverse of
/// <see cref="UserLayer.UserLayerIndexOfKind"/>, including across interleaved text and the
/// non-object entries (effects) that share the unified list but belong to neither kind.
/// </summary>
[TestFixture]
internal sealed class ObjectIndexOfKindTest : DocumentHarness
{
	[Test]
	public void BothDirectionsAgreeAcrossInterleavedObjects ()
	{
		UserLayer layer = Layer (0);

		ShapeObject first = Box (new Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (first);
		TextObject text = Text ("A", new PointI (0, 8));
		layer.AddText (text);
		ShapeObject second = Box (new Color (0, 0, 1, 1), new RectangleI (0, 0, 3, 3));
		layer.AddShape (second);

		Assert.Multiple (() => {
			Assert.That (UserLayer.ObjectIndexOfKind (layer, isText: false, kindIndex: 0), Is.EqualTo (0));
			Assert.That (UserLayer.ObjectIndexOfKind (layer, isText: true, kindIndex: 0), Is.EqualTo (1));
			Assert.That (UserLayer.ObjectIndexOfKind (layer, isText: false, kindIndex: 1), Is.EqualTo (2));

			Assert.That (UserLayer.UserLayerIndexOfKind (layer, isText: false, objectIndex: 0), Is.EqualTo (0));
			Assert.That (UserLayer.UserLayerIndexOfKind (layer, isText: true, objectIndex: 1), Is.EqualTo (0));
			Assert.That (UserLayer.UserLayerIndexOfKind (layer, isText: false, objectIndex: 2), Is.EqualTo (1));
		});
	}

	[Test]
	public void MissingKindIndexReturnsNegativeOne ()
	{
		UserLayer layer = Layer (0);
		layer.AddShape (Box (new Color (1, 0, 0, 1), new RectangleI (0, 0, 3, 3)));

		Assert.Multiple (() => {
			Assert.That (UserLayer.ObjectIndexOfKind (layer, isText: false, kindIndex: 3), Is.EqualTo (-1));
			Assert.That (UserLayer.ObjectIndexOfKind (layer, isText: true, kindIndex: 0), Is.EqualTo (-1));
		});
	}
}
