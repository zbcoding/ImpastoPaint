using System;
using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Impasto: the "Add-ins" container that add-in contributions live under, so a plugin's
/// entries are visibly separate from the application's own.
///
/// <para>
/// Every main menu can hold one. It is created when the first contribution arrives and
/// removed again when the last one leaves, so a menu no add-in touched shows nothing. The
/// container is appended as a section, which pins it below the menu's own items - sorting it
/// by label would float it to a different position in every locale.
/// </para>
///
/// <para>
/// Placement is a path: <c>{Root}/Add-in/Category</c> gives
/// <c>Effects ▸ Add-ins ▸ Add-in ▸ Category ▸ item</c>, so an add-in keeps its own grouping
/// inside its own submenu. Two levels below the container is the ceiling - each one is another
/// pop-out to traverse - and a deeper path is folded into the last label rather than adding
/// another level.
/// </para>
/// </summary>
public sealed class AddinMenu
{
	/// <summary>
	/// First segment of a menu path that places the entry under the Add-ins container. Not
	/// user-visible - the container's label is supplied here so it stays translated.
	/// </summary>
	/// <remarks>
	/// An add-in does not need to use this: placement is decided from where the contribution
	/// came from (see <see cref="PathFor"/>), so add-ins written against upstream Pinta, which
	/// know nothing about the container, still land inside it. This is the grammar that decision
	/// is expressed in, and an add-in that wants to choose its own pack name can use it.
	/// </remarks>
	public const string Root = "__addins__";

	public const char PathSeparator = '/';

	// How much of a path below the container becomes real submenus. Two, so the deepest entry
	// is Menu > Add-ins > Add-in > Category > item.
	private const int MaxNestedLevels = 2;

	private readonly ChromeManager chrome;

	// The section is appended once per menu and kept even while empty: an empty section
	// renders nothing, and re-appending on each use would stack up duplicates.
	private readonly Dictionary<MainMenu, Gio.Menu> sections = [];
	private readonly Dictionary<MainMenu, Gio.Menu> containers = [];

	// Submenus by resolved path, so two packs with the same name under different parents stay
	// distinct and a second entry joins the submenu the first one created.
	private readonly Dictionary<string, Gio.Menu> submenus = [];

	internal AddinMenu (ChromeManager chrome)
	{
		this.chrome = chrome;
	}

	public static string[] SplitPath (string path)
		=> path.Split (PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>
	/// True when <paramref name="path"/> places its entry under the Add-ins container.
	/// </summary>
	public static bool IsAddinPath (string path)
		=> path.StartsWith (Root, StringComparison.Ordinal)
			&& (path.Length == Root.Length || path[Root.Length] == PathSeparator);

	/// <summary>
	/// The menu path a contribution belongs at, decided by which assembly declared it: anything
	/// that did not ship with the application is an add-in contribution and goes under the
	/// Add-ins container, grouped by the add-in's name.
	/// </summary>
	/// <param name="contributor">
	/// The contributed type - an effect or adjustment. Its assembly is what identifies the
	/// add-in.
	/// </param>
	/// <param name="category">
	/// The category the contribution asked for, used as a qualifier below the add-in's name.
	/// Pass null for a contribution that has no category, e.g. an adjustment.
	/// </param>
	/// <returns>
	/// A path under <see cref="Root"/>, or null when the contribution is the application's own
	/// and belongs wherever it always has.
	/// </returns>
	public static string? PathFor (Type contributor, string? category)
	{
		if (AddinNameOf (contributor) is not string addinName)
			return null;

		return ComposePath (addinName, category);
	}

	/// <summary>
	/// True when an add-in ships this type rather than the application. Placement decisions
	/// outside the menus - the toolbox section a tool's button lands in, say - use this so
	/// they agree with the menus about what came from where.
	/// </summary>
	public static bool IsFromAddin (Type contributor)
		=> AddinNameOf (contributor) is not null;

	/// <summary>
	/// Groups by add-in name, qualified by the category when the add-in asked for a meaningful
	/// one. <see cref="BaseEffect.EffectMenuCategory"/> defaults to "General", which says
	/// nothing once the entries are already grouped by add-in, so it is left off.
	/// </summary>
	public static string ComposePath (string addinName, string? category)
		=> string.IsNullOrWhiteSpace (category) || category == BaseEffect.DefaultMenuCategory
			? $"{Root}{PathSeparator}{addinName}"
			: $"{Root}{PathSeparator}{addinName}{PathSeparator}{category}";

	/// <summary>
	/// The name of the add-in that ships this type, or null when the application itself does.
	/// Matched on the assembly's directory: the application's assemblies sit beside the
	/// executable, and an installed add-in lives in its own directory under the add-in registry.
	/// </summary>
	private static string? AddinNameOf (Type contributor)
	{
		string location = contributor.Assembly.Location;

		if (location.Length == 0) // Single-file or dynamic assembly: not an installed add-in.
			return null;

		if (System.IO.Path.GetDirectoryName (location) is not string directory)
			return null;

		if (PathsEqual (directory, SystemManager.GetExecutableDirectory ()))
			return null;

		foreach (Mono.Addins.Addin addin in Mono.Addins.AddinManager.Registry.GetAddins ()) {
			if (System.IO.Path.GetDirectoryName (addin.AddinFile) is not string addinDirectory)
				continue;

			if (PathsEqual (directory, addinDirectory))
				return addin.Name;
		}

		// Loaded from outside the application directory but not registered - name it after the
		// assembly rather than silently filing it with the application's own entries.
		return contributor.Assembly.GetName ().Name;
	}

	private static bool PathsEqual (string left, string right)
		=> string.Equals (
			System.IO.Path.TrimEndingDirectorySeparator (left),
			System.IO.Path.TrimEndingDirectorySeparator (right),
			StringComparison.Ordinal);

	/// <summary>
	/// The menu an entry with this path belongs in, creating the container and any intermediate
	/// submenu on the way. A path that does not start with <see cref="Root"/> resolves to a
	/// plain category of <paramref name="menu"/>, which is what the application's own entries
	/// have always used.
	/// </summary>
	/// <param name="resolvedKey">
	/// Identifies the menu that was returned. Hand it back to <see cref="PruneEmpty"/> when the
	/// entry is removed.
	/// </param>
	public Gio.Menu ResolvePath (MainMenu menu, string path, out string resolvedKey)
	{
		List<string> segments = [.. SplitPath (path)];
		bool underAddins = segments.Count > 0 && segments[0] == Root;

		if (underAddins)
			segments.RemoveAt (0);

		FoldBelowCeiling (segments);

		Gio.Menu parent = underAddins ? GetOrCreateContainer (menu) : chrome.GetMainMenu (menu);
		string key = underAddins ? Root : string.Empty;

		foreach (string segment in segments) {
			key = key.Length == 0 ? segment : $"{key}{PathSeparator}{segment}";
			parent = GetOrCreateSubmenu (parent, segment, key);
		}

		resolvedKey = key;
		return parent;
	}

	/// <summary>
	/// Joins every segment past the ceiling into the deepest label, so a plugin that nests
	/// three deep costs one submenu reading "Distort - Warp" instead of three pop-outs.
	/// </summary>
	private static void FoldBelowCeiling (List<string> segments)
	{
		if (segments.Count <= MaxNestedLevels)
			return;

		int first = MaxNestedLevels - 1;
		int count = segments.Count - first;
		string folded = string.Join (" - ", segments.GetRange (first, count));

		segments.RemoveRange (first, count);
		segments.Add (folded);
	}

	private Gio.Menu GetOrCreateSubmenu (Gio.Menu parent, string label, string key)
	{
		if (submenus.TryGetValue (key, out Gio.Menu? existing))
			return existing;

		Gio.Menu submenu = Gio.Menu.New ();
		parent.AppendMenuItemSorted (Gio.MenuItem.NewSubmenu (label, submenu));
		submenus.Add (key, submenu);
		return submenu;
	}

	/// <summary>
	/// Drops any submenu this path created and has left empty, innermost first, and then the
	/// container itself once nothing is under it. Call after removing an entry.
	/// </summary>
	public void PruneEmpty (MainMenu menu, string resolvedKey)
	{
		string key = resolvedKey;

		while (key.Length > 0 && key != Root) {
			if (!submenus.TryGetValue (key, out Gio.Menu? submenu) || submenu.GetNItems () > 0)
				return;

			int separator = key.LastIndexOf (PathSeparator);
			string parentKey = separator < 0 ? string.Empty : key[..separator];
			string label = key[(separator + 1)..];

			Gio.Menu parent =
				parentKey.Length == 0 ? chrome.GetMainMenu (menu)
				: parentKey == Root ? containers[menu]
				: submenus[parentKey];

			RemoveByLabel (parent, label);
			submenus.Remove (key);

			key = parentKey;
		}

		if (key == Root)
			ReleaseContainer (menu);
	}

	private Gio.Menu GetOrCreateContainer (MainMenu menu)
	{
		if (containers.TryGetValue (menu, out Gio.Menu? existing))
			return existing;

		if (!sections.TryGetValue (menu, out Gio.Menu? section)) {
			section = Gio.Menu.New ();
			chrome.GetMainMenu (menu).AppendSection (null, section);
			sections.Add (menu, section);
		}

		Gio.Menu container = Gio.Menu.New ();
		section.AppendItem (Gio.MenuItem.NewSubmenu (Translations.GetString ("Add-ins"), container));
		containers.Add (menu, container);
		return container;
	}

	private void ReleaseContainer (MainMenu menu)
	{
		if (!containers.TryGetValue (menu, out Gio.Menu? container) || container.GetNItems () > 0)
			return;

		sections[menu].Remove (0);
		containers.Remove (menu);
	}

	private static void RemoveByLabel (Gio.Menu menu, string label)
	{
		for (int i = 0; i < menu.GetNItems (); i++) {
			if (menu.GetItemAttributeValue (i, "label", GLib.VariantType.String) is not GLib.Variant existing)
				continue;

			if (existing.GetString (out nuint _) != label)
				continue;

			menu.Remove (i);
			return;
		}
	}
}
