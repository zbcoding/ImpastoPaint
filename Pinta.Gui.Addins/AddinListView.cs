using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mono.Addins;
using Mono.Addins.Setup;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Gui.Addins;

[GObject.Subclass<Adw.Bin>]
internal sealed partial class AddinListView
{
	// Add-ins are rendered as one labeled section per source: what the user installed, what
	// ships with the application, or the repository an available add-in comes from. A source is
	// never merged into another, and sections keep a fixed order rather than the order add-ins
	// happened to be enumerated in.
	private sealed class Group
	{
		public required Gio.ListStore Model { get; init; }
		public required Gtk.SingleSelection Selection { get; init; }
		public required Gtk.Widget Widget { get; init; }
		public required int Rank { get; init; }
	}

	// Section order. What the user installed comes first: it is the part they act on, and the
	// bundled section is only there to say what the application already provides.
	private const int UserInstalledRank = 0;
	private const int RepositoryRank = 1;
	private const int BundledRank = 2;

	private readonly Dictionary<string, Group> groups = [];
	private bool changing_selection;
	private bool has_selection;

	private Gtk.Box list_box;

	private Adw.StatusPage empty_list_page;
	private Gtk.ScrolledWindow list_view_scroll;
	private Adw.ViewStack list_view_stack;

	private AddinInfoView info_view;

	/// <summary>
	/// Event raised when addins are installed or uninstalled.
	/// </summary>
	public event EventHandler? OnAddinChanged;

	[MemberNotNull (nameof (list_box))]
	[MemberNotNull (nameof (empty_list_page), nameof (list_view_scroll), nameof (list_view_stack))]
	[MemberNotNull (nameof (info_view))]
	partial void Initialize ()
	{
		Gtk.Box listBox = Gtk.Box.New (Gtk.Orientation.Vertical, 12);
		listBox.SetAllMargins (6);

		Gtk.ScrolledWindow listViewScroll = Gtk.ScrolledWindow.New ();
		listViewScroll.SetChild (listBox);
		listViewScroll.SetSizeRequest (300, 400);
		listViewScroll.SetPolicy (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);

		Adw.StatusPage emptyListPage = Adw.StatusPage.New ();
		emptyListPage.IconName = StandardIcons.SystemSearch;
		emptyListPage.Title = Translations.GetString ("No Items Found");
		emptyListPage.AddCssClass (AdwaitaStyles.Compact);

		Adw.ViewStack listViewStack = Adw.ViewStack.New ();
		listViewStack.Add (listViewScroll);
		listViewStack.Add (emptyListPage);

		AddinInfoView infoView = AddinInfoView.New ();
		infoView.OnAddinChanged += (o, e) => OnAddinChanged?.Invoke (o, e);

		Adw.Flap flap = Adw.Flap.New ();
		flap.FoldPolicy = Adw.FlapFoldPolicy.Never;
		flap.Locked = true;
		flap.Content = listViewStack;
		flap.Separator = Gtk.Separator.New (Gtk.Orientation.Vertical);
		flap.FlapPosition = Gtk.PackType.End;
		flap.SetFlap (infoView);

		// --- References to keep

		list_box = listBox;
		list_view_scroll = listViewScroll;
		empty_list_page = emptyListPage;
		list_view_stack = listViewStack;
		info_view = infoView;

		// --- Post-initialization

		SetChild (flap);
	}

	internal void Configure (SystemManager system, IChromeService chrome)
	{
		info_view.Configure (system, chrome);
	}

	public static new AddinListView New ()
	{
		AddinListView view = NewWithProperties ([]);
		return view;
	}

	public void Clear ()
	{
		foreach (Group group in groups.Values)
			list_box.Remove (group.Widget);

		groups.Clear ();
		has_selection = false;

		list_view_stack.VisibleChild = empty_list_page;
		info_view.Update (null);
	}

	public void AddAddin (
		SetupService service,
		AddinHeader info,
		Addin addin,
		AddinStatus status)
	{
		bool bundled = Utilities.IsBundledWithApplication (addin);

		AddItem (
			bundled ? Translations.GetString ("Included with Impasto") : Translations.GetString ("Installed add-ins"),
			bundled ? BundledRank : UserInstalledRank,
			AddinListViewItem.NewForInstalledAddin (service, info, addin, status));
	}

	public void AddAddinRepositoryEntry (
		SetupService service,
		AddinHeader info,
		AddinRepositoryEntry addin,
		AddinStatus status)
	{
		AddItem (GetSourceLabel (addin), RepositoryRank, AddinListViewItem.NewForAvailableAddin (service, info, addin, status));
	}

	private void AddItem (string sourceLabel, int rank, AddinListViewItem item)
	{
		list_view_stack.VisibleChild = list_view_scroll;

		Group group = GetOrCreateGroup (sourceLabel, rank);
		group.Model.Append (item);

		// Select the very first item added across all groups, so the info panel is never
		// empty while the list has entries. Selection models don't reliably signal a change
		// for the very first item (see the SingleSelection docs), so update directly too.
		if (!has_selection) {
			group.Selection.Selected = 0;
			HandleSelectionChanged (group);
		}
	}

	private Group GetOrCreateGroup (string sourceLabel, int rank)
	{
		if (groups.TryGetValue (sourceLabel, out Group? existing))
			return existing;

		Gio.ListStore listStore = Gio.ListStore.New (AddinListViewItem.GetGType ());

		Gtk.SingleSelection selectionModel = Gtk.SingleSelection.New (listStore);
		selectionModel.Autoselect = false;

		Gtk.SignalListItemFactory itemFactory = Gtk.SignalListItemFactory.New ();
		itemFactory.OnSetup += (factory, args) => {
			var listItem = (Gtk.ListItem) args.Object;
			listItem.SetChild (AddinListViewItemWidget.New ());
		};
		itemFactory.OnBind += (factory, args) => {
			var listItem = (Gtk.ListItem) args.Object;
			var modelItem = (AddinListViewItem) listItem.GetItem ()!;
			var widget = (AddinListViewItemWidget) listItem.GetChild ()!;
			widget.Update (modelItem);
		};

		Gtk.ListView listView = Gtk.ListView.New (selectionModel, itemFactory);

		Gtk.Label header = Gtk.Label.New (sourceLabel);
		header.Halign = Gtk.Align.Start;
		header.AddCssClass (AdwaitaStyles.Title4);

		Gtk.Box sectionBox = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		sectionBox.Append (header);
		sectionBox.Append (listView);

		Group group = new () {
			Model = listStore,
			Selection = selectionModel,
			Widget = sectionBox,
			Rank = rank,
		};

		selectionModel.OnSelectionChanged += (_, _) => HandleSelectionChanged (group);

		groups[sourceLabel] = group;

		// Place the section by rank, after the last one that sorts at or above it, so the order
		// does not depend on which add-in the registry happened to hand over first.
		Gtk.Widget? previous = groups.Values
			.Where (g => g != group && g.Rank <= rank)
			.OrderBy (g => g.Rank)
			.LastOrDefault ()
			?.Widget;

		list_box.InsertChildAfter (sectionBox, previous);

		return group;
	}

	private void HandleSelectionChanged (Group group)
	{
		if (changing_selection)
			return;

		uint selected = group.Selection.Selected;

		// GTK_INVALID_LIST_POSITION is uint.MaxValue.
		if (selected == uint.MaxValue) {
			// This group lost its selection. If it was the one holding it - e.g. its items were
			// replaced - the detail pane would otherwise keep showing a deselected add-in.
			if (!groups.Values.Any (g => g.Selection.Selected != uint.MaxValue)) {
				has_selection = false;
				info_view.Update (null);
			}
			return;
		}

		// Enforce a single selection across every section: claiming a selection in one
		// group clears whatever was selected in the others.
		changing_selection = true;
		foreach (Group other in groups.Values) {
			if (other != group && other.Selection.Selected != uint.MaxValue)
				other.Selection.Selected = uint.MaxValue;
		}
		changing_selection = false;

		has_selection = true;
		info_view.Update ((AddinListViewItem) group.Model.GetObject (selected)!);
	}

	/// <summary>
	/// Groups add-ins by repository source. Pinta's community repositories and Impasto's own
	/// checked-in repository get product-facing names; an unrecognized future repository gets
	/// its host name so sources remain visibly separate.
	/// </summary>
	private static string GetSourceLabel (AddinRepositoryEntry? repositoryEntry)
	{
		if (repositoryEntry is null
			|| !Uri.TryCreate (repositoryEntry.RepositoryUrl, UriKind.Absolute, out Uri? uri))
			return Translations.GetString ("Pinta Add-ins");

		if (uri.Host == "raw.githubusercontent.com"
			&& uri.AbsolutePath.StartsWith ("/zbcoding/ImpastoPaint/", StringComparison.Ordinal))
			return Translations.GetString ("Impasto Add-ins");

		return uri.Host == "pintaproject.github.io"
			? Translations.GetString ("Pinta Add-ins")
			: uri.Host;
	}
}

[Flags]
internal enum AddinStatus
{
	NotInstalled = 0,
	Installed = 1,
	Disabled = 2,
	HasUpdate = 4,
}
