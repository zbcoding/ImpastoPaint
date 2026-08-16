using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class SoloLayerHistoryItemTests
{
	[OneTimeSetUp]
	public void OneTimeSetUp ()
		=> Utilities.EnsureNativeLibraries ();

	[Test]
	public void UndoRedo_RestoresVisibilityAndReappliesSoloState ()
	{
		using ImageSurface bottomSurface = new (Format.Argb32, 1, 1);
		using ImageSurface middleSurface = new (Format.Argb32, 1, 1);
		using ImageSurface topSurface = new (Format.Argb32, 1, 1);

		UserLayer bottom = new (bottomSurface) { Hidden = false };
		UserLayer middle = new (middleSurface) { Hidden = true };
		UserLayer top = new (topSurface) { Hidden = false };

		SoloLayerHistoryItem historyItem = new (
			"icon",
			"Solo Layer",
			[bottom, middle, top],
			middle);

		historyItem.Redo ();
		Assert.Multiple (() => {
			Assert.That (bottom.Hidden, Is.True);
			Assert.That (middle.Hidden, Is.False);
			Assert.That (top.Hidden, Is.True);
		});

		historyItem.Undo ();
		Assert.Multiple (() => {
			Assert.That (bottom.Hidden, Is.False);
			Assert.That (middle.Hidden, Is.True);
			Assert.That (top.Hidden, Is.False);
		});

		historyItem.Redo ();
		Assert.Multiple (() => {
			Assert.That (bottom.Hidden, Is.True);
			Assert.That (middle.Hidden, Is.False);
			Assert.That (top.Hidden, Is.True);
		});
	}

	[Test]
	public void AlreadySolo_HasNoChanges ()
	{
		using ImageSurface bottomSurface = new (Format.Argb32, 1, 1);
		using ImageSurface topSurface = new (Format.Argb32, 1, 1);

		UserLayer bottom = new (bottomSurface) { Hidden = false };
		UserLayer top = new (topSurface) { Hidden = true };

		SoloLayerHistoryItem historyItem = new (
			"icon",
			"Solo Layer",
			[bottom, top],
			bottom);

		Assert.That (historyItem.HasChanges, Is.False);
	}
}
