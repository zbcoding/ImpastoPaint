using Pinta.Core;

namespace Pinta.Actions;

internal sealed class SnapToGridToggledAction : IActionHandler
{
	private readonly ViewActions view;
	private readonly CanvasGridManager canvas_grid;

	internal SnapToGridToggledAction (
		ViewActions view,
		CanvasGridManager canvasGrid)
	{
		this.view = view;
		canvas_grid = canvasGrid;
	}

	void IActionHandler.Initialize ()
	{
		view.SnapToGrid.Value = canvas_grid.SnapEnabled;
		view.SnapToGrid.Toggled += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		view.SnapToGrid.Toggled -= Activated;
	}

	private void Activated (bool value, bool interactive)
	{
		canvas_grid.SnapEnabled = value;
		canvas_grid.SaveGridSettings ();
	}
}
