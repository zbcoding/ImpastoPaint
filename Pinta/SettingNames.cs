namespace Pinta;

internal static class SettingNames
{
	internal const string NEW_IMAGE_WIDTH = "new-image-width";
	internal const string NEW_IMAGE_HEIGHT = "new-image-height";
	internal const string NEW_IMAGE_BACKGROUND = "new-image-bg";

	// Impasto: default size of the blank canvas created on startup (Settings dialog).
	internal const string DEFAULT_CANVAS_WIDTH = "default-canvas-width";
	internal const string DEFAULT_CANVAS_HEIGHT = "default-canvas-height";
	internal const string CANVAS_SURROUND_COLOR = "canvas-surround-color";
	internal const string DEFAULT_CANVAS_SURROUND_COLOR = "";
	internal const string PASTE_EXTERNAL_IMAGES_TO_NEW_LAYER = "paste-external-images-to-new-layer";
	internal const string POPOVER_HINT_MODE = "popover-hint-mode";

	internal const string RULER_METRIC = "ruler-metric";
	internal const string COLOR_SCHEME = "color-scheme";
	internal const string WINDOW_MAXIMIZED = "window-maximized";
	internal const string WINDOW_SIZE_WIDTH = "window-size-width";
	internal const string WINDOW_SIZE_HEIGHT = "window-size-height";
	internal const string RULER_SHOWN = "ruler-shown";
	internal const string IMAGE_TABS_SHOWN = "image-tabs-shown";
	internal const string TOOL_WINDOWS_SHOWN = "tool-windows-shown";
	internal const string TOOLBAR_SHOWN = "toolbar-shown";
	internal const string MENUBAR_SHOWN = "menubar-shown";
	internal const string STATUSBAR_SHOWN = "statusbar-shown";
	internal const string TOOLBOX_SHOWN = "toolbox-shown";
	// Impasto: individually toggle the status bar's cursor position and image
	// size/aspect ratio widgets (Settings dialog). Mirrors Pinta.Core.SettingNames.
	internal const string STATUSBAR_SHOW_CURSOR_POSITION = "statusbar-show-cursor-position";
	internal const string STATUSBAR_SHOW_IMAGE_SIZE = "statusbar-show-image-size";
	internal const string COLORS_SHOWN = "colors-shown";
	internal const string TOOLBOX_PINNED = "toolbox-pinned";
	// Impasto: pre-fork single-column toolbox layout, offered as an opt-in for users who
	// prefer it over the sectioned 2-column grid.
	internal const string TOOLBOX_CLASSIC_LAYOUT = "toolbox-classic-layout";
	// Impasto: wrap the tool settings onto extra rows when the window is too narrow,
	// instead of scrolling them horizontally.
	internal const string TOOL_SETTINGS_WRAP_ROWS = "tool-settings-wrap-rows";
	// Impasto: skip the "Rasterize Objects?" confirmation dialog. Mirrors Pinta.Core.SettingNames.
	internal const string SKIP_RASTERIZE_OBJECTS_DIALOG = "skip-rasterize-objects-dialog";
	internal const string COLORS_FLOATING = "colors-floating";
	// Impasto: add a third row of darker colors to the default palette.
	internal const string EXTENDED_PALETTE_ROWS = "extended-palette-rows";
	internal const string LAST_DIALOG_DIRECTORY = "last-dialog-directory";
	internal const string LAST_SELECTED_TOOL = "last-selected-tool";

	internal const string RESIZE_CANVAS_ANCHOR = "resize-canvas-anchor";
	internal const string RESIZE_CANVAS_MAINTAIN_ASPECT = "resize-canvas-maintain-aspect";
	internal const string RESIZE_CANVAS_USE_PERCENTAGE = "resize-canvas-use-percentage";
	internal const string RESIZE_CANVAS_PERCENTAGE = "resize-canvas-percentage";
	internal const string RESIZE_CANVAS_WIDTH = "resize-canvas-width";
	internal const string RESIZE_CANVAS_HEIGHT = "resize-canvas-height";

	internal const string RESIZE_IMAGE_MAINTAIN_ASPECT = "resize-image-maintain-aspect";
	internal const string RESIZE_IMAGE_USE_PERCENTAGE = "resize-image-use-percentage";
	internal const string RESIZE_IMAGE_PERCENTAGE = "resize-image-percentage";
	internal const string RESIZE_IMAGE_WIDTH = "resize-image-width";
	internal const string RESIZE_IMAGE_HEIGHT = "resize-image-height";
	internal const string RESIZE_IMAGE_RESAMPLING = "resize-image-resampling";
}
