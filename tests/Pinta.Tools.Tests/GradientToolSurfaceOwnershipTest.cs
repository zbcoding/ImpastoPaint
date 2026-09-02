using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// The gradient handle stays active after a drag ends, so RenderGradient keeps reading
/// <c>undo_surface</c> for the pre-gradient pixels every time a colour or the gradient type
/// changes. History items take ownership of the surface they are handed and dispose it once their
/// diff succeeds, so handing over the field itself left that read pointing at a disposed surface -
/// a crash on the next colour change rather than a wrong picture.
/// </summary>
[TestFixture]
internal sealed class GradientToolSurfaceOwnershipTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private GradientTool? tool;

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

	private GradientTool Activate ()
	{
		PintaCore.Workspace.ActiveWorkspace.Canvas = Gtk.DrawingArea.New ();

		GradientTool t = new (PintaCore.Services);

		typeof (BaseTool).GetMethod ("DoBuildToolBar", NonPublicInstance)!
			.Invoke (t, [Gtk.Box.New (Gtk.Orientation.Horizontal, 0)]);
		typeof (BaseTool).GetMethod ("DoActivated", NonPublicInstance)!.Invoke (t, [Document]);

		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!.GetSetMethod (nonPublic: true)!
			.Invoke (PintaCore.Tools, [t]);

		tool = t;
		return t;
	}

	private static ToolMouseEventArgs MouseArgs (PointD canvasPos) => new () {
		PointDouble = canvasPos,
		MouseButton = MouseButton.Left,
	};

	private static void Send (string method, BaseTool t, ToolMouseEventArgs e, Document doc)
		=> typeof (BaseTool).GetMethod (method, NonPublicInstance)!.Invoke (t, [doc, e]);

	private static ImageSurface? UndoSurface (GradientTool t)
		=> (ImageSurface?) typeof (GradientTool).GetField ("undo_surface", NonPublicInstance)!.GetValue (t);

	// Drag a gradient inside a small selection, release, then change the primary colour - the exact
	// sequence that used to re-enter RenderGradient with a surface the pushed history item had
	// already disposed. The selection matters: it clips the gradient to a few pixels, which is what
	// lets SurfaceDiff.Create succeed and take the dispose branch. A full-canvas gradient changes
	// too much for a diff to be worth storing, so it keeps the surface and hides the bug.
	[Test]
	public void ChangingTheColourAfterAGradientDragDoesNotUseADisposedSurface ()
	{
		Document.Selection = SelectionOf (new RectangleI (0, 0, 4, 4));
		Document.Selection.Visible = true;

		GradientTool t = Activate ();

		Send ("DoMouseDown", t, MouseArgs (new PointD (0, 0)), Document);
		Send ("DoMouseMove", t, MouseArgs (new PointD (4, 4)), Document);
		Send ("DoMouseUp", t, MouseArgs (new PointD (4, 4)), Document);

		ImageSurface? kept = UndoSurface (t);
		Assert.That (kept, Is.Not.Null, "the tool still needs its own copy of the pre-gradient pixels");
		Assert.DoesNotThrow (() => _ = kept!.Width,
			"pushing the history item must not dispose the surface the tool still reads");

		Assert.DoesNotThrow (
			() => PintaCore.Palette.PrimaryColor = new Color (1, 0, 0, 0.5),
			"re-rendering after the drag must not touch a disposed surface");
	}
}
