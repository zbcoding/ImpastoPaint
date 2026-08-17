// EffectDataSerializer.cs
//
// Turns an effect's EffectData into name/value strings and back, so a modifier node's settings can
// round-trip through the ORA sidecar entry (see docs-private/layer-effects-model.md).
//
// Reflection over the public read/write properties, with a converter table for the value types the
// effects actually use. A property whose type has no converter is left out: it loads with the
// effect's default rather than failing the whole node.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Pinta.Core;

public static class EffectDataSerializer
{
	private static CultureInfo Format => CultureInfo.InvariantCulture;

	private delegate string WriteValue (object value);
	private delegate object ReadValue (string text);

	private sealed record Converter (WriteValue Write, ReadValue Read);

	private static readonly Dictionary<Type, Converter> converters = new () {
		[typeof (int)] = new (v => ((int) v).ToString (Format), t => int.Parse (t, Format)),
		[typeof (double)] = new (v => ((double) v).ToString (Format), t => double.Parse (t, Format)),
		[typeof (float)] = new (v => ((float) v).ToString (Format), t => float.Parse (t, Format)),
		[typeof (bool)] = new (v => (bool) v ? "1" : "0", t => t == "1"),
		[typeof (string)] = new (v => (string) v, t => t),
		[typeof (RandomSeed)] = new (
			v => ((RandomSeed) v).Value.ToString (Format),
			t => new RandomSeed (int.Parse (t, Format))),
		[typeof (DegreesAngle)] = new (
			v => ((DegreesAngle) v).Degrees.ToString (Format),
			t => new DegreesAngle (double.Parse (t, Format))),
		[typeof (RadiansAngle)] = new (
			v => ((RadiansAngle) v).Radians.ToString (Format),
			t => new RadiansAngle (double.Parse (t, Format))),
		[typeof (CenterOffset<double>)] = new (
			v => Pair (((CenterOffset<double>) v).Horizontal, ((CenterOffset<double>) v).Vertical),
			t => { double[] p = Doubles (t, 2); return new CenterOffset<double> (p[0], p[1]); }),
		[typeof (PointD)] = new (
			v => Pair (((PointD) v).X, ((PointD) v).Y),
			t => { double[] p = Doubles (t, 2); return new PointD (p[0], p[1]); }),
		[typeof (PointI)] = new (
			v => Pair (((PointI) v).X, ((PointI) v).Y),
			t => { double[] p = Doubles (t, 2); return new PointI ((int) p[0], (int) p[1]); }),
		[typeof (Cairo.Color)] = new (
			v => ((Cairo.Color) v).ToHex (addAlpha: true),
			t => Cairo.Color.FromHex (t) ?? Cairo.Color.Black),
		[typeof (ColorBgra)] = new (
			v => ((ColorBgra) v).BGRA.ToString (Format),
			t => ColorBgra.FromUInt32 (uint.Parse (t, Format))),
		[typeof (UnaryPixelOps.Level)] = new (WriteLevel, ReadLevel),
		[typeof (SortedList<int, int>[])] = new (WriteControlPoints, ReadControlPoints),
	};

	// Levels' input/output ranges and per-channel gamma (the lookup table it drives is derived).
	// Written as the five constructor arguments in order, because the four colour setters clamp
	// against each other: setting them one at a time would drag the values around.
	private static string WriteLevel (object value)
	{
		UnaryPixelOps.Level level = (UnaryPixelOps.Level) value;
		return string.Join (',',
			level.ColorInLow.BGRA.ToString (Format),
			level.ColorInHigh.BGRA.ToString (Format),
			level.ColorOutLow.BGRA.ToString (Format),
			level.ColorOutHigh.BGRA.ToString (Format),
			level.GetGamma (0).ToString (Format),
			level.GetGamma (1).ToString (Format),
			level.GetGamma (2).ToString (Format));
	}

	private static object ReadLevel (string text)
	{
		string[] parts = text.Split (',');
		if (parts.Length != 7)
			throw new FormatException ($"Expected 7 comma-separated level values, got \"{text}\"");

		ColorBgra Colour (int index) => ColorBgra.FromUInt32 (uint.Parse (parts[index], Format));
		float[] gamma = [
			float.Parse (parts[4], Format),
			float.Parse (parts[5], Format),
			float.Parse (parts[6], Format),
		];

		return new UnaryPixelOps.Level (Colour (0), Colour (1), gamma, Colour (2), Colour (3));
	}

	// Curves' control points: one curve per channel (luminosity has a single curve, RGB has three),
	// each a set of input:output pairs. Channels are separated by ';' and points by ',' — neither
	// character can appear in an integer, so the split needs no escaping.
	private static string WriteControlPoints (object value)
	{
		SortedList<int, int>[] channels = (SortedList<int, int>[]) value;
		return string.Join (';',
			Array.ConvertAll (channels, channel => channel is null
				? string.Empty
				: string.Join (',', channel.Select (point => $"{point.Key}:{point.Value}"))));
	}

	private static object ReadControlPoints (string text)
	{
		return Array.ConvertAll (text.Split (';'), channel => {
			SortedList<int, int> points = [];
			if (channel.Length == 0)
				return points;

			foreach (string point in channel.Split (',')) {
				string[] pair = point.Split (':');
				if (pair.Length != 2)
					throw new FormatException ($"Expected an input:output control point, got \"{point}\"");
				points[int.Parse (pair[0], Format)] = int.Parse (pair[1], Format);
			}

			return points;
		});
	}

	/// <summary>
	/// Whether a property of this type survives a save and reload. Effect data holding a type that
	/// does not reloads that one property with the effect's default.
	/// </summary>
	public static bool CanSerialize (Type type)
		=> TryConverterFor (type, out _);

	/// <summary>
	/// The names of <paramref name="data"/>'s settings that no converter covers, in declaration order,
	/// and empty when every setting round-trips. This is what turns an effect's
	/// <see cref="BaseEffect.SurvivesSaveAndReload"/> claim into something checkable: a node whose
	/// effect claims the settings survive but holds one of these would reload with a different
	/// picture than the one that was saved.
	/// </summary>
	public static IReadOnlyList<string> UnsupportedSettings (EffectData? data)
	{
		if (data is null)
			return [];

		List<string> unsupported = [];
		foreach (PropertyInfo property in Properties (data.GetType ())) {
			if (!TryConverterFor (property.PropertyType, out _))
				unsupported.Add (property.Name);
		}

		return unsupported;
	}

	private static string Pair (double first, double second)
		=> $"{first.ToString (Format)},{second.ToString (Format)}";

	private static double[] Doubles (string text, int count)
	{
		string[] parts = text.Split (',');
		if (parts.Length != count)
			throw new FormatException ($"Expected {count} comma-separated numbers, got \"{text}\"");

		double[] values = new double[count];
		for (int i = 0; i < count; i++)
			values[i] = double.Parse (parts[i], Format);

		return values;
	}

	/// <summary>
	/// The settings of <paramref name="data"/> as property name / text pairs. Empty when the effect
	/// has no data at all.
	/// </summary>
	public static IReadOnlyDictionary<string, string> ToText (EffectData? data)
	{
		Dictionary<string, string> result = [];
		if (data is null)
			return result;

		foreach (PropertyInfo property in Properties (data.GetType ())) {
			if (!TryConverterFor (property.PropertyType, out Converter? converter))
				continue;

			object? value = property.GetValue (data);
			if (value is null)
				continue;

			try {
				result[property.Name] = converter.Write (value);
			} catch (Exception) {
				// A property that refuses to be written is skipped, not fatal: the rest of the
				// effect's settings are still worth saving.
			}
		}

		return result;
	}

	/// <summary>
	/// Applies saved property text to <paramref name="data"/>. Unknown names and values that no
	/// longer parse are ignored, so an effect that gained, lost or retyped a property still loads.
	/// </summary>
	public static void ApplyText (EffectData? data, IReadOnlyDictionary<string, string> values)
	{
		if (data is null)
			return;

		foreach (PropertyInfo property in Properties (data.GetType ())) {
			if (!values.TryGetValue (property.Name, out string? text))
				continue;

			if (!TryConverterFor (property.PropertyType, out Converter? converter))
				continue;

			try {
				property.SetValue (data, converter.Read (text));
			} catch (Exception) {
				// Same reasoning as ToText: one bad value must not cost the whole node.
			}
		}
	}

	private static IEnumerable<PropertyInfo> Properties (Type dataType)
	{
		foreach (PropertyInfo property in dataType.GetProperties (BindingFlags.Public | BindingFlags.Instance)) {
			if (property.CanRead && property.CanWrite && property.GetIndexParameters ().Length == 0)
				yield return property;
		}
	}

	private static bool TryConverterFor (Type type, out Converter converter)
	{
		Type effective = UnderlyingType (type);

		if (effective.IsEnum) {
			// Enums are written by name so reordering the members cannot repoint a saved value.
			Type enumType = effective;
			converter = new (v => v.ToString () ?? "", t => Enum.Parse (enumType, t, ignoreCase: true));
			return true;
		}

		if (converters.TryGetValue (effective, out Converter? found)) {
			converter = found;
			return true;
		}

		converter = null!;
		return false;
	}

	// Nullable<T> boxes as its T, so a converter for T serves the nullable form unchanged; a null
	// value is skipped by the callers above.
	private static Type UnderlyingType (Type type)
		=> Nullable.GetUnderlyingType (type) ?? type;
}
