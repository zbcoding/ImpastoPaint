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

	// The handle can come back to life after finalizing retired the tool's copy: undoing a finalized
	// gradient hands the stored GradientData back (Active=true) via Swap, but nothing re-establishes
	// undo_surface. A gradient-type or alpha-blending change then re-enters RenderGradient and reads
	// the retired surface - a null dereference, not just the disposed-surface read the drag path used
	// to hit. RenderGradient must re-establish its base instead of crashing.
	[Test]
	public void TypeChangeAfterUndoOfFinalizedGradientDoesNotCrash ()
	{
		Document.Selection = SelectionOf (new RectangleI (0, 0, 4, 4));
		Document.Selection.Visible = true;

		GradientTool t = Activate ();

		Send ("DoMouseDown", t, MouseArgs (new PointD (0, 0)), Document);
		Send ("DoMouseMove", t, MouseArgs (new PointD (4, 4)), Document);
		Send ("DoMouseUp", t, MouseArgs (new PointD (4, 4)), Document);

		// Commit the drag: pushes the "Finalized" item, disposes undo_surface and nulls it.
		// (parameter-typed lookup: "Finalize" alone is ambiguous with Object.Finalize)
		typeof (GradientTool).GetMethod ("Finalize", NonPublicInstance, null, [typeof (Document)], null)!
			.Invoke (t, [Document]);

		Assert.That (UndoSurface (t), Is.Null, "finalize retires the tool's copy");

		// Undo the Finalized item -> the stored GradientData (Active=true) is swapped back onto the
		// handle, so it is live again with no undo surface behind it.
		Document.History.Undo ();

		// Finalize unsubscribes the palette handlers, so a colour change no longer re-renders; the
		// gradient-type handler is subscribed once in OnBuildToolBar and never removed. A translucent
		// colour forces the alpha-blending branch, the one place undo_surface is read.
		PintaCore.Palette.PrimaryColor = new Color (1, 0, 0, 0.5);

		Assert.DoesNotThrow (
			() => typeof (GradientTool).GetMethod ("HandleGradientTypeChanged", NonPublicInstance)!
				.Invoke (t, [null, EventArgs.Empty]),
			"re-rendering after undoing a finalized gradient must not dereference the retired surface");

		Assert.That (UndoSurface (t), Is.Not.Null, "the re-render re-establishes a base for later renders");
	}
}
