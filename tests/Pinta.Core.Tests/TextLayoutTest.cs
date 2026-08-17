using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class TextLayoutTest
{
	[OneTimeSetUp]
	public void Init ()
	{
		Pango.Module.Initialize ();
		PangoCairo.Module.Initialize ();
	}

	[TestCase ("one two supercalifragilisticexpialidocious", 2, false)]
	[TestCase ("one two three four five six supercalifragilisticexpialidocious", 2, true)]
	[TestCase ("one two", 1, true)]
	[TestCase ("one two\nthree four", 2, true)]
	public void FlowJustificationSpacing (
		string text,
		int expectedLineCount,
		bool expected)
	{
		const int wrapWidth = 200;

		Pango.FontMap fontMap = PangoCairo.FontMapHelper.GetDefault ();
		using Pango.Context context = fontMap.CreateContext ();
		using Pango.Layout layout = Pango.Layout.New (context);
		layout.SetFontDescription (Pango.FontDescription.FromString ("Sans 12"));
		layout.SetWidth (PangoExtensions.UnitsFromPixels (wrapWidth));
		layout.SetText (text, -1);

		Assert.That (layout.GetLineCount (), Is.EqualTo (expectedLineCount));
		Assert.That (
			TextLayout.IsFlowJustificationSpacingAcceptable (layout, text, wrapWidth),
			Is.EqualTo (expected));
	}

	[TestCase (100, 90, 10, true)]
	[TestCase (100, 91, 10, true)]
	[TestCase (100, 89, 10, false)]
	[TestCase (100, 50, 0, true)]
	public void IsJustificationSpacingAcceptable (
		int layoutWidth,
		int lineWidth,
		int naturalSpaceWidth,
		bool expected)
	{
		bool actual = TextLayout.IsJustificationSpacingAcceptable (
			layoutWidth,
			lineWidth,
			naturalSpaceWidth);

		Assert.That (actual, Is.EqualTo (expected));
	}
}
