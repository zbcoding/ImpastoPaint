using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The Line tool's "New Points" dropdown (Curve/Line) replaces the removed Shape Type dropdown's
/// Open/Closed toggle for this tool. Curve is unchanged from existing behaviour: a newly added
/// point gets the usual curve tension. Line makes a newly added point tension-0 instead
/// (DefaultEndPointTension) - the cardinal spline's tangent at that point is then zero, which keeps
/// the segment inside the straight line between its two neighbors (no curve overshoot) while still
/// meeting the next segment with a continuous, corner-free tangent, rather than a plain polyline's
/// sharp elbow. Named distinctly from the existing "Curved Segments" toggle beside it (which gates
/// click-to-insert on an existing segment) so the two adjacent dropdowns read as unrelated controls.
/// </summary>
[TestFixture]
internal sealed class LineToolSegmentModeTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private LineCurveTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is not null) {
			tool.EditEngine.HandleDeactivated (null);
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			tool = null;
		}

		// This setting is process-global (like the pre-existing Curved Segments/Rasterize Mode
		// toggles it sits beside), not per-test; leave it as found.
		PintaCore.Settings.PutSetting (SettingNames.SHAPE_LINE_CURVE_MODE, false);

		// Every ShapeTool constructor registers itself into this static, process-lifetime map;
		// leaving this test's instances in it would make a later test's differently-typed shape
		// look like it needs a tool switch mid-draw (see the Move/FinishSelection test's own note).
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.OpenLineCurveSeries);
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.ClosedLineCurveSeries);
	}

	// Same pattern as ShapeEditEngineDragStateTest: drives BaseEditEngine directly rather than
	// through the full BaseTool.DoActivated, which would build the rest of LineCurveTool's own
	// toolbar (antialiasing button etc.) this headless harness has no shell for.
	// HandleBuildToolBar still has to run - it is what creates the New Points dropdown - and
	// ToolManager.CurrentTool has to be set first for the reasons ShapeEditEngineDragStateTest notes.
	private LineCurveTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		LineCurveTool t = new (PintaCore.Services);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		t.EditEngine.HandleBuildToolBar (Gtk.Box.New (Gtk.Orientation.Horizontal, 0), PintaCore.Settings, "linecurve-segment-test");
		t.EditEngine.HandleActivated ();

		tool = t;
		return t;
	}

	private static ToolBarDropDownButton NewPointsButton (BaseEditEngine engine)
		=> (ToolBarDropDownButton) typeof (ArrowedEditEngine).GetField ("line_or_curve_button", NonPublicInstance)!.GetValue (engine)!;

	private static double NewPointTension (BaseEditEngine engine)
		=> (double) typeof (BaseEditEngine).GetProperty ("NewPointTension", NonPublicInstance)!.GetValue (engine)!;

	[Test]
	public void CurveIsTheDefaultAndKeepsTheExistingMidPointTension ()
	{
		LineCurveTool t = Activate ();

		Assert.That (NewPointTension (t.EditEngine), Is.EqualTo (BaseEditEngine.DefaultMidPointTension),
			"Curve must be the default, unchanged from the tool's behaviour before this setting existed");
	}

	[Test]
	public void SwitchingToLineMakesNewlyAddedPointsTensionZero ()
	{
		LineCurveTool t = Activate ();

		NewPointsButton (t.EditEngine).SelectedIndex = 1; // Line

		Assert.That (NewPointTension (t.EditEngine), Is.EqualTo (BaseEditEngine.DefaultEndPointTension),
			"Line mode must make newly added points tension-0, so segments stay straight with no curve overshoot");
	}

	[Test]
	public void OtherShapeToolsAreUnaffectedByLineMode ()
	{
		PintaCore.Settings.PutSetting (SettingNames.SHAPE_LINE_CURVE_MODE, true);

		RectangleTool rectangle = new (PintaCore.Services);

		Assert.That (NewPointTension (rectangle.EditEngine), Is.EqualTo (BaseEditEngine.DefaultMidPointTension),
			"the New Points toggle is scoped to the Line tool; Rectangle (and every other shape tool) must keep the default curve tension regardless of it");
	}
}
