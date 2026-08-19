using System;

namespace Pinta.Core;

/// <summary>
/// Impasto: the groups tools are shown in, shared by the toolbox column and the tool
/// dropdown so both present the same grouping. A group is a band of tool priorities; an
/// add-in's tools take the trailing group whatever priority they pick, so a plugin never
/// lands inside a group of related built-ins.
/// </summary>
public static class ToolSections
{
	/// <param name="UpperBound">
	/// Highest tool priority (inclusive) belonging to this section. The last one is unbounded,
	/// so anything the application ships lands somewhere.
	/// </param>
	/// <param name="Name">
	/// Resolved on demand rather than stored, so the label follows the loaded translation.
	/// </param>
	private sealed record Section (int UpperBound, Func<string> Name);

	private static readonly Section[] built_in = [
		new (8, () => Translations.GetString ("Move")),
		new (12, () => Translations.GetString ("View")),
		new (20, () => Translations.GetString ("Select")),
		new (36, () => Translations.GetString ("Paint")),
		new (46, () => Translations.GetString ("Shapes")),
		new (int.MaxValue, () => Translations.GetString ("Retouch")),
	];

	/// <summary>
	/// Add-in tools get the trailing section, below every built-in one. Grouping them by
	/// priority instead would drop them into a section of related built-ins - or into a
	/// toolbox stack's flyout - where nothing distinguishes them from the application's own.
	/// </summary>
	public static int AddinIndex => built_in.Length;

	/// <summary>The number of sections, including the add-in one.</summary>
	public static int Count => built_in.Length + 1;

	public static int IndexOf (BaseTool tool)
	{
		if (AddinMenu.AddinNameOf (tool.GetType ()) is not null)
			return AddinIndex;

		for (int i = 0; i < built_in.Length; i++)
			if (tool.Priority <= built_in[i].UpperBound)
				return i;

		return built_in.Length - 1;
	}

	public static string NameOf (int index)
		=> index == AddinIndex
			? Translations.GetString ("Add-ins")
			: built_in[index].Name ();
}
