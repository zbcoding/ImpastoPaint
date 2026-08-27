using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A second mouse button pressed mid-stroke (a real chord on multi-button mice/tablets) must be
/// ignored outright by RecolorTool.OnMouseDown, the same way BaseBrushTool ignores it. Before this
/// guard, the chorded down still flipped reversed_stroke even though the base class blocked it from
/// restarting the drag, so the rest of the still-held first stroke silently painted backwards.
/// </summary>
[TestFixture]
internal sealed class RecolorChordGuardTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// FromBgra is blue-first: (b, g, r, a). Red comes from the shared harness.
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	private static readonly Cairo.Color RedC = new (1, 0, 0);
	private static readonly Cairo.Color BlueC = new (0, 0, 1);

	private RecolorTool? tool;

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

	private RecolorTool ActivateRecolor ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		RecolorTool t = new (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		t.UseAntialiasing = false;

		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private void MouseDown (PointI at, MouseButton button)
	{
		ToolMouseEventArgs down = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = button,
		};
		typeof (RecolorTool).GetMethod ("OnMouseDown", NonPublicInstance)!.Invoke (tool, [Document, down]);
	}

	private void MouseMove (PointI at)
	{
		ToolMouseEventArgs move = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
		};
		typeof (RecolorTool).GetMethod ("OnMouseMove", NonPublicInstance)!.Invoke (tool, [Document, move]);
	}

	[Test]
	public void ChordedRightDownMidStrokeDoesNotFlipTheHeldLeftStroke ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		PintaCore.Palette.PrimaryColor = BlueC;
		PintaCore.Palette.SecondaryColor = RedC;

		ActivateRecolor ();

		PointI at = new (12, 12);

		// Left button down starts a forward stroke: this alone paints `at` from secondary to primary.
		MouseDown (at, MouseButton.Left);
		Assert.That (layer.Surface.GetColorBgra (at), Is.EqualTo (Blue),
			"the initial left-button stroke has to paint forward (secondary -> primary)");

		// A second button pressed without releasing the first (mouse_button is still Left) must be a
		// complete no-op: BaseBrushTool's guard already refuses to restart the drag, so RecolorTool
		// must refuse to touch reversed_stroke/stencil too.
		MouseDown (at, MouseButton.Right);

		// The still-active left drag continues; revisiting the same pixel must still be a forward
		// repaint; before the fix, the chorded down flipped reversed_stroke, so this move undid the
		// paint by swapping it back toward secondary.
		MouseMove (at);

		Assert.That (layer.Surface.GetColorBgra (at), Is.EqualTo (Blue),
			"a chorded second button mid-stroke must not flip the direction of the still-held first stroke");
	}
}
