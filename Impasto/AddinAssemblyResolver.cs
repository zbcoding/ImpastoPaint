//
// AddinAssemblyResolver.cs
//
// Author:
//       zbcoding
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Pinta;

/// <summary>
/// Lets add-ins compiled against a different release of the host libraries still load.
///
/// An add-in's assembly references the exact <c>AssemblyVersion</c> of the Pinta library it was
/// built against (e.g. <c>Pinta.Core, Version=3.1.0.0</c>). That is a separate mechanism from the
/// Mono.Addins manifest dependency (<c>&lt;Addin id="Pinta" version="3.1"/&gt;</c>): the manifest
/// governs whether the add-in is offered at all, while the assembly reference is resolved by the
/// CLR, which matches versions exactly and fails the whole load on any mismatch. The failure
/// surfaces indirectly - the add-in's own types appear to not exist:
///
///   InvalidOperationException: Type '...Extension, ...' not found in add-in '...'
///
/// This fork's libraries are versioned on its own release number, which will never equal the
/// number any published add-in was compiled against, so without this fallback no existing add-in
/// can load. Redirect requests for the host libraries to whichever copy is already loaded,
/// ignoring the requested version.
///
/// This moves failures from load time to use time: an add-in built against an API that has since
/// been removed or resignatured now binds successfully and throws when the user invokes it. That
/// is contained - extension creation and initialization are caught per add-in in
/// <c>MainWindow.UpdateExtension</c>, and effect render faults are caught per tile in
/// <c>AsyncEffectRenderer</c> - so one bad add-in cannot take the application down. The trade is
/// deliberate: it is what <see cref="Pinta.Core.PintaCore.PintaAddinCompatVersion"/> now guards,
/// since this resolver is the gate it replaced.
/// </summary>
internal static class AddinAssemblyResolver
{
	/// <summary>
	/// Host libraries that make up the add-in ABI. Only these are redirected - anything else an
	/// add-in fails to resolve is a genuinely missing dependency and must still surface as one.
	/// </summary>
	private static readonly string[] host_assembly_names = [
		"Pinta.Core",
		"Pinta.Tools",
		"Pinta.Effects",
		"Pinta.Gui.Widgets",
		"Pinta.Docking",
		"Pinta.Resources",
	];

	// Deliberately not synchronized: Install is called once, from the UI thread, before add-ins
	// are loaded. A lock here would guard nothing.
	private static bool installed;

	/// <summary>
	/// Must be called before <see cref="Mono.Addins.AddinManager.Initialize"/>, so the fallback is
	/// in place for the add-in assemblies loaded during registry scanning and extension creation.
	/// Must stay registered on <see cref="AssemblyLoadContext.Default"/>: that is what guarantees
	/// the handler only ever receives Default, which is what makes reading
	/// <see cref="AssemblyLoadContext.Assemblies"/> in the handler sound.
	/// </summary>
	public static void Install ()
	{
		if (installed)
			return;

		installed = true;
		AssemblyLoadContext.Default.Resolving += ResolveHostAssembly;
	}

	private static Assembly? ResolveHostAssembly (AssemblyLoadContext context, AssemblyName requested)
	{
		if (requested.Name is null || !host_assembly_names.Contains (requested.Name, StringComparer.OrdinalIgnoreCase))
			return null;

		// The host libraries are loaded before any add-in is, so the already-loaded copy is the
		// one to bind against; its version is deliberately ignored.
		return context.Assemblies.FirstOrDefault (
			a => string.Equals (a.GetName ().Name, requested.Name, StringComparison.OrdinalIgnoreCase));
	}
}
