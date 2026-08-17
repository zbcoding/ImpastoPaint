using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

// Applying an effect builds a node holding its own instance of that effect. Most effects take the
// service provider they were registered with; a few take nothing. An effect whose constructor fits
// neither shape used to throw MissingMethodException the moment the user applied it, so this walks
// every shipped effect type rather than trusting one example.
[TestFixture]
internal sealed class ModifierNodeInstantiationTest
{
	private static IEnumerable<Type> ShippedEffectTypes ()
		=> typeof (InkSketchEffect).Assembly
			.GetTypes ()
			.Where (t => !t.IsAbstract && typeof (BaseEffect).IsAssignableFrom (t));

	[TestCaseSource (nameof (ShippedEffectTypes))]
	public void EveryEffectCanBeCopiedIntoANode (Type effectType)
	{
		IServiceProvider services = Utilities.CreateMockServices ();

		BaseEffect menuInstance;
		try {
			menuInstance = (BaseEffect) Activator.CreateInstance (effectType, services)!;
		} catch (MissingMethodException) {
			menuInstance = (BaseEffect) Activator.CreateInstance (effectType)!;
		}

		EffectModifierNode node = EffectModifierNode.FromEffect (menuInstance, clip: null, services);

		Assert.That (
			node.Effect,
			Is.Not.SameAs (menuInstance),
			$"{effectType.Name}: the node shares the menu's instance, so editing the node would rewrite the menu's settings");
	}
}
