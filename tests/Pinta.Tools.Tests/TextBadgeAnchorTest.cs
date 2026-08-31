using System;
using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

// The bug this pins: the "Obj." badge was placed from the *axis-aligned* bounds of the rotated
// interaction rectangle - leftmost X, lowest Y - so it sat under the object's lower-left corner
// only while the object was upright. Any rotation walked it away from that corner, furthest at the
// diagonals, until it floated well clear of the box it labels.
[TestFixture]
internal sealed class TextBadgeAnchorTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	private TextTool? tool;

	[TearDown]
	public void ReleaseTool ()
	{
		if (tool is null)
			return;

		// The constructor's subscription to the static selection event outlives the instance.
		var handler = (Action<UserLayer, int>) Delegate.CreateDelegate (
			typeof (Action<UserLayer, int>), tool,
			typeof (TextTool).GetMethod ("HandleTextSelectRequested", NonPublicInstance)!);
		LayerObjectSelection.TextSelectRequested -= handler;

		tool = null;
	}

	private TextTool Tool ()
	{
		tool = new TextTool (PintaCore.Services);
		return tool;
	}

	private static PointD BadgeAnchor (TextTool t, TextObject obj)
		=> (PointD) typeof (TextTool).GetMethod ("GetBadgeAnchor", NonPublicInstance)!.Invoke (t, [obj])!;

	private static PointD[] InteractionCorners (TextTool t, TextObject obj)
		=> (PointD[]) typeof (TextTool).GetMethod ("GetInteractionCorners", NonPublicInstance, null, [typeof (TextObject)], null)!
			.Invoke (t, [obj])!;

	private static TextObject Text (double rotation)
		=> new (new TextEngine (["Impasto"]) { Origin = new PointI (8, 8) }) { Rotation = rotation };

	private static double Distance (PointD a, PointD b)
		=> Math.Sqrt ((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

	// The badge hangs a fixed 3px below the box's lower-left corner. Whatever the rotation, that
	// is the distance from the corner it labels - it is the AABB placement's drift that this
	// number catches, since the AABB corner runs away from the real one as the box turns.
	[TestCase (0.0)]
	[TestCase (30.0)]
	[TestCase (90.0)]
	[TestCase (210.0)]
	[TestCase (-45.0)]
	public void TheBadgeStaysAFixedGapBelowTheLowerLeftCorner (double rotation)
	{
		TextTool t = Tool ();
		TextObject obj = Text (rotation);

		PointD lowerLeft = InteractionCorners (t, obj)[3];

		Assert.That (Distance (BadgeAnchor (t, obj), lowerLeft), Is.EqualTo (3.0).Within (1e-6),
			"the badge must keep its gap to the corner it labels, at every rotation");
	}

	// ...and it hangs below in the *object's* frame, not the screen's: the offset runs along the
	// box's own downward edge, so the badge sits outside the box rather than across it.
	[TestCase (30.0)]
	[TestCase (90.0)]
	[TestCase (210.0)]
	public void TheGapRunsAlongTheBoxsOwnDownwardEdge (double rotation)
	{
		TextTool t = Tool ();
		TextObject obj = Text (rotation);

		PointD[] corners = InteractionCorners (t, obj);
		PointD anchor = BadgeAnchor (t, obj);

		// The top-right to bottom-right edge is the box's local "down" direction, rotated.
		PointD down = new (corners[2].X - corners[1].X, corners[2].Y - corners[1].Y);
		double downLength = Math.Sqrt (down.X * down.X + down.Y * down.Y);
		PointD offset = new (anchor.X - corners[3].X, anchor.Y - corners[3].Y);
		double offsetLength = Math.Sqrt (offset.X * offset.X + offset.Y * offset.Y);

		double cosine = (down.X * offset.X + down.Y * offset.Y) / (downLength * offsetLength);

		Assert.That (cosine, Is.EqualTo (1.0).Within (1e-6),
			"the badge must sit outside the box's lower edge, not somewhere across the canvas axes");
	}
}
