using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The smooth eraser walks the brush box clipped to the surface. Its loops stopped one short of the
/// inclusive Right/Bottom, and the clip is exactly what lands there, so the canvas's final column
/// and row could never be erased however hard the user scrubbed them.
/// </summary>
[TestFixture]
internal sealed class EraserEdgeCoverageTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private EraserTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (tool, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
		tool = null;
	}

	/// <summary>Activates the eraser in Smooth mode, which is the mode with the pixel loops.</summary>
	private EraserTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		EraserTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		// The toolbar dropdown is not built here, so the private field is the only way in.
		typeof (EraserTool).GetField ("eraser_type", NonPublicInstance)!
			.SetValue (t, System.Enum.Parse (typeof (EraserTool).GetNestedType ("EraserType", NonPublicInstance)!, "Smooth"));

		tool = t;
		return t;
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	private static void Send (string method, BaseTool t, ToolMouseEventArgs e, Document doc)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (t, [doc, e]);

	private void EraseAlong (PointD from, PointD to)
	{
		EraserTool t = Activate ();
		Send ("DoMouseDown", t, MouseArgs (from), Document);
		Send ("DoMouseMove", t, MouseArgs (to), Document);
		Send ("DoMouseUp", t, MouseArgs (to), Document);
	}

	[Test]
	public void ScrubbingTheRightEdgeErasesTheLastColumn ()
	{
		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);
		int lastColumn = surface.Width - 1;
		int middleRow = surface.Height / 2;

		EraseAlong (new PointD (lastColumn, middleRow - 4), new PointD (lastColumn, middleRow + 4));

		Assert.That (surface.GetReadOnlyPixelData ()[middleRow * surface.Width + lastColumn].A,
			Is.LessThan (Red.A),
			"the canvas's last column has to be erasable");
	}

	[Test]
	public void ScrubbingTheBottomEdgeErasesTheLastRow ()
	{
		ImageSurface surface = Layer (0).Surface;
		Fill (surface, Red);
		int lastRow = surface.Height - 1;
		int middleColumn = surface.Width / 2;

		EraseAlong (new PointD (middleColumn - 4, lastRow), new PointD (middleColumn + 4, lastRow));

		Assert.That (surface.GetReadOnlyPixelData ()[lastRow * surface.Width + middleColumn].A,
			Is.LessThan (Red.A),
			"the canvas's last row has to be erasable");
	}
}
