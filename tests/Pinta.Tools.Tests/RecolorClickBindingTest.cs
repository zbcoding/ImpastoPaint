using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The recolor tool's two directions. A plain left stroke repaints canvas pixels near the
/// SECONDARY color with the primary; right click runs the opposite way, and so does the
/// user-configurable reverse gesture (default Alt+Click) so trackpads and styluses without an
/// easy second button aren't stuck with one direction.
/// </summary>
[TestFixture]
internal sealed class RecolorClickBindingTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	// FromBgra is blue-first: (b, g, r, a). Red comes from the shared harness.
	private static readonly ColorBgra Green = ColorBgra.FromBgra (0, 255, 0, 255);
	private static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	private static readonly ColorBgra Yellow = ColorBgra.FromBgra (0, 255, 255, 255);
	// The matching Cairo.Colors for the palette swatches.
	private static readonly Cairo.Color RedC = new (1, 0, 0);
	private static readonly Cairo.Color GreenC = new (0, 1, 0);
	private static readonly Cairo.Color BlueC = new (0, 0, 1);
	private static readonly Cairo.Color YellowC = new (1, 1, 0);

	private RecolorTool? tool;

	[TearDown]
	public void DeactivateTool ()
	{
		if (tool is null)
			return;

		// Same un-faking as RecolorObjectGuardTest: drop subscriptions and the borrowed
		// ToolManager.CurrentTool so later fixtures start clean.
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

		// Deterministic pixels: the default antialiased brush only fractionally covers the clicked
		// pixel (a ~π/4 blend), so assertions would see intermediate colors. With antialiasing off,
		// the width-2 disc still paints the clicked pixel's center at full strength.
		t.UseAntialiasing = false;

		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	/// <summary>
	/// Delivers a full left click (press + release) at <paramref name="at"/>, optionally holding
	/// modifiers such as the reverse gesture (default Alt).
	/// </summary>
	private void Click (PointI at, bool reverseGesture = false)
	{
		ToolMouseEventArgs down = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
			State = reverseGesture ? Gdk.ModifierType.AltMask : default,
		};
		typeof (RecolorTool).GetMethod ("OnMouseDown", NonPublicInstance)!
			.Invoke (tool, [Document, down]);

		ToolMouseEventArgs up = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Left,
			State = reverseGesture ? Gdk.ModifierType.AltMask : default,
		};
		typeof (BaseBrushTool).GetMethod ("OnMouseUp", NonPublicInstance)!
			.Invoke (tool, [Document, up]);
	}

	private void RightClick (PointI at)
	{
		ToolMouseEventArgs down = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Right,
		};
		typeof (RecolorTool).GetMethod ("OnMouseDown", NonPublicInstance)!
			.Invoke (tool, [Document, down]);

		ToolMouseEventArgs up = new () {
			PointDouble = new PointD (at.X + 0.5, at.Y + 0.5),
			MouseButton = MouseButton.Right,
		};
		typeof (BaseBrushTool).GetMethod ("OnMouseUp", NonPublicInstance)!
			.Invoke (tool, [Document, up]);
	}

	// --- Loop A: plain left click replaces secondary-colored pixels with the primary -------------

	[Test]
	public void PlainLeftClickReplacesSecondaryPixelsWithPrimary ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		PintaCore.Palette.PrimaryColor = BlueC;
		PintaCore.Palette.SecondaryColor = RedC;

		ActivateRecolor ();
		Click (new PointI (12, 12));

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Blue),
			"a plain click has to repaint the secondary-colored canvas with the primary color");
	}

	// --- Loop B: right click runs the opposite swap ----------------------------------------------

	[Test]
	public void RightClickReplacesPrimaryPixelsWithSecondary ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Blue);
		PintaCore.Palette.PrimaryColor = BlueC;
		PintaCore.Palette.SecondaryColor = GreenC;

		ActivateRecolor ();
		RightClick (new PointI (12, 12));

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Green),
			"right click has to run the reverse swap: primary-colored pixels become secondary");
	}

	// --- Loop C: the reverse gesture (default Alt + left click) mirrors right click --------------

	[Test]
	public void ReverseGestureOnLeftClickRunsTheOppositeSwap ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Blue);
		PintaCore.Palette.PrimaryColor = BlueC;
		PintaCore.Palette.SecondaryColor = GreenC;

		ActivateRecolor ();
		Click (new PointI (12, 12), reverseGesture: true);

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Green),
			"holding the reverse gesture during a left click has to behave like right click");
	}

	// --- Loop D: without the gesture held, a left click must never run the reverse swap ----------

	[Test]
	public void ReverseGestureNotHeldDoesNotRunTheReverseSwap ()
	{
		UserLayer layer = Layer (0);
		// Primary blue ground, on which neither direction is confusable: the forward swap only
		// looks for SECONDARY-colored pixels (yellow, absent here), while an erroneous reverse
		// swap would find this blue ground and repaint it yellow.
		Fill (layer.Surface, Blue);
		PintaCore.Palette.PrimaryColor = BlueC;
		PintaCore.Palette.SecondaryColor = YellowC;

		ActivateRecolor ();
		Click (new PointI (12, 12));

		Assert.That (layer.Surface.GetColorBgra (new PointI (12, 12)), Is.EqualTo (Blue),
			"a plain left click may never run the reverse swap, whatever colors the palette holds");
	}
}
