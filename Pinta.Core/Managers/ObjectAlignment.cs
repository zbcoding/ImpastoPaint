using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// A line a moved object is being held on because it lines up with another object, in canvas
/// coordinates. A vertical guide runs at X = <see cref="Position"/> from <see cref="Start"/> to
/// <see cref="End"/> along Y; a horizontal one runs the other way.
/// </summary>
public readonly record struct AlignmentGuide (bool Vertical, double Position, double Start, double End);

/// <summary>
/// The lines an object offers for alignment: the edges and centre lines of its box, and for text
/// also the edges and centre lines of its glyphs and its first line's baseline.
/// </summary>
/// <param name="Box">The object's box; a guide is drawn out to reach it.</param>
/// <param name="XLines">Vertical lines, as X positions.</param>
/// <param name="YLines">Horizontal lines, as Y positions.</param>
public sealed record AlignmentLines (RectangleD Box, double[] XLines, double[] YLines)
{
	public static AlignmentLines OfBox (RectangleD box)
		=> new (box, EdgesAndCentre (box.X, box.Width), EdgesAndCentre (box.Y, box.Height));

	internal static double[] EdgesAndCentre (double start, double length)
		=> [start, start + length / 2.0, start + length];
}

/// <summary>
/// One drag's alignment: what is being moved, as it was when the drag started, and what it can
/// line up with. Taken once at the start because the dragged object is written back into its
/// layer on every move.
/// </summary>
public sealed record AlignmentDrag (AlignmentLines Moving, IReadOnlyList<AlignmentLines> Targets)
{
	public static AlignmentDrag None { get; } = new (AlignmentLines.OfBox (default), []);
}

/// <summary>
/// Impasto: lines a moved object up with the other objects on the canvas - their edges and centre
/// lines, and the glyphs and baseline of text - the way the canvas guides line it up with the
/// canvas itself.
/// </summary>
public static class ObjectAlignment
{
	/// <summary>
	/// Starts a drag of a box - a shape or a moved selection - collecting every visible shape and
	/// text object, and the painted content of every visible layer, to align it to. With Align to
	/// objects off this is <see cref="AlignmentDrag.None"/>, so the drag skips that scan.
	/// </summary>
	/// <param name="moving">
	/// The dragged box when the drag starts. A target with the same box is the dragged object
	/// itself (or an exact duplicate, which aligning to would not move it), so it is left out.
	/// Content covering the whole canvas is left out too: its lines are the canvas's own, and those
	/// belong to the separate snap toggle.
	/// </param>
	public static AlignmentDrag BeginDrag (Document document, IChromeService chrome, RectangleD moving)
	{
		if (!PintaCore.CanvasGrid.AlignToObjects)
			return AlignmentDrag.None;

		return new (AlignmentLines.OfBox (moving), CollectTargets (document, chrome, moving, null));
	}

	/// <summary>Starts a drag of a text object, which aligns by its glyphs and baseline as well as its box.</summary>
	public static AlignmentDrag BeginDrag (Document document, IChromeService chrome, TextObject moving)
	{
		if (!PintaCore.CanvasGrid.AlignToObjects)
			return AlignmentDrag.None;

		TextLayout layout = new (chrome);
		RectangleD box = moving.TextBounds.ToDouble ();
		return new (LinesOf (moving, layout), CollectTargets (document, chrome, box, moving));
	}

	private static List<AlignmentLines> CollectTargets (Document document, IChromeService chrome, RectangleD moving, TextObject? movingText)
	{
		RectangleD canvas = new (0, 0, document.ImageSize.Width, document.ImageSize.Height);
		TextLayout layout = new (chrome);
		List<AlignmentLines> targets = [];

		foreach (UserLayer layer in document.Layers.UserLayers) {
			if (layer.Hidden)
				continue;

			if (PaintedBounds (layer.Surface) is RectangleD painted && IsTarget (painted, moving, canvas))
				targets.Add (AlignmentLines.OfBox (painted));

			foreach (ILayerObject layerObject in layer.Objects) {
				if (layerObject.Hidden || layerObject == movingText)
					continue;

				AlignmentLines? lines = layerObject switch {
					TextObject text when !text.IsEmpty => LinesOf (text, layout),
					ShapeObject shape => ShapeBox (shape) is RectangleD box ? AlignmentLines.OfBox (box) : null,
					_ => null,
				};
				if (lines is not null && IsTarget (lines.Box, moving, canvas))
					targets.Add (lines);
			}
		}

		return targets;
	}

	private static bool IsTarget (RectangleD box, RectangleD moving, RectangleD canvas)
		=> box.Width > 0 && box.Height > 0 && box != moving && box != canvas;

	/// <summary>
	/// A text object's box is padded well outside its letters for the move and resize grips, so
	/// text also offers the lines a reader sees: the left, centre and right of its glyphs, their
	/// top, middle and bottom, and the first line's baseline, which is what puts two sizes of text
	/// on one line. Rotated text only offers its box, as its glyph lines are not horizontal.
	/// </summary>
	private static AlignmentLines LinesOf (TextObject text, TextLayout layout)
	{
		AlignmentLines box = AlignmentLines.OfBox (text.TextBounds.ToDouble ());
		if (text.IsEmpty || text.Rotation % 360 != 0)
			return box;

		layout.Engine = text.Engine;
		layout.Layout.GetPixelExtents (out RectangleI ink, out _);
		PointI origin = text.Engine.Origin;
		double baseline = origin.Y + PangoExtensions.UnitsToPixels (layout.Layout.GetBaseline ());

		return box with {
			XLines = [.. box.XLines, .. AlignmentLines.EdgesAndCentre (origin.X + ink.X, ink.Width)],
			YLines = [.. box.YLines, .. AlignmentLines.EdgesAndCentre (origin.Y + ink.Y, ink.Height), baseline],
		};
	}

	/// <summary>
	/// Shapes are measured by their control points, the same box the shape tools snap a moved
	/// shape by, so two shapes centre on each other exactly.
	/// </summary>
	private static RectangleD? ShapeBox (ShapeObject shape)
	{
		if (shape.ControlPoints.Count == 0)
			return null;

		double left = double.MaxValue, top = double.MaxValue;
		double right = double.MinValue, bottom = double.MinValue;
		foreach (ShapeControlPoint point in shape.ControlPoints) {
			left = Math.Min (left, point.Position.X);
			top = Math.Min (top, point.Position.Y);
			right = Math.Max (right, point.Position.X);
			bottom = Math.Max (bottom, point.Position.Y);
		}
		return new RectangleD (left, top, right - left, bottom - top);
	}

	private static RectangleD? PaintedBounds (ImageSurface surface)
	{
		surface.Flush ();
		return PaintedBounds (surface.GetReadOnlyPixelData (), surface.Width, surface.Height)?.ToDouble ();
	}

	/// <summary>
	/// The box around every pixel that is not fully transparent, or null for an empty layer. An
	/// image pasted onto a layer of its own is found this way; a layer painted edge to edge comes
	/// out as the whole canvas. Runs on every drag start over every visible layer, so each row is
	/// searched with the vectorized span search: premultiplied transparent pixels are all-zero.
	/// </summary>
	internal static RectangleI? PaintedBounds (ReadOnlySpan<ColorBgra> pixels, int width, int height)
	{
		ReadOnlySpan<uint> words = MemoryMarshal.Cast<ColorBgra, uint> (pixels);
		int left = width, right = -1, top = -1, bottom = -1;

		for (int y = 0; y < height; ++y) {
			ReadOnlySpan<uint> row = words.Slice (y * width, width);

			int first = row.IndexOfAnyExcept (0u);
			if (first < 0)
				continue;
			int last = row.LastIndexOfAnyExcept (0u);

			if (top < 0)
				top = y;
			bottom = y;
			left = Math.Min (left, first);
			right = Math.Max (right, last);
		}

		return top < 0 ? null : new RectangleI (left, top, right - left + 1, bottom - top + 1);
	}

	/// <summary>
	/// Along one axis, the smallest move within <paramref name="tolerance"/> that lands one of the
	/// moved object's lines on one of a target's lines. The moved object's lines ride along with
	/// <paramref name="origin"/>, the box's leading edge. Returns the box's new origin and the
	/// line it landed on, or null when no target is close.
	/// </summary>
	internal static (double Origin, double Line)? AlignExtent (
		double origin,
		AlignmentLines moving,
		IReadOnlyList<AlignmentLines> targets,
		bool horizontal,
		double tolerance)
	{
		double movingStart = horizontal ? moving.Box.X : moving.Box.Y;
		double[] movingLines = horizontal ? moving.XLines : moving.YLines;

		(double, double)? best = null;
		double bestDistance = tolerance;

		foreach (AlignmentLines target in targets) {
			foreach (double line in horizontal ? target.XLines : target.YLines) {
				foreach (double movingLine in movingLines) {
					double offset = movingLine - movingStart;
					double distance = Math.Abs (origin + offset - line);
					if (distance >= bestDistance)
						continue;
					bestDistance = distance;
					best = (line - offset, line);
				}
			}
		}

		return best;
	}

	/// <summary>
	/// The guide to draw for a line the moved box landed on: long enough to reach the box and every
	/// target that has one of its own lines there, so it shows what the box is lined up with.
	/// </summary>
	internal static AlignmentGuide GuideFor (RectangleD moved, double line, IReadOnlyList<AlignmentLines> targets, bool vertical)
	{
		const double SAME_LINE = 0.01;

		double start = vertical ? moved.Y : moved.X;
		double end = start + (vertical ? moved.Height : moved.Width);

		foreach (AlignmentLines target in targets) {
			bool sharesLine = false;
			foreach (double targetLine in vertical ? target.XLines : target.YLines)
				sharesLine |= Math.Abs (targetLine - line) < SAME_LINE;
			if (!sharesLine)
				continue;

			double spanStart = vertical ? target.Box.Y : target.Box.X;
			double spanLength = vertical ? target.Box.Height : target.Box.Width;
			start = Math.Min (start, spanStart);
			end = Math.Max (end, spanStart + spanLength);
		}

		return new AlignmentGuide (vertical, line, start, end);
	}
}
