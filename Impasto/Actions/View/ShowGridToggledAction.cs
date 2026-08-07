using Pinta.Core;

namespace Pinta.Actions;

internal sealed class ShowGridToggledAction : IActionHandler
{
	private readonly ViewActions view;
	private readonly CanvasGridManager canvas_grid;

	internal ShowGridToggledAction (
		ViewActions view,
		CanvasGridManager canvasGrid)
	{
		this.view = view;
		canvas_grid = canvasGrid;
	}

	void IActionHandler.Initialize ()
	{
		view.ShowGrid.Value = canvas_grid.ShowGrid;
		view.ShowGrid.Toggled += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		view.ShowGrid.Toggled -= Activated;
	}

	private void Activated (bool value, bool interactive)
	{
		canvas_grid.ShowGrid = value;
		canvas_grid.SaveGridSettings ();
	}
}
