using System;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

[TestFixture]
internal sealed class Quad3DTest
{
	private static readonly PinholeCamera camera = PinholeCamera.Of (new Size (200, 100), 60);

	[Test]
	public void ACornerMovedOnScreenShowsExactlyWhereItWasDropped ()
	{
		// Behind the canvas plane, so a slip in Unproject's scale would show as a sub-pixel miss.
		Point3D[] corners = [new (10, 10, 40), new (90, 10, 0), new (90, 90, 0), new (10, 90, 0)];
		PointD drop = new (33.25, 17.5);

		Point3D[] moved = Quad3D.MoveCorner (camera, corners, 0, drop);

		PointD shown = camera.Project (moved[0]);
		Assert.That (shown.X, Is.EqualTo (drop.X).Within (1e-9));
		Assert.That (shown.Y, Is.EqualTo (drop.Y).Within (1e-9));
		Assert.That (moved[0].Z, Is.EqualTo (40), "moving across the screen keeps the corner's depth");
	}

	[Test]
	public void PushingACornerOneDoublingHalvesItsDistanceFromTheCentreOnScreen ()
	{
		Point3D[] corners = Quad3D.Flat ([new (10, 10), new (90, 10), new (90, 90), new (10, 90)]);

		Point3D[] pushed = Quad3D.PushCorner (camera, corners, 0, 1);

		PointD shown = camera.Project (pushed[0]);
		// The centre is (100, 50); the corner sat (-90, -40) from it.
		Assert.That (shown.X, Is.EqualTo (100 - 45).Within (1e-9));
		Assert.That (shown.Y, Is.EqualTo (50 - 20).Within (1e-9));
	}

	[Test]
	public void ACornerBehindTheCameraIsNotShown ()
	{
		Point3D[] corners = [new (10, 10, -camera.FocalLength), new (90, 10, 0), new (90, 90, 0), new (10, 90, 0)];

		Assert.That (Quad3D.Project (camera, corners), Is.Null);
	}

	[Test]
	public void TiltingThereAndBackIsFlatAgain ()
	{
		Point3D[] corners = Quad3D.Flat ([new (10, 10), new (90, 10), new (90, 90), new (10, 90)]);
		double step = 5 * Math.PI / 180;

		for (int i = 0; i < 9; i++)
			corners = Quad3D.Tilt (corners, step, 0);
		Assert.That (Quad3D.IsFlat (corners), Is.False);

		for (int i = 0; i < 9; i++)
			corners = Quad3D.Tilt (corners, -step, 0);
		Assert.That (Quad3D.IsFlat (corners), Is.True, "rounding left by the round trip is not depth");
	}
}
