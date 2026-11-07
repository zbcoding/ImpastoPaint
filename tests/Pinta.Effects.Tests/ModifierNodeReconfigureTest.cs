using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

// A committed effect becomes a node that keeps one effect instance for good and renders it directly,
// while reconfiguring edits that instance's data in place and painting changes its input. Anything the
// effect derives from its data or its input must follow those changes, or the node shows something
// other than what the dialog previewed. Each check compares the node with a fresh effect holding the
// same settings, rendered directly.
[TestFixture]
internal sealed class ModifierNodeReconfigureTest
{
	[Test]
	public void BrightnessContrastNodeRendersItsCurrentSettings ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		ImageSurface input = Utilities.LoadImage ("input.png");

		BrightnessContrastEffect menuEffect = new (services);
		menuEffect.Data.Brightness = 40;
		menuEffect.Data.Contrast = 30;
		EffectModifierNode node = EffectModifierNode.FromEffect (menuEffect, clip: null, services);

		AssertNodeRendersLike (node, NewBrightnessContrast (services, brightness: 40, contrast: 30), input, "as committed");

		((BrightnessContrastEffect) node.Effect).Data.Brightness = -20;

		AssertNodeRendersLike (node, NewBrightnessContrast (services, brightness: -20, contrast: 30), input, "after reconfiguring");
	}

	[Test]
	public void PosterizeNodeRendersItsCurrentSettings ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		ImageSurface input = Utilities.LoadImage ("input.png");

		PosterizeEffect menuEffect = new (services);
		SetLevels (menuEffect.Data, 4);
		EffectModifierNode node = EffectModifierNode.FromEffect (menuEffect, clip: null, services);

		PosterizeEffect committed = new (services);
		SetLevels (committed.Data, 4);
		AssertNodeRendersLike (node, committed, input, "as committed");

		SetLevels (((PosterizeEffect) node.Effect).Data, 9);

		PosterizeEffect reconfigured = new (services);
		SetLevels (reconfigured.Data, 9);
		AssertNodeRendersLike (node, reconfigured, input, "after reconfiguring");

		// The dialog writes all three channels, then announces the change once.
		static void SetLevels (PosterizeData data, int levels)
		{
			data.Red = data.Green = data.Blue = levels;
			data.FirePropertyChanged ("_all_");
		}
	}

	[Test]
	public void CurvesNodeRendersItsCurrentCurve ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		ImageSurface input = Utilities.LoadImage ("input.png");

		CurvesEffect menuEffect = new (services);
		menuEffect.Data.Mode = ColorTransferMode.Luminosity;
		menuEffect.Data.ControlPoints = [new () { { 0, 0 }, { 100, 170 }, { 255, 255 } }];
		EffectModifierNode node = EffectModifierNode.FromEffect (menuEffect, clip: null, services);

		AssertNodeRendersLike (node, NewCurves (services, [new () { { 0, 0 }, { 100, 170 }, { 255, 255 } }]), input, "as committed");

		// The dialog drags points within the lists it was given, then announces the change.
		CurvesData nodeData = ((CurvesEffect) node.Effect).Data;
		nodeData.ControlPoints![0].Remove (100);
		nodeData.ControlPoints[0][150] = 60;
		nodeData.FirePropertyChanged (nameof (CurvesData.ControlPoints));

		AssertNodeRendersLike (node, NewCurves (services, [new () { { 0, 0 }, { 150, 60 }, { 255, 255 } }]), input, "after reconfiguring");
	}

	[Test]
	public void CurvesDataCopyDoesNotShareTheCurveBeingEdited ()
	{
		CurvesData original = new () {
			Mode = ColorTransferMode.Luminosity,
			ControlPoints = [new () { { 0, 0 }, { 255, 255 } }],
		};

		CurvesData copy = original.Clone ();
		original.ControlPoints[0][128] = 30;

		Assert.That (copy.ControlPoints![0].ContainsKey (128), Is.False,
			"a copy handed to a render saw a point the dialog added afterwards");
	}

	[Test]
	public void AutoLevelNodeLevelsTheInputItIsGiven ()
	{
		IServiceProvider services = Utilities.CreateMockServices ();
		EffectModifierNode node = EffectModifierNode.FromEffect (new AutoLevelEffect (services), clip: null, services);

		ImageSurface firstInput = Utilities.LoadImage ("input.png");
		AssertNodeRendersLike (node, new AutoLevelEffect (services), firstInput, "on its first input");

		// Painting the layer hands the node different pixels, with a different histogram.
		ImageSurface paintedInput = Utilities.LoadImage ("level1.png");
		AssertNodeRendersLike (node, new AutoLevelEffect (services), paintedInput, "after the layer changed");
	}

	private static BrightnessContrastEffect NewBrightnessContrast (IServiceProvider services, int brightness, int contrast)
	{
		BrightnessContrastEffect effect = new (services);
		effect.Data.Brightness = brightness;
		effect.Data.Contrast = contrast;
		return effect;
	}

	private static CurvesEffect NewCurves (IServiceProvider services, SortedList<int, int>[] controlPoints)
	{
		CurvesEffect effect = new (services);
		effect.Data.Mode = ColorTransferMode.Luminosity;
		effect.Data.ControlPoints = controlPoints;
		return effect;
	}

	/// <summary>
	/// The node's output must equal <paramref name="expectedEffect"/> rendered straight over the whole
	/// input and blended back the way a node blends (normal mode, full opacity).
	/// </summary>
	private static void AssertNodeRendersLike (EffectModifierNode node, BaseEffect expectedEffect, ImageSurface input, string when)
	{
		ImageSurface actual = EffectModifierNode.CopyOf (input);
		node.Apply (actual);

		ImageSurface rendered = EffectModifierNode.CopyOf (input);
		expectedEffect.Render (input, rendered, [new RectangleI (0, 0, input.Width, input.Height)]);
		rendered.MarkDirty ();

		ImageSurface expected = EffectModifierNode.CopyOf (input);
		using (Context g = new (expected))
			g.BlendSurface (rendered);

		Assert.That (
			actual.GetReadOnlyPixelData ().SequenceEqual (expected.GetReadOnlyPixelData ()),
			Is.True,
			$"{expectedEffect.Name} node rendered something other than its settings {when}");
	}
}
