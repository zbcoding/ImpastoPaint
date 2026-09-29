using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Both erasers walk the brush box clipped to the surface. The Smooth one's loops stopped one
/// short of the inclusive Right/Bottom, and the clip is exactly what lands there, so the canvas's
/// final column and row could never be erased however hard the user scrubbed them.
/// </summary>
[TestFixture]
internal sealed class EraserEdgeCoverageTest : EraserToolHarness
{
	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void ScrubbingTheRightEdgeErasesTheLastColumn (string type)
	{
		Configure (type);
		int lastColumn = Canvas.Width - 1;
		int middleRow = Canvas.Height / 2;

		Stroke ((lastColumn, middleRow - 4), (lastColumn, middleRow + 4));

		Assert.That (PixelAt (lastColumn, middleRow).A, Is.LessThan (Red.A),
			"the canvas's last column has to be erasable");
	}

	[TestCase ("Smooth")]
	[TestCase ("Normal")]
	public void ScrubbingTheBottomEdgeErasesTheLastRow (string type)
	{
		Configure (type);
		int lastRow = Canvas.Height - 1;
		int middleColumn = Canvas.Width / 2;

		Stroke ((middleColumn - 4, lastRow), (middleColumn + 4, lastRow));

		Assert.That (PixelAt (middleColumn, lastRow).A, Is.LessThan (Red.A),
			"the canvas's last row has to be erasable");
	}
}
