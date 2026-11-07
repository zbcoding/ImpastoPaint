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
using System.Linq;
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

	// Keyed by the curve it was built from: a committed node renders with this instance while its curve
	// is reconfigured in place, so the tables must follow whatever Data holds at render time.
	private sealed record PixelOpCache (ColorTransferMode Mode, KeyValuePair<int, int>[][] ControlPoints, UnaryPixelOp Op);
	private PixelOpCache? pixel_op_cache;

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.ControlPoints == null)
			return;

		GetPixelOp ().Apply (dest, src, rois);
	}

	/// <summary>
	/// The pixel op for the current curve, rebuilt only when mode or control points differ from the cached one.
	/// Tiles render in parallel; each reads the cache reference once, so a racing rebuild only costs duplicate tables.
	/// </summary>
	private UnaryPixelOp GetPixelOp ()
	{
		ColorTransferMode mode = Data.Mode;
		KeyValuePair<int, int>[][] controlPoints = Array.ConvertAll (Data.ControlPoints!, channel => channel.ToArray ());
		PixelOpCache? cache = pixel_op_cache;
		if (cache is not null && cache.Mode == mode && HaveSameControlPoints (cache.ControlPoints, controlPoints))
			return cache.Op;

		cache = new PixelOpCache (mode, controlPoints, MakeUop (mode, controlPoints));
		pixel_op_cache = cache;
		return cache.Op;
	}

	private static bool HaveSameControlPoints (KeyValuePair<int, int>[][] a, KeyValuePair<int, int>[][] b)
		=> a.Length == b.Length && a.Zip (b).All (pair => pair.First.AsSpan ().SequenceEqual (pair.Second));

	private static UnaryPixelOp MakeUop (ColorTransferMode mode, KeyValuePair<int, int>[][] controlPoints)
	{
		UnaryPixelOp op;
		byte[][] transferCurves;
		int entries;

		switch (mode) {
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

			KeyValuePair<int, int>[] channelControlPoints = controlPoints[channel];

			SplineInterpolator<double> interpolator = new ();

			foreach ((int x, int y) in channelControlPoints)
				interpolator.Add (x, y);

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
