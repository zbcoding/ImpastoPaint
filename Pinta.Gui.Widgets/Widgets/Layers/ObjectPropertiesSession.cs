using System;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// The edit an Object Properties window makes, kept apart from its widgets: every change applies
/// straight to the object with no history, so the canvas shows the real result; <see cref="Commit"/>
/// records the session as one undoable step per changed property and <see cref="Cancel"/> puts the
/// starting values back.
/// <para>
/// The window is non-modal, so its object can leave its layer while the window is open: deleted,
/// baked, or swapped for a new instance when the shape and text tools persist their edits (objects
/// carry no identity that survives that swap). The moment the session sees its object gone it ends
/// and raises <see cref="ObjectLost"/> so the window closes, instead of staying open with controls
/// that silently do nothing.
/// </para>
/// <para>
/// ponytail: loss is noticed after the fact. Any other history step recorded while the window holds
/// uncommitted values (a shape edit, a delete, undo of a shape step) snapshots the object with those
/// values, so undoing that step brings them back without a step of their own. Closing that gap needs
/// the window settled before another step records — a pre-record hook in the history/tool layer.
/// </para>
/// </summary>
public sealed class ObjectPropertiesSession
{
	private readonly LayersListViewItem row;
	private readonly Document document;
	private bool ended;

	/// <param name="objectRow">The layers dock row of the object to edit.</param>
	public ObjectPropertiesSession (LayersListViewItem objectRow)
	{
		if (!objectRow.IsObjectRow || objectRow.Document is not Document rowDocument)
			throw new ArgumentException ("An object properties session needs an object row.", nameof (objectRow));

		row = LayersListViewItem.NewPinnedToObject (objectRow);
		document = rowDocument;

		InitialName = row.ObjectName;
		InitialHidden = row.ObjectHidden;
		InitialOpacity = row.ObjectOpacity;
		InitialBlendMode = row.ObjectBlendMode;

		document.History.HistoryItemAdded += OnHistoryChanged;
		document.History.ActionUndone += OnHistoryChanged;
		document.History.ActionRedone += OnHistoryChanged;
		LayerObjectSelection.ObjectsChanged += CheckObjectExists;
		PintaCore.Workspace.DocumentClosed += OnDocumentClosed;
	}

	public string InitialName { get; }
	public bool InitialHidden { get; }
	public double InitialOpacity { get; }
	public BlendMode InitialBlendMode { get; }

	/// <summary>Raised once, after the session ended itself because its object left its layer.</summary>
	public event EventHandler? ObjectLost;

	public void SetOpacity (double opacity)
	{
		if (IsObjectAvailable ())
			row.SetObjectOpacity (opacity);
	}

	public void SetBlendMode (BlendMode mode)
	{
		if (IsObjectAvailable ())
			row.SetObjectBlendMode (mode);
	}

	public void SetHidden (bool hidden)
	{
		if (IsObjectAvailable ())
			row.SetObjectHiddenLive (hidden);
	}

	/// <summary>
	/// Records every property that changed since the session opened, one undoable step each. Records
	/// nothing for an object that has left its layer.
	/// </summary>
	public void Commit (string name)
	{
		if (ended)
			return;

		End ();

		if (row.ObjectOpacity != InitialOpacity)
			row.PushObjectOpacityHistory (InitialOpacity);

		if (row.ObjectBlendMode != InitialBlendMode)
			row.PushObjectBlendModeHistory (InitialBlendMode);

		if (row.ObjectHidden != InitialHidden)
			row.PushObjectHiddenHistory (InitialHidden);

		// RenameObject is a no-op when the name is unchanged, and pushes its own history item.
		row.RenameObject (name);
	}

	/// <summary>Puts the starting values back, with no history.</summary>
	public void Cancel ()
	{
		if (ended)
			return;

		End ();

		row.SetObjectOpacity (InitialOpacity);
		row.SetObjectBlendMode (InitialBlendMode);
		row.SetObjectHiddenLive (InitialHidden);
	}

	/// <summary>
	/// Whether the session can still edit its object. Checked before every change, not only on the
	/// notifications below, because some edits replace objects without raising any of them (a shape
	/// tool persisting its live engines outside a history step).
	/// </summary>
	private bool IsObjectAvailable ()
	{
		CheckObjectExists ();
		return !ended;
	}

	private void CheckObjectExists ()
	{
		if (!ended && !row.ObjectExists)
			Lose ();
	}

	private void OnHistoryChanged (object? sender, EventArgs e) => CheckObjectExists ();

	private void OnDocumentClosed (object? sender, DocumentEventArgs e)
	{
		if (!ended && e.Document == document)
			Lose ();
	}

	// Nothing is left to restore: the object (or its document) is gone, and a step that removed or
	// replaced it has already recorded whatever it held.
	private void Lose ()
	{
		End ();
		ObjectLost?.Invoke (this, EventArgs.Empty);
	}

	private void End ()
	{
		if (ended)
			return;

		ended = true;
		document.History.HistoryItemAdded -= OnHistoryChanged;
		document.History.ActionUndone -= OnHistoryChanged;
		document.History.ActionRedone -= OnHistoryChanged;
		LayerObjectSelection.ObjectsChanged -= CheckObjectExists;
		PintaCore.Workspace.DocumentClosed -= OnDocumentClosed;
	}
}
