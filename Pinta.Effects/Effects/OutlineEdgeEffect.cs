/////////////////////////////////////////////////////////////////////////////////
// Derived from the MIT-licensed Paint.NET 3.x source release, via Pinta.      //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See THIRD-PARTY-NOTICES.md for full licensing and attribution details.      //
//                                                                             //
// Ported to Pinta by: Hanh Pham <hanh.pham@gmx.com>                           //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class OutlineEdgeEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsStylizeOutline;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Outline Edge");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Stylize");

	public OutlineEdgeData Data => (OutlineEdgeData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public OutlineEdgeEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new OutlineEdgeData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	// Algorithm Code Ported From PDN
	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		int thickness = Data.Thickness;
		int intensity = Data.Intensity;
		LocalHistogram.RenderRect (Apply, thickness, source, destination, roi);

		// === Methods ===

		ColorBgra Apply (ColorBgra src, int area, Span<int> hb, Span<int> hg, Span<int> hr, Span<int> ha)
		{
			int minCount1 = area * (100 - intensity) / 200;
			int minCount2 = area * (100 + intensity) / 200;

			// The second percentile continues the first one's scan, so it equals an independent lookup.
			int b1 = LocalHistogram.GetChannelPercentile (minCount1, hb);
			int b2 = LocalHistogram.GetChannelPercentile (minCount2, hb);
			int g1 = LocalHistogram.GetChannelPercentile (minCount1, hg);
			int g2 = LocalHistogram.GetChannelPercentile (minCount2, hg);
			int r1 = LocalHistogram.GetChannelPercentile (minCount1, hr);
			int r2 = LocalHistogram.GetChannelPercentile (minCount2, hr);
			int a2 = LocalHistogram.GetChannelPercentile (minCount2, ha);

			return ColorBgra.FromBgra (
				b: (byte) (255 - (b2 - b1)),
				g: (byte) (255 - (g2 - g1)),
				r: (byte) (255 - (r2 - r1)),
				a: (byte) a2)
				.ToPremultipliedAlpha ();
		}
	}

	public sealed class OutlineEdgeData : EffectData
	{
		[Caption ("Thickness")]
		[MinimumValue (1), MaximumValue (200)]
		public int Thickness { get; set; } = 3;

		[Caption ("Intensity")]
		[MinimumValue (0), MaximumValue (100)]
		public int Intensity { get; set; } = 50;

		[Skip]
		public override bool IsDefault => Thickness == 0;
	}
}
