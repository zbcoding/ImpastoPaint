//
// LineCurveEditEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
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
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class LineCurveEditEngine : ArrowedEditEngine
{
	protected override string ShapeName
		=> Translations.GetString ("Open Curve Shape");

	private Gtk.CheckButton? close_shape_check;

	// Default for the next shape; the checkbox owns the live value once built.
	private bool close_shape_default;
	private bool prev_close_shape;

	public LineCurveEditEngine (
		IServiceProvider services,
		ShapeTool passedOwner
	)
		: base (services, passedOwner)
	{
	}

	protected override ShapeEngine CreateShape (
		bool ctrlKey,
		bool clickedOnControlPoint,
		PointD prevSelPoint)
	{
		LineCurveSeriesEngine newEngine = NewShapeEngine (BaseEditEngine.ShapeTypes.OpenLineCurveSeries, closed: close_shape_default, LineCap.Square);

		AddLinePoints (ctrlKey, clickedOnControlPoint, newEngine, prevSelPoint);

		//Set the new arrow's settings to be the same as what's in the toolbar settings.
		setNewArrowSettings (newEngine);

		return newEngine;
	}

	protected override void MovePoint (List<ControlPoint> controlPoints)
	{
		base.MovePoint (controlPoints);
	}

	protected override void BuildShapeToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		base.BuildShapeToolBar (tb, settings, toolPrefix);

		if (close_shape_check is null)
			close_shape_default = prev_close_shape = settings.GetSetting (SettingNames.SHAPE_CLOSED_SHAPE, false);

		tb.Append (CloseShapeCheckBox);

		SetArrowControlsEnabled (!close_shape_default);
		SetArrowOptionsVisible (!close_shape_default);
	}

	public override void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		base.OnSaveSettings (settings, toolPrefix);

		if (close_shape_check is not null)
			settings.PutSetting (SettingNames.SHAPE_CLOSED_SHAPE, close_shape_check.Active);
	}

	public override void UpdateToolbarSettings (ShapeEngine engine)
	{
		if (engine.ShapeType == ShapeTypes.OpenLineCurveSeries) {
			if (close_shape_check is not null)
				CloseShapeCheckBox.Active = ((LineCurveSeriesEngine) engine).Closed;
			SetArrowControlsEnabled (!((LineCurveSeriesEngine) engine).Closed);
			SetArrowOptionsVisible (!((LineCurveSeriesEngine) engine).Closed);
		}

		base.UpdateToolbarSettings (engine);
	}

	protected override void RecallPreviousSettings ()
	{
		if (close_shape_check is not null)
			CloseShapeCheckBox.Active = prev_close_shape;

		base.RecallPreviousSettings ();
	}

	protected override void StorePreviousSettings ()
	{
		if (close_shape_check is not null)
			prev_close_shape = close_shape_check.Active;

		base.StorePreviousSettings ();
	}


	private Gtk.CheckButton CloseShapeCheckBox
		=> close_shape_check ??= CreateCloseShapeCheckBox ();

	private Gtk.CheckButton CreateCloseShapeCheckBox ()
	{
		Gtk.CheckButton result = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Close shape"));
		result.TooltipText = Translations.GetString ("Connect the last point back to the first, closing the outline so fill modes show a closed shape.");
		result.FocusOnClick = false;
		result.Active = close_shape_default;
		result.OnToggled += (o, e) => CloseShapeToggled ();
		return result;
	}

	private void CloseShapeToggled ()
	{
		close_shape_default = CloseShapeCheckBox.Active;

		if (ActiveShapeEngine is LineCurveSeriesEngine activeEngine) {
			activeEngine.Closed = close_shape_default;
			PersistShapeObjectsIfLive (workspace.ActiveDocument.Layers.CurrentUserLayer);
			LayerObjectSelection.RaiseObjectsChanged ();
			DrawActiveShape (false, false, true, false, false);
		}

		SetArrowControlsEnabled (!close_shape_default);
		SetArrowOptionsVisible (!close_shape_default);

		StorePreviousSettings ();
	}
}
