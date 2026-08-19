using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Brings up a whole live document — every manager, a real <see cref="DocumentHistory"/> — for the
/// interaction suites, and supplies the pieces a test composes a scene out of.
///
/// <para>
/// The Core suite was written believing it could not touch <see cref="PintaCore"/> (see the notes in
/// <see cref="LayerMaskTest"/>). It can: the static constructor only needs GTK's type system loaded,
/// which <c>Gtk.Module.Initialize</c> does without opening a display. Widgets construct fine
/// unrealized, so the chrome gets a real window that is only ever written to.
/// </para>
/// </summary>
internal abstract class DocumentHarness
{
	/// <summary>
	/// Big enough that regions can be told apart, and that real text rendering has room to put ink
	/// somewhere a test can find it. Small enough that a full-surface effect pass stays instant.
	/// </summary>
	protected const int CanvasSize = 32;

	protected static readonly ColorBgra Red = ColorBgra.FromBgra (0, 0, 255, 255);
	protected static readonly ColorBgra Blue = ColorBgra.FromBgra (255, 0, 0, 255);
	protected static readonly ColorBgra Green = ColorBgra.FromBgra (0, 255, 0, 255);
	protected static readonly ColorBgra Transparent = ColorBgra.FromBgra (0, 0, 0, 0);

	protected Document Document { get; private set; } = null!;

	[OneTimeSetUp]
	public void InitializeCore ()
	{
		Gtk.Module.Initialize ();
		Cairo.Module.Initialize ();

		// Activating a document writes the window title and appends to the Window menu, so the chrome
		// needs a shell and that menu needs registering. Nothing here is realized; no display is opened.
		Gtk.Application app = Gtk.Application.New ("com.github.zbcoding.Impasto.Tests", Gio.ApplicationFlags.NonUnique);
		PintaCore.Chrome.InitializeApplication (app);
		PintaCore.Chrome.InitializeWindowShell (Gtk.Window.New ());
		PintaCore.Actions.Window.RegisterActions (app, Gio.Menu.New ());

		// Shapes are drawn by Pinta.Tools, which Core cannot reference, through a delegate seam. These
		// suites are about how a shape composites — its order against nodes, its clip, its blend, its
		// survival through history — not about how its geometry is rasterized (ShapeObjectTest and the
		// Tools suite own that), so a deterministic stand-in renderer is what they want anyway.
		LayerObjectSelection.ShapeRenderer = RenderShapeForTest;
	}

	// A fresh document per test, activated because history items reach for
	// PintaCore.Workspace.ActiveDocument, and closed again so no test sees another's stack.
	[SetUp]
	public void OpenDocument ()
	{
		Document = new Document (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (CanvasSize, CanvasSize));

		Document.Layers.AddNewLayer (string.Empty);
		PintaCore.Workspace.ActivateDocument (Document);
	}

	[TearDown]
	public void CloseDocument ()
	{
		PintaCore.Workspace.CloseDocument (Document);
	}

	// --- The scene ------------------------------------------------------------------------------

	protected UserLayer Layer (int index) => Document.Layers[index];

	protected UserLayer AddLayer (ColorBgra? fill = null)
	{
		UserLayer layer = Document.Layers.AddNewLayer (string.Empty);
		if (fill is ColorBgra color)
			Fill (layer.Surface, color);
		return layer;
	}

	protected static void Fill (ImageSurface surface, ColorBgra color)
	{
		surface.GetPixelData ().Fill (color);
		surface.MarkDirty ();
	}

	protected static void FillRect (ImageSurface surface, RectangleI rect, ColorBgra color)
	{
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int y = rect.Top; y <= rect.Bottom; ++y)
			for (int x = rect.Left; x <= rect.Right; ++x)
				data[(y * surface.Width) + x] = color;
		surface.MarkDirty ();
	}

	/// <summary>
	/// What the canvas actually shows for a layer, flattened. A layer carrying modifiers renders as
	/// one accumulated composite; a layer carrying only objects renders as its raster with the object
	/// surfaces painted over. Reading either one alone would silently test the wrong path, so this
	/// goes through <see cref="UserLayer.GetLayersToPaint"/> — the same enumeration the canvas paints.
	/// </summary>
	protected static ColorBgra Shown (UserLayer layer, int x, int y)
	{
		using ImageSurface flat = CairoExtensions.CreateImageSurface (
			Format.Argb32, layer.Surface.Width, layer.Surface.Height);

		using (Context g = new (flat)) {
			foreach (Layer piece in layer.GetLayersToPaint ()) {
				if (piece.Hidden)
					continue;
				g.BlendSurface (piece.Surface, piece.BlendMode, piece.Opacity);
			}
		}
		flat.MarkDirty ();

		return flat.GetColorBgra (new PointI (x, y));
	}

	protected static ColorBgra Shown (UserLayer layer, PointI at) => Shown (layer, at.X, at.Y);

	/// <summary>A frozen rectangular selection, as a node or object drawn inside one carries.</summary>
	protected static DocumentSelection SelectionOf (RectangleI region)
	{
		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (new RectangleD (region.X, region.Y, region.Width, region.Height));
		return selection;
	}

	/// <summary>An elliptical selection inscribed in <paramref name="region"/>: the same bounding box, less area.</summary>
	protected static DocumentSelection EllipseIn (RectangleI region)
	{
		DocumentSelection selection = new ();
		selection.CreateEllipseSelection (new RectangleD (region.X, region.Y, region.Width, region.Height));
		return selection;
	}

	/// <summary>An arbitrary selection outline, for the concave and notched cases a rectangle cannot express.</summary>
	protected static DocumentSelection PolygonSelection (params PointI[] points)
	{
		DocumentSelection selection = new ();
		selection.SelectionPolygons.Add ([.. points.Select (p => new ClipperLib.IntPoint (p.X, p.Y))]);
		return selection;
	}

	protected void Refresh (UserLayer layer)
		=> ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, layer);

	/// <summary>Adds an object to a layer the way the app does: as one undoable list swap.</summary>
	protected void AddObject (UserLayer layer, ILayerObject item, string label)
	{
		List<ILayerObject> before = ObjectOpacity.CloneAll (layer.Objects);
		layer.Objects.Add (item);
		Refresh (layer);
		Document.History.PushNewItem (
			new LayerObjectsHistoryItem (PintaCore.Workspace, PintaCore.Chrome, string.Empty, label, layer, before));
	}

	/// <summary>Edits a layer's raster as one undoable step, the way a brush stroke does.</summary>
	protected void PaintRaster (UserLayer layer, Action<ImageSurface> paint, string label)
	{
		ImageSurface before = layer.Surface.Clone ();
		paint (layer.Surface);
		layer.Surface.MarkDirty ();
		Refresh (layer);
		Document.History.PushNewItem (
			new SimpleHistoryItem (string.Empty, label, before, Document.Layers.IndexOf (layer)));
	}

	// --- Test effects ---------------------------------------------------------------------------
	//
	// Not the shipped effects: these have to be cheap, exactly predictable per channel, and
	// non-commutative with each other, so that composition order shows up in the pixels rather than
	// having to be inferred.

	protected sealed class InvertEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Invert (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
			=> PerPixel (src, dst, rois, c => ColorBgra.FromBgra ((byte) (255 - c.B), (byte) (255 - c.G), (byte) (255 - c.R), c.A));
	}

	protected sealed class HalveEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Halve (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
			=> PerPixel (src, dst, rois, c => ColorBgra.FromBgra ((byte) (c.B / 2), (byte) (c.G / 2), (byte) (c.R / 2), c.A));
	}

	/// <summary>Records every region it was handed, so a test can assert on what the node decided to render.</summary>
	protected sealed class RegionRecordingEffect : BaseEffect
	{
		public List<RectangleI> Regions { get; } = [];
		public override bool IsTileable => false;
		public override string Name => "Region recorder (test)";

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			foreach (RectangleI roi in rois)
				Regions.Add (roi);
		}
	}

	private static void PerPixel (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois, Func<ColorBgra, ColorBgra> f)
	{
		Span<ColorBgra> dstData = dst.GetPixelData ();
		ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
		foreach (RectangleI roi in rois)
			for (int y = roi.Top; y <= roi.Bottom; ++y)
				for (int x = roi.Left; x <= roi.Right; ++x) {
					int i = (y * dst.Width) + x;
					dstData[i] = f (srcData[i]);
				}
	}

	protected static EffectModifierNode Invert (DocumentSelection? clip = null) => new (new InvertEffect (), clip);
	protected static EffectModifierNode Halve (DocumentSelection? clip = null) => new (new HalveEffect (), clip);

	// --- Objects --------------------------------------------------------------------------------

	/// <summary>
	/// A closed polygon shape. Control points are given in canvas coordinates, so a test can place a
	/// concave or self-crossing outline over a chosen region and assert on where its ink landed.
	/// </summary>
	protected static ShapeObject Polygon (Color fill, params PointD[] points)
	{
		ShapeObject shape = new () {
			ShapeType = ShapeObjectType.OpenLineCurveSeries,
			Closed = true,
			FillColor = fill,
			OutlineColor = fill,
			AntiAliasing = false,
		};
		foreach (PointD p in points)
			shape.ControlPoints.Add (new ShapeControlPoint { Position = p });
		return shape;
	}

	protected static ShapeObject Box (Color fill, RectangleI region)
		=> Polygon (
			fill,
			new PointD (region.Left, region.Top),
			new PointD (region.Right + 1, region.Top),
			new PointD (region.Right + 1, region.Bottom + 1),
			new PointD (region.Left, region.Bottom + 1));

	/// <summary>
	/// An editable text object with an opaque colour, anchored at <paramref name="origin"/>. Pango
	/// draws down and to the right of that point, so an origin needs room below it on the canvas.
	/// </summary>
	protected static TextObject Text (string line, PointI origin)
		=> new (new TextEngine ([line]) {
			Origin = origin,
			PrimaryColor = new Color (0, 0, 1, 1),
		});

	/// <summary>
	/// Stand-in for Pinta.Tools' shape renderer: fills the control-point polygon, honouring the
	/// properties that decide how a shape composites (clip, opacity, blend, hidden). Geometry fidelity
	/// is deliberately not the point — see the note in <see cref="InitializeCore"/>.
	/// </summary>
	private static void RenderShapeForTest (ImageSurface surface, UserLayer layer, ShapeObject shape)
	{
		if (shape.Hidden || shape.ControlPoints.Count < 3)
			return;

		using ImageSurface scratch = CairoExtensions.CreateImageSurface (Format.Argb32, surface.Width, surface.Height);
		using (Context g = new (scratch)) {
			g.Antialias = Antialias.None;
			g.MoveTo (shape.ControlPoints[0].Position.X, shape.ControlPoints[0].Position.Y);
			for (int i = 1; i < shape.ControlPoints.Count; ++i)
				g.LineTo (shape.ControlPoints[i].Position.X, shape.ControlPoints[i].Position.Y);
			g.ClosePath ();
			// EvenOdd, so a self-crossing outline leaves the hole a filled render would not.
			g.FillRule = FillRule.EvenOdd;
			g.SetSourceColor (shape.FillColor);
			g.Fill ();
		}
		scratch.MarkDirty ();

		using Context target = new (surface);
		if (shape.Clip is not null) {
			target.AppendPath (shape.Clip.SelectionPath);
			target.FillRule = FillRule.EvenOdd;
			target.Clip ();
		}
		target.BlendSurface (scratch, shape.BlendMode, Math.Clamp (shape.Opacity, 0, 1));
	}
}
