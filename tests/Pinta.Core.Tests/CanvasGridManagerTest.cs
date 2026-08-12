using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class CanvasGridManagerTest
{
	private sealed class MockSettingsService : ISettingsService
	{
		private readonly Dictionary<string, object> settings = [];

#pragma warning disable CS0067
		public event EventHandler? SaveSettingsBeforeQuit;
#pragma warning restore CS0067

		public T GetSetting<T> (string key, T defaultValue)
			=> settings.TryGetValue (key, out var value) ? (T) value : defaultValue;

		public string GetUserSettingsDirectory () => throw new NotSupportedException ();

		public void PutSetting (string key, object value) => settings[key] = value;
	}

	private sealed class MockWorkspaceService : IWorkspaceService
	{
		public Document ActiveDocument => throw new NotSupportedException ();

		public DocumentWorkspace ActiveWorkspace => throw new NotSupportedException ();

		public bool HasOpenDocuments => false;

		public Size ImageSize => throw new NotSupportedException ();

		public SelectionModeHandler SelectionHandler => throw new NotSupportedException ();

#pragma warning disable CS0067
		public event EventHandler? ActiveDocumentChanged;
		public event EventHandler? SelectionChanged;
		public event EventHandler? LayerAdded;
		public event EventHandler? LayerRemoved;
		public event EventHandler? SelectedLayerChanged;
		public event System.ComponentModel.PropertyChangedEventHandler? LayerPropertyChanged;
		public event EventHandler? ViewSizeChanged;
#pragma warning restore CS0067
	}

	private static CanvasGridManager CreateManager (MockSettingsService settings)
		=> new (new MockWorkspaceService (), settings);

	[Test]
	public void LoadGridSettings_ClampsCorruptZeroSpacingsToMinimum ()
	{
		var settings = new MockSettingsService ();
		settings.PutSetting (SettingNames.CANVAS_GRID_WIDTH, 0);
		settings.PutSetting (SettingNames.CANVAS_GRID_HEIGHT, 0);
		settings.PutSetting (SettingNames.CANVAS_AXONOMETRIC_WIDTH, 0);

		CanvasGridManager manager = CreateManager (settings);

		Assert.That (manager.CellWidth, Is.EqualTo (1));
		Assert.That (manager.CellHeight, Is.EqualTo (1));
		Assert.That (manager.AxonometricWidth, Is.EqualTo (1));
	}

	[Test]
	public void LoadGridSettings_KeepsValidSpacings ()
	{
		var settings = new MockSettingsService ();
		settings.PutSetting (SettingNames.CANVAS_GRID_WIDTH, 32);
		settings.PutSetting (SettingNames.CANVAS_GRID_HEIGHT, 16);
		settings.PutSetting (SettingNames.CANVAS_AXONOMETRIC_WIDTH, 48);

		CanvasGridManager manager = CreateManager (settings);

		Assert.That (manager.CellWidth, Is.EqualTo (32));
		Assert.That (manager.CellHeight, Is.EqualTo (16));
		Assert.That (manager.AxonometricWidth, Is.EqualTo (48));
	}

	[Test]
	public void SnapStep_ReturnsNull_WhenGridCellIsNotPositive ()
	{
		var settings = new MockSettingsService ();
		CanvasGridManager manager = CreateManager (settings);
		manager.ShowGrid = true;
		manager.SnapEnabled = true;
		manager.CellWidth = 0;
		manager.CellHeight = 0;

		Assert.That (manager.SnapStep, Is.Null);

		manager.CellWidth = 64;
		manager.CellHeight = 0;

		Assert.That (manager.SnapStep, Is.Null);
	}

	[Test]
	public void SnapStep_ReturnsCellSize_WhenGridCellIsPositive ()
	{
		var settings = new MockSettingsService ();
		CanvasGridManager manager = CreateManager (settings);
		manager.ShowGrid = true;
		manager.SnapEnabled = true;
		manager.CellWidth = 64;
		manager.CellHeight = 32;

		Assert.That (manager.SnapStep, Is.EqualTo (new PointD (64, 32)));
	}

	[Test]
	public void SnapPoint_DoesNotReturnNaN_WhenGridCellIsNotPositive ()
	{
		var settings = new MockSettingsService ();
		CanvasGridManager manager = CreateManager (settings);
		manager.ShowGrid = true;
		manager.SnapEnabled = true;
		manager.CellWidth = 0;
		manager.CellHeight = 0;

		PointD result = manager.SnapPoint (new PointD (123.4, 567.8));

		Assert.That (double.IsFinite (result.X), Is.True);
		Assert.That (double.IsFinite (result.Y), Is.True);
	}

	[Test]
	public void SnapPoint_SnapsToGrid_WhenCellSizeIsValid ()
	{
		var settings = new MockSettingsService ();
		CanvasGridManager manager = CreateManager (settings);
		manager.ShowGrid = true;
		manager.SnapEnabled = true;
		manager.CellWidth = 64;
		manager.CellHeight = 32;

		PointD result = manager.SnapPoint (new PointD (130, 95));

		Assert.That (result.X, Is.EqualTo (128));
		Assert.That (result.Y, Is.EqualTo (96));
	}

	private static readonly SnapGuides[] horizontal_guides =
		[SnapGuides.Left, SnapGuides.HorizontalCenter, SnapGuides.Right];

	// The point of offering the whole box to the guides: a drag that is merely
	// near centered lands exactly centered, which a corner-only anchor can't do.
	[Test]
	public void SnapExtentToGuides_CentersBox_WhenNearlyCentered ()
	{
		(double origin, SnapGuides guide) = CanvasGridManager.SnapExtentToGuides (
			origin: 96, size: 100, extent: 300, tolerance: 8, horizontal_guides);

		Assert.That (origin, Is.EqualTo (100));
		Assert.That (guide, Is.EqualTo (SnapGuides.HorizontalCenter));
	}

	[Test]
	public void SnapExtentToGuides_AlignsTrailingEdge_ToOppositeCanvasEdge ()
	{
		(double origin, SnapGuides guide) = CanvasGridManager.SnapExtentToGuides (
			origin: 196, size: 100, extent: 300, tolerance: 8, horizontal_guides);

		Assert.That (origin, Is.EqualTo (200));
		Assert.That (guide, Is.EqualTo (SnapGuides.Right));
	}

	[Test]
	public void SnapExtentToGuides_LeavesBoxAlone_WhenNoGuideIsWithinTolerance ()
	{
		(double origin, SnapGuides guide) = CanvasGridManager.SnapExtentToGuides (
			origin: 60, size: 100, extent: 300, tolerance: 8, horizontal_guides);

		Assert.That (origin, Is.EqualTo (60));
		Assert.That (guide, Is.EqualTo (SnapGuides.None));
	}
}
