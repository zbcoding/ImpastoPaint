/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

// Additional code:
//
// FloodTool.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public abstract class FloodTool : BaseTool
{
	protected Label? mode_label;
	protected ToolBarDropDownButton? mode_button;
	protected Separator? mode_sep;
	protected Label? tolerance_label;
	protected Scale? tolerance_slider;

	public FloodTool (IServiceProvider services) : base (services) { }

	public override bool WritesToCurrentLayer
		=> true;

	public override bool HandlesLiveObjectsItself => true;

	public override bool PaintsMaskSurface
		=> true;

	protected bool IsGlobalMode => ModeDropDown.SelectedItem.GetTagOrDefault (false);
	protected float Tolerance => (float) (ToleranceSlider.GetValue () / 100);
	protected virtual bool CalculatePolygonSet => true;
	protected bool LimitToSelection { get; set; } = true;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (ModeLabel);
		tb.Append (ModeDropDown);
		tb.Append (Separator);
		tb.Append (ToleranceLabel);
		tb.Append (ToleranceSlider);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		var pos = e.Point;

		// Don't do anything if we're outside the canvas
		if (pos.X < 0 || pos.X >= document.ImageSize.Width)
			return;
		if (pos.Y < 0 || pos.Y >= document.ImageSize.Height)
			return;

		base.OnMouseDown (document, e);

		Cairo.Region limitRegion = CairoExtensions.CreateRegion (document.GetSelectedBounds (true));

		// See if the mouse click is valid
		if (LimitToSelection && !limitRegion.ContainsPoint (pos.X, pos.Y))
			return;

		// A coloring tool gets first claim: a click landing on a live object's ink recolors
		// that object instead of flooding whatever sits underneath it.
		if (TryRecolorObjectAt (document, pos))
			return;

		FloodedRegion flooded = ComputeFloodedRegion (document, pos, IsGlobalMode || e.IsShiftPressed, limitRegion);

		OnFillRegionComputed (document, flooded.Stencil);

		if (flooded.Polygons is not null)
			OnFillRegionComputed (document, flooded.Polygons);
	}

	/// <summary>
	/// The flood of similar color around one point: the pixels it covers, and those same pixels as
	/// polygons when <see cref="CalculatePolygonSet"/> asks for them.
	/// </summary>
	protected sealed record FloodedRegion (BitMask Stencil, IReadOnlyList<IReadOnlyList<PointI>>? Polygons);

	/// <summary>
	/// Floods the region of similar color around <paramref name="pos"/> at the current tolerance
	/// and returns it. Returned rather than dispatched to <see cref="OnFillRegionComputed"/> so
	/// that a tool can re-flood a point it already sampled and decide on the spot what to do with
	/// the result - the magic wand re-floods its click points when the tolerance changes.
	/// </summary>
	protected FloodedRegion ComputeFloodedRegion (Document document, PointI pos, bool globalMode, Cairo.Region limitRegion)
	{
		// Sample the pixels the user can see: for a layer with live shapes/text, effects, a transform
		// or a mask, that includes what renders on top of the raster the fill lands in. While the mask
		// itself is the paint target, the mask surface *is* what is being edited, so that one is
		// sampled directly instead.
		UserLayer currentLayer = document.Layers.CurrentUserLayer;
		Cairo.ImageSurface? snapshot = document.Layers.CurrentMaskIsTarget ? null : currentLayer.CreateVisibleSnapshot ();
		try {
			Cairo.ImageSurface surface = snapshot ?? document.Layers.CurrentPaintSurface;

			var stencilBuffer = new BitMask (surface.Width, surface.Height);
			var tol = (int) (Tolerance * Tolerance * 256);

			RectangleD boundingBox;

			if (globalMode)
				CairoExtensions.FillStencilByColor (surface, stencilBuffer, surface.GetColorBgra (pos), tol, out boundingBox, limitRegion, LimitToSelection);
			else
				CairoExtensions.FillStencilFromPoint (surface, stencilBuffer, pos, tol, out boundingBox, limitRegion, LimitToSelection);

			// If a derived tool is only going to use the stencil,
			// don't waste time building the polygon set
			return new FloodedRegion (
				stencilBuffer,
				CalculatePolygonSet ? stencilBuffer.CreatePolygonSet (boundingBox, PointI.Zero) : null);
		} finally {
			snapshot?.Dispose ();
		}
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (mode_button is not null)
			settings.PutSetting (SettingNames.FloodToolFillMode (this), mode_button.SelectedIndex);
		if (tolerance_slider is not null)
			settings.PutSetting (SettingNames.FloodToolFillTolerance (this), (int) tolerance_slider.GetValue ());
	}

	protected virtual void OnFillRegionComputed (Document document, IReadOnlyList<IReadOnlyList<PointI>> polygonSet) { }
	protected virtual void OnFillRegionComputed (Document document, BitMask stencil) { }

	/// <summary>
	/// Called when the user moves the tolerance slider. The base does nothing - a tool that can
	/// show the new tolerance without another click overrides this.
	/// </summary>
	protected virtual void OnToleranceChanged () { }

	/// <summary>
	/// Called before the flood fill samples: lets a derived coloring tool recolor a live
	/// shape/text object when <paramref name="pos"/> lands on its ink, so the bucket colors the
	/// object the user actually clicked rather than the raster underneath it. Returns whether the
	/// click was consumed. The base never consumes - the magic wand selects through this unchanged.
	/// </summary>
	protected virtual bool TryRecolorObjectAt (Document document, PointI pos) => false;

	protected Label ModeLabel => mode_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Flood Mode")));
	protected Label ToleranceLabel => tolerance_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Tolerance")));
	protected Scale ToleranceSlider {
		get {
			if (tolerance_slider is null) {
				tolerance_slider = GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.FloodToolFillTolerance (this), 0));
				tolerance_slider.TooltipText = Translations.GetString ("Higher tolerance includes more colors similar to the clicked pixel in the fill.");
				tolerance_slider.OnValueChanged += (_, _) => OnToleranceChanged ();
			}
			return tolerance_slider;
		}
	}
	protected Separator Separator => mode_sep ??= GtkExtensions.CreateToolBarSeparator ();

	protected ToolBarDropDownButton ModeDropDown {
		get {
			if (mode_button is null) {
				mode_button = ToolBarDropDownButton.New ();

				mode_button.AddItem (Translations.GetString ("Contiguous"), Pinta.Resources.Icons.ToolFreeformShape, false, Translations.GetString ("Fill only the connected region of similar color touching the click point."));
				mode_button.AddItem (Translations.GetString ("Global"), Pinta.Resources.Icons.HelpWebsite, true, Translations.GetString ("Fill every matching pixel in the image, even disconnected regions. Hold Shift to use this mode temporarily."));

				mode_button.SelectedIndex = Settings.GetSetting (SettingNames.FloodToolFillMode (this), 0);
			}

			return mode_button;
		}
	}
}
