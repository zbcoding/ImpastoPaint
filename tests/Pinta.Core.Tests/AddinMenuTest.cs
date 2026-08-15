using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class AddinMenuTest
{
	[OneTimeSetUp]
	public void SetUp () => Utilities.EnsureNativeLibraries ();

	private static (AddinMenu menu, Gio.Menu effects) CreateMenu ()
	{
		Gio.Menu effects = Gio.Menu.New ();
		ChromeManager chrome = new ();

		chrome.InitializeMainMenu (new Dictionary<MainMenu, Gio.Menu> {
			[MainMenu.Effects] = effects,
		});

		return (new AddinActions (chrome).Menu, effects);
	}

	private static string? Label (Gio.Menu menu, int index)
		=> menu.GetItemAttributeValue (index, "label", GLib.VariantType.String)?.GetString (out nuint _);

	private static Gio.Menu Submenu (Gio.Menu menu, int index)
		=> (Gio.Menu) menu.GetItemLink (index, "submenu")!;

	private static Gio.Menu Section (Gio.Menu menu, int index)
		=> (Gio.Menu) menu.GetItemLink (index, "section")!;

	/// <summary>
	/// A menu no add-in contributed to must look untouched: the container costs a menu entry,
	/// so creating it up front would show an empty "Add-ins" submenu everywhere.
	/// </summary>
	[Test]
	public void Container_IsNotCreatedUntilAnAddinNeedsIt ()
	{
		var (menu, effects) = CreateMenu ();

		Assert.That (effects.GetNItems (), Is.EqualTo (0));

		menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out _);

		Assert.That (effects.GetNItems (), Is.EqualTo (1));
	}

	/// <summary>
	/// The application's own categories are plain entries of the menu they belong to - the
	/// add-ins container must not capture them.
	/// </summary>
	[Test]
	public void PlainCategory_StaysADirectCategoryOfTheMenu ()
	{
		var (menu, effects) = CreateMenu ();

		Gio.Menu category = menu.ResolvePath (MainMenu.Effects, "Distort", out string key);

		Assert.That (key, Is.EqualTo ("Distort"));
		Assert.That (Label (effects, 0), Is.EqualTo ("Distort"));
		Assert.That (Submenu (effects, 0).GetNItems (), Is.EqualTo (0));
		Assert.That (category, Is.Not.Null);
	}

	/// <summary>
	/// Effects ▸ Add-ins ▸ Pack. The container is a section so it stays pinned below the menu's
	/// own items instead of sorting into them.
	/// </summary>
	[Test]
	public void AddinPath_NestsUnderAContainerSection ()
	{
		var (menu, effects) = CreateMenu ();

		menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out string key);

		Gio.Menu section = Section (effects, 0);
		Assert.That (Label (section, 0), Is.EqualTo ("Add-ins"));

		Gio.Menu container = Submenu (section, 0);
		Assert.That (Label (container, 0), Is.EqualTo ("Pack"));
		Assert.That (key, Is.EqualTo ($"{AddinMenu.Root}/Pack"));
	}

	/// <summary>
	/// A second effect from the same pack joins the submenu the first one created, rather than
	/// adding a duplicate entry beside it.
	/// </summary>
	[Test]
	public void SamePath_ReusesTheSubmenu ()
	{
		var (menu, effects) = CreateMenu ();

		Gio.Menu first = menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out _);
		Gio.Menu second = menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out _);

		Assert.That (second, Is.SameAs (first));
		Assert.That (Submenu (Section (effects, 0), 0).GetNItems (), Is.EqualTo (1));
	}

	/// <summary>
	/// Two packs with the same name in different menus are different menus - keying on the
	/// requested name alone would merge them.
	/// </summary>
	[Test]
	public void SameNameUnderDifferentParents_AreSeparateMenus ()
	{
		var (menu, _) = CreateMenu ();

		Gio.Menu plain = menu.ResolvePath (MainMenu.Effects, "Pack", out _);
		Gio.Menu underAddins = menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out _);

		Assert.That (underAddins, Is.Not.SameAs (plain));
	}

	/// <summary>
	/// Traversal depth is capped at add-in then category: a path deeper than that keeps its
	/// first two levels and folds the rest into the last label, rather than adding pop-outs.
	/// </summary>
	[Test]
	public void PathBelowTheCeiling_IsFoldedIntoTheLastLabel ()
	{
		var (menu, effects) = CreateMenu ();

		menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Glitches/Warp/Extra", out _);

		Gio.Menu container = Submenu (Section (effects, 0), 0);
		Assert.That (Label (container, 0), Is.EqualTo ("Glitches"));

		Gio.Menu addin = Submenu (container, 0);
		Assert.That (addin.GetNItems (), Is.EqualTo (1));
		Assert.That (Label (addin, 0), Is.EqualTo ("Warp - Extra"));
	}

	/// <summary>
	/// Disabling an add-in has to leave the menu as it found it, or the user is left with an
	/// empty Add-ins submenu that does nothing.
	/// </summary>
	[Test]
	public void PruneEmpty_RemovesTheSubmenuAndTheContainer ()
	{
		var (menu, effects) = CreateMenu ();

		menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out string key);
		menu.PruneEmpty (MainMenu.Effects, key);

		// The section itself is kept - it renders nothing while empty, and re-appending one per
		// use would stack up duplicates - but nothing is under it.
		Assert.That (Section (effects, 0).GetNItems (), Is.EqualTo (0));
	}

	/// <summary>
	/// Pruning must not touch a pack that still has entries in it.
	/// </summary>
	[Test]
	public void PruneEmpty_KeepsASubmenuThatStillHasItems ()
	{
		var (menu, effects) = CreateMenu ();

		Gio.Menu pack = menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Pack", out string key);
		pack.AppendItem (Gio.MenuItem.New ("An effect", "app.SomeEffect"));

		menu.PruneEmpty (MainMenu.Effects, key);

		Gio.Menu container = Submenu (Section (effects, 0), 0);
		Assert.That (Label (container, 0), Is.EqualTo ("Pack"));
	}

	/// <summary>
	/// A pack that empties while another remains loses only itself.
	/// </summary>
	[Test]
	public void PruneEmpty_KeepsTheContainerWhileAnotherPackRemains ()
	{
		var (menu, effects) = CreateMenu ();

		Gio.Menu kept = menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Kept", out _);
		kept.AppendItem (Gio.MenuItem.New ("An effect", "app.SomeEffect"));

		menu.ResolvePath (MainMenu.Effects, $"{AddinMenu.Root}/Gone", out string goneKey);
		menu.PruneEmpty (MainMenu.Effects, goneKey);

		Gio.Menu container = Submenu (Section (effects, 0), 0);
		Assert.That (container.GetNItems (), Is.EqualTo (1));
		Assert.That (Label (container, 0), Is.EqualTo ("Kept"));
	}

	/// <summary>
	/// An add-in's entries group under the add-in's own name, which is the part a user can act
	/// on - it is what the Add-in Manager lists and what they would disable.
	/// </summary>
	[Test]
	public void ComposePath_GroupsByAddinName ()
	{
		Assert.That (
			AddinMenu.ComposePath ("Glitch Effects", "Distort"),
			Is.EqualTo ($"{AddinMenu.Root}/Glitch Effects/Distort"));
	}

	/// <summary>
	/// "General" is what an effect that never chose a category reports, so qualifying with it
	/// would put every such add-in behind a submenu named after nothing.
	/// </summary>
	[Test]
	public void ComposePath_LeavesOffACategoryThatSaysNothing ()
	{
		Assert.Multiple (() => {
			Assert.That (
				AddinMenu.ComposePath ("Glitch Effects", BaseEffect.DefaultMenuCategory),
				Is.EqualTo ($"{AddinMenu.Root}/Glitch Effects"));
			Assert.That (
				AddinMenu.ComposePath ("Glitch Effects", null),
				Is.EqualTo ($"{AddinMenu.Root}/Glitch Effects"));
		});
	}

	/// <summary>
	/// The application's own effects are not add-in contributions, so they keep the categories
	/// they have always had rather than being swept into the container.
	/// </summary>
	[Test]
	public void PathFor_LeavesTheApplicationsOwnTypesAlone ()
	{
		Assert.That (AddinMenu.PathFor (typeof (AddinMenu), "Blurs"), Is.Null);
	}

	/// <summary>
	/// The shape a real add-in effect lands in: its own submenu under the container, with the
	/// category it asked for kept inside that.
	/// </summary>
	[Test]
	public void ComposedPath_NestsTheCategoryInsideTheAddin ()
	{
		var (menu, effects) = CreateMenu ();

		menu.ResolvePath (MainMenu.Effects, AddinMenu.ComposePath ("Glitch Effects", "Distort"), out _);

		Gio.Menu container = Submenu (Section (effects, 0), 0);
		Assert.That (Label (container, 0), Is.EqualTo ("Glitch Effects"));
		Assert.That (Label (Submenu (container, 0), 0), Is.EqualTo ("Distort"));
	}

	[Test]
	public void IsAddinPath_MatchesOnlyTheRootSegment ()
	{
		Assert.Multiple (() => {
			Assert.That (AddinMenu.IsAddinPath ($"{AddinMenu.Root}/Pack"), Is.True);
			Assert.That (AddinMenu.IsAddinPath (AddinMenu.Root), Is.True);
			Assert.That (AddinMenu.IsAddinPath ($"{AddinMenu.Root}Pack"), Is.False);
			Assert.That (AddinMenu.IsAddinPath ("Distort"), Is.False);
		});
	}
}
