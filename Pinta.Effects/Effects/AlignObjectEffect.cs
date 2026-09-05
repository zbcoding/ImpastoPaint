using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class AlignObjectEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsAlignObject;

	public override string Name => Translations.GetString ("Align Object");

	public override string EffectMenuCategory => Translations.GetString ("Object");

	public override bool IsConfigurable => true;

	public override bool IsTileable => false;

	public AlignObjectData Data => (AlignObjectData) EffectData!; // NRT - Set in constructor

	private readonly IChromeService chrome;

	public AlignObjectEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		EffectData = new AlignObjectData ();
	}
	public override async Task<bool> LaunchConfiguration ()
	{
		using AlignmentDialog dialog = AlignmentDialog.New (chrome);

		// Align to the default position
		Data.Position = dialog.SelectedPosition;

		dialog.PositionChanged += (_, _) => {
			Data.Position = dialog.SelectedPosition;
		};

		Gtk.ResponseType response = await dialog.RunAsync ();

		dialog.Destroy ();

		return Gtk.ResponseType.Ok == response;
	}

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		// If no selection, it's the whole image
		RectangleI selection = rois[0];
		AlignPosition align = Data.Position;

		// A selection dragged fully off the canvas clamps to zero size on the far edge, where the
		// origin is not a pixel: there is nothing to align and nothing safe to sample.
		if (selection.IsEmpty)
			return;

		RectangleI objectBounds = Utility.GetObjectBounds (src, selection);

		// Calculate the new position for the object
		PointI newPosition = CalculateNewPosition (objectBounds, align, selection);

		// Draw the object in the new position
		MoveObject (src, dest, objectBounds, newPosition, selection);
	}

	private static PointI CalculateNewPosition (
		RectangleI objectBounds,
		AlignPosition align,
		RectangleI selectionBounds)
	{
		return align switch {
			AlignPosition.TopLeft => new (
				selectionBounds.X,
				selectionBounds.Y),
			AlignPosition.TopCenter => new (
				selectionBounds.X + selectionBounds.Width / 2 - objectBounds.Width / 2,
				selectionBounds.Y),
			AlignPosition.TopRight => new (
				RightAlignedX (objectBounds, selectionBounds),
				selectionBounds.Y),
			AlignPosition.CenterLeft => new (
				selectionBounds.X,
				selectionBounds.Y + selectionBounds.Height / 2 - objectBounds.Height / 2),
			AlignPosition.Center => new (
				selectionBounds.X + selectionBounds.Width / 2 - objectBounds.Width / 2,
				selectionBounds.Y + selectionBounds.Height / 2 - objectBounds.Height / 2),
			AlignPosition.CenterRight => new (
				RightAlignedX (objectBounds, selectionBounds),
				selectionBounds.Y + selectionBounds.Height / 2 - objectBounds.Height / 2),
			AlignPosition.BottomLeft => new (
				selectionBounds.X,
				BottomAlignedY (objectBounds, selectionBounds)),
			AlignPosition.BottomCenter => new (
				selectionBounds.X + selectionBounds.Width / 2 - objectBounds.Width / 2,
				BottomAlignedY (objectBounds, selectionBounds)),
			AlignPosition.BottomRight => new (
				RightAlignedX (objectBounds, selectionBounds),
				BottomAlignedY (objectBounds, selectionBounds)),
			_ => PointI.Zero,
		};
	}

	// Right and Bottom are the last pixel inside the rectangle, not one past it, so aligning to
	// them takes the far edge (X + Width) minus the object's own extent - an object as wide as the
	// region lands back on the region's origin instead of a pixel outside it.
	private static int RightAlignedX (RectangleI objectBounds, RectangleI selectionBounds)
		=> selectionBounds.X + selectionBounds.Width - objectBounds.Width;

	private static int BottomAlignedY (RectangleI objectBounds, RectangleI selectionBounds)
		=> selectionBounds.Y + selectionBounds.Height - objectBounds.Height;

	private static void MoveObject (
		ImageSurface src,
		ImageSurface dest,
		RectangleI objectBounds,
		PointI newPosition,
		RectangleI selectionBounds)
	{
		var src_data = src.GetReadOnlyPixelData ();
		var dst_data = dest.GetPixelData ();
		int width = src.Width;

		// Clear the selection area
		var backgroundColor = src.GetColorBgra (new PointI (selectionBounds.Left, selectionBounds.Top));
		for (int y = 0; y < selectionBounds.Height; y++) {
			var dst_row = dst_data.Slice ((selectionBounds.Y + y) * width + selectionBounds.X, selectionBounds.Width);
			dst_row.Fill (backgroundColor);
		}

		// Draw the object in the new position
		for (int y = 0; y < objectBounds.Height; y++) {
			var src_row = src_data.Slice ((objectBounds.Y + y) * width + objectBounds.X, objectBounds.Width);
			var dst_row = dst_data.Slice ((newPosition.Y + y) * width + newPosition.X, objectBounds.Width);
			src_row.CopyTo (dst_row);
		}
	}

	public sealed class AlignObjectData : EffectData
	{
		private AlignPosition position = AlignPosition.Center;

		[Caption ("Position")]
		public AlignPosition Position {
			get => position;
			set {
				if (value == position) return;
				position = value;
				FirePropertyChanged (nameof (Position));
			}
		}

		[Skip]
		public override bool IsDefault => Position == AlignPosition.Center;
	}
}

public enum AlignPosition
{
	TopLeft,
	TopCenter,
	TopRight,
	CenterLeft,
	Center,
	CenterRight,
	BottomLeft,
	BottomCenter,
	BottomRight,
}
