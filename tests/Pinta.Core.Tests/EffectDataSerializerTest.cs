using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The parameters of a saved layer-effect node have to come back as the same values, or reopening a
/// document silently changes what the effect does. These cover every value type the converter table
/// claims to handle, plus the two ways a document can disagree with the effect it names: a property
/// type with no converter, and a saved value that no longer parses.
/// </summary>
[TestFixture]
internal sealed class EffectDataSerializerTest
{
	private sealed class SampleData : EffectData
	{
		public int Count { get; set; }
		public double Amount { get; set; }
		public bool Enabled { get; set; }
		public string Label { get; set; } = "";
		public RandomSeed Seed { get; set; } = new (7);
		public DegreesAngle Angle { get; set; }
		public RadiansAngle Turn { get; set; }
		public CenterOffset<double> Offset { get; set; }
		public PointD Spot { get; set; }
		public PointI Pixel { get; set; }
		public Cairo.Color Tint { get; set; }
		public ColorBgra Pixel32 { get; set; }
		public BlendMode Mode { get; set; }

		// No converter: it must be skipped rather than throwing or blocking its neighbours.
		public int[]? Table { get; set; }
	}

	private static SampleData Populated () => new () {
		Count = 42,
		Amount = -3.5,
		Enabled = true,
		Label = "outline",
		Seed = new RandomSeed (12345),
		Angle = new DegreesAngle (137.25),
		Turn = new RadiansAngle (1.5),
		Offset = new CenterOffset<double> (0.25, -0.75),
		Spot = new PointD (10.5, -20.25),
		Pixel = new PointI (3, 4),
		Tint = new Cairo.Color (0.2, 0.4, 0.6, 0.8),
		Pixel32 = ColorBgra.FromBgra (1, 2, 3, 4),
		Mode = BlendMode.ColorBurn,
		Table = [1, 2, 3],
	};

	[Test]
	public void EveryConvertibleValueSurvivesTheRoundTrip ()
	{
		SampleData original = Populated ();
		SampleData restored = new ();

		EffectDataSerializer.ApplyText (restored, EffectDataSerializer.ToText (original));

		Assert.Multiple (() => {
			Assert.That (restored.Count, Is.EqualTo (original.Count));
			Assert.That (restored.Amount, Is.EqualTo (original.Amount));
			Assert.That (restored.Enabled, Is.EqualTo (original.Enabled));
			Assert.That (restored.Label, Is.EqualTo (original.Label));
			Assert.That (restored.Seed.Value, Is.EqualTo (original.Seed.Value));
			Assert.That (restored.Angle.Degrees, Is.EqualTo (original.Angle.Degrees));
			Assert.That (restored.Turn.Radians, Is.EqualTo (original.Turn.Radians));
			Assert.That (restored.Offset, Is.EqualTo (original.Offset));
			Assert.That (restored.Spot, Is.EqualTo (original.Spot));
			Assert.That (restored.Pixel, Is.EqualTo (original.Pixel));
			Assert.That (restored.Pixel32.BGRA, Is.EqualTo (original.Pixel32.BGRA));
			Assert.That (restored.Mode, Is.EqualTo (original.Mode));
			// Hex is 8 bits per channel, so compare at that resolution rather than exactly.
			Assert.That (restored.Tint.ToHex (), Is.EqualTo (original.Tint.ToHex ()));
		});
	}

	[Test]
	public void UnconvertiblePropertyIsLeftOutRatherThanFailing ()
	{
		IReadOnlyDictionary<string, string> written = EffectDataSerializer.ToText (Populated ());

		Assert.That (written.ContainsKey (nameof (SampleData.Table)), Is.False);
		Assert.That (written, Contains.Key (nameof (SampleData.Count)));
	}

	[Test]
	public void EnumIsStoredByNameSoReorderingMembersCannotRepointIt ()
	{
		IReadOnlyDictionary<string, string> written = EffectDataSerializer.ToText (Populated ());

		Assert.That (written[nameof (SampleData.Mode)], Is.EqualTo (nameof (BlendMode.ColorBurn)));
	}

	[Test]
	public void UnparseableValueLeavesTheOtherPropertiesAlone ()
	{
		SampleData restored = new ();

		EffectDataSerializer.ApplyText (restored, new Dictionary<string, string> {
			[nameof (SampleData.Amount)] = "not a number",
			[nameof (SampleData.Count)] = "9",
			["PropertyThatNoLongerExists"] = "1",
		});

		Assert.That (restored.Count, Is.EqualTo (9));
		Assert.That (restored.Amount, Is.EqualTo (0));
	}
}
