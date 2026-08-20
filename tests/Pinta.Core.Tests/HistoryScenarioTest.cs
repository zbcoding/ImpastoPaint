using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// One long session — paint, shapes, text, an effect node, a second layer, layer properties, a mask,
/// then the coordinate-changing ops that bake all of it — walked back and forth through its own
/// history. The single-step suites each prove one item restores what it changed; what this adds is
/// that the stack stays consistent over a long walk, including the compound items where a bake and a
/// transform undo together, and that arriving at a given step by a different route lands on the same
/// document. A state that only reconstructs correctly when the walk is monotonic passes every
/// single-step test and still corrupts a real session.
/// </summary>
[TestFixture]
internal sealed class HistoryScenarioTest : DocumentHarness
{
	private UserLayer Bottom => Layer (0);

	// Everything a step could plausibly change: the canvas, each layer's properties and structure,
	// and the pixels the canvas actually shows. Two positions with the same fingerprint are the same
	// document as far as the user can tell.
	private string Fingerprint ()
	{
		StringBuilder description = new ();

		description.Append (Document.ImageSize).Append ('|');

		foreach (UserLayer layer in Document.Layers.UserLayers)
			description
				.Append (layer.Name).Append (',')
				.Append (layer.Opacity.ToString ("F3")).Append (',')
				.Append (layer.BlendMode).Append (',')
				.Append (layer.Hidden).Append (',')
				.Append (layer.Objects.Count).Append (',')
				.Append (layer.HasMask).Append (';');

		using ImageSurface flattened = Document.GetFlattenedImage ();
		description.Append (Checksum (flattened));

		return description.ToString ();
	}

	// FNV-1a over the flattened pixels: any changed pixel changes the value, and it needs no
	// dependency to compute.
	private static uint Checksum (ImageSurface surface)
	{
		uint hash = 2166136261;
		foreach (ColorBgra pixel in surface.GetReadOnlyPixelData ())
			foreach (byte channel in new[] { pixel.B, pixel.G, pixel.R, pixel.A })
				hash = (hash ^ channel) * 16777619;
		return hash;
	}

	private string StepName (int pointer)
		=> Document.History.Items.ElementAt (pointer).Text;

	// Fifteen steps, ordered so the object-model work happens while there are still objects, and the
	// baking ops (flip, rotate, resize, crop) come after and have something to bake.
	private void BuildTheSession ()
	{
		// The app opens a document with this on the stack, and its presence is what makes every later
		// step undoable — the first item is never undone.
		Document.History.PushNewItem (new BaseHistoryItem (string.Empty, "Open Image"));

		PaintRaster (Bottom, s => Fill (s, Red), "Fill");
		AddObject (Bottom, Box (new Color (0, 0, 1, 1), new RectangleI (2, 2, 12, 12)), "Box");
		AddObject (Bottom, Invert (SelectionOf (new RectangleI (0, 0, 16, 16))), "Invert");
		AddObject (Bottom, Text ("Ab", new PointI (2, 16)), "Text");

		UserLayer top = Document.Layers.AddNewLayer ("Top");
		Document.History.PushNewItem (
			new AddLayerHistoryItem (string.Empty, "Add Layer", Document.Layers.IndexOf (top)));

		PaintRaster (top, s => FillRect (s, new RectangleI (16, 0, 16, 32), Green), "Paint");

		LayerProperties before = new (top.Name, top.Hidden, top.Opacity, top.BlendMode);
		LayerProperties after = new ("Overlay", false, 0.6, BlendMode.Multiply);
		after.SetProperties (top);
		Document.History.PushNewItem (
			new UpdateLayerPropertiesHistoryItem (string.Empty, "Layer Properties", Document.Layers.IndexOf (top), before, after));

		LayerMask mask = Bottom.CreateMask ();
		Fill (mask.Surface, ColorBgra.FromBgra (128, 128, 128, 128));
		Refresh (Bottom);
		Document.History.PushNewItem (
			new LayerMaskHistoryItem (PintaCore.Workspace, string.Empty, "Add Mask", Bottom, null, mask.Surface));

		AddObject (top, Halve (), "Halve");

		Activate (PintaCore.Actions.Image.FlipHorizontal);
		Activate (PintaCore.Actions.Image.Rotate180);

		Document.ResizeCanvas (new Size (48, 48), Anchor.NW, compoundAction: null);

		Document.Selection.CreateRectangleSelection (new RectangleD (4, 4, 24, 24));
		CropToSelection ();

		Document.ResizeImage (new Size (12, 12), ResamplingMode.NearestNeighbor);
	}

	private static void Activate (Command command)
	{
		command.Sensitive = true;
		command.Activate ();
	}

	// The document each pointer position should show, recorded on the way back down so the forward
	// and random walks have something to be checked against.
	private Dictionary<int, string> WalkBackRecordingEveryStep ()
	{
		Dictionary<int, string> states = new () { [Document.History.Pointer] = Fingerprint () };

		while (Document.History.CanUndo) {
			int leaving = Document.History.Pointer;
			Document.History.Undo ();
			states[Document.History.Pointer] = Fingerprint ();
			Assert.That (Document.History.Pointer, Is.EqualTo (leaving - 1));
		}

		return states;
	}

	[Test]
	public void UndoingAndRedoingTheWholeSessionLandsBackWhereItStarted ()
	{
		BuildTheSession ();

		string finished = Fingerprint ();
		Assert.That (Document.History.CanRedo, Is.False, "the session was just built; there is nothing ahead of it");

		Dictionary<int, string> states = WalkBackRecordingEveryStep ();
		Assert.That (states, Has.Count.GreaterThanOrEqualTo (15), "the walk needs more than a handful of steps to be worth anything");

		while (Document.History.CanRedo) {
			Document.History.Redo ();
			int at = Document.History.Pointer;
			Assert.That (Fingerprint (), Is.EqualTo (states[at]),
				$"redoing '{StepName (at)}' (step {at}) did not rebuild what undoing it took apart");
		}

		Assert.That (Fingerprint (), Is.EqualTo (finished), "a full round trip has to end on the document it started from");
	}

	// The same positions reached out of order. A step that restores itself correctly only when the
	// previous step was its own neighbour — one caching against a stale snapshot, say — passes the
	// linear walk and fails here.
	[Test]
	public void SteppingThroughTheSessionOutOfOrderAgreesWithTheLinearWalk ()
	{
		BuildTheSession ();

		Dictionary<int, string> states = WalkBackRecordingEveryStep ();
		while (Document.History.CanRedo)
			Document.History.Redo ();

		// Fixed seed: a failure here has to be reproducible, and an arbitrary walk is only useful if
		// the walk that failed can be run again.
		Random walk = new (20260819);

		for (int move = 0; move < 80; ++move) {
			bool forward = walk.Next (2) == 0;

			if (forward && Document.History.CanRedo)
				Document.History.Redo ();
			else if (Document.History.CanUndo)
				Document.History.Undo ();
			else if (Document.History.CanRedo)
				Document.History.Redo ();
			else
				break;

			int at = Document.History.Pointer;
			Assert.That (Fingerprint (), Is.EqualTo (states[at]),
				$"move {move} reached step {at} ('{StepName (at)}') out of order and found a different document");
		}
	}
}
