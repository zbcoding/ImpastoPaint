using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A user can remove a key ("None") from a tool binding. That is a saved empty override, and it has to
/// stay distinct from having no override: reading it as "unset" hands the default key straight back,
/// so the binding the user cleared keeps firing.
/// </summary>
[TestFixture]
internal sealed class RemovedBindingTest : ToolsTestHarness
{
	private static readonly ToolBindingDescriptor Decrease = KeyboardShortcutManager.BrushDecreaseWidth;
	private static readonly ToolBindingDescriptor Increase = KeyboardShortcutManager.BrushIncreaseWidth;

	[TearDown]
	public void RestoreBindings ()
	{
		PintaCore.Shortcuts.ResetToolBinding (Decrease);
		PintaCore.Shortcuts.ResetToolBinding (Increase);
	}

	[Test]
	public void ARemovedKeyDoesNotFallBackToTheDefault ()
	{
		Assert.That (Decrease.DefaultGesture.IsValid, Is.True, "setup: the binding has to have a default to fall back to");

		PintaCore.Shortcuts.SetToolBinding (Decrease, KeyGesture.None);

		Assert.That (PintaCore.Shortcuts.GetToolBinding (Decrease).IsValid, Is.False,
			"the user removed the key; the default must not come back");
	}

	// Binding None to a second command in the same tab is not a key clash: neither owns a key.
	// Treating it as one would strip the first command's removal, and its default would return.
	[Test]
	public void RemovingASecondKeyInTheSameTabKeepsTheFirstRemoval ()
	{
		PintaCore.Shortcuts.SetToolBinding (Decrease, KeyGesture.None);
		PintaCore.Shortcuts.SetToolBinding (Increase, KeyGesture.None);

		Assert.Multiple (() => {
			Assert.That (PintaCore.Shortcuts.GetToolBinding (Decrease).IsValid, Is.False);
			Assert.That (PintaCore.Shortcuts.GetToolBinding (Increase).IsValid, Is.False);
		});
	}

	[Test]
	public void ResettingARemovedKeyRestoresTheDefault ()
	{
		PintaCore.Shortcuts.SetToolBinding (Decrease, KeyGesture.None);

		PintaCore.Shortcuts.ResetToolBinding (Decrease);

		Assert.That (PintaCore.Shortcuts.GetToolBinding (Decrease), Is.EqualTo (Decrease.DefaultGesture));
	}
}
