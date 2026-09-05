using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The middle popover-hint tier exists to keep the toolbox discoverable while leaving the canvas
/// alone, so it has to be a real subset - it was shipped once as a duplicate of "All".
/// </summary>
[TestFixture]
internal sealed class PopoverHintModeTest
{
	private object? saved_mode;

	// Nothing here maps a popover: a GTK popup needs a realized toplevel, which a headless run has
	// no display for. The gate properties are the whole policy - every hint surface asks one of
	// them before it shows anything - so this fixture pins the policy, not the widget mechanics.

	[SetUp]
	public void SaveMode ()
	{
		saved_mode = PintaCore.Settings.GetSetting<object?> (TransientHintPopover.SettingKey, null);
	}

	[TearDown]
	public void RestoreMode ()
	{
		if (saved_mode is not null)
			PintaCore.Settings.PutSetting (TransientHintPopover.SettingKey, saved_mode);
	}

	private static void SetMode (PopoverHintMode mode)
		=> PintaCore.Settings.PutSetting (TransientHintPopover.SettingKey, (int) mode);

	[Test]
	public void ToolButtonsOnlyShowsToolButtonHintsAndNothingElse ()
	{
		SetMode (PopoverHintMode.ToolButtonsOnly);

		Assert.Multiple (() => {
			Assert.That (TransientHintPopover.ShouldShowToolButtonHint, Is.True, "toolbox tool buttons keep their hints");
			Assert.That (TransientHintPopover.ShouldShow, Is.False, "the canvas and other chrome do not");
		});
	}

	[Test]
	public void AllShowsEverySurface ()
	{
		SetMode (PopoverHintMode.All);

		Assert.Multiple (() => {
			Assert.That (TransientHintPopover.ShouldShowToolButtonHint, Is.True);
			Assert.That (TransientHintPopover.ShouldShow, Is.True);
		});
	}

	[Test]
	public void NoneShowsNoSurface ()
	{
		SetMode (PopoverHintMode.None);

		Assert.Multiple (() => {
			Assert.That (TransientHintPopover.ShouldShowToolButtonHint, Is.False);
			Assert.That (TransientHintPopover.ShouldShow, Is.False);
		});
	}

	/// <summary>
	/// The stored value is the enum's ordinal, so a settings file written before the tier was
	/// renamed still has to select the middle tier rather than shift onto None.
	/// </summary>
	[Test]
	public void StoredOrdinalStillSelectsTheMiddleTier ()
	{
		PintaCore.Settings.PutSetting (TransientHintPopover.SettingKey, 1);

		Assert.That (TransientHintPopover.Mode, Is.EqualTo (PopoverHintMode.ToolButtonsOnly));
	}
}
