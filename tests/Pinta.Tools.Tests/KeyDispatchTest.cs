using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// Pins docs-private/refactor.md T6: TextTool and BaseEditEngine each dispatch keys through two
// paths - a configured-binding lookup and a hardcoded-keycode fallback - that have drifted out of
// sync before. The lookup tables backing the first path are the single source of truth for "which
// commands exist and which binding fires them"; this asserts each table is complete (every command
// bound exactly once) and that GetToolBinding on each descriptor actually consults the user's
// override map rather than silently always returning the compiled-in default.
[TestFixture]
internal sealed class KeyDispatchTest : ToolsTestHarness
{
	[Test]
	public void ShapeKeyBindingsCoverEveryCommandExactlyOnce ()
	{
		var commands = BaseEditEngine.shape_key_bindings.Select (kb => kb.Command).ToList ();
		var expected = Enum.GetValues<BaseEditEngine.ShapeKeyCommand> ();

		Assert.That (commands, Is.EquivalentTo (expected));
		Assert.That (commands.Distinct ().Count (), Is.EqualTo (commands.Count), "no ShapeKeyCommand should be bound twice");
	}

	[Test]
	public void TextKeyBindingsCoverEveryCommandExactlyOnce ()
	{
		var commands = TextTool.text_key_bindings.Select (kb => kb.Command).ToList ();
		var expected = Enum.GetValues<TextTool.TextKeyCommand> ();

		Assert.That (commands, Is.EquivalentTo (expected));
		Assert.That (commands.Distinct ().Count (), Is.EqualTo (commands.Count), "no TextKeyCommand should be bound twice");
	}

	[Test]
	public void FontSizeBindingsCoverEveryCommandExactlyOnce ()
	{
		var commands = TextTool.font_size_bindings.Select (kb => kb.Command).ToList ();
		var expected = Enum.GetValues<TextTool.FontSizeCommand> ();

		Assert.That (commands, Is.EquivalentTo (expected));
		Assert.That (commands.Distinct ().Count (), Is.EqualTo (commands.Count));
	}

	// The old versions of these three asserted GetToolBinding(b) == b.DefaultGesture with no
	// override ever configured - i.e. x == x, unable to fail even if DefaultGesture named the wrong
	// physical key. Reseed with a deliberately different override per binding and check the round
	// trip: GetToolBinding has to return the override while it is set, and the default once it is
	// reset. That is the only real behaviour GetToolBinding has.

	[Test]
	public void ShapeKeyBindingsHonourAConfiguredOverride ()
		=> AssertOverrideRoundTrips (BaseEditEngine.shape_key_bindings.Select (kb => (kb.Binding, kb.Command.ToString ())));

	[Test]
	public void TextKeyBindingsHonourAConfiguredOverride ()
		=> AssertOverrideRoundTrips (TextTool.text_key_bindings.Select (kb => (kb.Binding, kb.Command.ToString ())));

	[Test]
	public void FontSizeBindingsHonourAConfiguredOverride ()
		=> AssertOverrideRoundTrips (TextTool.font_size_bindings.Select (kb => (kb.Binding, kb.Command.ToString ())));

	private static void AssertOverrideRoundTrips (IEnumerable<(ToolBindingDescriptor Binding, string Command)> bindings)
	{
		KeyGesture f13 = new (new Gdk.Key (Gdk.Constants.KEY_F13));
		KeyGesture f14 = new (new Gdk.Key (Gdk.Constants.KEY_F14));

		foreach ((ToolBindingDescriptor binding, string command) in bindings) {
			// Pick a key that is definitely not this binding's own default, so the override is a
			// real change GetToolBinding has to report.
			KeyGesture wrong = binding.DefaultGesture == f13 ? f14 : f13;

			try {
				PintaCore.Shortcuts.SetToolBinding (binding, wrong);
				Assert.That (PintaCore.Shortcuts.GetToolBinding (binding), Is.EqualTo (wrong),
					$"{command}: GetToolBinding has to return the configured override, not the compiled-in default");
			} finally {
				PintaCore.Shortcuts.ResetToolBinding (binding);
			}

			Assert.That (PintaCore.Shortcuts.GetToolBinding (binding), Is.EqualTo (binding.DefaultGesture),
				$"{command}: clearing the override has to restore the default gesture");
		}
	}
}
