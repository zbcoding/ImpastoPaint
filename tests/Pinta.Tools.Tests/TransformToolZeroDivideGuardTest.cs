using System.Reflection;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// BaseTransformTool.ApplyActiveTransform computes a corner-drag scale factor as
/// (dragStart - center) offset by the mouse delta, divided by (dragStart - center). The sibling
/// scale computations in this file (ComputeEdgeScaleTransform, ComputeScaleTransform) all guard
/// that divisor against zero and fall back to a scale of 1; this one didn't, so a drag whose start
/// point sits exactly on the content's horizontal or vertical center line divided by zero into an
/// Infinity/NaN scale that corrupted the live transform matrix.
/// </summary>
[TestFixture]
internal sealed class TransformToolZeroDivideGuardTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	[Test]
	public void ScalingFromExactCenterXFallsBackToUnitScaleInsteadOfInfinity ()
	{
		MoveSelectionTool tool = new (PintaCore.Services);

		RectangleD sourceRect = new (0, 0, 20, 10);
		PointD center = sourceRect.GetCenter (); // (10, 5)

		// original_point.X == center.X makes c1.X (= original_point - center) exactly 0, the
		// divisor for sx.
		SetField (tool, "source_rect", sourceRect);
		SetField (tool, "original_point", new PointD (center.X, 0));
		SetField (tool, "is_scaling", true);

		ToolMouseEventArgs e = new () { PointDouble = new PointD (center.X + 5, 3) };

		typeof (BaseTransformTool).GetMethod ("ApplyActiveTransform", NonPublicInstance)!
			.Invoke (tool, [Document, e]);

		Matrix transform = (Matrix) typeof (BaseTransformTool).GetField ("transform", NonPublicInstance)!.GetValue (tool)!;
		PointD probe = transform.TransformPoint (new PointD (1, 1));

		Assert.That (double.IsFinite (probe.X) && double.IsFinite (probe.Y), Is.True,
			"a drag starting exactly on the center's X coordinate must fall back to scale 1 on that " +
			"axis, not divide by zero into an Infinity/NaN transform");
	}

	private static void SetField (object target, string name, object value)
		=> typeof (BaseTransformTool).GetField (name, NonPublicInstance)!.SetValue (target, value);
}
