using System;

namespace Pinta.Core;

/// <summary>
/// Shared machinery for the app's transient canvas hint popovers (the clone-stamp origin
/// reminder, the text tool's edit hint, the transform tool's nudge hint): the settings key/gate
/// that lets a user turn every such popover off, and the popover+label widget mechanics (lazily
/// create or reuse, reparent if the canvas changed, position, and show). Each caller keeps its own
/// trigger and dismiss timing - a fixed auto-dismiss delay, a hover-in delay, a hold-to-show delay -
/// since those differ enough between tools that folding them in here would blur a real behavioral
/// difference rather than remove duplication. See docs-private/popoverhints.md for this
/// subsystem's intent.
/// </summary>
public sealed class TransientHintPopover
{
	public const string SettingKey = "popover-hint-mode";

	public static bool ShouldShow
		=> PintaCore.Settings.GetSetting (SettingKey, (int) PopoverHintMode.All) != (int) PopoverHintMode.None;

	private Gtk.Popover? popover;
	private Gtk.Label? label;
	private string? last_text;

	/// <summary>Whether the popover is currently created (shown or merely retained for reuse).</summary>
	public bool Exists => popover is not null;

	/// <summary>The text most recently passed to <see cref="Show"/>; lets a caller recognize its own hint.</summary>
	public string? LastText => last_text;

	/// <summary>
	/// Shows the hint, creating the popover on first use or reusing (and re-labelling/re-parenting)
	/// it otherwise. <paramref name="anchorView"/> is in view coordinates. <paramref name="configure"/>
	/// runs once, only when the label is first created (for a caller-specific tweak like margins).
	/// </summary>
	public void Show (
		Gtk.Widget canvas,
		string text,
		PointD anchorView,
		int maxWidthChars = 60,
		double clampMax = 10_000,
		Action<Gtk.Label>? configure = null)
	{
		last_text = text;

		if (popover is null) {
			popover = Gtk.Popover.New ();
			popover.Autohide = false;
			popover.Position = Gtk.PositionType.Bottom;
			popover.SetParent (canvas);
			label = Gtk.Label.New (text);
			label.Wrap = true;
			label.MaxWidthChars = maxWidthChars;
			configure?.Invoke (label);
			popover.SetChild (label);
		} else {
			label?.SetText (text);
			// Re-parent if canvas changed.
			if (popover.GetParent () != canvas) {
				popover.Unparent ();
				popover.SetParent (canvas);
			}
		}

		popover.PointingTo = new Gdk.Rectangle {
			X = (int) Math.Clamp (anchorView.X, 0, clampMax),
			Y = (int) Math.Clamp (anchorView.Y, 0, clampMax),
			Width = 1,
			Height = 1,
		};
		popover.Popup ();
	}

	public void Hide ()
	{
		if (popover is null)
			return;
		try {
			popover.Popdown ();
		} catch {
			// Ignore if already closed.
		}
	}

	/// <summary>Hides and releases the popover entirely (e.g. on tool deactivation).</summary>
	public void Dispose ()
	{
		Hide ();
		if (popover is not null) {
			popover.Unparent ();
			popover = null;
		}
		last_text = null;
	}
}
