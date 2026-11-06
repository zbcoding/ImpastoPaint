using System;
using Pinta.Core;

namespace Pinta.Actions;

/// <summary>
/// The canvas grid owns whether the grid is shown. A user toggle of View → Show Grid writes it
/// there; any other change to the grid (e.g. the Canvas Grid dialog) is mirrored back into the
/// toggle's checked state.
/// </summary>
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
		canvas_grid.SettingsChanged += SyncToggleFromGrid;
	}

	void IActionHandler.Uninitialize ()
	{
		view.ShowGrid.Toggled -= Activated;
		canvas_grid.SettingsChanged -= SyncToggleFromGrid;
	}

	private void Activated (bool value, bool interactive)
	{
		// Non-interactive toggles come from SyncToggleFromGrid, which already reflects the grid.
		if (!interactive)
			return;

		canvas_grid.ShowGrid = value;
		canvas_grid.SaveGridSettings ();
	}

	private void SyncToggleFromGrid (object? sender, EventArgs e)
	{
		view.ShowGrid.Value = canvas_grid.ShowGrid;
	}
}
