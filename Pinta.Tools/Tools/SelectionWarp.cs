using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using ClipperLib;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>What dragging the Move Selected Pixels grips does.</summary>
internal enum TransformMode
{
	Move,
	Skew,
	Perspective,
}

/// <summary>
/// Skew and perspective for the floating selection. The content's outline is a quad (corners in
/// order around it); a drag reshapes that quad and the lifted pixels are resampled to fill it.
/// <para>
/// Consecutive warps form a session that always resamples from the pixels as they were when the
/// session began, so adjusting the corners over and over blurs them once rather than once per
/// drag. Anything else touching the selection or the floating pixels ends the session; the next
/// warp starts a new one from whatever is on screen.
/// </para>
/// </summary>
internal sealed class SelectionWarp
{
	private sealed class Session (Document document, ImageSurface pixels, Matrix pixelsToCanvas, PointD[] quad, DocumentSelection selection, DocumentSelection previousSelection)
	{
		public Document Document { get; } = document;
		public ImageSurface Pixels { get; } = pixels;
		public Matrix PixelsToCanvas { get; } = pixelsToCanvas;
		public PointD[] OriginalQuad { get; } = quad;
		public DocumentSelection Selection { get; } = selection;
		public DocumentSelection PreviousSelection { get; } = previousSelection;

		/// <summary>The surface the selection layer shows while this session is still current.</summary>
		public ImageSurface Shown { get; set; } = null!;
		public PointD[] Quad { get; set; } = quad;
	}

	private sealed class Gesture (Session session, TransformMode mode, int grip, PointD start, ImageSurface surfaceBefore)
	{
		public Session Session { get; } = session;
		public TransformMode Mode { get; } = mode;
		public int Grip { get; } = grip;
		public PointD Start { get; } = start;
		public PointD[] StartQuad { get; } = session.Quad;
		public ImageSurface SurfaceBefore { get; } = surfaceBefore;
		public ImageSurface? Output { get; set; }
	}

	private Session? session;
	private Gesture? gesture;

	public bool IsDragging => gesture is not null;

	/// <summary>True while this class itself is replacing the document's selection.</summary>
	public bool IsApplying { get; private set; }

	/// <summary>The outline grips sit on, or null when there is no visible selection.</summary>
	public PointD[]? CurrentQuad (Document document)
	{
		if (!document.Selection.Visible)
			return null;

		return IsSessionCurrent (document) ? session!.Quad : QuadOf (document.Selection);
	}

	/// <summary>
	/// Ends the session, and abandons any drag still open; the next warp starts over from what is
	/// on screen.
	/// </summary>
	public void Forget ()
	{
		gesture = null;
		session?.Pixels.Dispose ();
		session = null;
	}

	public void Begin (Document document, TransformMode mode, int grip, PointD start)
	{
		if (!IsSessionCurrent (document)) {
			Forget ();
			session = StartSession (document);
		}

		gesture = new Gesture (session!, mode, grip, start, document.Layers.SelectionLayer.Surface);
	}

	public void Update (Document document, PointD point)
	{
		if (gesture is null)
			return;

		PointD drag = point - gesture.Start;
		PointD[] target = gesture.Mode == TransformMode.Skew
			? Skew (gesture.StartQuad, gesture.Grip, drag)
			: MoveCorner (gesture.StartQuad, gesture.Grip, drag);

		// A quad folded over itself has no perspective mapping; hold the last good shape.
		if (!IsConvex (target))
			return;

		Session s = gesture.Session;
		if (!Homography.TryCreate (target, s.OriginalQuad, out Homography canvasToOriginal)
			|| !Homography.TryCreate (s.OriginalQuad, target, out Homography originalToCanvas))
			return;

		Layer layer = document.Layers.SelectionLayer;
		if (gesture.Output is null) {
			// ponytail: the warped pixels are baked at canvas positions into a surface the size of
			// the original, so whatever lands outside that surface (left of or above the canvas, or
			// past its far edges) is dropped once the session ends and a later move cannot bring it
			// back. Cost ceiling: corner drags well off the canvas. Upgrade: size the output to the
			// quad's bounds and place it through layer.Transform, once every selection-layer reader
			// honours that transform.
			gesture.Output = CairoExtensions.CreateImageSurface (Format.Argb32, s.Pixels.Width, s.Pixels.Height);
			layer.Surface = gesture.Output;
			layer.Transform.InitIdentity ();
			s.Shown = gesture.Output;
		}

		Render (s.Pixels, s.PixelsToCanvas, canvasToOriginal, target, gesture.Output);

		// Before the selection changes: its SelectionChanged listeners read the quad back.
		s.Quad = target;

		IsApplying = true;
		try {
			document.Selection = MapSelection (s.Selection, originalToCanvas, target);
		} finally {
			IsApplying = false;
		}

		document.Workspace.Invalidate ();
	}

	/// <summary>
	/// Finishes the drag. Returns the undo step for the resampled pixels, or null if the drag never
	/// changed the shape.
	/// </summary>
	public BaseHistoryItem? End (Document document, string icon, string text)
	{
		if (gesture is null)
			return null;

		Gesture finished = gesture;
		gesture = null;

		if (finished.Output is null)
			return null;

		Session s = finished.Session;
		if (Homography.TryCreate (s.OriginalQuad, s.Quad, out Homography originalToCanvas))
			document.PreviousSelection = MapSelection (s.PreviousSelection, originalToCanvas, s.Quad);

		return new SelectionLayerSurfaceHistoryItem (icon, text, document, finished.SurfaceBefore);
	}

	// The session is only good while the selection layer still shows what it last produced. A
	// commit and re-lift, an undo, or a plain move all swap the surface or the selection, and the
	// latter calls Forget through the tool's SelectionChanged handler.
	private bool IsSessionCurrent (Document document)
		=> session is not null
			&& session.Document == document
			&& document.Layers.ShowSelectionLayer
			&& document.Layers.SelectionLayer.Surface == session.Shown;

	private static Session StartSession (Document document)
	{
		Layer layer = document.Layers.SelectionLayer;
		return new Session (
			document,
			layer.Surface.Clone (),
			layer.Transform.Clone (),
			QuadOf (document.Selection),
			document.Selection.Clone (),
			document.PreviousSelection.Clone ()) {
			Shown = layer.Surface,
		};
	}

	/// <summary>
	/// The selection's own outline when it is a convex four-cornered polygon (a rectangle, or one
	/// already skewed), otherwise its bounding box. Corners run in polygon order.
	/// </summary>
	internal static PointD[] QuadOf (DocumentSelection selection)
	{
		List<List<IntPoint>> polygons = selection.SelectionPolygons;
		if (polygons.Count == 1 && IsQuadPolygon (polygons[0])) {
			PointD[] corners = [.. polygons[0].Take (4).Select (p => new PointD (p.X, p.Y))];
			if (IsConvex (corners))
				return corners;
		}

		RectangleD bounds = selection.GetBounds ();
		return [
			new (bounds.Left, bounds.Top),
			new (bounds.Right, bounds.Top),
			new (bounds.Right, bounds.Bottom),
			new (bounds.Left, bounds.Bottom),
		];
	}

	// Four corners, or four plus the first repeated to close the ring.
	private static bool IsQuadPolygon (List<IntPoint> polygon)
		=> polygon.Count == 4 || (polygon.Count == 5 && polygon[4] == polygon[0]);

	/// <summary>
	/// Slides edge <paramref name="edge"/> (corner <c>edge</c> to corner <c>edge + 1</c>) along its
	/// own direction by the part of <paramref name="drag"/> that runs along it. The opposite edge
	/// stays put, so a rectangle becomes a parallelogram.
	/// </summary>
	internal static PointD[] Skew (ReadOnlySpan<PointD> quad, int edge, PointD drag)
	{
		PointD[] result = quad.ToArray ();
		int next = (edge + 1) % 4;
		PointD direction = quad[next] - quad[edge];
		double length = Math.Sqrt ((direction.X * direction.X) + (direction.Y * direction.Y));
		if (length < 1e-9)
			return result;

		double along = ((drag.X * direction.X) + (drag.Y * direction.Y)) / (length * length);
		PointD shift = direction.Scaled (along);
		result[edge] = quad[edge] + shift;
		result[next] = quad[next] + shift;
		return result;
	}

	/// <summary>Moves one corner freely, the other three stay put.</summary>
	internal static PointD[] MoveCorner (ReadOnlySpan<PointD> quad, int corner, PointD drag)
	{
		PointD[] result = quad.ToArray ();
		result[corner] = quad[corner] + drag;
		return result;
	}

	/// <summary>Whether the quad turns the same way at every corner, so it neither folds nor collapses.</summary>
	internal static bool IsConvex (ReadOnlySpan<PointD> quad)
	{
		int sign = 0;
		for (int i = 0; i < 4; i++) {
			double turn = Cross (quad[(i + 1) % 4] - quad[i], quad[(i + 2) % 4] - quad[(i + 1) % 4]);
			if (Math.Abs (turn) < 1e-6)
				return false;

			int turnSign = Math.Sign (turn);
			if (sign != 0 && turnSign != sign)
				return false;
			sign = turnSign;
		}

		return true;
	}

	internal static DocumentSelection MapSelection (DocumentSelection selection, Homography map, ReadOnlySpan<PointD> quad)
	{
		List<List<IntPoint>> polygons = new (selection.SelectionPolygons.Count);
		foreach (List<IntPoint> polygon in selection.SelectionPolygons) {
			List<IntPoint> mapped = new (polygon.Count);
			foreach (IntPoint point in polygon) {
				PointD p = map.TryMap (new PointD (point.X, point.Y), out PointD m) ? m : new PointD (point.X, point.Y);
				mapped.Add (new IntPoint ((long) Math.Round (p.X), (long) Math.Round (p.Y)));
			}
			polygons.Add (mapped);
		}

		DocumentSelection result = selection.Clone ();
		result.SelectionPolygons = polygons;
		result.HandleBounds = BoundsOf (quad);
		result.MarkDirty ();
		return result;
	}

	/// <summary>
	/// Fills <paramref name="destination"/> with <paramref name="source"/> warped onto
	/// <paramref name="quad"/>. Each destination pixel centre inside the quad goes back through
	/// <paramref name="canvasToOriginal"/> (the warp, inverted) and then through the source's own
	/// placement <paramref name="sourceToCanvas"/>, inverted, to the source pixel it samples.
	/// </summary>
	internal static void Render (
		ImageSurface source,
		Matrix sourceToCanvas,
		Homography canvasToOriginal,
		ReadOnlySpan<PointD> quad,
		ImageSurface destination)
	{
		destination.GetPixelData ().Clear ();

		Matrix canvasToSource = sourceToCanvas.Clone ();
		canvasToSource.Invert ();
		// The bindings expose no matrix fields, so read the affine map off three mapped points:
		// source = origin + x·xAxis + y·yAxis.
		PointD origin = canvasToSource.TransformPoint (new PointD (0, 0));
		PointD xAxis = canvasToSource.TransformPoint (new PointD (1, 0)) - origin;
		PointD yAxis = canvasToSource.TransformPoint (new PointD (0, 1)) - origin;

		ConvexQuad region = new (quad);
		RectangleD bounds = BoundsOf (quad).Inflated (1, 1);
		int left = Math.Max (0, (int) Math.Floor (bounds.Left));
		int top = Math.Max (0, (int) Math.Floor (bounds.Top));
		int right = Math.Min (destination.Width, (int) Math.Ceiling (bounds.Right));
		int bottom = Math.Min (destination.Height, (int) Math.Ceiling (bounds.Bottom));
		if (left >= right || top >= bottom) {
			destination.MarkDirty ();
			return;
		}

		int sourceWidth = source.Width;
		int sourceHeight = source.Height;
		int destinationWidth = destination.Width;

		// Bands, like LayerTransformNode: one pixel-buffer lookup per band rather than per row.
		int rows = bottom - top;
		int bands = Math.Min (rows, Math.Max (1, Environment.ProcessorCount));
		int bandHeight = (rows + bands - 1) / bands;

		Parallel.For (0, bands, band => {
			int bandTop = top + (band * bandHeight);
			int bandBottom = Math.Min (bandTop + bandHeight, bottom);
			ReadOnlySpan<ColorBgra> sourcePixels = source.GetReadOnlyPixelData ();
			Span<ColorBgra> destinationPixels = destination.GetPixelData ();

			for (int y = bandTop; y < bandBottom; y++) {
				int row = y * destinationWidth;
				for (int x = left; x < right; x++) {
					PointD centre = new (x + 0.5, y + 0.5);
					if (!region.Covers (centre) || !canvasToOriginal.TryMap (centre, out PointD original))
						continue;

					double sx = origin.X + (xAxis.X * original.X) + (yAxis.X * original.Y);
					double sy = origin.Y + (xAxis.Y * original.X) + (yAxis.Y * original.Y);
					destinationPixels[row + x] = SampleAtCentre (source, sourcePixels, sourceWidth, sourceHeight, sx, sy);
				}
			}
		});

		destination.MarkDirty ();
	}

	// Bilinear sample where integer + 0.5 is a pixel's centre. Points off the surface are empty
	// rather than clamped to the border, which would smear edge pixels across the quad.
	private static ColorBgra SampleAtCentre (ImageSurface source, ReadOnlySpan<ColorBgra> pixels, int width, int height, double x, double y)
	{
		if (!(x >= 0 && y >= 0 && x < width && y < height))
			return ColorBgra.Transparent;

		return source.GetBilinearSample (
			pixels,
			width,
			height,
			(float) Math.Clamp (x - 0.5, 0, width - 1),
			(float) Math.Clamp (y - 0.5, 0, height - 1));
	}

	private static double Cross (PointD a, PointD b) => (a.X * b.Y) - (a.Y * b.X);

	private static RectangleD BoundsOf (ReadOnlySpan<PointD> quad)
	{
		double minX = double.MaxValue, minY = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue;
		foreach (PointD p in quad) {
			minX = Math.Min (minX, p.X);
			minY = Math.Min (minY, p.Y);
			maxX = Math.Max (maxX, p.X);
			maxY = Math.Max (maxY, p.Y);
		}

		return new RectangleD (minX, minY, maxX - minX, maxY - minY);
	}

	/// <summary>
	/// Point-in-quad test for a convex quad of either winding, widened by one pixel so the
	/// anti-aliased fringe just outside the outline still gets sampled.
	/// </summary>
	private readonly struct ConvexQuad
	{
		private const double Fringe = 1.0;

		private readonly PointD c0, c1, c2, c3;
		private readonly double l0, l1, l2, l3;
		private readonly double winding;

		public ConvexQuad (ReadOnlySpan<PointD> quad)
		{
			(c0, c1, c2, c3) = (quad[0], quad[1], quad[2], quad[3]);
			l0 = Length (c1 - c0);
			l1 = Length (c2 - c1);
			l2 = Length (c3 - c2);
			l3 = Length (c0 - c3);
			winding = Math.Sign (Cross (c1 - c0, c2 - c1));
		}

		public bool Covers (PointD p)
			=> Inside (c0, c1, l0, p) && Inside (c1, c2, l1, p) && Inside (c2, c3, l2, p) && Inside (c3, c0, l3, p);

		private bool Inside (PointD a, PointD b, double length, PointD p)
			=> winding * Cross (b - a, p - a) >= -Fringe * length;

		private static double Length (PointD v) => Math.Sqrt ((v.X * v.X) + (v.Y * v.Y));
	}
}
