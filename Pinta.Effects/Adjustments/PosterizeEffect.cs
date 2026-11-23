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

public sealed class PosterizeEffect : BaseEffect
{
	public sealed override bool IsTileable => true;

	public override int? SampleRadius => 0;

	public override string Icon => Pinta.Resources.Icons.AdjustmentsPosterize;

	public override string Name => Translations.GetString ("Posterize");

	public override bool IsConfigurable => true;

	public override string AdjustmentMenuKey => "P";

	public PosterizeData Data => (PosterizeData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;

	public PosterizeEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();

		EffectData = new PosterizeData ();
	}

	public override async Task<bool> LaunchConfiguration ()
	{
		// TODO: Delegate `EffectData` changes to event handlers or similar
		using PosterizeDialog dialog = PosterizeDialog.New (chrome, Data);
		dialog.Title = Name;
		dialog.IconName = Icon;

		Gtk.ResponseType response = await dialog.RunAsync ();

		dialog.Destroy ();

		return Gtk.ResponseType.Ok == response;
	}

	// Built per call from the current data: a committed node renders with this instance while its data
	// is reconfigured, and the three 256-entry tables cost nothing next to the pixels.
	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
		=> new UnaryPixelOps.PosterizePixel (Data.Red, Data.Green, Data.Blue).Apply (dest, src, rois);
}

public sealed class PosterizeData : EffectData
{
	public int Red { get; set; } = 16;
	public int Green { get; set; } = 16;
	public int Blue { get; set; } = 16;
}
