using System;

namespace Pinta.Core;


/// <summary>
/// The canvas-relative lines that snapping can pull a point onto, when there is
/// no grid or ruler to snap to instead.
/// </summary>
[Flags]
public enum SnapGuides
{
	None = 0,
	Left = 1 << 0,
	HorizontalCenter = 1 << 1,
	Right = 1 << 2,
	Top = 1 << 3,
	VerticalCenter = 1 << 4,
	Bottom = 1 << 5,
}

public interface ICanvasGridService
{
	bool ShowGrid { get; set; }
	int CellWidth { get; set; }
	int CellHeight { get; set; }
	Cairo.Color GridColor { get; set; }
	bool SnapEnabled { get; set; }

	/// <summary>
	/// Spacing that tool input snaps to, or null when nothing to snap to is
	/// visible.
	/// </summary>
	PointD? SnapStep { get; }

	PointD SnapPoint (PointD point);

	/// <summary>
	/// Which canvas guides the last snapped point landed on, so the canvas can
	/// show them while they are holding the point.
	/// </summary>
	SnapGuides ActiveGuides { get; }

	/// <summary>
	/// Drops the guide display, e.g. once the drag that was snapping ends.
	/// </summary>
	void ClearActiveGuides ();

	/// <summary>
	/// The ruler's metric, as the index used by the "rulermetric" action.
	/// </summary>
	int RulerMetric { get; set; }

	/// <summary>
	/// Spacing, in canvas pixels, between the tick marks the rulers last drew.
	/// Set by the rulers themselves, since the spacing they pick depends on the
	/// zoom and on how wide their labels are.
	/// </summary>
	double RulerTickWidth { get; set; }
	double RulerTickHeight { get; set; }

	/// <summary>
	/// Whether the rulers are currently shown, which is what makes their tick
	/// marks available as a snapping target.
	/// </summary>
	bool RulersVisible { get; set; }

	bool ShowAxonometricGrid { get; set; }
	int AxonometricWidth { get; set; }
	DegreesAngle AxonometricAngle { get; set; }

	public void SaveGridSettings ();

	public void LoadGridSettings ();

	public event EventHandler SettingsChanged;
}


public sealed class CanvasGridManager : ICanvasGridService
{
	private readonly SettingsManager settings;
	private readonly IWorkspaceService workspace;

	private bool show_grid;
	private int cell_width;
	private int cell_height;
	private Cairo.Color grid_color;
	private bool snap_enabled;

	private bool show_axonometric_grid;
	private int axonometric_width;
	private DegreesAngle axonometric_angle;

	public bool ShowGrid {
		get => show_grid;
		set => SetProperty (ref show_grid, value);
	}

	public int CellWidth {
		get => cell_width;
		set => SetProperty (ref cell_width, value);
	}

	public int CellHeight {
		get => cell_height;
		set => SetProperty (ref cell_height, value);
	}

	public Cairo.Color GridColor {
		get => grid_color;
		set => SetProperty (ref grid_color, value);
	}

	public bool SnapEnabled {
		get => snap_enabled;
		set => SetProperty (ref snap_enabled, value);
	}

	/// <summary>
	/// Snap spacing in canvas pixels: the grid's cell size when the grid is
	/// shown, otherwise one unit of the ruler's current metric. Null when
	/// neither the grid nor the rulers are on - there is nothing visible to
	/// snap to, so snapping stays out of the way.
	/// </summary>
	public PointD? SnapStep {
		get {
			if (!SnapEnabled)
				return null;

			// The axonometric lattice isn't a rectangular step; SnapPoint handles it.
			if (AxonometricSnapActive)
				return null;

			if (ShowGrid)
				return new (CellWidth, CellHeight);

			if (!RulersVisible)
				return null;

			// The rulers report the spacing of the ticks they drew. Before they
			// have drawn, fall back to one unit of the current metric - pixels /
			// inches / centimeters, matching Ruler's MetricType order.
			double unit = ruler_metric switch {
				1 => 72.0,
				2 => 28.35,
				_ => 1.0,
			};
			return new (
				RulerTickWidth > 0 ? RulerTickWidth : unit,
				RulerTickHeight > 0 ? RulerTickHeight : unit);
		}
	}

	/// <summary>
	/// The ruler's metric, as the index used by the "rulermetric" action.
	/// </summary>
	public int RulerMetric {
		get => ruler_metric;
		set => ruler_metric = value;
	}
	private int ruler_metric;

	public bool RulersVisible { get; set; }

	public double RulerTickWidth { get; set; }
	public double RulerTickHeight { get; set; }

	/// <summary>
	/// How close, in screen pixels, the cursor must be to a canvas guide for it
	/// to snap. Regular spacings snap unconditionally; the canvas guides are
	/// only three lines per axis, so they pull instead of quantizing.
	/// </summary>
	private const double CANVAS_GUIDE_TOLERANCE = 8.0;

	public SnapGuides ActiveGuides {
		get => active_guides;
		private set {
			if (active_guides == value) return;
			active_guides = value;
			workspace.Invalidate ();
		}
	}
	private SnapGuides active_guides;

	public void ClearActiveGuides () => ActiveGuides = SnapGuides.None;

	public PointD SnapPoint (PointD point)
	{
		if (!SnapEnabled) {
			ClearActiveGuides ();
			return point;
		}

		if (AxonometricSnapActive) {
			ClearActiveGuides ();
			return SnapToAxonometricLattice (point);
		}

		if (SnapStep is PointD step) {
			ClearActiveGuides ();
			return new (
				Math.Round (point.X / step.X) * step.X,
				Math.Round (point.Y / step.Y) * step.Y);
		}

		// Nothing regular to snap to, so fall back to the canvas itself: its
		// edges and its two centre lines.
		if (!workspace.HasOpenDocuments) {
			ClearActiveGuides ();
			return point;
		}

		Size imageSize = workspace.ImageSize;
		double tolerance = CANVAS_GUIDE_TOLERANCE / workspace.GetScale ();

		(double x, SnapGuides xGuide) = SnapToGuides (
			point.X,
			imageSize.Width,
			tolerance,
			[SnapGuides.Left, SnapGuides.HorizontalCenter, SnapGuides.Right]);

		(double y, SnapGuides yGuide) = SnapToGuides (
			point.Y,
			imageSize.Height,
			tolerance,
			[SnapGuides.Top, SnapGuides.VerticalCenter, SnapGuides.Bottom]);

		ActiveGuides = xGuide | yGuide;

		return new (x, y);
	}

	/// <summary>
	/// The axonometric grid, when shown, is the finest thing on the canvas, so
	/// it takes priority over the rectangular grid and the ruler ticks.
	/// </summary>
	private bool AxonometricSnapActive
		=> ShowAxonometricGrid
		&& AxonometricWidth > 0
		&& AxonometricAngle.Degrees > 0
		&& AxonometricAngle.Degrees < 90;

	/// <summary>
	/// The lattice the axonometric grid's lines cross at: verticals every
	/// <see cref="AxonometricWidth"/> pixels, and rows every
	/// width * tan(angle) pixels, where a row is only reachable from columns of
	/// the same parity (the diagonals skip every other crossing).
	/// </summary>
	private PointD SnapToAxonometricLattice (PointD point)
	{
		double columnWidth = AxonometricWidth;
		double rowHeight = columnWidth * Math.Tan (AxonometricAngle.ToRadians ().Radians);

		double column = Math.Round (point.X / columnWidth);
		double row = point.Y / rowHeight;

		// Snap to the nearest row with the column's parity.
		double parityRow = Math.Round ((row - column) / 2.0) * 2.0 + column;

		return new (column * columnWidth, parityRow * rowHeight);
	}

	private static (double, SnapGuides) SnapToGuides (
		double value,
		double extent,
		double tolerance,
		SnapGuides[] names)
	{
		double[] guides = [0.0, extent / 2.0, extent];

		double nearest = value;
		SnapGuides nearestGuide = SnapGuides.None;
		double bestDistance = tolerance;

		for (int i = 0; i < guides.Length; ++i) {
			double distance = Math.Abs (value - guides[i]);
			if (distance >= bestDistance) continue;
			bestDistance = distance;
			nearest = guides[i];
			nearestGuide = names[i];
		}

		return (nearest, nearestGuide);
	}

	public bool ShowAxonometricGrid {
		get => show_axonometric_grid;
		set => SetProperty (ref show_axonometric_grid, value);
	}

	public int AxonometricWidth {
		get => axonometric_width;
		set => SetProperty (ref axonometric_width, value);
	}

	public DegreesAngle AxonometricAngle {
		get => axonometric_angle;
		set => SetProperty (ref axonometric_angle, value);
	}

	public CanvasGridManager (WorkspaceManager workspace, SettingsManager settings)
	{
		this.settings = settings;
		this.workspace = workspace;

		// Invalidate the workspace if the grid is changed to redraw the grid
		SettingsChanged += (_, __) => {
			workspace.Invalidate ();
		};

		LoadGridSettings ();
	}

	public void SaveGridSettings ()
	{
		settings.PutSetting (SettingNames.SHOW_CANVAS_GRID, ShowGrid);
		settings.PutSetting (SettingNames.CANVAS_GRID_WIDTH, CellWidth);
		settings.PutSetting (SettingNames.CANVAS_GRID_HEIGHT, CellHeight);
		settings.PutSetting (SettingNames.CANVAS_GRID_COLOR, GridColor.ToHex ());
		settings.PutSetting (SettingNames.SNAP_TO_GRID, SnapEnabled);

		settings.PutSetting (SettingNames.SHOW_CANVAS_AXONOMETRIC_GRID, ShowAxonometricGrid);
		settings.PutSetting (SettingNames.CANVAS_AXONOMETRIC_WIDTH, AxonometricWidth);
		settings.PutSetting (SettingNames.CANVAS_AXONOMETRIC_ANGLE, AxonometricAngle.Degrees);
	}

	public void LoadGridSettings ()
	{
		ShowGrid = settings.GetSetting (SettingNames.SHOW_CANVAS_GRID, false);
		CellWidth = settings.GetSetting (SettingNames.CANVAS_GRID_WIDTH, 64);
		CellHeight = settings.GetSetting (SettingNames.CANVAS_GRID_HEIGHT, 64);
		SnapEnabled = settings.GetSetting (SettingNames.SNAP_TO_GRID, false);
		GridColor = Cairo.Color.FromHex (settings.GetSetting (SettingNames.CANVAS_GRID_COLOR, string.Empty)) ?? new Cairo.Color (0, 0, 0);

		ShowAxonometricGrid = settings.GetSetting (SettingNames.SHOW_CANVAS_AXONOMETRIC_GRID, false);
		AxonometricWidth = settings.GetSetting (SettingNames.CANVAS_AXONOMETRIC_WIDTH, 64);
		AxonometricAngle = new (settings.GetSetting<double> (SettingNames.CANVAS_AXONOMETRIC_ANGLE, 30));
	}

	private void SetProperty<T> (ref T field, T value)
	{
		if (Equals (field, value)) return;
		field = value;
		SettingsChanged?.Invoke (this, EventArgs.Empty);
	}

	public event EventHandler SettingsChanged;
}
