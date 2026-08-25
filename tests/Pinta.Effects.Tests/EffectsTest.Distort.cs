using NUnit.Framework;
using Pinta.Core;
using Pinta.Effects.Tests;

namespace Pinta.Effects;

partial class EffectsTest
{
	[Test]
	[Category ("Distort")]
	public void Bulge ()
	{
		BulgeEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = 56;
		effect.Data.Offset = new CenterOffset<double> (0, 0);
		Utilities.TestEffect (effect, "bulge1.png");
	}

	[Test]
	[Category ("Distort")]
	public void BulgeIn ()
	{
		BulgeEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = -59;
		effect.Data.Offset = new CenterOffset<double> (-0.184, -0.304);
		Utilities.TestEffect (effect, "bulge2.png");
	}

	[Test]
	[Category ("Distort")]
	public void BulgeSmallerRadius ()
	{
		BulgeEffect effect = new BulgeEffect (Utilities.CreateMockServices ());
		effect.Data.Amount = 56;
		effect.Data.Offset = new CenterOffset<double> (0, 0);
		effect.Data.RadiusPercentage = 50;
		Utilities.TestEffect (effect, "bulge3.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents1 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		Utilities.TestEffect (effect, "dents1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents2 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		effect.Data.Scale = 50;
		Utilities.TestEffect (effect, "dents2.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents3 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		effect.Data.Roughness = 100;
		Utilities.TestEffect (effect, "dents3.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents4 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		effect.Data.Tension = 100;
		Utilities.TestEffect (effect, "dents4.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents5 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		effect.Data.Quality = 1;
		Utilities.TestEffect (effect, "dents5.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents6 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (-1, -1);
		Utilities.TestEffect (effect, "dents6.png");
	}

	[Test]
	[Category ("Distort")]
	public void Dents7 ()
	{
		DentsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (0, 0);
		effect.Data.EdgeBehavior = EdgeBehavior.Clamp;
		Utilities.TestEffect (effect, "dents7.png");
	}

	[Test]
	[Category ("Distort")]
	public void FrostedGlass ()
	{
		FrostedGlassEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = 7;
		effect.Data.Seed = new (42);
		Utilities.TestEffect (effect, "frostedglass1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Pixelate1 ()
	{
		PixelateEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "pixelate1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Pixelate2 ()
	{
		PixelateEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CellSize = 10;
		Utilities.TestEffect (effect, "pixelate2.png");
	}

	[Test]
	[Category ("Distort")]
	public void PolarInversion1 ()
	{
		PolarInversionEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = 1;
		Utilities.TestEffect (effect, "polarinversion1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile1 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		Utilities.TestEffect (effect, "tile1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile2 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-90);
		effect.Data.TileSize = 32;
		effect.Data.Intensity = 4;
		Utilities.TestEffect (effect, "tile2.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile3 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.WaveType = TileType.Curved;
		Utilities.TestEffect (effect, "tile3.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile4 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.TileSize = 10;
		effect.Data.WaveType = TileType.Curved;
		Utilities.TestEffect (effect, "tile4.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile5 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Clamp;
		Utilities.TestEffect (effect, "tile5.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile6 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Reflect;
		Utilities.TestEffect (effect, "tile6.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile7 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Primary;
		Utilities.TestEffect (effect, "tile7.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile8 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Secondary;
		Utilities.TestEffect (effect, "tile8.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile9 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Transparent;
		Utilities.TestEffect (effect, "tile9.png");
	}

	[Test]
	[Category ("Distort")]
	public void Tile10 ()
	{
		TileEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Rotation = new DegreesAngle (-30);
		effect.Data.EdgeBehavior = EdgeBehavior.Original;
		Utilities.TestEffect (effect, "tile10.png");
	}

	[Test]
	[Category ("Distort")]
	public void Twist1 ()
	{
		TwistEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = -45;
		Utilities.TestEffect (effect, "twist1.png");
	}

	[Test]
	[Category ("Distort")]
	public void Twist2 ()
	{
		TwistEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = 20;
		effect.Data.Antialias = 4;
		Utilities.TestEffect (effect, "twist2.png");
	}

	[Test]
	[Category ("Distort")]
	public void Twist3 ()
	{
		TwistEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "twist3.png");
	}

	[Test]
	[Category ("Distort")]
	public void Twist4 ()
	{
		TwistEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (-0.6, -0.6);
		effect.Data.EdgeBehavior = EdgeBehavior.Clamp;
		Utilities.TestEffect (effect, "twist4.png");
	}

	[Test]
	[Category ("Distort")]
	public void Twist5 ()
	{
		TwistEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CenterOffset = new CenterOffset<double> (-0.6, -0.6);
		effect.Data.EdgeBehavior = EdgeBehavior.Primary;
		Utilities.TestEffect (effect, "twist5.png");
	}
}
