/////////////////////////////////////////////////////////////////////////////////
// Derived from the MIT-licensed Paint.NET 3.x source release, via Pinta.      //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See THIRD-PARTY-NOTICES.md for full licensing and attribution details.      //
//                                                                             //
// Ported to Pinta by: Krzysztof Marecki <marecki.krzysztof@gmail.com>         //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class BrightnessContrastEffect : BaseEffect
{
	// Keyed by the values it was built from: a committed node keeps this instance for good while its
	// data is replaced or edited, so the table must follow whatever Data holds at render time.
	private sealed record PixelOpCache (int Brightness, int Contrast, BrightnessContrastPixelOp Op);
	private PixelOpCache? pixel_op_cache;

	public sealed override bool IsTileable
		=> true;

	public override int? SampleRadius => 0;

	public override string Icon
		=> Resources.Icons.AdjustmentsBrightnessContrast;

	public override string Name
		=> Translations.GetString ("Brightness / Contrast");

	public override bool IsConfigurable
		=> true;

	public override string AdjustmentMenuKey
		=> "B";

	public BrightnessContrastData Data
		=> (BrightnessContrastData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public BrightnessContrastEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new BrightnessContrastData ();
	}

	/// <summary>
	/// The pixel op for the current data, rebuilt only when brightness or contrast differ from the cached one.
	/// Tiles render in parallel; each reads the cache reference once, so a racing rebuild only costs a duplicate table.
	/// </summary>
	private BrightnessContrastPixelOp GetPixelOp ()
	{
		int brightness = Data.Brightness;
		int contrast = Data.Contrast;
		PixelOpCache? cache = pixel_op_cache;
		if (cache is not null && cache.Brightness == brightness && cache.Contrast == contrast)
			return cache.Op;

		cache = new PixelOpCache (brightness, contrast, new BrightnessContrastPixelOp (brightness, contrast));
		pixel_op_cache = cache;
		return cache.Op;
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	private readonly record struct BrightnessContrastSettings (BrightnessContrastPixelOp PreRender, Size CanvasSize);
	private static BrightnessContrastSettings CreateSettings (ImageSurface destination, BrightnessContrastPixelOp preRender)
		=> new (PreRender: preRender, CanvasSize: destination.GetSize ());

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		BrightnessContrastSettings settings = CreateSettings (destination, GetPixelOp ());

		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();

		foreach (var pixel in Tiling.GeneratePixelOffsets (roi, settings.CanvasSize))
			destinationData[pixel.memoryOffset] = settings.PreRender.Apply (sourceData[pixel.memoryOffset]);
	}

	const int DEFAULT_BRIGHTNESS = 0;
	const int DEFAULT_CONTRAST = 0;

	public sealed class BrightnessContrastData : EffectData
	{
		private int brightness = DEFAULT_BRIGHTNESS;
		private int contrast = DEFAULT_CONTRAST;

		[Caption ("Brightness")]
		public int Brightness {
			get => brightness;
			set {
				if (value == brightness) return;
				brightness = value;
				FirePropertyChanged (nameof (Brightness));
			}
		}

		[Caption ("Contrast")]
		public int Contrast {
			get => contrast;
			set {
				if (value == contrast) return;
				contrast = value;
				FirePropertyChanged (nameof (Contrast));
			}
		}

		[Skip]
		public override bool IsDefault => Brightness == DEFAULT_BRIGHTNESS && Contrast == DEFAULT_CONTRAST;
	}
}
