using System;

namespace Pinta.Core;


public interface ICanvasGridService
{
	bool ShowGrid { get; set; }
	int CellWidth { get; set; }
	int CellHeight { get; set; }
	Cairo.Color GridColor { get; set; }
	bool SnapEnabled { get; set; }

	/// <summary>
	/// Spacing that tool input snaps to, or null when snapping is off.
	/// </summary>
	PointD? SnapStep { get; }

	PointD SnapPoint (PointD point);

	/// <summary>
	/// The ruler's metric, as the index used by the "rulermetric" action.
	/// </summary>
	int RulerMetric { get; set; }

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
	/// shown, otherwise one unit of the ruler's current metric.
	/// </summary>
	public PointD? SnapStep {
		get {
			if (!SnapEnabled)
				return null;

			if (ShowGrid)
				return new (CellWidth, CellHeight);

			// Pixels / inches / centimeters, matching Ruler's MetricType order and
			// its pixels-per-unit values. MetricType itself lives in the widgets
			// assembly, which Core cannot reference.
			double unit = ruler_metric switch {
				1 => 72.0,
				2 => 28.35,
				_ => 1.0,
			};
			return new (unit, unit);
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

	public PointD SnapPoint (PointD point)
	{
		if (SnapStep is not PointD step)
			return point;

		return new (
			Math.Round (point.X / step.X) * step.X,
			Math.Round (point.Y / step.Y) * step.Y);
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
