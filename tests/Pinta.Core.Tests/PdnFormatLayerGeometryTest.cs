using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// PdnFormat.CopyPixels recovers bytes-per-pixel as (stride * 8 / width). Unlike the document's own
/// width/height, a layer's width/height/stride/length come straight from the file's NRBF record with
/// no bounds check, so a crafted 0 width used to throw an unhelpful DivideByZeroException (or, for a
/// crafted huge stride, silently overflow into a bogus bpp) instead of the InvalidDataException this
/// importer raises for every other malformed field. A layer's declared length and its dimensions
/// against the document's were unchecked entirely: a crafted length64 forced a multi-gigabyte
/// zeroing allocation, and a layer larger than its document threw a raw IndexOutOfRangeException deep
/// inside CopyPixels instead of failing here.
/// </summary>
[TestFixture]
internal sealed class PdnFormatLayerGeometryTest
{
	// stride * height for the "ordinary" 100x50 layer below - the only length CopyPixels can index
	// without running off either end of the buffer.
	private const long OrdinaryLength = 400 * 50;

	[TestCase (0, 10, 40, TestName = "Zero width")]
	[TestCase (10, 0, 40, TestName = "Zero height")]
	[TestCase (-5, 10, 40, TestName = "Negative width")]
	[TestCase (30000, 10, 40, TestName = "Width over the 20000 cap")]
	public void InvalidWidthOrHeightThrowsInvalidData (int width, int height, int stride)
	{
		Assert.Throws<InvalidDataException> (() =>
			PdnFormat.ValidateLayerGeometry (width, height, stride, (long) stride * height, width, height, layerIndex: 0));
	}

	[TestCase (0, TestName = "Zero stride")]
	[TestCase (-4, TestName = "Negative stride")]
	[TestCase (int.MaxValue, TestName = "Overflow-prone stride")]
	public void InvalidStrideThrowsInvalidData (int stride)
	{
		Assert.Throws<InvalidDataException> (() =>
			PdnFormat.ValidateLayerGeometry (10, 10, stride, 100, 10, 10, layerIndex: 0));
	}

	[Test]
	public void OrdinaryLayerGeometryPasses ()
	{
		Assert.DoesNotThrow (() =>
			PdnFormat.ValidateLayerGeometry (100, 50, 400, OrdinaryLength, 100, 50, layerIndex: 0));
	}

	// The original bug: a crafted length64 forces `new byte[length]` to zero a multi-gigabyte
	// buffer. Requiring an exact match against stride*height closes it without a separate ceiling -
	// stride and height are already capped above this check, so the match caps length too. No
	// allocation is attempted here; ValidateLayerGeometry only compares numbers.
	[TestCase (OrdinaryLength * 1000, TestName = "Length wildly larger than stride*height")]
	[TestCase (OrdinaryLength - 1, TestName = "Length smaller than stride*height")]
	[TestCase (0, TestName = "Zero length")]
	public void MismatchedLengthThrowsInvalidData (long length)
	{
		Assert.Throws<InvalidDataException> (() =>
			PdnFormat.ValidateLayerGeometry (100, 50, 400, length, 100, 50, layerIndex: 0));
	}

	[TestCase (200, 50, TestName = "Layer wider than its document")]
	[TestCase (100, 100, TestName = "Layer taller than its document")]
	[TestCase (50, 50, TestName = "Layer smaller than its document")]
	public void LayerDimensionsMismatchingTheDocumentThrowsInvalidData (int docWidth, int docHeight)
	{
		Assert.Throws<InvalidDataException> (() =>
			PdnFormat.ValidateLayerGeometry (100, 50, 400, OrdinaryLength, docWidth, docHeight, layerIndex: 0));
	}

	// The original bug: `new List<LayerInfo>(layersSize)` pre-allocated straight from a claimed
	// ArrayList._size with no bound of its own - a crafted multi-billion claim forced a
	// multi-gigabyte allocation before the loop that validates each entry ever ran. The fix clamps
	// to what GetArray actually decoded, so capacity can never exceed real, already-materialized
	// memory. int.MaxValue is passed straight through to prove that without allocating anything.
	[TestCase (5, 5, 5, TestName = "Claim matches the actual array")]
	[TestCase (-1, 5, 0, TestName = "Negative claim clamps to zero")]
	[TestCase (int.MaxValue, 3, 3, TestName = "Huge claim clamps to the actual array, not allocated")]
	[TestCase (2, 8, 2, TestName = "Claim smaller than the actual array is left alone")]
	public void SafeLayerListCapacityClampsToTheActualArray (int claimedSize, int actualArrayLength, int expected)
		=> Assert.That (PdnFormat.SafeLayerListCapacity (claimedSize, actualArrayLength), Is.EqualTo (expected));
}
