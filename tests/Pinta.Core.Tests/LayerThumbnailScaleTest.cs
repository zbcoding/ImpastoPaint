using System;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The layers pad's Thumbnail Size slider bottoms out at hiding the thumbnails and grows them
/// through fixed steps, and a hand-edited settings file must not crash the layer list with an
/// out-of-range step. Each test pins one of those promises; the row layout and the menu slider
/// itself are verified live in the app.
/// </summary>
[TestFixture]
internal sealed class LayerThumbnailScaleTest
{
	private object? saved_step;

	// Same reason as PopoverHintModeTest: reading a setting touches PintaCore, whose static
	// constructor needs the bindings loaded even though no widget is created here.
	[OneTimeSetUp]
	public void InitializeGtk () => Gtk.Module.Initialize ();

	[SetUp]
	public void SaveStep ()
	{
		saved_step = PintaCore.Settings.GetSetting<object?> (LayerThumbnailScale.SettingKey, null);
	}

	// The settings service has no remove, so an absent value is restored as the default rather
	// than left at whatever step the test just set - these run against the one static PintaCore.
	[TearDown]
	public void RestoreStep ()
	{
		PintaCore.Settings.PutSetting (
			LayerThumbnailScale.SettingKey,
			saved_step ?? 2);
	}

	/// <summary>
	/// The default step renders the size the rows have always used, so existing installs see no
	/// change until they touch the slider.
	/// </summary>
	[Test]
	public void DefaultStepKeepsHistoricalSize ()
	{
		LayerThumbnailScale.Step = 2;

		Assert.That (LayerThumbnailScale.Width, Is.EqualTo (60));
		Assert.That (LayerThumbnailScale.Height, Is.EqualTo (40));
	}

	/// <summary>
	/// The slider's lowest step hides the thumbnails; every other step shows them.
	/// </summary>
	[Test]
	public void OffStepHidesThumbnails ()
	{
		LayerThumbnailScale.Step = 0;

		Assert.That (LayerThumbnailScale.Width, Is.EqualTo (0));
		Assert.That (LayerThumbnailScale.Enabled, Is.False);
	}
	[Test]
	public void EnabledStepsShowThumbnails ()
	{
		for (int step = 1; step <= LayerThumbnailScale.MaxStep; ++step) {
			LayerThumbnailScale.Step = step;

			Assert.That (LayerThumbnailScale.Width, Is.GreaterThan (0));
			Assert.That (LayerThumbnailScale.Enabled, Is.True);
		}
	}

	/// <summary>
	/// Every visible step keeps the rows' 3:2 shape and is strictly larger than the last, so a
	/// typo in the size table can't ship a square thumbnail or a slider that shrinks going right.
	/// </summary>
	[Test]
	public void StepsKeepShapeAndGrowMonotonically ()
	{
		int last_width = 0;

		for (int step = 1; step <= LayerThumbnailScale.MaxStep; ++step) {
			LayerThumbnailScale.Step = step;

			int width = LayerThumbnailScale.Width;
			Assert.That (width, Is.GreaterThan (last_width));
			Assert.That (LayerThumbnailScale.Height, Is.EqualTo (width * 2 / 3));

			last_width = width;
		}
	}

	/// <summary>
	/// A stored step from a hand-edited (or newer-version) settings file clamps into range: the
	/// layer list indexes its size table by this value, so an unclamped read would throw.
	/// </summary>
	[Test]
	public void OutOfRangeStoredStepClamps ()
	{
		PintaCore.Settings.PutSetting (LayerThumbnailScale.SettingKey, 99);
		Assert.That (LayerThumbnailScale.Step, Is.EqualTo (LayerThumbnailScale.MaxStep));
		Assert.That (LayerThumbnailScale.Width, Is.GreaterThan (0));

		PintaCore.Settings.PutSetting (LayerThumbnailScale.SettingKey, -3);
		Assert.That (LayerThumbnailScale.Step, Is.EqualTo (0));
		Assert.That (LayerThumbnailScale.Enabled, Is.False);
	}

	/// <summary>
	/// Open lists resize on Changed, so it fires exactly when the visible size moves - a drag
	/// settling back on its starting step must not thrash every row.
	/// </summary>
	[Test]
	public void ChangedFiresOnRealChangeOnly ()
	{
		LayerThumbnailScale.Step = 1;

		int fires = 0;
		void Count (object? sender, EventArgs args) => ++fires;
		LayerThumbnailScale.Changed += Count;

		try {
			LayerThumbnailScale.Step = 3;
			LayerThumbnailScale.Step = 3;
		} finally {
			LayerThumbnailScale.Changed -= Count;
		}

		Assert.That (fires, Is.EqualTo (1));
	}

	/// <summary>
	/// The step round-trips through the settings service, which is what remembers it between
	/// sessions.
	/// </summary>
	[Test]
	public void StepRoundTripsToSettings ()
	{
		LayerThumbnailScale.Step = 4;

		Assert.That (
			PintaCore.Settings.GetSetting (LayerThumbnailScale.SettingKey, -1),
			Is.EqualTo (4));
	}
}
