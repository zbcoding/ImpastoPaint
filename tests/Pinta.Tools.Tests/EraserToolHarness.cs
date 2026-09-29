using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// An activated <see cref="EraserTool"/> on the harness's document, driven through the same
/// mouse entry points the canvas uses. The toolbar is never shown, so the settings the user would
/// change in it are set through the widgets behind them.
/// </summary>
internal abstract class EraserToolHarness : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	protected EraserTool Eraser { get; private set; } = null!;

	protected ImageSurface Canvas => Layer (0).Surface;

	[SetUp]
	public void ActivateEraser ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();
		Document.Workspace.CanvasWindow = Gtk.DrawingArea.New ();

		Eraser = new EraserTool (PintaCore.Services);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (Eraser, [Document]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [Eraser]);

		Fill (Canvas, Red);
	}

	[TearDown]
	public void DeactivateEraser ()
	{
		typeof (BaseTool).GetMethod ("DoDeactivated", NonPublicInstance)!.Invoke (Eraser, [Document, null]);
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [null]);
	}

	/// <summary>Sets the toolbar's choices. <paramref name="type"/> is "Normal" or "Smooth".</summary>
	protected void Configure (string type = "Normal", int width = 6, int opacity = 100, int feather = 100, bool antialias = true)
	{
		typeof (EraserTool).GetField ("eraser_type", NonPublicInstance)!
			.SetValue (Eraser, Enum.Parse (typeof (EraserTool).GetNestedType ("EraserType", NonPublicInstance)!, type));
		((Gtk.SpinButton) typeof (BaseBrushTool).GetProperty ("BrushWidthSpinButton", NonPublicInstance)!.GetValue (Eraser)!)
			.Value = width;
		((Gtk.Scale) typeof (EraserTool).GetProperty ("OpacitySlider", NonPublicInstance)!.GetValue (Eraser)!)
			.SetValue (opacity);
		((Gtk.Scale) typeof (EraserTool).GetProperty ("FeatherSlider", NonPublicInstance)!.GetValue (Eraser)!)
			.SetValue (feather);
		Eraser.UseAntialiasing = antialias;
	}

	protected void Press (double x, double y, MouseButton button = MouseButton.Left)
		=> Send ("DoMouseDown", x, y, button);

	protected void Drag (double x, double y, MouseButton button = MouseButton.Left)
		=> Send ("DoMouseMove", x, y, button);

	protected void Release (double x, double y, MouseButton button = MouseButton.Left)
		=> Send ("DoMouseUp", x, y, button);

	/// <summary>One whole stroke: press at the first point, move through the rest, release at the last.</summary>
	protected void Stroke (MouseButton button, params (double X, double Y)[] path)
	{
		Press (path[0].X, path[0].Y, button);
		foreach ((double x, double y) in path[1..])
			Drag (x, y, button);
		Release (path[^1].X, path[^1].Y, button);
	}

	protected void Stroke (params (double X, double Y)[] path)
		=> Stroke (MouseButton.Left, path);

	private void Send (string method, double x, double y, MouseButton button)
	{
		ToolMouseEventArgs e = new () {
			PointDouble = new PointD (x, y),
			MouseButton = button,
		};
		typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (Eraser, [Document, e]);
	}

	protected ColorBgra PixelAt (int x, int y)
		=> Canvas.GetReadOnlyPixelData ()[y * Canvas.Width + x];

	protected ColorBgra[] Snapshot ()
		=> Canvas.GetReadOnlyPixelData ().ToArray ();
}
