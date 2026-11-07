/////////////////////////////////////////////////////////////////////////////////
// Derived from the MIT-licensed Paint.NET 3.x source release, via Pinta.      //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See THIRD-PARTY-NOTICES.md for full licensing and attribution details.      //
//                                                                             //
// Ported to Pinta by: Krzysztof Marecki <marecki.krzysztof@gmail.com>         //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class CurvesEffect : BaseEffect
{
	public sealed override bool IsTileable => true;

	public override string Icon => Pinta.Resources.Icons.AdjustmentsCurves;

	public override string Name => Translations.GetString ("Curves");

	public override bool IsConfigurable => true;

	public override string AdjustmentMenuKey => "M";

	public CurvesData Data => (CurvesData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;

	public CurvesEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();

		EffectData = new CurvesData ();
	}

	public override async Task<bool> LaunchConfiguration ()
	{
		// TODO: Delegate `EffectData` changes to event handlers or similar
		using CurvesDialog dialog = CurvesDialog.New (chrome, Data);
		dialog.Title = Name;
		dialog.IconName = Icon;

		Gtk.ResponseType response = await dialog.RunAsync ();

		dialog.Destroy ();

		return Gtk.ResponseType.Ok == response;
	}

	// The op is rebuilt per call from the current data: a committed node renders with this instance while
	// its curve is reconfigured, and the transfer tables are only 256 entries per channel.
	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.ControlPoints == null)
			return;

		MakeUop ().Apply (dest, src, rois);
	}

	private UnaryPixelOp MakeUop ()
	{
		UnaryPixelOp op;
		byte[][] transferCurves;
		int entries;

		switch (Data.Mode) {
			case ColorTransferMode.Rgb:
				UnaryPixelOps.ChannelCurve cc = new UnaryPixelOps.ChannelCurve ();
				transferCurves = [cc.CurveR, cc.CurveG, cc.CurveB];
				entries = 256;
				op = cc;
				break;

			case ColorTransferMode.Luminosity:
				UnaryPixelOps.LuminosityCurve lc = new UnaryPixelOps.LuminosityCurve ();
				transferCurves = [lc.Curve];
				entries = 256;
				op = lc;
				break;

			default:
				throw new InvalidEnumArgumentException ();
		}


		int channels = transferCurves.Length;

		for (int channel = 0; channel < channels; channel++) {

			SortedList<int, int> channelControlPoints = Data.ControlPoints![channel]; // NRT - Code expects this to be not-null

			IList<int> xa = channelControlPoints.Keys;
			IList<int> ya = channelControlPoints.Values;

			SplineInterpolator<double> interpolator = new ();

			int length = channelControlPoints.Count;

			for (int i = 0; i < length; i++) {
				interpolator.Add (xa[i], ya[i]);
			}

			for (int i = 0; i < entries; i++) {
				transferCurves[channel][i] = Utility.ClampToByte (interpolator.Interpolate (i));
			}
		}

		return op;
	}
}

public sealed class CurvesData : EffectData
{
	public SortedList<int, int>[]? ControlPoints { get; set; }
	public ColorTransferMode Mode { get; set; }

	public override CurvesData Clone ()
	{
		// The dialog edits its control point lists in place while clones render on worker threads,
		// so each clone gets lists of its own.
		SortedList<int, int>[]? controlPoints = ControlPoints is null
			? null
			: Array.ConvertAll (ControlPoints, channel => new SortedList<int, int> (channel));

		return new () {
			Mode = Mode,
			ControlPoints = controlPoints,
		};
	}
}
