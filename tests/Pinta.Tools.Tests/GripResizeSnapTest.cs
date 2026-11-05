using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Resizing moved content by a grip snaps the dragged corner or edge the way moving it snaps the
/// whole box, so a picture can be sized to exactly match one on another layer. Drives
/// <see cref="ToolManager"/>, which snaps the cursor before the tool sees it, as the app does.
/// </summary>
[TestFixture]
internal sealed class GripResizeSnapTest : ToolsTestHarness
{
	private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// An 8x8 red square at (8,8) on the bottom layer, selected; its lower-right grip is at (16,16).
	private static readonly RectangleI square = new (8, 8, 8, 8);

	private BaseTool? tool;
	private bool saved_snap;
	private bool saved_align;
	private bool saved_show_grid;
	private int saved_cell_width;
	private int saved_cell_height;
	private int saved_mode;

	[SetUp]
	public void SelectSquare ()
	{
		ICanvasGridService grid = PintaCore.CanvasGrid;
		saved_snap = grid.SnapEnabled;
		saved_align = grid.AlignToObjects;
		saved_show_grid = grid.ShowGrid;
		saved_cell_width = grid.CellWidth;
		saved_cell_height = grid.CellHeight;
		grid.SnapEnabled = false;
		grid.AlignToObjects = true;
		grid.ShowGrid = false;

		// Grips resize only in the plain move mode; the warp modes put their own grips there.
		saved_mode = PintaCore.Settings.GetSetting (SettingNames.MOVE_SELECTED_MODE, (int) TransformMode.Move);
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) TransformMode.Move);

		Paint (Layer (0), square);
		Document.Selection = SelectionOf (square);
		Document.Selection.Visible = true;
	}

	[TearDown]
	public void RestoreToolAndToggles ()
	{
		if (tool is not null) {
			typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			tool = null;
		}

		ICanvasGridService grid = PintaCore.CanvasGrid;
		grid.ClearActiveGuides ();
		grid.SnapEnabled = saved_snap;
		grid.AlignToObjects = saved_align;
		grid.ShowGrid = saved_show_grid;
		grid.CellWidth = saved_cell_width;
		grid.CellHeight = saved_cell_height;
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, saved_mode);
	}

	// The user's case: the same picture on two layers, one resized by its corner until it matches
	// the other. Dropped roughly on the other picture's corner, it takes that picture's size.
	[Test]
	public void CornerDraggedNearAnotherLayersPictureCorner_LandsOnIt ()
	{
		AddPicture (new RectangleI (20, 4, 8, 20)); // right edge x = 28, bottom edge y = 24
		Activate ();

		Drag (new PointD (16, 16), new PointD (26.5, 22.5));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (8, 8, 20, 16)));
	}

	// An edge grip only moves along one axis, so only that axis may snap or show a guide: a guide
	// across the other axis would claim an alignment the drag cannot make.
	[Test]
	public void EdgeGripSnapsAndShowsGuidesOnlyAlongItsOwnAxis ()
	{
		AddPicture (new RectangleI (20, 4, 8, 20)); // right edge x = 28, centre line y = 14
		Activate ();

		Press (new PointD (16, 12)); // the right-edge grip
		Move (new PointD (26.5, 14.5));

		Assert.That (PintaCore.CanvasGrid.ActiveAlignmentGuides, Has.All.Matches<AlignmentGuide> (g => g.Vertical),
			"the horizontal centre line lies across the edge grip's path, not along it");
		Release (new PointD (26.5, 14.5));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (8, 8, 20, 8)));
	}

	// With a 10px grid the press on the (16,16) grip snaps to (20,20), well clear of the grip. The
	// grip has to be found under the real cursor, or the resize turns into a move.
	[Test]
	public void GripOffTheGridIsStillGrabbed_AndItsCornerLandsOnTheGrid ()
	{
		ICanvasGridService grid = PintaCore.CanvasGrid;
		grid.SnapEnabled = true;
		grid.ShowGrid = true;
		grid.CellWidth = 10;
		grid.CellHeight = 10;
		Activate ();

		Drag (new PointD (16, 16), new PointD (29, 21));

		Assert.That (Document.GetSelectedBounds (true), Is.EqualTo (new RectangleI (8, 8, 22, 12)));
	}

	/// <summary>A picture pasted onto a layer of its own, leaving the square's layer current.</summary>
	private void AddPicture (RectangleI region)
	{
		Paint (Document.Layers.AddNewLayer (string.Empty), region);
		Document.Layers.SetCurrentUserLayer (0);
	}

	private static void Paint (UserLayer layer, RectangleI region)
	{
		var pixels = layer.Surface.GetPixelData ();
		for (int y = region.Top; y < region.Top + region.Height; ++y)
			for (int x = region.Left; x < region.Left + region.Width; ++x)
				pixels[(y * layer.Surface.Width) + x] = Red;
		layer.Surface.MarkDirty ();
	}

	private void Activate ()
	{
		// Zoomed in so the grips' hit areas stay on the corners; tolerance is 2 canvas px here.
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.Scale = 4;

		MoveSelectedTool t = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		tool = t;
	}

	private void Drag (PointD from, PointD to)
	{
		Press (from);
		Move (to);
		Release (to);
	}

	private void Press (PointD at) => PintaCore.Tools.DoMouseDown (Document, MouseArgs (at));
	private void Move (PointD at) => PintaCore.Tools.DoMouseMove (Document, MouseArgs (at));
	private void Release (PointD at) => PintaCore.Tools.DoMouseUp (Document, MouseArgs (at));

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		WindowPoint = PintaCore.Workspace.CanvasPointToView (canvasPos),
		MouseButton = MouseButton.Left,
	};
}
