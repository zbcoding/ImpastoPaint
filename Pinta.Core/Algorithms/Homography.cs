using System;

namespace Pinta.Core;

/// <summary>
/// A projective (perspective) mapping of the plane, the 3x3 matrix that carries one quadrilateral
/// onto another. Row-major: <c>x' = (M00 x + M01 y + M02) / (M20 x + M21 y + M22)</c>, likewise
/// for <c>y'</c> with row 1.
/// </summary>
public readonly struct Homography
{
	public readonly double M00, M01, M02;
	public readonly double M10, M11, M12;
	public readonly double M20, M21, M22;

	private Homography (ReadOnlySpan<double> m)
	{
		M00 = m[0]; M01 = m[1]; M02 = m[2];
		M10 = m[3]; M11 = m[4]; M12 = m[5];
		M20 = m[6]; M21 = m[7]; M22 = m[8];
	}

	/// <summary>
	/// Solves the mapping that carries each of the four <paramref name="from"/> points onto the
	/// matching <paramref name="to"/> point. Fails when either set is degenerate (three points on
	/// a line), which has no such mapping.
	/// </summary>
	public static bool TryCreate (ReadOnlySpan<PointD> from, ReadOnlySpan<PointD> to, out Homography result)
	{
		result = default;
		if (from.Length != 4 || to.Length != 4)
			throw new ArgumentException ("A homography is defined by exactly four point pairs.");

		if (HasThreeOnALine (from) || HasThreeOnALine (to))
			return false;

		// The solve pins the bottom-right entry to 1, which cannot express a mapping that sends the
		// origin to infinity, so a perfectly good quad whose vanishing line crossed (0,0) failed.
		// Solving between the point sets re-centred on their centroids avoids it: the vanishing line
		// never crosses a convex quad's interior, where its centroid lies.
		PointD fromCentre = Centroid (from);
		PointD toCentre = Centroid (to);

		Span<double> equations = stackalloc double[8 * 9];
		for (int i = 0; i < 4; i++) {
			double x = from[i].X - fromCentre.X;
			double y = from[i].Y - fromCentre.Y;
			double u = to[i].X - toCentre.X;
			double v = to[i].Y - toCentre.Y;
			int first = (i * 2) * 9;
			int second = first + 9;

			equations[first] = x;
			equations[first + 1] = y;
			equations[first + 2] = 1;
			equations[first + 6] = -u * x;
			equations[first + 7] = -u * y;
			equations[first + 8] = u;

			equations[second + 3] = x;
			equations[second + 4] = y;
			equations[second + 5] = 1;
			equations[second + 6] = -v * x;
			equations[second + 7] = -v * y;
			equations[second + 8] = v;
		}

		for (int column = 0; column < 8; column++) {
			int pivot = column;
			double pivotMagnitude = Math.Abs (equations[(pivot * 9) + column]);
			for (int row = column + 1; row < 8; row++) {
				double magnitude = Math.Abs (equations[(row * 9) + column]);
				if (magnitude > pivotMagnitude) {
					pivot = row;
					pivotMagnitude = magnitude;
				}
			}
			if (pivotMagnitude < 1e-12)
				return false;

			if (pivot != column)
				for (int entry = column; entry < 9; entry++)
					(equations[(column * 9) + entry], equations[(pivot * 9) + entry]) =
						(equations[(pivot * 9) + entry], equations[(column * 9) + entry]);

			double divisor = equations[(column * 9) + column];
			for (int entry = column; entry < 9; entry++)
				equations[(column * 9) + entry] /= divisor;

			for (int row = 0; row < 8; row++) {
				if (row == column)
					continue;
				double factor = equations[(row * 9) + column];
				for (int entry = column; entry < 9; entry++)
					equations[(row * 9) + entry] -= factor * equations[(column * 9) + entry];
			}
		}

		Span<double> m = stackalloc double[9];
		for (int i = 0; i < 8; i++)
			m[i] = equations[(i * 9) + 8];
		m[8] = 1;

		// Undo the re-centring: H = translate(toCentre) · H' · translate(-fromCentre).
		for (int row = 0; row < 3; row++)
			m[(row * 3) + 2] -= (fromCentre.X * m[row * 3]) + (fromCentre.Y * m[(row * 3) + 1]);
		for (int column = 0; column < 3; column++) {
			m[column] += toCentre.X * m[6 + column];
			m[3 + column] += toCentre.Y * m[6 + column];
		}

		result = new Homography (m);
		return true;
	}

	// The linear solve still "succeeds" for such a set, with a singular matrix that maps a whole
	// region onto a line.
	private static bool HasThreeOnALine (ReadOnlySpan<PointD> points)
	{
		for (int skip = 0; skip < 4; skip++) {
			PointD a = points[(skip + 1) % 4];
			PointD b = points[(skip + 2) % 4];
			PointD c = points[(skip + 3) % 4];
			double area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
			if (Math.Abs (area) < 1e-9)
				return true;
		}

		return false;
	}

	private static PointD Centroid (ReadOnlySpan<PointD> points)
	{
		double x = 0, y = 0;
		foreach (PointD p in points) {
			x += p.X;
			y += p.Y;
		}

		return new PointD (x / points.Length, y / points.Length);
	}

	/// <summary>
	/// Maps <paramref name="point"/>. Fails for a point on the mapping's vanishing line, which
	/// goes to infinity.
	/// </summary>
	public bool TryMap (PointD point, out PointD mapped)
	{
		double denominator = (M20 * point.X) + (M21 * point.Y) + M22;
		if (Math.Abs (denominator) < 1e-12) {
			mapped = default;
			return false;
		}

		mapped = new PointD (
			((M00 * point.X) + (M01 * point.Y) + M02) / denominator,
			((M10 * point.X) + (M11 * point.Y) + M12) / denominator);
		return true;
	}
}
