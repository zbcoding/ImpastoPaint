using System;
using NUnit.Framework;
using Color = Cairo.Color;
using CssColorFormat = Cairo.CssColorFormat;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ColorTests
{
	[OneTimeSetUp]
	public void InitializeGdk () => Gdk.Module.Initialize ();

	[TestCase (1, 0, 0, 0, 1, 1)]
	[TestCase (0, 1, 0, 120, 1, 1)]
	[TestCase (0, 0, 1, 240, 1, 1)]
	[TestCase (0, 0.5, 1, 210, 1, 1)]
	[TestCase (0.2, 0.5, 0.25, 130, 0.6, 0.5)]
	public void ColorToHsv (double r, double g, double b, double h, double s, double v)
	{
		Color c = new (r, g, b);
		HsvColor hsv = c.ToHsv ();
		Assert.That (hsv, Is.EqualTo (new HsvColor (h, s, v)));
		Color c2 = hsv.ToColor ();
		// assert reversibility; color > hsv > color retains same info
		// floating point rounding
		c2 = new (Math.Round (c2.R, 4), Math.Round (c2.G, 4), Math.Round (c2.B, 4));
		Assert.That (c2, Is.EqualTo (c));
	}

	[TestCase ("FFFFFF", 1, 1, 1, 1)]
	[TestCase ("FFFF", 1, 1, 1, 1)]
	[TestCase ("FFF", 1, 1, 1, 1)]
	[TestCase ("#FFFFFF", 1, 1, 1, 1)]
	[TestCase ("FFFF", 1, 1, 1, 1)]
	[TestCase ("#FFF", 1, 1, 1, 1)]
	[TestCase ("CC33AA99", 0.8, 0.2, 0.6667, 0.6)]
	[TestCase ("#CC33AA99", 0.8, 0.2, 0.6667, 0.6)]
	[TestCase ("C3A9", 0.8, 0.2, 0.6667, 0.6)]
	[TestCase ("C3A", 0.8, 0.2, 0.6667, 1)]
	public void FromHex (string hex, double r, double g, double b, double a)
	{
		Color hc = Color.FromHex (hex)!.Value;
		hc = new (Math.Round (hc.R, 4), Math.Round (hc.G, 4), Math.Round (hc.B, 4), Math.Round (hc.A, 4));
		Color expectedColor = new (r, g, b, a);
		Assert.That (hc, Is.EqualTo (expectedColor));
	}

	[TestCase ("#ff5733", 1, 0.3412, 0.2, 1)]
	[TestCase ("ff5733", 1, 0.3412, 0.2, 1)]
	[TestCase ("rgb(255 87 51)", 1, 0.3412, 0.2, 1)]
	[TestCase ("rgb(255 87 51 / 50%)", 1, 0.3412, 0.2, 0.5)]
	[TestCase ("hsl(11 100% 60%)", 1, 0.3467, 0.2, 1)]
	[TestCase ("hsl(11 100% 60% / 50%)", 1, 0.3467, 0.2, 0.5)]
	[TestCase ("oklch(65% 0.2 35)", 0.9394, 0.3236, 0.1669, 1)]
	[TestCase ("hwb(11 20% 0%)", 1, 0.3467, 0.2, 1)]
	[TestCase ("hwb(11 20% 0% / 50%)", 1, 0.3467, 0.2, 0.5)]
	[TestCase ("hwb(0 100% 100%)", 0.5, 0.5, 0.5, 1)]
	// Commas are an accepted alternative to spaces in every function that takes them.
	[TestCase ("rgb(255, 87, 51)", 1, 0.3412, 0.2, 1)]
	[TestCase ("rgb(255, 87, 51, 0.5)", 1, 0.3412, 0.2, 0.5)]
	[TestCase ("hsl(11, 100%, 60%)", 1, 0.3467, 0.2, 1)]
	// rgba()/hsla() are aliases of rgb()/hsl(), so either spelling takes either syntax.
	[TestCase ("rgba(255 87 51 / 50%)", 1, 0.3412, 0.2, 0.5)]
	[TestCase ("rgba(255, 87, 51, 0.5)", 1, 0.3412, 0.2, 0.5)]
	[TestCase ("rgba(255 87 51)", 1, 0.3412, 0.2, 1)]
	[TestCase ("hsla(11 100% 60% / 50%)", 1, 0.3467, 0.2, 0.5)]
	[TestCase ("rgb(255 87 51 / 0.5)", 1, 0.3412, 0.2, 0.5)]
	[TestCase ("rebeccapurple", 0.4, 0.2, 0.6, 1)]
	[TestCase ("transparent", 0, 0, 0, 0)]
	public void FromCssCode (string code, double r, double g, double b, double a)
	{
		Color actual = Color.FromCssCode (code, Color.Black)!.Value;
		actual = new (
			Math.Round (actual.R, 4),
			Math.Round (actual.G, 4),
			Math.Round (actual.B, 4),
			Math.Round (actual.A, 4));
		Assert.That (actual, Is.EqualTo (new Color (r, g, b, a)));
	}

	[Test]
	public void FromCssCodeResolvesCurrentColor ()
	{
		Color current = new (0.1, 0.2, 0.3, 0.4);
		Assert.That (Color.FromCssCode ("currentColor", current), Is.EqualTo (current));
	}

	// A user who types one notation should keep seeing that notation after editing
	// the color with the wheel or sliders, not have it collapse back to hex.
	[TestCase ("rgb(255 87 51)", CssColorFormat.Rgb)]
	[TestCase ("hsl(11 100% 60%)", CssColorFormat.Hsl)]
	[TestCase ("hwb(11 20% 0%)", CssColorFormat.Hwb)]
	[TestCase ("oklch(65% 0.2 35)", CssColorFormat.Oklch)]
	[TestCase ("#ff5733", CssColorFormat.Hex)]
	[TestCase ("rebeccapurple", CssColorFormat.Hex)]
	public void FromCssCodeReportsFormat (string code, CssColorFormat expected)
	{
		Color.FromCssCode (code, Color.Black, out CssColorFormat format);
		Assert.That (format, Is.EqualTo (expected));
	}

	[TestCase ("rgb(255 87 51)")]
	[TestCase ("rgb(255 87 51 / 50%)")]
	[TestCase ("hsl(11 100% 60%)")]
	[TestCase ("hwb(11 20% 0%)")]
	[TestCase ("hwb(11 20% 0% / 50%)")]
	[TestCase ("oklch(65% 0.2 35)")]
	public void ToCssCodeRoundTrips (string code)
	{
		Color parsed = Color.FromCssCode (code, Color.Black, out CssColorFormat format)!.Value;
		string rendered = parsed.ToCssCode (format);
		Color reparsed = Color.FromCssCode (rendered, Color.Black)!.Value;

		Assert.That (reparsed.R, Is.EqualTo (parsed.R).Within (0.01));
		Assert.That (reparsed.G, Is.EqualTo (parsed.G).Within (0.01));
		Assert.That (reparsed.B, Is.EqualTo (parsed.B).Within (0.01));
		Assert.That (reparsed.A, Is.EqualTo (parsed.A).Within (0.01));
	}

	[TestCase ("rgb(1 2)")]
	[TestCase ("oklch(65% none 35)")]
	[TestCase ("not-a-color")]
	public void FromCssCodeRejectsInvalidInput (string code)
	{
		Assert.That (Color.FromCssCode (code, Color.Black), Is.Null);
	}

	[TestCase (0.6, 0, 0.3, 1.0, true, "99004CFF")]
	[TestCase (0.6, 0, 0.3, 1.0, false, "99004C")]
	public void ToHex (double r, double g, double b, double a, bool alpha, string expected)
	{
		Color c = new (r, g, b, a);
		Assert.That (c.ToHex (alpha), Is.EqualTo (expected));
	}

	[TestCase ("CC33AA99", 0.8, 0.2, 0.6667, 0.6)]
	public void FromBgraHexString (string bgraHex, double a, double r, double g, double b)
	{
#pragma warning disable CS0618 // Type or member is obsolete
		Color hc = Color.ParseBgraHexString (bgraHex)!.Value;
#pragma warning restore CS0618
		hc = new (Math.Round (hc.R, 4), Math.Round (hc.G, 4), Math.Round (hc.B, 4), Math.Round (hc.A, 4));
		Color expectedColor = new (r, g, b, a);
		Assert.That (hc, Is.EqualTo (expectedColor));
	}

	[TestCase (0.6, 0, 0.3, 1.0, "99004CFF")]
	public void ToBgraHexString (double a, double r, double g, double b, string expected)
	{
		Color c = new (r, g, b, a);
#pragma warning disable CS0618 // Type or member is obsolete
		string result = Color.ToBgraHexString (c);
#pragma warning restore CS0618
		Assert.That (result, Is.EqualTo (expected));
	}
}
