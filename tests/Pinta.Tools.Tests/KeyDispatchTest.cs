using System;
using System.Linq;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// Pins docs-private/refactor.md T6: TextTool and BaseEditEngine each dispatch keys through two
// paths - a configured-binding lookup and a hardcoded-keycode fallback - that have drifted out of
// sync before. The lookup tables backing the first path are the single source of truth for "which
// commands exist and which binding fires them"; this asserts each table is complete (every command
// bound exactly once) and that every binding's fresh-state gesture equals its own default, i.e. the
// binding path fires on exactly the physical key the fallback switch also guards for.
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
	public void ShapeKeyBindingsFireOnTheirDefaultGesture ()
	{
		foreach ((ToolBindingDescriptor binding, BaseEditEngine.ShapeKeyCommand command) in BaseEditEngine.shape_key_bindings) {
			Assert.That (PintaCore.Shortcuts.GetToolBinding (binding), Is.EqualTo (binding.DefaultGesture),
				$"{command}: fresh-state binding should equal its default gesture (no override configured)");
		}
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
	public void TextKeyBindingsFireOnTheirDefaultGesture ()
	{
		foreach ((ToolBindingDescriptor binding, TextTool.TextKeyCommand command) in TextTool.text_key_bindings) {
			Assert.That (PintaCore.Shortcuts.GetToolBinding (binding), Is.EqualTo (binding.DefaultGesture),
				$"{command}: fresh-state binding should equal its default gesture (no override configured)");
		}
	}

	[Test]
	public void FontSizeBindingsCoverEveryCommandExactlyOnce ()
	{
		var commands = TextTool.font_size_bindings.Select (kb => kb.Command).ToList ();
		var expected = Enum.GetValues<TextTool.FontSizeCommand> ();

		Assert.That (commands, Is.EquivalentTo (expected));
		Assert.That (commands.Distinct ().Count (), Is.EqualTo (commands.Count));
	}

	[Test]
	public void FontSizeBindingsFireOnTheirDefaultGesture ()
	{
		foreach ((ToolBindingDescriptor binding, TextTool.FontSizeCommand command) in TextTool.font_size_bindings) {
			Assert.That (PintaCore.Shortcuts.GetToolBinding (binding), Is.EqualTo (binding.DefaultGesture),
				$"{command}: fresh-state binding should equal its default gesture (no override configured)");
		}
	}
}
