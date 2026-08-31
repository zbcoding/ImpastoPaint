using System;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// SurfaceDiff.Create only reads original's pixels and never releases it - every history item that
/// calls it with a caller-owned "before" clone has to dispose that clone itself once the diff
/// succeeds, or it leaks a full-canvas native surface per undo step. SimpleHistoryItem's own
/// constructor comment named the intent ("If the diff was too big, store the original surface, else,
/// dispose it"); the else branch was never written, in all three Core history items that follow this
/// pattern.
/// </summary>
[TestFixture]
internal sealed class HistoryItemSurfaceOwnershipTest : DocumentHarness
{
	private static readonly RectangleI OnePixel = new (0, 0, 1, 1);

	[Test]
	public void SimpleHistoryItemDisposesTheOriginalSurfaceOnASuccessfulDiff ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);

		ImageSurface before = layer.Surface.Clone ();
		FillRect (layer.Surface, OnePixel, Blue);

		_ = new SimpleHistoryItem (string.Empty, "test", before, layerIndex: 0);

		Assert.Throws<ObjectDisposedException> (() => _ = before.Width,
			"a successful diff has to dispose the caller's clone, not leak it");
	}

	[Test]
	public void TextHistoryItemDisposesBothOriginalSurfacesOnASuccessfulDiff ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		Fill (layer.ObjectLayer.Layer.Surface, Red);

		ImageSurface beforeUser = layer.Surface.Clone ();
		ImageSurface beforeObject = layer.ObjectLayer.Layer.Surface.Clone ();
		FillRect (layer.Surface, OnePixel, Blue);
		FillRect (layer.ObjectLayer.Layer.Surface, OnePixel, Blue);

		_ = new TextHistoryItem (PintaCore.Workspace, string.Empty, "test", beforeObject, beforeUser, [], layer);

		Assert.Multiple (() => {
			Assert.Throws<ObjectDisposedException> (() => _ = beforeObject.Width,
				"a successful diff has to dispose the caller's object-layer clone, not leak it");
			Assert.Throws<ObjectDisposedException> (() => _ = beforeUser.Width,
				"a successful diff has to dispose the caller's base-raster clone, not leak it");
		});
	}

	[Test]
	public void RasterizeObjectsHistoryItemDisposesBothOriginalSurfacesOnASuccessfulDiff ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		Fill (layer.ObjectLayer.Layer.Surface, Red);

		ImageSurface beforeBase = layer.Surface.Clone ();
		ImageSurface beforeObject = layer.ObjectLayer.Layer.Surface.Clone ();
		FillRect (layer.Surface, OnePixel, Blue);
		FillRect (layer.ObjectLayer.Layer.Surface, OnePixel, Blue);

		_ = new RasterizeObjectsHistoryItem (PintaCore.Workspace, string.Empty, "test", beforeBase, beforeObject, [], layer);

		Assert.Multiple (() => {
			Assert.Throws<ObjectDisposedException> (() => _ = beforeBase.Width,
				"a successful diff has to dispose the caller's base-raster clone, not leak it");
			Assert.Throws<ObjectDisposedException> (() => _ = beforeObject.Width,
				"a successful diff has to dispose the caller's object-layer clone, not leak it");
		});
	}

	// SimpleHistoryItem.TargetSurface used to silently fall back to the layer's colour surface when
	// asked for a mask that no longer exists, which would have applied a mask-derived diff to raster
	// pixels instead of failing where the mismatch actually is. Believed unreachable under strictly
	// sequential undo/redo, but nothing guarded it - see LayerMaskHistoryItem.Set for the analogous
	// hazard it already avoids.
	[Test]
	public void UndoingAMaskTargetedItemAfterTheMaskIsDroppedFailsLoudly ()
	{
		UserLayer layer = Layer (0);
		layer.CreateMask ();
		Fill (layer.Mask!.Surface, Red);

		ImageSurface before = layer.Mask.Surface.Clone ();
		FillRect (layer.Mask.Surface, OnePixel, Blue);

		SimpleHistoryItem item = new (string.Empty, "test", before, layerIndex: 0, targetIsMask: true) {
			Document = Document,
		};

		layer.DropMask ();

		Assert.Throws<InvalidOperationException> (item.Undo,
			"undoing against a mask that no longer exists has to fail loudly, not silently corrupt the raster surface");
	}
}
