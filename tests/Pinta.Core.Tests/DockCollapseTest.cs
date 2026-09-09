using NUnit.Framework;
using Pinta.Docking;

namespace Pinta.Core.Tests;

/// <summary>
/// The side panel is the paned's resizable end child only while a pad is docked in it. With every
/// pad minimized to its icon the panel moves out of the paned, so it measures as just its icon
/// strip and has no handle for dragging a blank column wider. Each test pins one leg of that: the
/// placement itself is what the user sees as the collapse.
/// </summary>
[TestFixture]
internal sealed class DockCollapseTest
{
	// Widgets construct fine unrealized; no display is opened (see DocumentHarness).
	[OneTimeSetUp]
	public void InitializeGtk () => Gtk.Module.Initialize ();

	/// <summary>A pad as the app builds one: a dock item with a minimize/maximize header.</summary>
	private static DockItem Pad (string name)
		=> DockItem.New (Gtk.Box.New (Gtk.Orientation.Vertical, 0), name, "image-x-generic");

	private static Dock DockWith (params DockItem[] pads)
	{
		Dock dock = Dock.New ();
		foreach (DockItem pad in pads)
			dock.AddItem (pad, DockPlacement.Right);
		return dock;
	}

	// Whether the panel is the paned's end child, i.e. sized by the splitter and draggable. While
	// collapsed it is a plain child of the dock's own box instead.
	private static bool IsResizable (Dock dock)
		=> dock.RightPanel.GetParent () is Gtk.Paned;

	private static Gtk.Paned Splitter (Dock dock)
		=> (Gtk.Paned) dock.RightPanel.GetParent ()!;

	/// <summary>
	/// A dock with no pads yet has nothing to give the panel width, so it starts collapsed rather
	/// than being moved out on the first minimize.
	/// </summary>
	[Test]
	public void EmptyDockStartsCollapsed ()
	{
		Dock dock = DockWith ();

		Assert.That (dock.RightPanel.HasDockedItems, Is.False);
		Assert.That (IsResizable (dock), Is.False);
		Assert.That (dock.RightPanel.Hexpand, Is.False);
	}

	/// <summary>
	/// Adding a pad docks it, which is what gives the panel its resizable width.
	/// </summary>
	[Test]
	public void AddedPadMakesPanelResizable ()
	{
		Dock dock = DockWith (Pad ("layers"));

		Assert.That (dock.RightPanel.HasDockedItems, Is.True);
		Assert.That (IsResizable (dock), Is.True);
		Assert.That (dock.RightPanel.Hexpand, Is.True);
	}

	/// <summary>
	/// Minimizing the last docked pad collapses the panel: out of the paned, and no longer claiming
	/// the free width. The expansion has to be dropped explicitly - the empty panes inside the panel
	/// still expand, and the box would hand it their width.
	/// </summary>
	[Test]
	public void MinimizingTheLastPadCollapsesPanel ()
	{
		DockItem pad = Pad ("layers");
		Dock dock = DockWith (pad);

		pad.Minimize ();

		Assert.That (dock.RightPanel.HasDockedItems, Is.False);
		Assert.That (IsResizable (dock), Is.False);
		Assert.That (dock.RightPanel.Hexpand, Is.False);
	}

	/// <summary>
	/// Restoring the pad from its icon docks it again, which puts the panel back into the paned.
	/// </summary>
	[Test]
	public void MinimizedPadRedockingRestoresPanel ()
	{
		DockItem pad = Pad ("layers");
		Dock dock = DockWith (pad);

		pad.Minimize ();
		pad.Maximize ();

		Assert.That (dock.RightPanel.HasDockedItems, Is.True);
		Assert.That (IsResizable (dock), Is.True);
		Assert.That (dock.RightPanel.Hexpand, Is.True);
	}

	/// <summary>
	/// One pad minimized while another stays docked must not collapse the panel - the remaining pad
	/// is still there to show.
	/// </summary>
	[Test]
	public void PanelStaysResizableWhileAnyPadIsDocked ()
	{
		DockItem layers = Pad ("layers");
		DockItem history = Pad ("history");
		Dock dock = DockWith (layers, history);

		layers.Minimize ();

		Assert.That (dock.RightPanel.HasDockedItems, Is.True);
		Assert.That (IsResizable (dock), Is.True);

		history.Minimize ();

		Assert.That (dock.RightPanel.HasDockedItems, Is.False);
		Assert.That (IsResizable (dock), Is.False);
	}

	/// <summary>
	/// Collapsing moves the panel out of the paned but leaves the splitter position alone, so a pad
	/// brought back is as wide as the user last dragged it rather than resetting.
	/// </summary>
	[Test]
	public void RedockedPanelKeepsTheDraggedWidth ()
	{
		DockItem pad = Pad ("layers");
		Dock dock = DockWith (pad);
		Splitter (dock).Position = 640;

		pad.Minimize ();
		pad.Maximize ();

		Assert.That (Splitter (dock).Position, Is.EqualTo (640));
	}

	/// <summary>
	/// Minimizing is idempotent at the panel level: a second minimize of an already-minimized pad
	/// reports no state change, so nothing reparents the panel again.
	/// </summary>
	[Test]
	public void RepeatedMinimizeDoesNotChangeState ()
	{
		DockItem pad = Pad ("layers");
		Dock dock = DockWith (pad);
		pad.Minimize ();

		int changes = 0;
		dock.RightPanel.DockedItemsChanged += (_, _) => ++changes;
		pad.Minimize ();

		Assert.That (changes, Is.Zero);
		Assert.That (IsResizable (dock), Is.False);
	}
}
