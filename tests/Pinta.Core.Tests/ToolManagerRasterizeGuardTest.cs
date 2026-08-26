using System.Reflection;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// A raster brush tool writes straight to <c>UserLayer.Surface</c>, which sits underneath the live
/// object surface — a stroke that starts on top of a shape or text object would land invisibly,
/// hidden by the object drawn over it, with no indication anything happened. ToolManager.DoMouseDown
/// guards this the same way it already guards a layer's active transform: check the down point before
/// the stroke starts, offer to rasterize what it lands on, and only proceed once that is settled.
///
/// <para>
/// Deliberately down-point-only, not stroke-wide: a stroke that starts clear of every object and only
/// crosses one partway through a drag is left alone, exactly as painting under an untouched object
/// already behaves. Matching that is the point — the guard is a starting-point check, not a
/// mid-stroke tracker.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ToolManagerRasterizeGuardTest : DocumentHarness
{
	private FakeBrushTool tool = null!;

	// CurrentTool's setter is private, and the public path to it (SetCurrentTool) builds a real
	// toolbar for the tool — dragging in Adw, which this headless Core harness has no native binary
	// for. The guard under test lives entirely in DoMouseDown's read of CurrentTool, so bypassing the
	// toolbar machinery and setting it directly is testing the right seam, not a workaround.
	private static readonly PropertyInfo current_tool_property =
		typeof (ToolManager).GetProperty (nameof (ToolManager.CurrentTool))!;

	[OneTimeSetUp]
	public void CreateFakeTool () => tool = new FakeBrushTool (PintaCore.Services);

	// Runs after DocumentHarness's own [SetUp] activates the document — setting CurrentTool any
	// earlier would make the fake tool "active" while ActivateDocument fires its cursor-update event,
	// which reaches for a canvas widget this headless harness never wires up.
	[SetUp]
	public void ActivateFakeTool ()
	{
		current_tool_property.SetValue (PintaCore.Tools, tool);
		tool.MouseDownCount = 0;
	}

	// PintaCore.Tools is a static singleton, so a CurrentTool left set from this test would still be
	// "active" when the next test's [SetUp] activates its own document — same NRE this dodges above.
	// Runs before DocumentHarness's own [TearDown] (derived TearDown runs first), so CurrentTool is
	// already cleared by the time CloseDocument runs.
	[TearDown]
	public void DeactivateFakeTool () => current_tool_property.SetValue (PintaCore.Tools, null);

	[Test]
	public void StrokeStartingOnAShapeRasterizesItBeforePainting ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, Box (new Color (1, 0, 0, 1), new RectangleI (4, 4, 8, 8)), "Box");

		PintaCore.Tools.DoMouseDown (Document, MouseDownAt (8, 8));

		Assert.Multiple (() => {
			Assert.That (layer.ShapeObjects, Is.Empty, "the shape under the down point was baked");
			Assert.That (tool.MouseDownCount, Is.EqualTo (1), "the stroke still reaches the tool");
		});
	}

	[Test]
	public void StrokeStartingClearOfAShapePaintsUnderneathAsToday ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, Box (new Color (1, 0, 0, 1), new RectangleI (4, 4, 8, 8)), "Box");

		PintaCore.Tools.DoMouseDown (Document, MouseDownAt (24, 24));

		Assert.Multiple (() => {
			Assert.That (layer.ShapeObjects, Has.Count.EqualTo (1), "a stroke starting elsewhere leaves the shape alone");
			Assert.That (tool.MouseDownCount, Is.EqualTo (1));
		});
	}

	[Test]
	public void CancellingTheRasterizePromptSwallowsTheStroke ()
	{
		ObjectRasterizer.ConfirmPrompt = _ => false;

		UserLayer layer = Layer (0);
		AddObject (layer, Box (new Color (1, 0, 0, 1), new RectangleI (4, 4, 8, 8)), "Box");

		PintaCore.Tools.DoMouseDown (Document, MouseDownAt (8, 8));

		Assert.Multiple (() => {
			Assert.That (layer.ShapeObjects, Has.Count.EqualTo (1), "cancelling the prompt leaves the shape editable");
			Assert.That (tool.MouseDownCount, Is.Zero, "and the stroke never reaches the tool");
		});
	}

	private static ToolMouseEventArgs MouseDownAt (int x, int y)
		=> new () {
			MouseButton = MouseButton.Left,
			PointDouble = new PointD (x, y),
			RootPoint = new PointD (x, y),
			WindowPoint = new PointD (x, y),
		};

	/// <summary>A minimal stand-in for a raster brush tool: writes to the current layer, records
	/// whether the stroke ever reached it, and does nothing else — the guard under test lives in
	/// <see cref="ToolManager.DoMouseDown"/>, not in any particular tool's painting.</summary>
	private sealed class FakeBrushTool : BaseTool
	{
		public FakeBrushTool (System.IServiceProvider services) : base (services) { }

		public int MouseDownCount { get; set; }

		public override string Name => "Fake Brush (test)";
		public override string Icon => string.Empty;
		public override bool WritesToCurrentLayer => true;

		protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
			=> MouseDownCount++;
	}
}
