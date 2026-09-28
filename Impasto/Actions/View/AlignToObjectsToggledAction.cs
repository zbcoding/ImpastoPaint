using Pinta.Core;

namespace Pinta.Actions;

internal sealed class AlignToObjectsToggledAction : IActionHandler
{
	private readonly ViewActions view;
	private readonly CanvasGridManager canvas_grid;

	internal AlignToObjectsToggledAction (
		ViewActions view,
		CanvasGridManager canvasGrid)
	{
		this.view = view;
		canvas_grid = canvasGrid;
	}

	void IActionHandler.Initialize ()
	{
		view.AlignToObjects.Value = canvas_grid.AlignToObjects;
		view.AlignToObjects.Toggled += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		view.AlignToObjects.Toggled -= Activated;
	}

	private void Activated (bool value, bool interactive)
	{
		canvas_grid.AlignToObjects = value;
		canvas_grid.SaveGridSettings ();
	}
}
