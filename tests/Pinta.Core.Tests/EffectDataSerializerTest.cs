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

	private sealed class LevelsShapedData : EffectData
	{
		public UnaryPixelOps.Level Levels { get; set; } = new ();
	}

	// Levels' input/output ranges and gamma are what the user actually dialled in; the lookup table is
	// derived from them. The four colours clamp against each other when set individually, so this is
	// really a check that reloading rebuilds the level in one step instead of four.
	[Test]
	public void ALevelKeepsItsRangesAndGamma ()
	{
		UnaryPixelOps.Level level = new (
			ColorBgra.FromBgra (10, 20, 30, 255),
			ColorBgra.FromBgra (200, 210, 220, 255),
			[0.5f, 1.0f, 2.5f],
			ColorBgra.FromBgra (5, 6, 7, 255),
			ColorBgra.FromBgra (240, 245, 250, 255));

		LevelsShapedData restored = new ();
		EffectDataSerializer.ApplyText (restored, EffectDataSerializer.ToText (new LevelsShapedData { Levels = level }));

		Assert.Multiple (() => {
			Assert.That (restored.Levels.ColorInLow.BGRA, Is.EqualTo (level.ColorInLow.BGRA));
			Assert.That (restored.Levels.ColorInHigh.BGRA, Is.EqualTo (level.ColorInHigh.BGRA));
			Assert.That (restored.Levels.ColorOutLow.BGRA, Is.EqualTo (level.ColorOutLow.BGRA));
			Assert.That (restored.Levels.ColorOutHigh.BGRA, Is.EqualTo (level.ColorOutHigh.BGRA));
			for (int channel = 0; channel < 3; channel++)
				Assert.That (restored.Levels.GetGamma (channel), Is.EqualTo (level.GetGamma (channel)));
		});
	}

	private sealed class CurvesShapedData : EffectData
	{
		public SortedList<int, int>[]? ControlPoints { get; set; }
	}

	// Curves is one curve per channel, and a channel the user never touched is empty rather than
	// absent — so the per-channel split has to survive even when a curve carries no points.
	[Test]
	public void CurveControlPointsKeepTheirChannelsAndPoints ()
	{
		SortedList<int, int>[] curves = [
			new () { [0] = 0, [128] = 200, [255] = 255 },
			[],
			new () { [64] = 32 },
		];

		CurvesShapedData restored = new ();
		EffectDataSerializer.ApplyText (restored, EffectDataSerializer.ToText (new CurvesShapedData { ControlPoints = curves }));

		Assert.That (restored.ControlPoints, Is.Not.Null);
		SortedList<int, int>[] reloaded = restored.ControlPoints!;
		Assert.That (reloaded, Has.Length.EqualTo (3));
		Assert.Multiple (() => {
			Assert.That (reloaded[0], Is.EqualTo (curves[0]));
			Assert.That (reloaded[1], Is.Empty);
			Assert.That (reloaded[2], Is.EqualTo (curves[2]));
		});
	}

	// SurvivesSaveAndReload is a promise an add-in makes about its own data, and OraFormat.CanRestore
	// only keeps a node editable when the promise holds. What makes it checkable is naming the
	// settings that would come back as defaults, so a node claiming more than it can deliver is baked
	// rather than reopened showing different pixels.
	[Test]
	public void UnsupportedSettingsNamesOnlyTheSettingsThatCannotRoundTrip ()
	{
		Assert.Multiple (() => {
			Assert.That (
				EffectDataSerializer.UnsupportedSettings (Populated ()),
				Is.EqualTo (new[] { nameof (SampleData.Table) }),
				"the array has no converter and every other property does");

			Assert.That (
				EffectDataSerializer.UnsupportedSettings (new CurvesShapedData ()),
				Is.Empty,
				"a claim over fully covered data has to hold");

			Assert.That (
				EffectDataSerializer.UnsupportedSettings (null),
				Is.Empty,
				"an effect with no data at all promises nothing and loses nothing");
		});
	}
}
