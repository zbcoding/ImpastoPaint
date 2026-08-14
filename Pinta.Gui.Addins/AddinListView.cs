using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Mono.Addins;
using Mono.Addins.Setup;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Gui.Addins;

[GObject.Subclass<Adw.Bin>]
internal sealed partial class AddinListView
{
	// Add-ins are rendered as one labeled section per source (e.g. "Pinta Add-ins" for the
	// Pinta Community Addins repository) so a future second source - a native Impasto add-in
	// ecosystem, or a PDN-compatible one - never appears merged into today's single list.
	private sealed class Group
	{
		public required Gio.ListStore Model { get; init; }
		public required Gtk.SingleSelection Selection { get; init; }
		public required Gtk.Widget Widget { get; init; }
	}

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
		// Installed add-ins carry no repository info; the only add-in source this fork
		// currently exposes is Pinta Community Addins.
		AddItem (GetSourceLabel (null), AddinListViewItem.NewForInstalledAddin (service, info, addin, status));
	}

	public void AddAddinRepositoryEntry (
		SetupService service,
		AddinHeader info,
		AddinRepositoryEntry addin,
		AddinStatus status)
	{
		AddItem (GetSourceLabel (addin), AddinListViewItem.NewForAvailableAddin (service, info, addin, status));
	}

	private void AddItem (string sourceLabel, AddinListViewItem item)
	{
		list_view_stack.VisibleChild = list_view_scroll;

		Group group = GetOrCreateGroup (sourceLabel);
		group.Model.Append (item);

		// Select the very first item added across all groups, so the info panel is never
		// empty while the list has entries. Selection models don't reliably signal a change
		// for the very first item (see the SingleSelection docs), so update directly too.
		if (!has_selection) {
			group.Selection.Selected = 0;
			HandleSelectionChanged (group);
		}
	}

	private Group GetOrCreateGroup (string sourceLabel)
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
		};

		selectionModel.OnSelectionChanged += (_, _) => HandleSelectionChanged (group);

		groups[sourceLabel] = group;
		list_box.Append (sectionBox);

		return group;
	}

	private void HandleSelectionChanged (Group group)
	{
		if (changing_selection)
			return;

		uint selected = group.Selection.Selected;

		// GTK_INVALID_LIST_POSITION is uint.MaxValue.
		if (selected == uint.MaxValue)
			return;

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
	/// Groups add-ins by the host of the repository they came from - add-in ids in the actual
	/// Pinta Community Addins repository (e.g. "WebP", "BlockBrush") don't carry any namespace
	/// convention to group on, but the repository host is stable and known. Installed add-ins
	/// (no repository entry) and anything served from pintaproject.github.io fall under the
	/// same "Pinta Add-ins" heading; an unrecognized future repository gets its own section
	/// labeled by host, ready for a native Impasto or PDN-compatible source later.
	/// </summary>
	private static string GetSourceLabel (AddinRepositoryEntry? repositoryEntry)
	{
		string? host = repositoryEntry is not null && Uri.TryCreate (repositoryEntry.RepositoryUrl, UriKind.Absolute, out Uri? uri)
			? uri.Host
			: null;

		return host switch {
			null or "pintaproject.github.io" => Translations.GetString ("Pinta Add-ins"),
			_ => host,
		};
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
