using System.Linq;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// The test harness never calls Translations.Init, so GetString runs its no-catalog fallback path.
/// That path used to return the raw "{0}"-style template unformatted, so every
/// GetString("Layer {0}", n) call collapsed to the literal "Layer {0}" - and DocumentLayers
/// .NextLayerName loops until the generated name is unique, so the second auto-named layer in any
/// document always collided with the first and the loop spun forever. It surfaced as a CI timeout
/// across dozens of fixtures rather than a failing assertion; this pins it directly.
/// </summary>
[TestFixture]
internal sealed class TranslationsFallbackTest : DocumentHarness
{
	[Test]
	public void GetStringFormatsTheFallbackWhenNoCatalogIsLoaded ()
		=> Assert.That (Translations.GetString ("Layer {0}", 2), Is.EqualTo ("Layer 2"),
			"with no catalog, the fallback still has to substitute the format args, not return the raw template");

	[Test]
	public void GetStringFallbackHandlesMultipleArgs ()
		=> Assert.That (Translations.GetString ("{0} of {1}", 3, 7), Is.EqualTo ("3 of 7"));

	[Test]
	public void AutoNamedLayersGetDistinctNamesAndDoNotHang ()
	{
		// Before the fix this loop never returned: NextLayerName spun forever on the second layer.
		UserLayer first = Document.Layers[0];
		UserLayer second = Document.Layers.AddNewLayer (string.Empty);
		UserLayer third = Document.Layers.AddNewLayer (string.Empty);

		Assert.That (new[] { first.Name, second.Name, third.Name }.Distinct ().Count (), Is.EqualTo (3),
			"each auto-named layer has to get a unique name, or NextLayerName's de-dup loop never terminates");
	}
}
