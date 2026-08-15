using System;
using Cairo;
using Pinta.Core;

// The manifest. PintaCompatVersion is what the host offers add-ins; depending on it is what
// makes this add-in loadable, and the registry refuses it on a host that is too old.
[assembly: Mono.Addins.Addin ("ImpastoSampleAddin", "0.1", Category = "Sample")]
[assembly: Mono.Addins.AddinName ("Impasto Sample Add-in")]
[assembly: Mono.Addins.AddinDescription ("Fixture exercising the add-in contract: an effect under the Add-ins menu container, and a tool in the toolbox.")]
[assembly: Mono.Addins.AddinDependency ("Pinta", PintaCore.PintaCompatVersion)]

namespace ImpastoSampleAddin;

/// <summary>
/// Everything an add-in contributes is registered from here, and undone in Uninitialize so
/// disabling the add-in leaves the application as it found it.
/// </summary>
[Mono.Addins.Extension]
public sealed class SampleExtension : IExtension
{
	public void Initialize ()
	{
		PintaCore.Effects.RegisterEffect (new SampleEffect ());
		PintaCore.Tools.AddTool (new SampleTool (PintaCore.Services));
	}

	public void Uninitialize ()
	{
		PintaCore.Effects.UnregisterInstanceOfEffect<SampleEffect> ();
		PintaCore.Tools.RemoveInstanceOfTool<SampleTool> ();
	}
}

/// <summary>
/// Lands at Effects ▸ Add-ins ▸ Impasto Sample Add-in ▸ Halve Opacity without asking to: the
/// host groups an add-in's effects under that container by itself. The category below is only
/// a qualifier under the add-in's name, so an add-in with one effect can leave it alone.
/// </summary>
public sealed class SampleEffect : BaseEffect
{
	public override string Name => "Halve Opacity";

	public override string EffectMenuCategory => "Fixtures";

	public override bool IsTileable => true;

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();
		int width = source.Width;

		for (int y = roi.Top; y <= roi.Bottom; ++y) {
			for (int x = roi.Left; x <= roi.Right; ++x) {
				int i = y * width + x;
				// Straight-alpha halving on premultiplied data is a plain scale of all
				// four channels, which is why this needs no conversion.
				destinationData[i] = ColorBgra.FromBgra (
					(byte) (sourceData[i].B / 2),
					(byte) (sourceData[i].G / 2),
					(byte) (sourceData[i].R / 2),
					(byte) (sourceData[i].A / 2));
			}
		}
	}
}

/// <summary>
/// Ships no icon on purpose: the toolbox falls back to a stand-in rather than GTK's
/// broken-image glyph. An add-in that wants its own icon puts it in
/// <c>icons/hicolor/scalable/actions/&lt;name&gt;-symbolic.svg</c> beside this assembly and
/// returns that name from <see cref="Icon"/>.
/// </summary>
public sealed class SampleTool : BaseTool
{
	public SampleTool (IServiceProvider services) : base (services) { }

	public override string Name => "Sample Tool";

	public override string Icon => "impasto-sample-tool-symbolic";

	public override string StatusBarText => "Fixture tool from the sample add-in.";

	// Past every built-in bound, so it lands in the toolbox's trailing section rather than
	// joining a section - or a stack - that the application owns.
	public override int Priority => 1000;
}
