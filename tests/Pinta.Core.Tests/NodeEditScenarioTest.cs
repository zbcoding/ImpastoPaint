using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A session spent editing nodes that already exist, rather than adding new ones: strength, blend
/// mode, name and visibility changed one at a time, an effect's own settings reopened and re-applied,
/// objects and nodes dragged past each other, layers swapped, and a bake over the top of all of it —
/// then walked back and forth through the stack.
///
/// <para>
/// <see cref="HistoryScenarioTest"/> covers a session that builds things up; this one covers the
/// awkward part, where a step's meaning depends on state a later step changes. Two seams make that
/// sharp. A per-object property step addresses its object by list index, so any reorder between the
/// step and its undo changes what that index names. And a node caches its render, keyed on the pixels
/// beneath it and a version counter its settings bump, so a node edited while hidden has to come back
/// showing the new settings rather than the render it went away with.
/// </para>
/// </summary>
[TestFixture]
internal sealed class NodeEditScenarioTest : DocumentHarness
{
	private UserLayer Bottom => Layer (0);

	// --- A node whose settings can be reopened and changed -----------------------------------------
	//
	// The harness effects take no parameters, so nothing there can stand in for reopening an effect's
	// dialog and pressing OK again. This one adds a constant to every channel: one number, visible in
	// the pixels, and cheap enough to re-render on every step of a long walk.

	internal sealed class StepData : EffectData
	{
		public int Shift { get; set; } = 40;
	}

	internal sealed class StepEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Step (test)";
		public override bool SurvivesSaveAndReload => false;

		public StepEffect ()
		{
			EffectData = new StepData ();
		}

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			int shift = ((StepData) EffectData!).Shift;
			Span<ColorBgra> dstData = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();

			foreach (RectangleI roi in rois)
				for (int y = roi.Top; y <= roi.Bottom; ++y)
					for (int x = roi.Left; x <= roi.Right; ++x) {
						int i = (y * dst.Width) + x;
						ColorBgra c = srcData[i];
						dstData[i] = ColorBgra.FromBgra (Raise (c.B, shift), Raise (c.G, shift), Raise (c.R, shift), c.A);
					}
		}

		private static byte Raise (byte channel, int by) => (byte) Math.Clamp (channel + by, 0, 255);
	}

	private static int ShiftOf (ILayerObject obj) => ((StepData) ((EffectModifierNode) obj).Effect.EffectData!).Shift;

	// --- Pushing the kinds of step the dock pushes ------------------------------------------------

	/// <summary>
	/// One per-object property change, the way LayersListViewItem does it: apply the value, then push
	/// an item that carries the value to go back to and addresses the object by its index.
	/// </summary>
	private void PushProperty<T> (
		UserLayer layer,
		ILayerObject target,
		string label,
		Func<ILayerObject, T> get,
		Action<ILayerObject, T> set,
		T value)
	{
		int index = layer.Objects.IndexOf (target);
		Assert.That (index, Is.GreaterThanOrEqualTo (0), "the scene handed a property step an object that is not on the layer");

		T before = get (target);
		set (target, value);
		Refresh (layer);

		Document.History.PushNewItem (
			new ObjectPropertyHistoryItem<T> (
				PintaCore.Workspace, PintaCore.Chrome, string.Empty, label, layer, index, get, set, before));
	}

	private void PushStrength (UserLayer layer, ILayerObject target, double strength)
		=> PushProperty (layer, target, $"Strength {strength:F2}", o => o.Opacity, (o, v) => o.Opacity = v, strength);

	private void PushBlend (UserLayer layer, ILayerObject target, BlendMode mode)
		=> PushProperty (layer, target, $"Blend {mode}", o => o.BlendMode, (o, v) => o.BlendMode = v, mode);

	private void PushHidden (UserLayer layer, ILayerObject target, bool hidden)
		=> PushProperty (layer, target, hidden ? "Hide Object" : "Show Object", o => o.Hidden, (o, v) => o.Hidden = v, hidden);

	private void PushRename (UserLayer layer, ILayerObject target, string name)
		=> PushProperty (layer, target, "Rename Object", o => o.Name, (o, v) => o.Name = v, name);

	/// <summary>
	/// Reopening an effect's dialog and pressing OK again. LivePreviewManager records this as a swap
	/// of the layer's whole object list, so the step that goes back is the list as it stood.
	/// </summary>
	private void PushSettings (UserLayer layer, EffectModifierNode node, int shift)
	{
		List<ILayerObject> before = ObjectOpacity.CloneAll (layer.Objects);

		StepData data = (StepData) node.Effect.EffectData!;
		data.Shift = shift;

		// What SimpleEffectDialog does when a control moves. The node's cached render is keyed on a
		// version counter this bumps, and ReconfigureModifier does not invalidate the cache itself, so
		// a settings change that stays quiet is a settings change the canvas never shows.
		data.FirePropertyChanged (nameof (StepData.Shift));

		Refresh (layer);

		Document.History.PushNewItem (
			new LayerObjectsHistoryItem (PintaCore.Workspace, PintaCore.Chrome, string.Empty, $"Settings shift={shift}", layer, before));
	}

	/// <summary>Dragging one object row onto another: a move within the unified list, across kinds.</summary>
	private void PushReorder (UserLayer layer, ILayerObject target, int to)
	{
		int from = layer.Objects.IndexOf (target);
		Assert.That (layer.MoveObjectAt (from, to), Is.True, $"the scene asked for a move ({from} -> {to}) the layer refused");

		Refresh (layer);
		Document.History.PushNewItem (
			new ObjectReorderHistoryItem (PintaCore.Workspace, PintaCore.Chrome, string.Empty, $"Reorder {from}->{to}", layer, from, to));
	}

	/// <summary>Move Layer Up on the bottom layer, as the Layers menu records it.</summary>
	private void PushLayerSwap ()
	{
		int from = Document.Layers.CurrentUserLayerIndex;
		Document.Layers.MoveCurrentLayerUp ();
		Document.History.PushNewItem (new SwapLayersHistoryItem (string.Empty, "Move Layer Up", from, from + 1));
	}

	private static void Activate (Command command)
	{
		command.Sensitive = true;
		command.Activate ();
	}

	// --- The session ------------------------------------------------------------------------------

	private void BuildTheSession ()
	{
		Document.History.PushNewItem (new BaseHistoryItem (string.Empty, "Open Image"));

		PaintRaster (Bottom, s => Fill (s, Red), "Fill");

		ShapeObject box = Box (new Color (0, 0, 1, 1), new RectangleI (2, 2, 12, 12));
		AddObject (Bottom, box, "Box");

		TextObject caption = Text ("Ab", new PointI (2, 16));
		AddObject (Bottom, caption, "Text");

		// Clipped to the left half, so moving it in the stack changes which objects it swallows.
		EffectModifierNode step = new (new StepEffect (), SelectionOf (new RectangleI (0, 0, 16, 32)));
		AddObject (Bottom, step, "Step");

		EffectModifierNode halve = Halve ();
		AddObject (Bottom, halve, "Halve");

		UserLayer top = Document.Layers.AddNewLayer ("Top");
		Document.History.PushNewItem (
			new AddLayerHistoryItem (string.Empty, "Add Layer", Document.Layers.IndexOf (top)));

		PaintRaster (top, s => FillRect (s, new RectangleI (16, 0, 15, 31), Green), "Paint");
		AddObject (top, Invert (), "Invert");

		// The plain edits: one property per step, the way the dock pushes them.
		PushStrength (Bottom, step, 0.35);
		PushBlend (Bottom, step, BlendMode.Multiply);
		PushRename (Bottom, step, "Step 40");

		// Reopen the dialog and give the effect a different number.
		PushSettings (Bottom, step, 90);

		// Edit a node's settings while it is hidden, then bring it back. The render it cached before
		// being hidden is for the old number; showing that again would be wrong.
		PushHidden (Bottom, step, true);
		PushSettings (Bottom, step, 15);
		PushHidden (Bottom, step, false);

		PushHidden (Bottom, halve, true);
		PushProperty (Bottom, box, "Object Opacity", o => o.Opacity, (o, v) => o.Opacity = v, 0.5);

		// Now move things past each other, so every index a step above recorded means something else.
		PushReorder (Bottom, step, 0);          // the node now runs before the shape and text are drawn
		PushReorder (Bottom, caption, Bottom.Objects.Count - 1);
		PushReorder (Bottom, halve, 1);

		// Strength zero: the node returns before it renders anything at all, a different path from
		// hidden and a different one again from a faint node.
		PushStrength (Bottom, step, 0.0);
		PushStrength (Bottom, step, 0.8);

		// A settings edit after the reorders, so the last edit and the first address the same node at
		// two different indices.
		PushSettings (Bottom, step, 60);

		Document.Layers.SetCurrentUserLayer (0);
		PushLayerSwap ();

		// Bakes every object and node on both layers, then flips. Everything above is now history
		// that has to survive being replayed across a compound step.
		Activate (PintaCore.Actions.Image.FlipHorizontal);
	}

	// --- What a step is allowed to change ---------------------------------------------------------

	private static string Describe (ILayerObject obj)
	{
		StringBuilder description = new ();

		description
			.Append (obj.GetType ().Name).Append (':')
			.Append (obj.Name).Append (',')
			.Append (obj.Opacity.ToString ("F3")).Append (',')
			.Append (obj.BlendMode).Append (',')
			.Append (obj.Hidden);

		if (obj is EffectModifierNode node) {
			description.Append (',').Append (node.Effect.Name);
			description.Append (",clip=").Append (node.Clip is null ? "none" : node.Clip.GetBounds ().ToInt ().ToString ());
			if (node.Effect.EffectData is StepData data)
				description.Append (",shift=").Append (data.Shift);
		}

		return description.ToString ();
	}

	/// <summary>
	/// The canvas, the layers, and every object's own properties in z-order — settings included. The
	/// pixels alone would not do here: a property that fails to restore can leave the image identical
	/// (a rename, a blend mode on a node whose strength is zero) and corrupt the next step anyway.
	/// </summary>
	private string Fingerprint ()
	{
		StringBuilder description = new ();

		description.Append (Document.ImageSize).Append ('|');

		foreach (UserLayer layer in Document.Layers.UserLayers) {
			description
				.Append (layer.Name).Append (',')
				.Append (layer.Opacity.ToString ("F3")).Append (',')
				.Append (layer.BlendMode).Append (',')
				.Append (layer.Hidden).Append ('[');

			foreach (ILayerObject obj in layer.Objects)
				description.Append (Describe (obj)).Append (';');

			description.Append (']');
		}

		using ImageSurface flattened = Document.GetFlattenedImage ();
		description.Append (Checksum (flattened));

		return description.ToString ();
	}

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

	// --- The walks --------------------------------------------------------------------------------

	[Test]
	public void UndoingAndRedoingASessionOfNodeEditsLandsBackWhereItStarted ()
	{
		BuildTheSession ();

		string finished = Fingerprint ();
		Dictionary<int, string> states = WalkBackRecordingEveryStep ();

		Assert.That (states, Has.Count.GreaterThanOrEqualTo (20), "the walk needs a long stack of edits to be worth anything");

		// Every step has to move the document somewhere new, or the walk is checking that nothing
		// happened. A step that leaves the fingerprint alone is either not being applied or not being
		// captured, and both make the rest of this test vacuous.
		Assert.That (states.Values.Distinct ().Count (), Is.EqualTo (states.Count),
			"two positions in the stack are indistinguishable, so the walk would pass without restoring anything");

		while (Document.History.CanRedo) {
			Document.History.Redo ();
			int at = Document.History.Pointer;
			Assert.That (Fingerprint (), Is.EqualTo (states[at]),
				$"redoing '{StepName (at)}' (step {at}) did not rebuild what undoing it took apart");
		}

		Assert.That (Fingerprint (), Is.EqualTo (finished), "a full round trip has to end on the document it started from");
	}

	[Test]
	public void SteppingThroughTheNodeEditsOutOfOrderAgreesWithTheLinearWalk ()
	{
		BuildTheSession ();

		Dictionary<int, string> states = WalkBackRecordingEveryStep ();
		while (Document.History.CanRedo)
			Document.History.Redo ();

		// Fixed seed: an arbitrary walk is only useful if the walk that failed can be run again.
		Random walk = new (20260820);

		for (int move = 0; move < 120; ++move) {
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

	/// <summary>
	/// Long runs in one direction, from both ends, rather than the short back-and-forth a coin flip
	/// produces. A cache keyed on the previous step's output survives single steps and fails here.
	/// </summary>
	[Test]
	public void RunsOfStepsInOneDirectionAgreeWithTheLinearWalk ()
	{
		BuildTheSession ();

		Dictionary<int, string> states = WalkBackRecordingEveryStep ();
		while (Document.History.CanRedo)
			Document.History.Redo ();

		Random walk = new (20260821);

		for (int run = 0; run < 12; ++run) {
			bool forward = run % 2 == 1;
			int length = 1 + walk.Next (states.Count);

			for (int step = 0; step < length; ++step) {
				if (forward && Document.History.CanRedo)
					Document.History.Redo ();
				else if (!forward && Document.History.CanUndo)
					Document.History.Undo ();
				else
					break;
			}

			int at = Document.History.Pointer;
			Assert.That (Fingerprint (), Is.EqualTo (states[at]),
				$"a run of {length} step(s) {(forward ? "forward" : "back")} ended at step {at} ('{StepName (at)}') on a different document");
		}
	}

	// --- The two seams the walks lean on ----------------------------------------------------------

	/// <summary>
	/// A node holds its own effect instance, and history holds copies of it. Editing the live node's
	/// settings must not reach into the snapshot: if it did, undo would restore the new value and the
	/// edit would be unrepeatable — while every fingerprint still matched, because both sides moved.
	/// </summary>
	[Test]
	public void EditingANodesSettingsLeavesTheCopyInHistoryAlone ()
	{
		Fill (Bottom.Surface, Red);
		EffectModifierNode step = new (new StepEffect ());
		AddObject (Bottom, step, "Step");

		PushSettings (Bottom, step, 90);
		Assert.That (Shown (Bottom, 4, 4).B, Is.EqualTo (90));

		Document.History.Undo ();

		Assert.That (Bottom.Objects.OfType<EffectModifierNode> ().Select (ShiftOf), Is.EqualTo (new[] { 40 }),
			"undoing the settings step restored a node still carrying the edited value");
		Assert.That (Shown (Bottom, 4, 4).B, Is.EqualTo (40));
	}

	/// <summary>
	/// A node renders once and caches, keyed on the pixels beneath it and a counter its settings bump.
	/// Hidden, it renders nothing at all — so the edit that lands while it is away has to be what it
	/// shows when it comes back, not the render it cached on the way out.
	/// </summary>
	[Test]
	public void UnhidingANodeShowsTheSettingsItWasGivenWhileHidden ()
	{
		Fill (Bottom.Surface, Red);
		EffectModifierNode step = new (new StepEffect ());
		AddObject (Bottom, step, "Step");
		Assert.That (Shown (Bottom, 4, 4).B, Is.EqualTo (40), "the node has to be showing before hiding it means anything");

		PushHidden (Bottom, step, true);
		Assert.That (Shown (Bottom, 4, 4).B, Is.EqualTo (0));

		PushSettings (Bottom, step, 120);
		PushHidden (Bottom, step, false);

		Assert.That (Shown (Bottom, 4, 4).B, Is.EqualTo (120),
			"the node came back showing the render it cached before it was hidden");
	}

	/// <summary>
	/// A property step names its object by index, so a reorder pushed after it changes what that index
	/// points at. Undoing in order unwinds the reorder first and the index means what it meant — but
	/// only if the reorder is genuinely undone rather than left half-applied.
	/// </summary>
	[Test]
	public void AReorderBetweenAPropertyStepAndItsUndoDoesNotSendItToTheWrongObject ()
	{
		Fill (Bottom.Surface, Red);

		EffectModifierNode first = new (new StepEffect ());
		EffectModifierNode second = new (new StepEffect ());
		AddObject (Bottom, first, "First");
		AddObject (Bottom, second, "Second");

		PushRename (Bottom, first, "renamed");
		PushReorder (Bottom, first, 1);

		Assert.That (Bottom.Objects.Select (o => o.Name), Is.EqualTo (new[] { "", "renamed" }));

		Document.History.Undo ();   // the reorder
		Document.History.Undo ();   // the rename

		Assert.That (Bottom.Objects.Select (o => o.Name), Is.EqualTo (new[] { "", "" }),
			"the rename was undone against whichever object the index named afterwards");
	}
}
