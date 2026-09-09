using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pinta.Gui.Widgets;

namespace Pinta.Core.Tests;

/// <summary>
/// A layer row shows the name beside its thumbnail, and stacks the name underneath once the
/// thumbnail would leave it no room. Rows are recycled between layers, object sub-rows and
/// thumbnail sizes, so every one of those states has to be (re)applied rather than assumed - a
/// row that keeps a previous binding's shape is the failure this fixture is here to catch.
/// <see cref="LayerThumbnailScaleTest"/> owns the slider's own value policy.
/// </summary>
[TestFixture]
internal sealed class LayerThumbnailRowLayoutTest : DocumentHarness
{
	private object? saved_step;

	// The one static PintaCore is shared across fixtures, and the settings service has no remove,
	// so an absent value is restored as the default step (see LayerThumbnailScaleTest).
	[SetUp]
	public void SaveStep ()
	{
		saved_step = PintaCore.Settings.GetSetting<object?> (LayerThumbnailScale.SettingKey, null);
	}

	[TearDown]
	public void RestoreStep ()
	{
		PintaCore.Settings.PutSetting (LayerThumbnailScale.SettingKey, saved_step ?? 2);
	}

	// --- The row --------------------------------------------------------------------------------

	private LayersListViewItemWidget LayerRow ()
	{
		LayersListViewItemWidget widget = LayersListViewItemWidget.New ();
		widget.SetItem (LayersListViewItem.New (Document, Layer (0)));
		return widget;
	}

	// The label and thumbnail live in their own box inside the row, which is what reflows; the
	// checkbox and badges stay put beside it.
	private static Gtk.Box ContentBox (LayersListViewItemWidget row)
		=> Children (row).OfType<Gtk.Box> ().Single ();

	private static Gtk.DrawingArea Thumbnail (LayersListViewItemWidget row)
		=> Children (ContentBox (row)).OfType<Gtk.DrawingArea> ().Single ();

	private static Gtk.Label RowLabel (LayersListViewItemWidget row)
		=> Children (ContentBox (row)).OfType<Gtk.Label> ().Single ();

	private static IEnumerable<Gtk.Widget> Children (Gtk.Widget parent)
	{
		for (Gtk.Widget? child = parent.GetFirstChild (); child is not null; child = child.GetNextSibling ())
			yield return child;
	}

	/// <summary>Types of the content box's children, top to bottom or left to right.</summary>
	private static string[] ContentOrder (LayersListViewItemWidget row)
		=> Children (ContentBox (row))
			.Select (c => c is Gtk.Label ? "label" : "thumbnail")
			.ToArray ();

	// --- Size -----------------------------------------------------------------------------------

	/// <summary>
	/// The row asks for exactly the slider's size, which is how the pad grows when the slider moves.
	/// </summary>
	[Test]
	public void ThumbnailTakesTheSliderSize ()
	{
		LayerThumbnailScale.Step = LayerThumbnailScale.MaxStep;
		LayersListViewItemWidget row = LayerRow ();

		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (Thumbnail (row).WidthRequest, Is.EqualTo (LayerThumbnailScale.Width));
		Assert.That (Thumbnail (row).HeightRequest, Is.EqualTo (LayerThumbnailScale.Height));
		Assert.That (Thumbnail (row).GetVisible (), Is.True);
	}

	/// <summary>
	/// A row built at one step and re-laid out at another follows, rather than keeping the size it
	/// was created with - the open pad resizes on the slider's Changed, it is not rebuilt.
	/// </summary>
	[Test]
	public void ResizedRowFollowsTheNewStep ()
	{
		LayerThumbnailScale.Step = 1;
		LayersListViewItemWidget row = LayerRow ();
		row.ApplyThumbnailLayout (labelBelow: false);
		int small = Thumbnail (row).WidthRequest;

		LayerThumbnailScale.Step = 4;
		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (Thumbnail (row).WidthRequest, Is.EqualTo (LayerThumbnailScale.Width));
		Assert.That (Thumbnail (row).WidthRequest, Is.GreaterThan (small));
	}

	// --- Shape ----------------------------------------------------------------------------------

	/// <summary>
	/// The usual row: name first, thumbnail trailing at the end of the row.
	/// </summary>
	[Test]
	public void NameSitsBesideThumbnailWhenItFits ()
	{
		LayerThumbnailScale.Step = 2;
		LayersListViewItemWidget row = LayerRow ();

		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (ContentBox (row).GetOrientation (), Is.EqualTo (Gtk.Orientation.Horizontal));
		Assert.That (ContentOrder (row), Is.EqualTo (new[] { "label", "thumbnail" }));
		Assert.That (Thumbnail (row).Halign, Is.EqualTo (Gtk.Align.End));
	}

	/// <summary>
	/// Stacked: thumbnail on top, name under it and still left-aligned with the row. Reversing the
	/// order matters - a vertical box would otherwise put the name above the thumbnail it labels.
	/// </summary>
	[Test]
	public void NarrowRowStacksNameUnderThumbnail ()
	{
		LayerThumbnailScale.Step = 5;
		LayersListViewItemWidget row = LayerRow ();

		row.ApplyThumbnailLayout (labelBelow: true);

		Assert.That (ContentBox (row).GetOrientation (), Is.EqualTo (Gtk.Orientation.Vertical));
		Assert.That (ContentOrder (row), Is.EqualTo (new[] { "thumbnail", "label" }));
		Assert.That (Thumbnail (row).Halign, Is.EqualTo (Gtk.Align.Start));
	}

	/// <summary>
	/// Widening the pad again un-stacks the row. Rows survive the reflow, so the stacked shape has
	/// to be undone rather than only ever applied.
	/// </summary>
	[Test]
	public void WidenedRowReturnsToSideBySide ()
	{
		LayerThumbnailScale.Step = 5;
		LayersListViewItemWidget row = LayerRow ();
		row.ApplyThumbnailLayout (labelBelow: true);

		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (ContentBox (row).GetOrientation (), Is.EqualTo (Gtk.Orientation.Horizontal));
		Assert.That (ContentOrder (row), Is.EqualTo (new[] { "label", "thumbnail" }));
	}

	// --- What gets a thumbnail at all -----------------------------------------------------------

	/// <summary>
	/// The slider's lowest step turns thumbnails off, which has to hide the widget and not just
	/// shrink it to a zero-size hole; the layer name stays.
	/// </summary>
	[Test]
	public void OffStepHidesThumbnailAndKeepsTheName ()
	{
		LayerThumbnailScale.Step = 0;
		LayersListViewItemWidget row = LayerRow ();

		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (Thumbnail (row).GetVisible (), Is.False);
		Assert.That (RowLabel (row).GetVisible (), Is.True);
		Assert.That (RowLabel (row).GetText (), Is.EqualTo (Layer (0).Name));
	}

	/// <summary>
	/// Object and mask sub-rows never draw a thumbnail - the tree expander supplies their
	/// indentation and they stand for something inside the layer, not the layer's pixels.
	/// </summary>
	[Test]
	public void SubRowsNeverGetAThumbnail ()
	{
		LayerThumbnailScale.Step = 3;
		LayersListViewItemWidget row = LayersListViewItemWidget.New ();
		row.SetItem (LayersListViewItem.NewMaskRow (Document, Layer (0)));

		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (Thumbnail (row).GetVisible (), Is.False);
	}

	/// <summary>
	/// Rows are recycled: the widget that showed a sub-row is handed a layer next. Its thumbnail
	/// has to come back, otherwise scrolling past an expanded layer leaves rows blank.
	/// </summary>
	[Test]
	public void RecycledRowRegainsItsThumbnail ()
	{
		LayerThumbnailScale.Step = 3;
		LayersListViewItemWidget row = LayersListViewItemWidget.New ();
		row.SetItem (LayersListViewItem.NewMaskRow (Document, Layer (0)));
		row.ApplyThumbnailLayout (labelBelow: false);

		row.SetItem (LayersListViewItem.New (Document, Layer (0)));
		row.ApplyThumbnailLayout (labelBelow: false);

		Assert.That (Thumbnail (row).GetVisible (), Is.True);
		Assert.That (Thumbnail (row).WidthRequest, Is.EqualTo (LayerThumbnailScale.Width));
	}

	// --- When the list decides to stack ---------------------------------------------------------

	/// <summary>
	/// The list stacks the name once the viewport can no longer hold the row's chrome, the
	/// thumbnail and a readable name; one pixel wider than that and it stays beside it.
	/// </summary>
	[Test]
	public void ViewportTooNarrowForTheNameStacksIt ()
	{
		LayerThumbnailScale.Step = 4;
		double needed = LayerThumbnailScale.Width + 90 + 60; // name + row chrome

		Assert.That (LayersListView.LabelBelowForViewport (needed - 1), Is.True);
		Assert.That (LayersListView.LabelBelowForViewport (needed), Is.False);
		Assert.That (LayersListView.LabelBelowForViewport (needed + 200), Is.False);
	}

	/// <summary>
	/// A viewport that fits the biggest thumbnail keeps every step side by side, and the narrowest
	/// pad a user can drag to still stacks at that step - the policy has to actually engage
	/// somewhere in the slider's range rather than being dead code at both ends.
	/// </summary>
	[Test]
	public void StackingDependsOnTheStep ()
	{
		const double pad_width = 260;

		LayerThumbnailScale.Step = 2;
		Assert.That (LayersListView.LabelBelowForViewport (pad_width), Is.False);

		LayerThumbnailScale.Step = LayerThumbnailScale.MaxStep;
		Assert.That (LayersListView.LabelBelowForViewport (pad_width), Is.True);
	}

	/// <summary>
	/// With thumbnails off there is nothing to stack under, and an unmeasured viewport (width 0,
	/// before the list is allocated) must not stack every row on startup.
	/// </summary>
	[Test]
	public void NothingToStackKeepsTheDefaultShape ()
	{
		LayerThumbnailScale.Step = 0;
		Assert.That (LayersListView.LabelBelowForViewport (10), Is.False);

		LayerThumbnailScale.Step = LayerThumbnailScale.MaxStep;
		Assert.That (LayersListView.LabelBelowForViewport (0), Is.False);
	}
}
