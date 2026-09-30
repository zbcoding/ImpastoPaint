using System;
using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A toolbar button's tooltip names the keys that trigger it. After the user rebinds one of those
/// keys the tooltip has to name the new key, or it teaches a shortcut that no longer works.
/// </summary>
[TestFixture]
internal sealed class ToolBarTooltipRebindTest : DocumentHarness
{
	private const string XdgConfigHome = "XDG_CONFIG_HOME";

	private string? saved_config_home;
	private string config_root = null!;

	// Rebinding writes keyboard-shortcuts.json into the user settings directory; keep it off the real one.
	[SetUp]
	public void RedirectSettingsDirectory ()
	{
		saved_config_home = Environment.GetEnvironmentVariable (XdgConfigHome);
		config_root = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Environment.SetEnvironmentVariable (XdgConfigHome, config_root);
	}

	[TearDown]
	public void RestoreSettingsDirectory ()
	{
		PintaCore.Shortcuts.ResetCommandShortcut (PintaCore.Actions.Edit.DeselectSelection);
		Environment.SetEnvironmentVariable (XdgConfigHome, saved_config_home);
		if (Directory.Exists (config_root))
			Directory.Delete (config_root, recursive: true);
	}

	[Test]
	public void DeselectTooltipNamesTheReboundQuickDeselectKey ()
	{
		EditActions edit = PintaCore.Actions.Edit;
		Gtk.Button deselect = edit.Deselect.CreateToolBarItem (alternate: edit.DeselectSelection, alternate_description: "Quick deselect");

		PintaCore.Shortcuts.SetCommandShortcut (edit.DeselectSelection, 0, "<Shift>F9");

		Assert.That (deselect.TooltipText, Does.Contain ("Quick deselect").And.Contain ("F9"));
	}

	[Test]
	public void TooltipNamesAShortcutAddedToAnUnboundCommand ()
	{
		EditActions edit = PintaCore.Actions.Edit;
		PintaCore.Shortcuts.SetCommandShortcut (edit.DeselectSelection, 0, string.Empty);
		Gtk.Button deselect = edit.Deselect.CreateToolBarItem (alternate: edit.DeselectSelection, alternate_description: "Quick deselect");
		Assume.That (deselect.TooltipText, Does.Not.Contain ("Quick deselect"));

		PintaCore.Shortcuts.SetCommandShortcut (edit.DeselectSelection, 0, "<Shift>F9");

		Assert.That (deselect.TooltipText, Does.Contain ("Quick deselect").And.Contain ("F9"));
	}
}
