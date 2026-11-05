using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Resizing and mirroring floating pixels with Move Selected's grips: the marching ants have to
/// stay on the pixels they float, and each grip has to show the resize cursor for where it now
/// sits rather than where it started.
/// </summary>
[TestFixture]
internal sealed class MoveSelectedResizeTest : ToolsTestHarness
{
	private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// An 8x8 red square at (8,8), selected, alone on its layer.
	private static readonly RectangleI square = new (8, 8, 8, 8);

	private MoveSelectedTool? tool;
	private bool saved_snap;
	private bool saved_align;
	private int saved_mode;

	[SetUp]
	public void SelectSquare ()
	{
		saved_snap = PintaCore.CanvasGrid.SnapEnabled;
		saved_align = PintaCore.CanvasGrid.AlignToObjects;
		saved_mode = PintaCore.Settings.GetSetting (SettingNames.MOVE_SELECTED_MODE, (int) TransformMode.Move);
		PintaCore.CanvasGrid.SnapEnabled = false;
		PintaCore.CanvasGrid.AlignToObjects = false;
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, (int) TransformMode.Move);

		ImageSurface surface = Layer (0).Surface;
		var pixels = surface.GetPixelData ();
		for (int y = square.Top; y < square.Top + square.Height; ++y)
			for (int x = square.Left; x < square.Left + square.Width; ++x)
				pixels[(y * surface.Width) + x] = Red;
		surface.MarkDirty ();

		Document.Selection = SelectionOf (square);
		Document.Selection.Visible = true;
	}

	[TearDown]
	public void RestoreToolAndSettings ()
	{
		if (tool is not null) {
			typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
			typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
				.Invoke (PintaCore.Tools, [null]);
			tool = null;
		}

		PintaCore.CanvasGrid.SnapEnabled = saved_snap;
		PintaCore.CanvasGrid.AlignToObjects = saved_align;
		PintaCore.Settings.PutSetting (SettingNames.MOVE_SELECTED_MODE, saved_mode);
	}

	// The selection outline is whole pixels; the pixels' transform is exact. Squashing the square
	// to 2.5 rows rounds its outline to 2, and stretching it back out used to stretch that rounding
	// error with it, leaving rows of floating pixels below the ants that moved along with them.
	[Test]
	public void SquashingThenStretchingKeepsTheAntsOnTheFloatingPixels ()
	{
		Activate ();

		Drag (new PointD (12, 16), new PointD (12, 10.5)); // bottom edge up: 2.5 rows tall
		Drag (new PointD (12, 10.5), new PointD (12, 30)); // and back down, past where it began

		Assert.That (FloatingBounds (), Is.EqualTo (new RectangleI (8, 8, 8, 22)), "setup: the pixels span rows 8 to 30");
		Assert.That (Document.Selection.GetBounds (), Is.EqualTo (new RectangleD (8, 8, 8, 22)),
			"the ants must outline the pixels they move, not the stretched rounding of an earlier size");
	}

	// Dragging the left edge across the right one mirrors the square. The grip that was the top-left
	// corner now sits at the top-right, so it has to show the top-right resize cursor.
	[Test]
	public void AfterAMirrorEachCornerGripShowsTheCursorForWhereItNowSits ()
	{
		Activate ();

		Drag (new PointD (8, 12), new PointD (24, 12)); // left edge past the right: x 16..24, mirrored
		Assert.That (FloatingBounds (), Is.EqualTo (new RectangleI (16, 8, 8, 8)), "setup: the square is mirrored onto x 16..24");

		Assert.Multiple (() => {
			Assert.That (CursorOver (new PointD (24, 8)), Is.EqualTo (Resources.StandardCursors.ResizeNE), "top-right");
			Assert.That (CursorOver (new PointD (16, 8)), Is.EqualTo (Resources.StandardCursors.ResizeNW), "top-left");
			Assert.That (CursorOver (new PointD (16, 16)), Is.EqualTo (Resources.StandardCursors.ResizeSW), "bottom-left");
			Assert.That (CursorOver (new PointD (24, 16)), Is.EqualTo (Resources.StandardCursors.ResizeSE), "bottom-right");
		});
	}

	private void Activate ()
	{
		// Zoomed in so neighbouring grips' hit areas stay apart on an 8px square.
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.Scale = 8;

		MoveSelectedTool t = new (PintaCore.Services);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		tool = t;
	}

	private string? CursorOver (PointD at)
	{
		PintaCore.Tools.DoMouseMove (Document, new ToolMouseEventArgs {
			PointDouble = at,
			WindowPoint = PintaCore.Workspace.CanvasPointToView (at),
			MouseButton = MouseButton.None,
		});
		return tool!.CurrentCursor?.GetName ();
	}

	private void Drag (PointD from, PointD to)
	{
		PintaCore.Tools.DoMouseDown (Document, MouseArgs (from));
		PintaCore.Tools.DoMouseMove (Document, MouseArgs (to));
		PintaCore.Tools.DoMouseUp (Document, MouseArgs (to));
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		WindowPoint = PintaCore.Workspace.CanvasPointToView (canvasPos),
		MouseButton = MouseButton.Left,
	};

	/// <summary>
	/// The box around the floating pixels as they are drawn, through their transform. Resampling
	/// fades a stretched edge into the neighbouring pixel, so only pixels at least half covered count.
	/// </summary>
	private RectangleI FloatingBounds ()
	{
		using ImageSurface shown = CairoExtensions.CreateImageSurface (Format.Argb32, CanvasSize, CanvasSize);
		using (Context g = new (shown))
			Document.Layers.SelectionLayer.DrawWithOperator (g, Operator.Over, opacity: 1.0, transform: true);
		shown.Flush ();

		var pixels = shown.GetReadOnlyPixelData ();
		int left = CanvasSize, top = CanvasSize, right = -1, bottom = -1;
		for (int y = 0; y < CanvasSize; ++y) {
			for (int x = 0; x < CanvasSize; ++x) {
				if (pixels[(y * CanvasSize) + x].A < 128)
					continue;
				left = System.Math.Min (left, x);
				right = System.Math.Max (right, x);
				top = System.Math.Min (top, y);
				bottom = System.Math.Max (bottom, y);
			}
		}
		return new RectangleI (left, top, right - left + 1, bottom - top + 1);
	}
}
