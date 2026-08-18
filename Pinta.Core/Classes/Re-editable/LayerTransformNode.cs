using System;
using Cairo;

namespace Pinta.Core;

/// <summary>Numeric settings for a layer-local affine and perspective transform.</summary>
public sealed class LayerTransformData : EffectData, IEquatable<LayerTransformData>
{
	[Caption ("Angle")]
	public DegreesAngle Angle { get; set; }

	[Caption ("Translate Horizontally"), MinimumValue (-10000), MaximumValue (10000)]
	public double TranslateHorizontal { get; set; }

	[Caption ("Translate Vertically"), MinimumValue (-10000), MaximumValue (10000)]
	public double TranslateVertical { get; set; }

	[Caption ("Horizontal Scale"), MinimumValue (0), MaximumValue (16)]
	public double ScaleHorizontal { get; set; } = 1;

	[Caption ("Vertical Scale"), MinimumValue (0), MaximumValue (16)]
	public double ScaleVertical { get; set; } = 1;

	[Caption ("Shear Horizontally"), MinimumValue (-89), MaximumValue (89)]
	public double ShearHorizontal { get; set; }

	[Caption ("Shear Vertically"), MinimumValue (-89), MaximumValue (89)]
	public double ShearVertical { get; set; }

	[Caption ("Flip Horizontally")]
	public bool FlipHorizontal { get; set; }

	[Caption ("Flip Vertically")]
	public bool FlipVertical { get; set; }

	[Caption ("Top Left Perspective — Horizontal"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveTopLeftHorizontal { get; set; }

	[Caption ("Top Left Perspective — Vertical"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveTopLeftVertical { get; set; }

	[Caption ("Top Right Perspective — Horizontal"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveTopRightHorizontal { get; set; }

	[Caption ("Top Right Perspective — Vertical"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveTopRightVertical { get; set; }

	[Caption ("Bottom Right Perspective — Horizontal"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveBottomRightHorizontal { get; set; }

	[Caption ("Bottom Right Perspective — Vertical"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveBottomRightVertical { get; set; }

	[Caption ("Bottom Left Perspective — Horizontal"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveBottomLeftHorizontal { get; set; }

	[Caption ("Bottom Left Perspective — Vertical"), MinimumValue (-10000), MaximumValue (10000)]
	public double PerspectiveBottomLeftVertical { get; set; }

	[Caption ("Resampling")]
	public ResamplingMode Resampling { get; set; } = ResamplingMode.Bilinear;

	public override bool IsDefault
		=> Angle.Degrees == 0
			&& TranslateHorizontal == 0
			&& TranslateVertical == 0
			&& ScaleHorizontal == 1
			&& ScaleVertical == 1
			&& ShearHorizontal == 0
			&& ShearVertical == 0
			&& !FlipHorizontal
			&& !FlipVertical
			&& PerspectiveTopLeftHorizontal == 0
			&& PerspectiveTopLeftVertical == 0
			&& PerspectiveTopRightHorizontal == 0
			&& PerspectiveTopRightVertical == 0
			&& PerspectiveBottomRightHorizontal == 0
			&& PerspectiveBottomRightVertical == 0
			&& PerspectiveBottomLeftHorizontal == 0
			&& PerspectiveBottomLeftVertical == 0;

	public bool Equals (LayerTransformData? other)
		=> other is not null
			&& Angle == other.Angle
			&& TranslateHorizontal == other.TranslateHorizontal
			&& TranslateVertical == other.TranslateVertical
			&& ScaleHorizontal == other.ScaleHorizontal
			&& ScaleVertical == other.ScaleVertical
			&& ShearHorizontal == other.ShearHorizontal
			&& ShearVertical == other.ShearVertical
			&& FlipHorizontal == other.FlipHorizontal
			&& FlipVertical == other.FlipVertical
			&& PerspectiveTopLeftHorizontal == other.PerspectiveTopLeftHorizontal
			&& PerspectiveTopLeftVertical == other.PerspectiveTopLeftVertical
			&& PerspectiveTopRightHorizontal == other.PerspectiveTopRightHorizontal
			&& PerspectiveTopRightVertical == other.PerspectiveTopRightVertical
			&& PerspectiveBottomRightHorizontal == other.PerspectiveBottomRightHorizontal
			&& PerspectiveBottomRightVertical == other.PerspectiveBottomRightVertical
			&& PerspectiveBottomLeftHorizontal == other.PerspectiveBottomLeftHorizontal
			&& PerspectiveBottomLeftVertical == other.PerspectiveBottomLeftVertical
			&& Resampling == other.Resampling;

	public override bool Equals (object? obj) => obj is LayerTransformData other && Equals (other);

	public override int GetHashCode ()
	{
		HashCode hash = new ();
		hash.Add (Angle);
		hash.Add (TranslateHorizontal);
		hash.Add (TranslateVertical);
		hash.Add (ScaleHorizontal);
		hash.Add (ScaleVertical);
		hash.Add (ShearHorizontal);
		hash.Add (ShearVertical);
		hash.Add (FlipHorizontal);
		hash.Add (FlipVertical);
		hash.Add (PerspectiveTopLeftHorizontal);
		hash.Add (PerspectiveTopLeftVertical);
		hash.Add (PerspectiveTopRightHorizontal);
		hash.Add (PerspectiveTopRightVertical);
		hash.Add (PerspectiveBottomRightHorizontal);
		hash.Add (PerspectiveBottomRightVertical);
		hash.Add (PerspectiveBottomLeftHorizontal);
		hash.Add (PerspectiveBottomLeftVertical);
		hash.Add (Resampling);
		return hash.ToHashCode ();
	}
}

/// <summary>A non-destructive transform in a layer's unified child list.</summary>
public sealed class LayerTransformNode : ILayerModifierNode
{
	public LayerTransformData Data { get; set; }
	public DocumentSelection? Clip { get; set; }

	public double Opacity { get; set; } = 1;
	public bool Hidden { get; set; }
	public string Name { get; set; } = string.Empty;
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	public string DisplayName => string.IsNullOrEmpty (Name) ? Translations.GetString ("Transform") : Name;
	public bool IsActive => !Hidden && Opacity > 0 && !Data.IsDefault;

	public LayerTransformNode (LayerTransformData data, DocumentSelection? clip = null)
	{
		Data = data;
		Clip = clip;
	}

	public LayerTransformNode Clone ()
		=> new ((LayerTransformData) Data.Clone (), Clip?.Clone ()) {
			Opacity = Opacity,
			Hidden = Hidden,
			Name = Name,
			BlendMode = BlendMode,
		};

	ILayerModifierNode ILayerModifierNode.CloneModifier () => Clone ();

	public void Apply (ImageSurface surface)
	{
		if (!IsActive)
			return;

		using ImageSurface source = EffectModifierNode.CopyOf (surface);
		using ImageSurface transformed = CairoExtensions.CreateImageSurface (Format.Argb32, surface.Width, surface.Height);
		if (!Render (source, transformed))
			return;

		using Context g = new (surface);
		if (Clip is not null) {
			g.AppendPath (Clip.SelectionPath);
			g.FillRule = FillRule.EvenOdd;
			g.Clip ();
		}
		if (BlendMode == BlendMode.Normal) {
			// A transform moves pixels and can leave the canvas bare, so Normal has to replace the
			// target (SOURCE) rather than composite over it — otherwise the unmoved pixels stay.
			g.Operator = Operator.Source;
			g.SetSourceSurface (transformed, 0, 0);
			g.PaintWithAlpha (Math.Clamp (Opacity, 0, 1));
		} else {
			g.BlendSurface (transformed, BlendMode, Math.Clamp (Opacity, 0, 1));
		}
	}

	private bool Render (ImageSurface source, ImageSurface destination)
	{
		int width = source.Width;
		int height = source.Height;
		if (width <= 0 || height <= 0)
			return false;

		PointD topLeft = TransformPoint (new PointD (0, 0), width, height) + new PointD (
			Data.PerspectiveTopLeftHorizontal,
			Data.PerspectiveTopLeftVertical);
		PointD topRight = TransformPoint (new PointD (width - 1, 0), width, height) + new PointD (
			Data.PerspectiveTopRightHorizontal,
			Data.PerspectiveTopRightVertical);
		PointD bottomRight = TransformPoint (new PointD (width - 1, height - 1), width, height) + new PointD (
			Data.PerspectiveBottomRightHorizontal,
			Data.PerspectiveBottomRightVertical);
		PointD bottomLeft = TransformPoint (new PointD (0, height - 1), width, height) + new PointD (
			Data.PerspectiveBottomLeftHorizontal,
			Data.PerspectiveBottomLeftVertical);

		Span<double> inverse = stackalloc double[9];
		if (!TryHomography (
			[topLeft, topRight, bottomRight, bottomLeft],
			[new PointD (0, 0), new PointD (width - 1, 0), new PointD (width - 1, height - 1), new PointD (0, height - 1)],
			inverse))
			return false;

		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();
		for (int y = 0; y < height; y++) {
			int row = y * width;
			for (int x = 0; x < width; x++) {
				double denominator = (inverse[6] * x) + (inverse[7] * y) + inverse[8];
				if (Math.Abs (denominator) < 1e-12)
					continue;

				double sourceX = ((inverse[0] * x) + (inverse[1] * y) + inverse[2]) / denominator;
				double sourceY = ((inverse[3] * x) + (inverse[4] * y) + inverse[5]) / denominator;
				destinationData[row + x] = Sample (source, sourceData, width, height, sourceX, sourceY);
			}
		}

		destination.MarkDirty ();
		return true;
	}

	private PointD TransformPoint (PointD point, int width, int height)
	{
		double centerX = (width - 1) / 2.0;
		double centerY = (height - 1) / 2.0;
		double scaleX = Math.Max (0.01, Data.ScaleHorizontal) * (Data.FlipHorizontal ? -1 : 1);
		double scaleY = Math.Max (0.01, Data.ScaleVertical) * (Data.FlipVertical ? -1 : 1);

		double x = (point.X - centerX) * scaleX;
		double y = (point.Y - centerY) * scaleY;
		double shearedX = x + (Math.Tan (DegreesToRadians (Data.ShearHorizontal)) * y);
		double shearedY = (Math.Tan (DegreesToRadians (Data.ShearVertical)) * x) + y;
		(double sin, double cos) = Math.SinCos (-Data.Angle.ToRadians ().Radians);

		return new PointD (
			(cos * shearedX) - (sin * shearedY) + centerX + Data.TranslateHorizontal,
			(sin * shearedX) + (cos * shearedY) + centerY + Data.TranslateVertical);
	}

	private ColorBgra Sample (
		ImageSurface source,
		ReadOnlySpan<ColorBgra> sourceData,
		int width,
		int height,
		double x,
		double y)
	{
		if (!double.IsFinite (x) || !double.IsFinite (y))
			return ColorBgra.Transparent;

		if (Data.Resampling == ResamplingMode.NearestNeighbor) {
			int nearestX = (int) Math.Round (x);
			int nearestY = (int) Math.Round (y);
			return nearestX < 0 || nearestY < 0 || nearestX >= width || nearestY >= height
				? ColorBgra.Transparent
				: sourceData[(nearestY * width) + nearestX];
		}

		return source.GetBilinearSample (sourceData, width, height, (float) x, (float) y);
	}

	private static double DegreesToRadians (double degrees) => degrees * Math.PI / 180.0;

	// Solves the projective mapping from four destination points to four source points. Keeping the
	// inverse directly avoids a second matrix inversion for every render.
	private static bool TryHomography (ReadOnlySpan<PointD> from, ReadOnlySpan<PointD> to, Span<double> result)
	{
		Span<double> equations = stackalloc double[8 * 9];
		for (int i = 0; i < 4; i++) {
			double x = from[i].X;
			double y = from[i].Y;
			double u = to[i].X;
			double v = to[i].Y;
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

		for (int i = 0; i < 8; i++)
			result[i] = equations[(i * 9) + 8];
		result[8] = 1;
		return true;
	}
}
