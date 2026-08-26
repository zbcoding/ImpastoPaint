using System;
using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Core.Tests;

/// <summary>
/// ToolManager.DoMouseDown's rasterize guard must not preempt the tools that manage live objects
/// themselves. The paint bucket recolors a clicked object's ink in place — its whole contract, and
/// what PaintBucketObjectRecolorTest locks in at the tool level. Routing the bucket's clicks
/// through the manager chokepoint silently bakes the object first (bbox probe, all intersecting
/// objects), after which TryRecolorObjectAt finds nothing and dumps the fill into the ground: the
/// object is destroyed as pixels and never changes color. The magic wand likewise only samples.
/// These tests drive the same PintaCore.Tools.DoMouseDown seam the app uses; nothing else covers
/// it, because the tool-level suites invoke OnMouseDown directly.
/// </summary>
[TestFixture]
internal sealed class ToolManagerLiveObjectOptOutTest : DocumentHarness
{
	// CurrentTool's setter is private and SetCurrentTool builds a real toolbar (Adw), which this
	// headless harness cannot construct — see ToolManagerRasterizeGuardTest for the same note.
	private static readonly PropertyInfo current_tool_property =
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;

	private FloodToolSpy tool = null!;

	[SetUp]
	public void ActivateFakeTool ()
	{
		tool = new FloodToolSpy (PintaCore.Services);
		current_tool_property.SetValue (PintaCore.Tools, tool);
	}

	[TearDown]
	public void DeactivateFakeTool ()
		=> current_tool_property.SetValue (PintaCore.Tools, null);

	[Test]
	public void BucketStyleFloodClickOnALiveObjectReachesTheToolUnbaked ()
	{
		UserLayer layer = Layer (0);
		FillRect (layer.Surface, new RectangleI (0, 0, 32, 32), Red);
		AddObject (layer, Box (new Color (0, 0, 1, 1), new RectangleI (8, 8, 16, 16)), "Box");

		int prompts = 0;
		ObjectRasterizer.ConfirmPrompt = _ => { prompts++; return true; };
		try {
			PintaCore.Tools.DoMouseDown (Document, MouseDownAt (12, 12));
		} finally {
			ObjectRasterizer.ConfirmPrompt = null;
		}

		Assert.Multiple (() => {
			Assert.That (prompts, Is.Zero,
				"an opted-out tool must not be intercepted by the down-point guard");
			Assert.That (layer.Objects, Has.Count.EqualTo (1),
				"the object stays editable - the flood tool owns the interaction");
			Assert.That (tool.MouseDownCount, Is.EqualTo (1),
				"the tool still receives the event so its own contract can run");
		});
	}

	[Test]
	public void NonGuardedBrushStillGetsGuarded ()
	{
		UserLayer layer = Layer (0);
		FillRect (layer.Surface, new RectangleI (0, 0, 32, 32), Red);
		AddObject (layer, Box (new Color (0, 0, 1, 1), new RectangleI (8, 8, 16, 16)), "Box");

		new PlainBrushSpy (PintaCore.Services, handlesObjectsItself: false);

		int prompts = 0;
		ObjectRasterizer.ConfirmPrompt = _ => { prompts++; return true; };
		try {
			current_tool_property.SetValue (PintaCore.Tools,
				new PlainBrushSpy (PintaCore.Services, handlesObjectsItself: false));
			PintaCore.Tools.DoMouseDown (Document, MouseDownAt (12, 12));
		} finally {
			ObjectRasterizer.ConfirmPrompt = null;
			current_tool_property.SetValue (PintaCore.Tools, null);
		}

		Assert.Multiple (() => {
			Assert.That (prompts, Is.EqualTo (1),
				"a plain raster brush over an object's bbox still gets the bake offer");
			Assert.That (layer.Objects, Is.Empty, "accepting bakes the object before the stroke");
		});
	}

	private static ToolMouseEventArgs MouseDownAt (int x, int y) => new () {
		PointDouble = new PointD (x + 0.5, y + 0.5),
		MouseButton = MouseButton.Left,
	};

	/// <summary>A flood-family tool that records delivery, standing in for the paint bucket.</summary>
	private sealed class FloodToolSpy : BaseTool
	{
		public int MouseDownCount;

		public FloodToolSpy (IServiceProvider services) : base (services) { }

		public override string Name => "FloodSpy";
		public override string Icon => string.Empty;
		public override bool WritesToCurrentLayer => true;
		public override bool HandlesLiveObjectsItself => true;

		protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
			=> MouseDownCount++;
	}

	/// <summary>A plain raster brush with no live-object handling of its own.</summary>
	private sealed class PlainBrushSpy : BaseTool
	{
		private bool HandlesItself { get; }

		public PlainBrushSpy (IServiceProvider services, bool handlesObjectsItself) : base (services)
			=> HandlesItself = handlesObjectsItself;

		public override string Name => "PlainBrushSpy";
		public override string Icon => string.Empty;
		public override bool WritesToCurrentLayer => true;
		public override bool HandlesLiveObjectsItself => HandlesItself;

		protected override void OnMouseDown (Document document, ToolMouseEventArgs e) { }
	}
}
