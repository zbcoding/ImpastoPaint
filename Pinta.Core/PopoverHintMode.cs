namespace Pinta.Core;

/// <summary>
/// Which of the app's popover hints a user wants. Stored by ordinal under
/// <see cref="TransientHintPopover.SettingKey"/>, so the order is part of the settings format.
/// </summary>
public enum PopoverHintMode
{
	/// <summary>Every popover hint: the toolbox's, the canvas ones, the palette swatch captions.</summary>
	All,

	/// <summary>Only the toolbox tool buttons' hover hints. Nothing on the canvas.</summary>
	ToolButtonsOnly,

	/// <summary>No popover hints at all. Ordinary tooltips are unaffected.</summary>
	None
}
