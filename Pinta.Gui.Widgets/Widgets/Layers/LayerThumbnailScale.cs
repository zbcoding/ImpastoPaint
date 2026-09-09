using System;
using System.Collections.Immutable;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// Impasto: the layer list's thumbnail size, shared by every place the list appears (the dock pad
/// and the floating layers window). One slider step per entry in <see cref="widths"/>; step 0 turns
/// thumbnails off entirely. The value is a view preference, not document state, so it lives here
/// rather than on a document and persists through the settings service.
/// </summary>
public static class LayerThumbnailScale
{
	public const string SettingKey = "layers-thumbnail-size";

	/// <summary>
	/// Thumbnail widths in pixels, one per slider step. The first is "off". Heights follow from
	/// <see cref="Height"/>, which keeps the 3:2 shape the rows have always used.
	/// </summary>
	private static readonly ImmutableArray<int> widths = [0, 40, 60, 90, 130, 180];

	private const int DefaultStep = 2; // 60px, the size before the slider existed.

	public static int MaxStep => widths.Length - 1;

	/// <summary>Raised when <see cref="Step"/> changes, so open lists can resize their rows.</summary>
	public static event EventHandler? Changed;

	public static int Step {
		get => Math.Clamp (
			PintaCore.Settings.GetSetting (SettingKey, DefaultStep),
			0,
			MaxStep);
		set {
			int clamped = Math.Clamp (value, 0, MaxStep);
			if (Step == clamped)
				return;

			PintaCore.Settings.PutSetting (SettingKey, clamped);
			Changed?.Invoke (null, EventArgs.Empty);
		}
	}

	/// <summary>Thumbnail width in pixels, or 0 when thumbnails are off.</summary>
	public static int Width => widths[Step];

	public static int Height => Width * 2 / 3;

	public static bool Enabled => Width > 0;
}
