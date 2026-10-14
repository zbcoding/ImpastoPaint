using System;
using System.Linq;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Grips on a four-cornered outline: one per edge midpoint for skew, one per corner for
/// perspective. Grip <c>i</c> is edge <c>i → i+1</c> or corner <c>i</c>, matching
/// <see cref="SelectionWarp"/>.
/// </summary>
internal sealed class QuadHandle : IToolHandle
{
	private static readonly Lazy<Gdk.Cursor> skew_horizontal_cursor = new (() => CreateCursor (Resources.Icons.SkewHorizontalCursor));
	private static readonly Lazy<Gdk.Cursor> skew_vertical_cursor = new (() => CreateCursor (Resources.Icons.SkewVerticalCursor));
	private static readonly Lazy<Gdk.Cursor> perspective_cursor = new (() => CreateCursor (Resources.Icons.PerspectiveCursor));

	private readonly IWorkspaceService workspace;
	private readonly MoveHandle[] grips;

	public QuadHandle (IWorkspaceService workspace)
	{
		this.workspace = workspace;
		grips = [.. Enumerable.Range (0, 4).Select (_ => new MoveHandle (workspace) { Active = true })];
	}

	public bool Active { get; set; }

	public bool ContainsPoint (PointD windowPoint)
		=> Active && grips.Any (g => g.ContainsPoint (windowPoint));

	public void Draw (Gtk.Snapshot snapshot)
	{
		foreach (MoveHandle grip in grips)
			grip.Draw (snapshot);
	}

	/// <summary>Places the grips on <paramref name="quad"/> for <paramref name="mode"/>.</summary>
	public void Update (ReadOnlySpan<PointD> quad, TransformMode mode)
	{
		for (int i = 0; i < 4; i++) {
			PointD a = quad[i];
			PointD b = quad[(i + 1) % 4];

			if (mode == TransformMode.Skew) {
				grips[i].CanvasPosition = new PointD ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
				// The glyph shows the direction the edge slides: along itself.
				grips[i].Cursor = Math.Abs (b.X - a.X) >= Math.Abs (b.Y - a.Y)
					? skew_horizontal_cursor.Value
					: skew_vertical_cursor.Value;
			} else {
				grips[i].CanvasPosition = a;
				grips[i].Cursor = perspective_cursor.Value;
			}
		}
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
		for (int i = 0; i < 4; i++) {
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
		for (int i = 0; i < 4; i++)
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
