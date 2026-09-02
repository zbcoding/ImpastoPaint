using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The six arrow options the Line tool shows for Size / Angle / Length are inserted beside the
/// arrow checkboxes only while an arrowhead is enabled, and ToolManager.BuildToolBar drains the
/// box HandleBuildToolBar filled right afterwards, regrouping every widget into per-setting
/// cluster boxes. Toggling an arrow at runtime used to insert into the long-emptied build box:
/// six Gtk-CRITICALs per click and no visible options, with the inserted flag set anyway so every
/// later retry bailed out early. The options must instead be inserted (and removed) wherever the
/// checkbox currently lives.
/// </summary>
[TestFixture]
internal sealed class ArrowOptionsInsertTest : ToolsTestHarness
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

		// These settings are process-global (like the Curved Segments/Rasterize Mode toggles the
		// arrow settings sit beside), not per-test; leave them as found.
		PintaCore.Settings.PutSetting (SettingNames.Arrow1 (Prefix), false);
		PintaCore.Settings.PutSetting (SettingNames.Arrow2 (Prefix), false);

		// Every ShapeTool constructor registers itself into this static, process-lifetime map;
		// leaving this test's instances in it would make a later test's differently-typed shape
		// look like it needs a tool switch mid-draw (see the Move/FinishSelection test's own note).
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.OpenLineCurveSeries);
		BaseEditEngine.CorrespondingTools.Remove (BaseEditEngine.ShapeTypes.ClosedLineCurveSeries);
	}

	private const string Prefix = "arrow-options-insert-test";

	private static IEnumerable<Gtk.Widget> ArrowOptionWidgets (BaseEditEngine engine)
		=> (IEnumerable<Gtk.Widget>) typeof (ArrowedEditEngine)
			.GetMethod ("GetArrowOptionToolbarItems", NonPublicInstance)!.Invoke (engine, null)!;

	private static Gtk.CheckButton ArrowOneCheckBox (BaseEditEngine engine)
		=> (Gtk.CheckButton) typeof (ArrowedEditEngine)
			.GetField ("show_arrow_one_box", NonPublicInstance)!.GetValue (engine)!;

	// Models the real SetCurrentTool sequence: ShapeTool.OnActivated runs EditEngine.HandleActivated
	// first, then ToolManager.BuildToolBar calls HandleBuildToolBar into a fresh box (the previous
	// box's widgets were unparented by ClearToolBar, so every build starts from unparented widgets).
	private (LineCurveTool tool, Gtk.Box buildBox) Activate (bool arrow)
	{
		PintaCore.Settings.PutSetting (SettingNames.Arrow1 (Prefix), arrow);

		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		PintaCore.Workspace.ActiveWorkspace.CanvasWindow = Gtk.DrawingArea.New ();

		LineCurveTool t = new (PintaCore.Services);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		t.EditEngine.HandleActivated ();

		Gtk.Box buildBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		t.EditEngine.HandleBuildToolBar (buildBox, PintaCore.Settings, Prefix);

		tool = t;
		return (t, buildBox);
	}

	// Moves every widget out of the build box into one cluster, the way ToolManager.BuildToolBar's
	// regrouping step does right after HandleBuildToolBar returns.
	private static Gtk.Box RegroupLikeToolManager (Gtk.Box buildBox)
	{
		Gtk.Box cluster = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		while (buildBox.GetFirstChild () is Gtk.Widget child) {
			buildBox.Remove (child);
			cluster.Append (child);
		}
		return cluster;
	}

	[Test]
	public void ArrowsEnabledAtBuildTimeInsertIntoTheBuildBox ()
	{
		(LineCurveTool t, Gtk.Box buildBox) = Activate (arrow: true);

		foreach (Gtk.Widget option in ArrowOptionWidgets (t.EditEngine))
			Assert.That (option.Parent, Is.EqualTo (buildBox),
				"arrow options enabled in the saved settings must be inserted during the build, so ToolManager's regrouping picks them up");
	}

	[Test]
	public void TogglingAnArrowOnAfterRegroupInsertsOptionsBesideTheCheckbox ()
	{
		(LineCurveTool t, Gtk.Box buildBox) = Activate (arrow: false);
		Gtk.Box cluster = RegroupLikeToolManager (buildBox);

		Gtk.CheckButton arrowOne = ArrowOneCheckBox (t.EditEngine);
		arrowOne.Active = true; // fires the same toggled handler a user click does

		Assert.That (arrowOne.Parent, Is.EqualTo (cluster), "setup: the checkbox must live in the regrouped cluster");
		foreach (Gtk.Widget option in ArrowOptionWidgets (t.EditEngine))
			Assert.That (option.Parent, Is.EqualTo (arrowOne.Parent),
				"toggled-on arrow options must be inserted beside the checkbox's current parent, not into the drained build box");
	}

	[Test]
	public void TogglingAnArrowOffAfterRegroupRemovesOptionsFromTheirCluster ()
	{
		(LineCurveTool t, Gtk.Box buildBox) = Activate (arrow: true);
		RegroupLikeToolManager (buildBox);

		Gtk.CheckButton arrowOne = ArrowOneCheckBox (t.EditEngine);
		arrowOne.Active = false;

		foreach (Gtk.Widget option in ArrowOptionWidgets (t.EditEngine))
			Assert.That (option.Parent, Is.Null,
				"toggled-off arrow options must be unparented from wherever the regroup left them");
	}

	[Test]
	public void ArrowsEnabledAtBuildTimeEndUpRegroupedIntoTheCluster ()
	{
		(LineCurveTool t, Gtk.Box buildBox) = Activate (arrow: true);
		Gtk.Box cluster = RegroupLikeToolManager (buildBox);

		foreach (Gtk.Widget option in ArrowOptionWidgets (t.EditEngine))
			Assert.That (option.Parent, Is.EqualTo (cluster),
				"arrow options inserted during the build must be picked up by ToolManager's regrouping");
	}
}
