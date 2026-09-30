using System.Collections.Generic;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// Moving an object lines it up with the other objects in the document - shapes, text, and images
/// on their own layers - independently of the grid/ruler/canvas snap toggle.
/// </summary>
[TestFixture]
internal sealed class ObjectAlignmentSnapTest : ToolsTestHarness
{
	private bool saved_snap;
	private bool saved_align;

	[SetUp]
	public void SnapOffAlignOn ()
	{
		saved_snap = PintaCore.CanvasGrid.SnapEnabled;
		saved_align = PintaCore.CanvasGrid.AlignToObjects;
		PintaCore.CanvasGrid.SnapEnabled = false;
		PintaCore.CanvasGrid.AlignToObjects = true;
	}

	[TearDown]
	public void RestoreToggles ()
	{
		PintaCore.CanvasGrid.ClearActiveGuides ();
		PintaCore.CanvasGrid.SnapEnabled = saved_snap;
		PintaCore.CanvasGrid.AlignToObjects = saved_align;
	}

	/// <summary>An image pasted onto a layer of its own: opaque pixels over <paramref name="region"/>.</summary>
	private void PasteImage (RectangleI region)
	{
		UserLayer layer = Document.Layers.AddNewLayer (string.Empty);
		var pixels = layer.Surface.GetPixelData ();
		for (int y = region.Top; y <= region.Bottom; ++y)
			for (int x = region.Left; x <= region.Right; ++x)
				pixels[y * layer.Surface.Width + x] = Red;
		layer.Surface.MarkDirty ();
	}

	private PointD Drag (RectangleD startBounds, RectangleD wanted)
		=> PintaCore.CanvasGrid.SnapRect (
			wanted,
			centerAnchor: false,
			ObjectAlignment.BeginDrag (Document, PintaCore.Chrome, startBounds));

	// The user's case: a caption dragged roughly under an image lands centred on it, with the
	// grid/ruler/canvas snap switched off.
	[Test]
	public void CaptionDraggedNearAnImagesCentreLine_LandsCentredOnIt ()
	{
		PasteImage (new RectangleI (4, 2, 12, 8)); // centre line x = 10
		RectangleD caption = new (20, 20, 6, 3);

		PointD landed = Drag (caption, caption with { X = 7.5 }); // centre 10.5

		Assert.That (landed.X, Is.EqualTo (7));
		Assert.That (PintaCore.CanvasGrid.ActiveAlignmentGuides, Has.Some.Matches<AlignmentGuide> (g => g.Vertical && g.Position == 10));
	}

	[Test]
	public void WithAlignToObjectsOff_TheDragIsLeftWhereThePointerPutIt ()
	{
		PintaCore.CanvasGrid.AlignToObjects = false;
		PasteImage (new RectangleI (4, 2, 12, 8));
		RectangleD caption = new (20, 20, 6, 3);

		PointD landed = Drag (caption, caption with { X = 7.5 });

		Assert.That (landed.X, Is.EqualTo (7.5));
		Assert.That (PintaCore.CanvasGrid.ActiveAlignmentGuides, Is.Empty);
	}

	// Collecting targets scans every visible layer's pixels, so a drag started with alignment off
	// must not pay for it - yet switching alignment back on has to work from the very next drag.
	[Test]
	public void ADragStartedWithAlignToObjectsOffCollectsNoTargets_AndTheNextDragAfterTurningItOnAligns ()
	{
		PasteImage (new RectangleI (4, 2, 12, 8));
		RectangleD caption = new (20, 20, 6, 3);

		PintaCore.CanvasGrid.AlignToObjects = false;
		AlignmentDrag offDrag = ObjectAlignment.BeginDrag (Document, PintaCore.Chrome, caption);

		PintaCore.CanvasGrid.AlignToObjects = true;
		PointD landed = Drag (caption, caption with { X = 7.5 });

		Assert.That (offDrag.Targets, Is.Empty, "nothing is collected while alignment is off");
		Assert.That (landed.X, Is.EqualTo (7), "the next drag lines up with the image again");
	}

	// The shape being dragged is still in its layer at its starting position. Aligning to that
	// copy would pull every small drag back to where it began.
	[Test]
	public void ADraggedShapeDoesNotAlignToItsOwnStartingPosition ()
	{
		ShapeObject shape = Box (new Cairo.Color (0, 0, 1), new RectangleI (2, 2, 8, 8));
		AddObject (Layer (0), shape, "Box");
		RectangleD start = new (2, 2, 8, 8);

		PointD landed = Drag (start, start with { X = 3.5 });

		Assert.That (landed.X, Is.EqualTo (3.5));
	}

	[Test]
	public void HiddenLayersAreNotAlignedTo ()
	{
		PasteImage (new RectangleI (4, 2, 12, 8));
		Document.Layers[Document.Layers.UserLayers.Count - 1].Hidden = true;
		RectangleD caption = new (20, 20, 6, 3);

		PointD landed = Drag (caption, caption with { X = 7.5 });

		Assert.That (landed.X, Is.EqualTo (7.5));
	}

	private static TextObject Text (string content, string font, PointI origin)
	{
		TextEngine engine = new ([content]) { Origin = origin };
		engine.SetFont (Pango.FontDescription.FromString (font), TextAlignment.Left, underline: false);
		TextObject text = new (engine);
		TextLayout layout = new (PintaCore.Chrome) { Engine = engine };
		// What the text tool stores: the layout box padded out for the grips.
		text.TextBounds = layout.GetLayoutBounds ().Inflated (12, 12);
		return text;
	}

	private static double Baseline (TextObject text)
	{
		TextLayout layout = new (PintaCore.Chrome) { Engine = text.Engine };
		return text.Engine.Origin.Y + PangoExtensions.UnitsToPixels (layout.Layout.GetBaseline ());
	}

	// A small caption next to a large heading: their padded boxes share no line, but dragging the
	// caption near the heading's baseline puts both on the same line of type.
	[Test]
	public void TextDraggedNearAnotherTextsBaseline_SitsOnThatBaseline ()
	{
		TextObject heading = Text ("Typography", "Sans 28", new PointI (0, 100));
		Layer (0).Objects.Add (heading);
		TextObject caption = Text ("gypsy", "Sans 9", new PointI (600, 0));
		Layer (0).Objects.Add (caption);
		RectangleD start = caption.TextBounds.ToDouble ();
		double captionBaselineOffset = Baseline (caption) - start.Y;
		double headingBaseline = Baseline (heading);

		RectangleD wanted = start with { Y = headingBaseline - captionBaselineOffset - 0.6 };
		PointD landed = PintaCore.CanvasGrid.SnapRect (
			wanted, centerAnchor: false, ObjectAlignment.BeginDrag (Document, PintaCore.Chrome, caption));

		Assert.That (landed.Y + captionBaselineOffset, Is.EqualTo (headingBaseline));
	}
}
