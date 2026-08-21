using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Brings up a live document the same way <c>Pinta.Core.Tests.DocumentHarness</c> does, but through
/// the real Pinta.Tools seams instead of a Core-side stand-in — this suite exists to test the code
/// Core's harness deliberately does not reach (see the note by its <c>ShapeRenderer</c> assignment):
/// the shape editing engines, their geometry, and the plumbing that keeps them in sync with
/// <see cref="UserLayer.Objects"/>.
/// </summary>
internal abstract class ToolsTestHarness
{
	protected const int CanvasSize = 32;

	protected static readonly ColorBgra Red = ColorBgra.FromBgra (0, 0, 255, 255);
	protected static readonly ColorBgra Transparent = ColorBgra.FromBgra (0, 0, 0, 0);

	protected Document Document { get; private set; } = null!;

	[OneTimeSetUp]
	public void InitializeCore ()
	{
		// Shared across every fixture in the run, same reasoning as Pinta.Core.Tests: re-running this
		// per fixture would leave a previous run's Application/Window to be finalized off the main
		// thread at process exit, which GTK does not tolerate.
		if (core_initialized)
			return;
		core_initialized = true;

		Gtk.Module.Initialize ();
		Cairo.Module.Initialize ();

		// Activating a document writes the window title and appends to the Window menu, so the chrome
		// needs a shell and that menu needs registering. Nothing here is realized; no display is opened.
		Gtk.Application app = Gtk.Application.New ("com.github.zbcoding.Impasto.ToolsTests", Gio.ApplicationFlags.NonUnique);
		PintaCore.Chrome.InitializeApplication (app);
		PintaCore.Chrome.InitializeWindowShell (Gtk.Window.New ());
		PintaCore.Actions.Window.RegisterActions (app, Gio.Menu.New ());

		// Wires up BaseEditEngine's static handlers, in particular the real LayerObjectSelection
		// .ShapeRenderer (not Core's stand-in) — the whole point of this suite over Core's is
		// exercising actual Pinta.Tools geometry (ShapeEngineCollection, ShapeObjectRenderer). Reading
		// any static member runs BaseEditEngine's static constructor; going through
		// CoreToolsExtension.Initialize() instead would register 20+ real tools and make the first one
		// current, which builds toolbar UI this headless harness has no shell for.
		_ = BaseEditEngine.SEngines;
	}

	private static bool core_initialized;

	[SetUp]
	public void OpenDocument ()
	{
		ObjectRasterizer.ConfirmPrompt = _ => true;

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
		ObjectRasterizer.ConfirmPrompt = null;

		if (PintaCore.Workspace.OpenDocuments.Contains (Document))
			PintaCore.Workspace.CloseDocument (Document);
	}

	// --- The scene ------------------------------------------------------------------------------

	protected UserLayer Layer (int index) => Document.Layers[index];

	protected static void Fill (ImageSurface surface, ColorBgra color)
	{
		surface.GetPixelData ().Fill (color);
		surface.MarkDirty ();
	}

	/// <summary>What the canvas actually shows for a layer — see the note on the Core equivalent.</summary>
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

	/// <summary>A frozen rectangular selection, as a node or object drawn inside one carries.</summary>
	protected static DocumentSelection SelectionOf (RectangleI region)
	{
		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (new RectangleD (region.X, region.Y, region.Width, region.Height));
		return selection;
	}

	protected void Refresh (UserLayer layer)
		=> ObjectOpacity.RefreshLayer (PintaCore.Workspace, PintaCore.Chrome, layer);

	/// <summary>Adds an object to a layer the way the app does: as one undoable list swap.</summary>
	protected void AddObject (UserLayer layer, ILayerObject item, string label)
	{
		var before = ObjectOpacity.CloneAll (layer.Objects);
		layer.Objects.Add (item);
		Refresh (layer);
		Document.History.PushNewItem (
			new LayerObjectsHistoryItem (PintaCore.Workspace, PintaCore.Chrome, string.Empty, label, layer, before));
	}

	// --- Test effects -----------------------------------------------------------------------------

	protected sealed class InvertEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Invert (test)";
		public override bool SurvivesSaveAndReload => false;

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> dstData = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
			foreach (RectangleI roi in rois)
				for (int y = roi.Top; y <= roi.Bottom; ++y)
					for (int x = roi.Left; x <= roi.Right; ++x) {
						int i = (y * dst.Width) + x;
						ColorBgra c = srcData[i];
						dstData[i] = ColorBgra.FromBgra ((byte) (255 - c.B), (byte) (255 - c.G), (byte) (255 - c.R), c.A);
					}
		}
	}

	protected static EffectModifierNode Invert (DocumentSelection? clip = null) => new (new InvertEffect (), clip);

	// --- Shapes -------------------------------------------------------------------------------------

	/// <summary>A closed, filled polygon shape, control points in canvas coordinates.</summary>
	protected static ShapeObject Polygon (Color fill, params PointD[] points)
	{
		ShapeObject shape = new () {
			ShapeType = ShapeObjectType.OpenLineCurveSeries,
			Closed = true,
			FillColor = fill,
			OutlineColor = fill,
			AntiAliasing = false,
			// FillStyle 1 = fill only, no stroke (ShapeObjectRenderer.RenderOpaque: fillShape when
			// >= 1, strokeShape when even) - the real renderer honours this, unlike Core's stand-in.
			FillStyle = 1,
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

	/// <summary>The real Pinta.Tools live editing engine for <paramref name="source"/>, as a shape
	/// tool would build and hold it in <see cref="BaseEditEngine.SEngines"/> while editing.</summary>
	protected static ShapeEngine LiveEngine (UserLayer layer, ShapeObject source)
		=> ShapeEngineCollection.Create (layer, source);
}
