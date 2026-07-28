//
// ColorPickerAllLayersTool.cs
//
// A color picker that always samples the composited image (all layers, ignoring
// the selection), i.e. the color that is visible at the top view. The regular
// ColorPickerTool only samples the current layer within the selection.

using System;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class ColorPickerAllLayersTool : ColorPickerTool
{
	public ColorPickerAllLayersTool (IServiceProvider services) : base (services)
	{
	}

	public override string Name => Translations.GetString ("Color Picker (All Layers)");
	public override string Icon => Pinta.Resources.Icons.ToolColorPickerAllLayers;
	public override string StatusBarText => Translations.GetString ("Selects the color in view.") + "\n" + Translations.GetString ("Left click to set primary color.\nRight click to set secondary color.");
	// Shares the K shortcut group with the regular picker via the toolbox stack, so give it no
	// key of its own to avoid a clash.
	public override Gdk.Key ShortcutKey => Gdk.Key.Invalid;
	public override int Priority => 34;

	protected override bool SampleLayerOnly => false;
	protected override bool ShowSampleTypeSelector => false;
}
