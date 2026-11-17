using System;
using System.Linq;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

/// <summary>
/// Repeat replays an applied effect on whatever the user has selected since, with the settings it
/// was applied with — not the menu instance's settings, which a reopened-and-cancelled dialog moves.
/// </summary>
[TestFixture]
internal sealed class RepeatLastEffectTest : DocumentHarness
{
	private static readonly RectangleI Corner = new (0, 0, 8, 8);

	[Test]
	public void RepeatAppliesTheRememberedSettingsToTheCurrentSelection ()
	{
		LastAppliedEffect lastApplied = new ("repeattest", "Repeat (test)", string.Empty, "<Primary>F");
		FillEffect menuInstance = new ();
		menuInstance.Data.Color = Green;
		lastApplied.Remember (menuInstance);
		menuInstance.Data.Color = Blue; // the dialog was reopened, a setting changed, then cancelled

		UserLayer layer = Layer (0);
		Fill (layer.Surface, Red);
		Document.Selection = SelectionOf (Corner);
		int stepsBefore = Document.History.Items.Count ();

		PintaCore.LivePreview.ApplyWithoutPreview (lastApplied.Effect!);

		Assert.That (Shown (layer, 2, 2), Is.EqualTo (Green), "inside the selection, in the applied settings");
		Assert.That (Shown (layer, 20, 20), Is.EqualTo (Red), "outside the selection is untouched");
		Assert.That (Document.History.Items.Count (), Is.EqualTo (stepsBefore + 1), "the repeat is one undoable step");
	}

	[Test]
	public void RepeatIsUnavailableUntilSomethingWasApplied ()
	{
		LastAppliedEffect lastApplied = new ("repeattest2", "Repeat (test)", string.Empty, "<Primary>F");
		lastApplied.SetDocumentsOpen (true);
		Assert.That (lastApplied.Command.Sensitive, Is.False);

		lastApplied.Remember (new FillEffect ());
		Assert.That (lastApplied.Command.Sensitive, Is.True);

		lastApplied.SetDocumentsOpen (false);
		Assert.That (lastApplied.Command.Sensitive, Is.False, "nothing to apply to");
	}

	private sealed class FillEffect : BaseEffect
	{
		public FillEffect () => EffectData = new FillData ();

		public FillData Data => (FillData) EffectData!;
		public override bool IsTileable => true;
		public override string Name => "Fill (test)";
		public override bool SurvivesSaveAndReload => false;

		public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			foreach (RectangleI roi in rois)
				FillRect (dst, roi, Data.Color);
		}
	}

	private sealed class FillData : EffectData
	{
		public ColorBgra Color { get; set; }
	}
}
