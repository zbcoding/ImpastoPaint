using System.ComponentModel;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class EffectDataCloneTest
{
	/// <summary>
	/// A cloned EffectData is committed into a modifier node; it must not carry the original's
	/// subscribers (a live preview's closure over a document and preview surface).
	/// </summary>
	[Test]
	public void ClonedEffectDataDoesNotNotifyTheOriginalsSubscribers ()
	{
		SampleData original = new ();
		int notified = 0;
		original.PropertyChanged += (object? _, PropertyChangedEventArgs _) => notified++;

		EffectData clone = original.Clone ();
		clone.FirePropertyChanged ("Value");

		Assert.That (notified, Is.Zero);
	}

	private sealed class SampleData : EffectData
	{
	}
}
