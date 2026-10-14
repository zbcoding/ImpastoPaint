using System;
using System.Linq;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Grips on a four-cornered outline: one per edge midpoint for skew, one per corner for
/// perspective. Grip <c>i</c> is edge <c>i → i+1</c> or corner <c>i</c>, matching
/// <see cref="SelectionWarp"/>. 3D mode has the corner grips plus a ring of tilt grips just
/// outside each edge, numbered from <see cref="SelectionWarp.FIRST_TILT_GRIP"/>.
/// </summary>
internal sealed class QuadHandle : IToolHandle
{
	private static readonly Lazy<Gdk.Cursor> skew_horizontal_cursor = new (() => CreateCursor (Resources.Icons.SkewHorizontalCursor));
	private static readonly Lazy<Gdk.Cursor> skew_vertical_cursor = new (() => CreateCursor (Resources.Icons.SkewVerticalCursor));
	private static readonly Lazy<Gdk.Cursor> perspective_cursor = new (() => CreateCursor (Resources.Icons.PerspectiveCursor));
	private static readonly Lazy<Gdk.Cursor> turn_cursor = new (() => CreateCursor (Resources.Icons.TurnCursor));
	private static readonly Lazy<Gdk.Cursor> tilt_cursor = new (() => CreateCursor (Resources.Icons.TiltCursor));
	private static readonly Lazy<Gdk.Cursor> depth_cursor = new (() => CreateCursor (Resources.Icons.DepthCursor));

	// How far outside its edge a tilt grip sits, in screen pixels, so it never covers a corner grip.
	private const double TILT_GRIP_GAP = 18;

	private readonly IWorkspaceService workspace;
	private readonly MoveHandle[] grips;
	private int grip_count = 4;

	public QuadHandle (IWorkspaceService workspace)
	{
		this.workspace = workspace;
		grips = [.. Enumerable.Range (0, 8).Select (_ => new MoveHandle (workspace) { Active = true })];
	}

	public bool Active { get; set; }

	/// <summary>The cursor shown over a corner grip while the depth key is held.</summary>
	public static Gdk.Cursor DepthCursor => depth_cursor.Value;

	public bool ContainsPoint (PointD windowPoint)
		=> Active && GripAt (windowPoint) >= 0;

	public void Draw (Gtk.Snapshot snapshot)
	{
		for (int i = 0; i < grip_count; i++)
			grips[i].Draw (snapshot);
	}

	/// <summary>Places the grips on <paramref name="quad"/> for <paramref name="mode"/>.</summary>
	public void Update (ReadOnlySpan<PointD> quad, TransformMode mode)
	{
		grip_count = mode == TransformMode.ThreeD ? 8 : 4;
		PointD centre = new ((quad[0].X + quad[1].X + quad[2].X + quad[3].X) / 4, (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) / 4);

		for (int i = 0; i < 4; i++) {
			PointD a = quad[i];
			PointD b = quad[(i + 1) % 4];
			PointD middle = new ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
			bool level = Math.Abs (b.X - a.X) >= Math.Abs (b.Y - a.Y);

			if (mode == TransformMode.Skew) {
				grips[i].CanvasPosition = middle;
				// The glyph shows the direction the edge slides: along itself.
				grips[i].Cursor = level ? skew_horizontal_cursor.Value : skew_vertical_cursor.Value;
				continue;
			}

			grips[i].CanvasPosition = a;
			grips[i].Cursor = perspective_cursor.Value;

			MoveHandle tilt = grips[SelectionWarp.FIRST_TILT_GRIP + i];
			tilt.CanvasPosition = Outside (middle, centre);
			// A level edge tilts up and down; an upright one turns left and right.
			tilt.Cursor = level ? tilt_cursor.Value : turn_cursor.Value;
		}
	}

	private PointD Outside (PointD middle, PointD centre)
	{
		PointD away = middle - centre;
		double length = Math.Sqrt ((away.X * away.X) + (away.Y * away.Y));
		if (length < 1e-9)
			return middle;

		double gap = TILT_GRIP_GAP / workspace.ActiveWorkspace.Scale;
		return middle + away.Scaled (gap / length);
	}

	/// <summary>
	/// The grip under <paramref name="windowPoint"/>, or -1. On a small selection the grips' hit
	/// areas overlap, so the nearest one wins rather than whichever comes first.
	/// </summary>
	public int GripAt (PointD windowPoint)
	{
		if (!Active)
			return -1;

		int nearest = -1;
		double nearestDistance = double.MaxValue;
		for (int i = 0; i < grip_count; i++) {
			if (!grips[i].ContainsPoint (windowPoint))
				continue;

			double distance = workspace.CanvasPointToView (grips[i].CanvasPosition).DistanceSquared (windowPoint);
			if (distance < nearestDistance) {
				nearest = i;
				nearestDistance = distance;
			}
		}

		return nearest;
	}

	public Gdk.Cursor CursorOf (int grip) => grips[grip].Cursor;

	public void Select (int grip)
	{
		for (int i = 0; i < grips.Length; i++)
			grips[i].Selected = i == grip;
	}

	// Read straight from the installed file, as the rotate cursor does: a theme lookup can hand back
	// a recoloured or missing icon and lose the white halo that keeps the glyph visible. The
	// hotspot is centred on the texture's natural size, since a hotspot past the edge trips a GDK
	// assertion.
	private static Gdk.Cursor CreateCursor (string icon)
	{
		Gdk.Texture texture = LoadInstalledIcon (icon) ?? Pinta.Resources.ResourceLoader.GetIcon (icon, 32);
		return Gdk.Cursor.NewFromTexture (texture, texture.Width / 2, texture.Height / 2, null);
	}

	private static Gdk.Texture? LoadInstalledIcon (string icon)
	{
		string path = System.IO.Path.Combine (
			SystemManager.GetDataRootDirectory (), "icons", "hicolor", "scalable", "actions", icon + ".svg");
		if (!System.IO.File.Exists (path))
			return null;

		try {
			return Gdk.Texture.NewFromFilename (path);
		} catch (GLib.GException) {
			return null;
		}
	}
}
