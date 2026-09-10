using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// The bug this pins: the Colors window crashed the whole application on startup, before any
/// window existed.
///
/// <para>
/// <see cref="ColorPickerPanel.New"/> constructs the widget and only then hands it its services:
/// <c>palette</c> and <c>chrome</c> are <c>null!</c> placeholders until <c>Configure</c> runs, so
/// anything in the widget that reads a service during construction has to defer through a lambda.
/// The swap button captured <c>palette.SwapColors</c> as a method group while wiring press-time
/// activation, which dereferenced the null palette inside <c>BuildColorDisplay</c> and threw
/// <see cref="System.NullReferenceException"/> out of the constructor.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ColorPickerPanelTest : DocumentHarness
{
	[Test]
	public void BuildingThePanelBeforeItsServicesAreConfiguredDoesNotThrow ()
	{
		ColorPickerPanel panel = ColorPickerPanel.New (PintaCore.Palette, PintaCore.Chrome, PintaCore.System);

		Assert.That (panel, Is.Not.Null);
	}
}
