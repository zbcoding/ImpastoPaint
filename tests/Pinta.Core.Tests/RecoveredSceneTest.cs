using System;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace Pinta.Core.Tests;

/// <summary>
/// What a crash recovery actually hands back. Autosave writes an OpenRaster file, and OpenRaster
/// stores rasters — so a layer's nodes, objects and mask have to be resolved on the way out or
/// they are simply not in the file. These tests build the scenes the earlier suites cover in
/// memory, put them through a real export and import, and check the picture came back.
///
/// <para>
/// How much stays editable varies by what the layer holds: shapes and text have sidecar entries and
/// come back as objects, while a node whose effect an importer cannot find again is baked. What must
/// never vary is the image itself - the picture the user was looking at when the process died.
/// </para>
/// </summary>
[TestFixture]
internal sealed class RecoveredSceneTest : DocumentHarness
{
	// Cairo works in premultiplied bytes and OpenRaster does not, so a channel can shift slightly
	// on the way through. Anything larger is a scene that failed to be written, not rounding.
	private const int Tolerance = 2;

	private static readonly Color OpaqueRed = new (1, 0, 0, 1);

	private string directory = null!;

	[SetUp]
	public void CreateWorkingDirectory ()
	{
		directory = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
		Directory.CreateDirectory (directory);
	}

	[TearDown]
	public void RemoveWorkingDirectory () => Directory.Delete (directory, recursive: true);

	/// <summary>
	/// A layer with a modifier renders through its composite rather than its own surface, and the
	/// composite is the only place the effect's output exists. Writing the base surface instead
	/// would recover the painting with every effect silently undone.
	/// </summary>
	[Test]
	public void AnEffectNodeIsInTheRecoveredImage ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Invert (), "Invert");

		AssertRoundTripsUnchanged ();
	}

	/// <summary>
	/// A node clipped to part of the canvas has to come back applying to that part and no other,
	/// so this fails both if the clip is dropped and if the effect is.
	/// </summary>
	[Test]
	public void AClippedNodeKeepsItsRegionThroughRecovery ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Invert (EllipseIn (new RectangleI (8, 8, 16, 16))), "Invert ellipse");

		AssertRoundTripsUnchanged ();
	}

	/// <summary>
	/// Shapes and text are written to sidecar entries rather than baked, which is what makes a
	/// recovered document still editable instead of a photograph of one. They have to come back as
	/// objects, on the layer they were on - and the raster underneath has to be the raster alone,
	/// or loading would draw the objects a second time on top of themselves.
	/// </summary>
	[Test]
	public void ShapeAndTextObjectsComeBackEditable ()
	{
		UserLayer layer = Layer (0);
		FillRect (layer.Surface, new RectangleI (0, 0, 16, 32), Red);
		AddObject (layer, Box (OpaqueRed, new RectangleI (18, 4, 8, 8)), "Box");
		AddObject (layer, Text ("Ag", new PointI (18, 16)), "Text");

		Document recovered = RoundTrip ();
		UserLayer restored = recovered.Layers[0];

		Assert.Multiple (() => {
			Assert.That (restored.ShapeObjects, Has.Count.EqualTo (1));
			Assert.That (restored.TextObjects, Has.Count.EqualTo (1));
			Assert.That (string.Concat (restored.TextObjects[0].Engine.Lines), Is.EqualTo ("Ag"));

			// The base raster is written without the objects on it, so the half of the canvas
			// they occupy is empty in the file and the half they do not is untouched paint.
			Assert.That (restored.Surface.GetColorBgra (new PointI (4, 4)), Is.EqualTo (Red));
		});
	}

	/// <summary>
	/// Each kind of object has its own sidecar entry, so they are read back a kind at a time. The
	/// position saved with each one is what reassembles the single list they came from: without it
	/// a layer comes back with its shapes and its text regrouped, which changes what draws over
	/// what and what a modifier below them applies to.
	/// </summary>
	[Test]
	public void InterleavedObjectsKeepTheirOrderThroughRecovery ()
	{
		UserLayer layer = Layer (0);
		AddObject (layer, Text ("one", new PointI (2, 2)), "Text one");
		AddObject (layer, Box (OpaqueRed, new RectangleI (2, 12, 8, 8)), "Box");
		AddObject (layer, Text ("two", new PointI (2, 22)), "Text two");

		Document recovered = RoundTrip ();

		Assert.That (
			recovered.Layers[0].Objects.Select (o => o.GetType ()),
			Is.EqualTo (new[] { typeof (TextObject), typeof (ShapeObject), typeof (TextObject) }));
	}

	/// <summary>
	/// An add-in supplies effects this build cannot promise to rebuild - the add-in may be gone the
	/// next time the file is opened - so <see cref="BaseEffect.SurvivesSaveAndReload"/> is false for
	/// them and saving turns their nodes into pixels. The node's editability is the price; the
	/// picture is what is being protected. EffectNodesToBake is what tells the user that up front.
	/// </summary>
	[Test]
	public void AnAddinEffectIsRasterizedBeforeSaving ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, Invert (), "Invert");

		Assert.That (OraFormat.EffectNodesToBake (Document), Is.Not.Empty, "the user is warned first");

		Document recovered = RoundTrip ();

		Assert.Multiple (() => {
			Assert.That (recovered.Layers[0].Objects, Is.Empty, "the node is pixels now, not a node");
			Assert.That (recovered.Layers[0].Surface.GetColorBgra (new PointI (4, 4)),
				Is.EqualTo (ColorBgra.FromBgra (255, 255, 0, 255)), "and the pixels are the inverted ones");
		});
	}

	/// <summary>
	/// The opposite case: an effect that does promise to survive is written as a node, and a build
	/// that cannot supply it keeps it as an inert placeholder rather than dropping it. The picture
	/// loses the effect - that is the known cost - but re-saving does not lose the node itself,
	/// which is what would happen if the importer discarded what it could not resolve.
	/// </summary>
	[Test]
	public void AnEffectThisBuildCannotSupplyComesBackAsAPlaceholder ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		AddObject (layer, new EffectModifierNode (new UnregisteredEffect (), clip: null), "Unregistered");

		Assert.That (OraFormat.EffectNodesToBake (Document), Is.Empty, "nothing is baked, so nothing to warn about");

		Document recovered = RoundTrip ();

		Assert.Multiple (() => {
			Assert.That (recovered.Layers[0].Objects, Has.Count.EqualTo (1));
			Assert.That (((EffectModifierNode) recovered.Layers[0].Objects[0]).Effect,
				Is.TypeOf<UnavailableEffect> ());
			Assert.That (((EffectModifierNode) recovered.Layers[0].Objects[0]).Effect.EffectId,
				Is.EqualTo (new UnregisteredEffect ().EffectId), "so a re-save writes the same node back");
		});
	}

	/// <summary>
	/// A mask is applied last, after everything else the layer holds, and is not part of any
	/// surface the layer owns. It has to be resolved into the pixels that get written.
	/// </summary>
	[Test]
	public void AMaskIsResolvedIntoTheRecoveredImage ()
	{
		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		layer.CreateMask ();
		FillRect (layer.Mask.Surface, new RectangleI (0, 0, 16, 32), ColorBgra.Black);
		layer.Mask.Surface.MarkDirty ();
		Refresh (layer);

		AssertRoundTripsUnchanged ();
	}

	/// <summary>
	/// The whole thing at once, across layers: a scene where the flattened result depends on the
	/// order of two non-commuting nodes, on per-layer opacity, on blend mode and on a hidden layer
	/// staying hidden - all of them agreeing.
	/// </summary>
	[Test]
	public void AWholeSceneRecoversAsTheUserLeftIt ()
	{
		UserLayer bottom = Layer (0);
		Fill (bottom.Surface, Blue);
		FillRect (bottom.Surface, new RectangleI (4, 4, 12, 12), Red);

		UserLayer top = AddLayer (Green);
		top.Opacity = 0.5;
		top.BlendMode = BlendMode.Multiply;
		AddObject (top, Halve (SelectionOf (new RectangleI (0, 0, 32, 16))), "Halve top half");
		AddObject (top, Invert (), "Invert");

		UserLayer hidden = AddLayer (Red);
		hidden.Hidden = true;

		AssertRoundTripsUnchanged ();
	}

	/// <summary>
	/// Exports the document, imports it back, and asserts the flattened picture is the same one.
	/// Flattened rather than layer by layer, because that is what the user is looking at and it
	/// is the only comparison that holds opacity and blend mode to account as well.
	/// </summary>
	private void AssertRoundTripsUnchanged ()
	{
		Document recovered = RoundTrip ();

		using ImageSurface expected = Document.GetFlattenedImage ();
		using ImageSurface actual = recovered.GetFlattenedImage ();

		Assert.That (actual.Width, Is.EqualTo (expected.Width));
		Assert.That (actual.Height, Is.EqualTo (expected.Height));

		for (int y = 0; y < expected.Height; y++) {
			for (int x = 0; x < expected.Width; x++) {

				ColorBgra want = expected.GetColorBgra (new PointI (x, y));
				ColorBgra got = actual.GetColorBgra (new PointI (x, y));

				if (Differs (want, got))
					Assert.Fail ($"({x},{y}) recovered as {got} but was {want}");
			}
		}
	}

	/// <summary>Writes the document out the way autosave does, and reads it back.</summary>
	private Document RoundTrip ()
	{
		FormatDescriptor format =
			PintaCore.ImageFormats.GetFormatByExtension ("ora")
			?? throw new AssertionException ("The OpenRaster format is unavailable.");

		string path = Path.Combine (directory, "scene.ora");

		format.Exporter!.Export (Document, Gio.FileHelper.NewForPath (path), PintaCore.Chrome.MainWindow);

		return format.Importer!.Import (Gio.FileHelper.NewForPath (path));
	}

	/// <summary>
	/// Claims it survives a save and reload, as an effect that ships with the app does, but is not
	/// in this build's registry - which is what an add-in looks like once it has been uninstalled.
	/// </summary>
	private sealed class UnregisteredEffect : BaseEffect
	{
		public override bool IsTileable => true;
		public override string Name => "Unregistered (test)";

		// A placeholder never renders on load, so what it does here only has to be visible.
		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Span<ColorBgra> destination = dst.GetPixelData ();
			ReadOnlySpan<ColorBgra> source = src.GetReadOnlyPixelData ();
			foreach (RectangleI roi in rois)
				for (int y = roi.Top; y <= roi.Bottom; ++y)
					for (int x = roi.Left; x <= roi.Right; ++x) {
						int i = (y * dst.Width) + x;
						destination[i] = ColorBgra.FromBgra (source[i].B, source[i].G, 0, source[i].A);
					}
		}
	}

	private static bool Differs (ColorBgra want, ColorBgra got)
		=> System.Math.Abs (want.B - got.B) > Tolerance
		|| System.Math.Abs (want.G - got.G) > Tolerance
		|| System.Math.Abs (want.R - got.R) > Tolerance
		|| System.Math.Abs (want.A - got.A) > Tolerance;
}
