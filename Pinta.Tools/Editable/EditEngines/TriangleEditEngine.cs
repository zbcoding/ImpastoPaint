//
// TriangleEditEngine.cs
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

public enum TriangleType
{
	Right = 0,
	Equilateral = 1,
}

public sealed class TriangleEditEngine : BaseEditEngine
{
	protected override string ShapeName
		=> Translations.GetString ("Triangle Shape");

	// Triangle is its own shape, like Ellipse or Rounded Line, not a variant of the Line/Curve
	// tool's open-vs-closed toggle.
	protected override bool SupportsShapeTypeConversion => false;

	private TriangleType selected_type = TriangleType.Right;
	private ToolBarDropDownButton? triangle_type_button;
	private Gtk.Label? triangle_type_label;
	private Gtk.Separator? triangle_type_sep;
	private string right_triangle_tooltip = "";
	private string equilateral_triangle_tooltip = "";

	public TriangleEditEngine (
		IServiceProvider services,
		ShapeTool passedOwner
	) : base (services, passedOwner)
	{
	}

	protected override ShapeEngine CreateShape (
		bool ctrlKey,
		bool clickedOnControlPoint,
		PointD prevSelPoint)
	{
		LineCurveSeriesEngine newEngine = NewShapeEngine (BaseEditEngine.ShapeTypes.Triangle, closed: true, LineCap.Square);
		newEngine.TriangleType = (int) selected_type;

		AddTrianglePoints (ctrlKey, clickedOnControlPoint, newEngine, prevSelPoint);

		return newEngine;
	}

	protected override void MovePoint (List<ControlPoint> controlPoints)
	{
		if (controlPoints.Count != 3) {
			base.MovePoint (controlPoints);
			return;
		}

		//Holding the type-switch key (default: Shift) while dragging draws the other
		//triangle type than the one picked in the toolbar.
		TriangleType effectiveType = selected_type;
		if (SelectedShapeEngine is LineCurveSeriesEngine lineEngine)
			effectiveType = (TriangleType) lineEngine.TriangleType;
		if (triangle_switch_down)
			effectiveType = effectiveType == TriangleType.Equilateral ? TriangleType.Right : TriangleType.Equilateral;

		if (effectiveType == TriangleType.Equilateral) {
			MoveEquilateralPoint (controlPoints);
			return;
		}

		MoveTriangularPoint (controlPoints);
		base.MovePoint (controlPoints);
	}

	//An equilateral triangle (one point up and two below, or the reverse when dragging
	//upwards): the apex stays on the first point, and the level base is centered beneath
	//it. The base follows the mouse's vertical drag; its half-width makes all sides equal.
	private void MoveEquilateralPoint (List<ControlPoint> controlPoints)
	{
		PointD apex = controlPoints[0].Position;

		//Use the absolute altitude so the triangle also grows when dragging upward,
		//putting the base on the opposite side of the apex (pointing down).
		double dy = current_point.Y - apex.Y;
		double altitude = Math.Max (Math.Abs (dy), 0.01d);
		double halfBase = altitude / Math.Sqrt (3d);

		double baseY = apex.Y + dy;

		controlPoints[1].Position = new PointD (apex.X - halfBase, baseY);
		controlPoints[2].Position = new PointD (apex.X + halfBase, baseY);
	}

	protected override void BuildTriangleTypeToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		triangle_type_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (triangle_type_sep);

		if (triangle_type_label == null) {
			string typeText = Translations.GetString ("Type");
			triangle_type_label = Gtk.Label.New ($" {typeText}: ");
		}

		tb.Append (triangle_type_label);

		if (triangle_type_button == null) {
			triangle_type_button = ToolBarDropDownButton.New ();

			string hint = TriangleTypeSwitchHint ();
			right_triangle_tooltip = Translations.GetString ("An isosceles right triangle, with the first point at its right angle.");
			equilateral_triangle_tooltip = Translations.GetString ("A triangle with equal sides, growing up or down from the first point.");

			triangle_type_button.AddItem (Translations.GetString ("Right Triangle"),
				Pinta.Resources.Icons.ToolTriangleRight, TriangleType.Right, right_triangle_tooltip + "\n" + hint);
			triangle_type_button.AddItem (Translations.GetString ("Equilateral Triangle"),
				Pinta.Resources.Icons.ToolTriangleEquilateral, TriangleType.Equilateral, equilateral_triangle_tooltip + "\n" + hint);

			triangle_type_button.SelectedIndex = settings.GetSetting (SettingNames.TriangleType (toolPrefix), (int) TriangleType.Right);
			selected_type = (TriangleType) triangle_type_button.SelectedIndex;

			triangle_type_button.SelectedItemChanged += (o, e) => {
				selected_type = triangle_type_button!.SelectedItem.GetTagOrDefault (TriangleType.Right);
				settings.PutSetting (SettingNames.TriangleType (toolPrefix), triangle_type_button.SelectedIndex);
				UpdateTriangleTypeTooltip ();
			};

			UpdateTriangleTypeTooltip ();
			PintaCore.Shortcuts.ShortcutsChanged += (_, _) => UpdateTriangleTypeTooltip ();
		}

		triangle_type_button.SelectedItem = triangle_type_button.Items[(int) selected_type];
		tb.Append (triangle_type_button);
	}

	private void UpdateTriangleTypeTooltip ()
	{
		if (triangle_type_button is null)
			return;

		string description = selected_type == TriangleType.Right ? right_triangle_tooltip : equilateral_triangle_tooltip;
		string text = triangle_type_button.SelectedItem.Text;
		triangle_type_button.TooltipText = $"{text}\n{description}\n{TriangleTypeSwitchHint ()}";
	}

	private static string TriangleTypeSwitchHint ()
	{
		KeyGesture gesture = PintaCore.Shortcuts.GetToolBinding (KeyboardShortcutManager.TriangleTypeSwitch);

		if (!gesture.IsValid)
			return Translations.GetString ("Hold the switch key to draw the other triangle type.");

		return Translations.GetString ("Hold {0} to switch between right and equilateral triangle.", gesture.ToLabel ());
	}

	public override void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		base.OnSaveSettings (settings, toolPrefix);

		if (triangle_type_button is not null)
			settings.PutSetting (SettingNames.TriangleType (toolPrefix), triangle_type_button.SelectedIndex);
	}

	public override void UpdateToolbarSettings (ShapeEngine engine)
	{
		if (engine is LineCurveSeriesEngine lineEngine)
			selected_type = (TriangleType) lineEngine.TriangleType;

		base.UpdateToolbarSettings (engine);
		if (triangle_type_button is not null)
			triangle_type_button.SelectedIndex = (int) selected_type;
	}
}
