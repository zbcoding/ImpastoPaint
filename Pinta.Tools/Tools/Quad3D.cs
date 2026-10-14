using System;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>A corner in 3D mode: canvas pixels across and down, and depth behind the canvas plane in the same pixels.</summary>
internal readonly record struct Point3D (double X, double Y, double Z);

/// <summary>
/// The pinhole camera 3D mode looks through. It sits <see cref="FocalLength"/> in front of the
/// canvas centre, so a point on the canvas plane (depth 0) shows exactly where it is, a point
/// behind the plane shrinks toward the centre, and one in front of it grows away from it.
/// </summary>
internal readonly record struct PinholeCamera (PointD Centre, double FocalLength)
{
	// Closer than this share of the focal length, a corner would blow up past any useful size.
	private const double NEAREST_DISTANCE = 0.05;
	private const double FARTHEST_DISTANCE = 100;

	/// <summary>The field of view spans the canvas's longer side.</summary>
	public static PinholeCamera Of (Size canvas, double fieldOfViewDegrees)
	{
		double halfAngle = fieldOfViewDegrees * Math.PI / 360;
		double halfSpan = Math.Max (canvas.Width, canvas.Height) / 2.0;
		return new (new PointD (canvas.Width / 2.0, canvas.Height / 2.0), halfSpan / Math.Tan (halfAngle));
	}

	public bool Sees (Point3D p)
	{
		double distance = FocalLength + p.Z;
		return distance >= NEAREST_DISTANCE * FocalLength && distance <= FARTHEST_DISTANCE * FocalLength;
	}

	public PointD Project (Point3D p)
	{
		double scale = FocalLength / (FocalLength + p.Z);
		return new (Centre.X + ((p.X - Centre.X) * scale), Centre.Y + ((p.Y - Centre.Y) * scale));
	}

	/// <summary>The point at depth <paramref name="z"/> that shows at <paramref name="screen"/>.</summary>
	public Point3D Unproject (PointD screen, double z)
	{
		double scale = (FocalLength + z) / FocalLength;
		return new (Centre.X + ((screen.X - Centre.X) * scale), Centre.Y + ((screen.Y - Centre.Y) * scale), z);
	}
}

/// <summary>
/// 3D mode's edits to the four corners of the floating pixels. Each corner keeps its own depth,
/// so the corners need not stay on one plane; what is drawn is always the projected quad.
/// </summary>
internal static class Quad3D
{
	public static Point3D[] Flat (ReadOnlySpan<PointD> quad)
	{
		Point3D[] corners = new Point3D[quad.Length];
		for (int i = 0; i < quad.Length; i++)
			corners[i] = new (quad[i].X, quad[i].Y, 0);
		return corners;
	}

	/// <summary>The quad the camera shows, or null when a corner is too close to or too far from it.</summary>
	public static PointD[]? Project (PinholeCamera camera, ReadOnlySpan<Point3D> corners)
	{
		PointD[] quad = new PointD[corners.Length];
		for (int i = 0; i < corners.Length; i++) {
			if (!camera.Sees (corners[i]))
				return null;
			quad[i] = camera.Project (corners[i]);
		}
		return quad;
	}

	/// <summary>Moves one corner across the screen to <paramref name="screen"/>, keeping its depth.</summary>
	public static Point3D[] MoveCorner (PinholeCamera camera, ReadOnlySpan<Point3D> corners, int corner, PointD screen)
	{
		Point3D[] result = corners.ToArray ();
		result[corner] = camera.Unproject (screen, corners[corner].Z);
		return result;
	}

	/// <summary>
	/// Pushes one corner straight back from the canvas plane, or pulls it forward for negative
	/// <paramref name="doublings"/>: each step doubles or halves its distance from the camera, so
	/// on screen it slides toward or away from the canvas centre.
	/// </summary>
	public static Point3D[] PushCorner (PinholeCamera camera, ReadOnlySpan<Point3D> corners, int corner, double doublings)
	{
		Point3D[] result = corners.ToArray ();
		Point3D p = corners[corner];
		double distance = (camera.FocalLength + p.Z) * Math.Pow (2, doublings);
		result[corner] = p with { Z = distance - camera.FocalLength };
		return result;
	}

	/// <summary>
	/// Turns the corners about their centre: <paramref name="yaw"/> about the vertical axis, then
	/// <paramref name="pitch"/> about the horizontal one, both in radians. A positive yaw swings
	/// the right side forward, a positive pitch the bottom.
	/// </summary>
	public static Point3D[] Tilt (ReadOnlySpan<Point3D> corners, double yaw, double pitch)
	{
		Point3D centre = Centre (corners);
		(double sinYaw, double cosYaw) = Math.SinCos (yaw);
		(double sinPitch, double cosPitch) = Math.SinCos (pitch);

		Point3D[] result = new Point3D[corners.Length];
		for (int i = 0; i < corners.Length; i++) {
			double x = corners[i].X - centre.X;
			double y = corners[i].Y - centre.Y;
			double z = corners[i].Z - centre.Z;

			(x, z) = ((x * cosYaw) + (z * sinYaw), (z * cosYaw) - (x * sinYaw));
			(y, z) = ((y * cosPitch) + (z * sinPitch), (z * cosPitch) - (y * sinPitch));

			result[i] = new (x + centre.X, y + centre.Y, z + centre.Z);
		}
		return result;
	}

	public static bool IsFlat (ReadOnlySpan<Point3D> corners)
	{
		foreach (Point3D p in corners)
			if (p.Z != 0)
				return false;
		return true;
	}

	private static Point3D Centre (ReadOnlySpan<Point3D> corners)
	{
		double x = 0, y = 0, z = 0;
		foreach (Point3D p in corners) {
			x += p.X;
			y += p.Y;
			z += p.Z;
		}
		return new (x / corners.Length, y / corners.Length, z / corners.Length);
	}
}
