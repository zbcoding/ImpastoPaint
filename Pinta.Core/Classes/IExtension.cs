using Pinta.Core;

[assembly: Mono.Addins.AddinRoot ("Pinta", PintaCore.PintaCompatVersion, CompatVersion = PintaCore.PintaAddinCompatVersion)]

namespace Pinta.Core;

[Mono.Addins.TypeExtensionPoint]
public interface IExtension
{
	void Initialize ();
	void Uninitialize ();
}
