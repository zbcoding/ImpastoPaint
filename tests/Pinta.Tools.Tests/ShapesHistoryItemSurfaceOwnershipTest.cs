using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Same leak as Pinta.Core.Tests.HistoryItemSurfaceOwnershipTest, in the Tools-side sibling that
/// follows the identical pattern: SurfaceDiff.Create only reads the caller's "before" clone and never
/// releases it, so a successful diff has to dispose that clone itself or it leaks a full-canvas
/// native surface on every shape create/delete/finalize.
/// </summary>
[TestFixture]
internal sealed class ShapesHistoryItemSurfaceOwnershipTest : ToolsTestHarness
{
	[Test]
	public void DisposesTheOriginalSurfaceOnASuccessfulDiff ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		ImageSurface before = layer.Surface.Clone ();
		FillRect (layer.Surface, new RectangleI (0, 0, 1, 1), Transparent);

		EllipseTool tool = new (PintaCore.Services);
		_ = new ShapesHistoryItem (tool.EditEngine, string.Empty, "test", before, layer, -1, -1, false);

		Assert.Throws<ObjectDisposedException> (() => _ = before.Width,
			"a successful diff has to dispose the caller's clone, not leak it");
	}

	private static void FillRect (ImageSurface surface, RectangleI rect, ColorBgra color)
	{
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int y = rect.Top; y <= rect.Bottom; ++y)
			for (int x = rect.Left; x <= rect.Right; ++x)
				data[(y * surface.Width) + x] = color;
		surface.MarkDirty ();
	}
}
