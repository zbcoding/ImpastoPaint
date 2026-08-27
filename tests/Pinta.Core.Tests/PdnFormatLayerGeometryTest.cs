using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// PdnFormat.CopyPixels recovers bytes-per-pixel as (stride * 8 / width). Unlike the document's own
/// width/height, a layer's width/height/stride come straight from the file's NRBF record with no
/// bounds check, so a crafted 0 width used to throw an unhelpful DivideByZeroException (or, for a
/// crafted huge stride, silently overflow into a bogus bpp) instead of the InvalidDataException this
/// importer raises for every other malformed field.
/// </summary>
[TestFixture]
internal sealed class PdnFormatLayerGeometryTest
{
	[TestCase (0, 10, 40, TestName = "Zero width")]
	[TestCase (10, 0, 40, TestName = "Zero height")]
	[TestCase (-5, 10, 40, TestName = "Negative width")]
	[TestCase (30000, 10, 40, TestName = "Width over the 20000 cap")]
	public void InvalidWidthOrHeightThrowsInvalidData (int width, int height, int stride)
	{
		Assert.Throws<InvalidDataException> (() => PdnFormat.ValidateLayerGeometry (width, height, stride, layerIndex: 0));
	}

	[TestCase (0, TestName = "Zero stride")]
	[TestCase (-4, TestName = "Negative stride")]
	[TestCase (int.MaxValue, TestName = "Overflow-prone stride")]
	public void InvalidStrideThrowsInvalidData (int stride)
	{
		Assert.Throws<InvalidDataException> (() => PdnFormat.ValidateLayerGeometry (10, 10, stride, layerIndex: 0));
	}

	[Test]
	public void OrdinaryLayerGeometryPasses ()
	{
		Assert.DoesNotThrow (() => PdnFormat.ValidateLayerGeometry (100, 50, 400, layerIndex: 0));
	}
}
