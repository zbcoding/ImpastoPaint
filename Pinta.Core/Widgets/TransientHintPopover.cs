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

	// Shared by the non-grabbing caption variant (see Caption): never takes the pointer grab,
	// no arrow, dismissed by hand. Reads as a tooltip rather than a menu anchored to the widget.
	private const string NoGrabCss = "toolbox-hint";

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

	/// <summary>
	/// A non-grabbing caption variant, for hints that track the pointer across many anchors
	/// (toolbox flyout entries, palette swatches): one popover is created per <see cref="CaptionPopover"/>
	/// and re-parented to whichever widget the pointer is over, so it can never take a grab —
	/// which is exactly what makes an autohiding tooltip freeze input next to a flyout's own
	/// grabbing popover. The caller owns trigger/dismiss timing (motion enter/leave + timeouts)
	/// and just calls <see cref="Show"/>/<see cref="Hide"/> as it does for the main hint.
	/// </summary>
	public sealed class CaptionPopover
	{
		private Gtk.Popover? popover;
		private Gtk.Label? label;

		public void Show (
			Gtk.Widget anchor,
			string text,
			Gtk.PositionType position,
			int maxWidthChars,
			string cssClass = NoGrabCss)
		{
			if (popover is null) {
				popover = Gtk.Popover.New ();
				// Not autohiding: an autohiding popover grabs the pointer, which is exactly
				// what makes a hint next to a grabbing popover freeze input. This one never
				// takes a grab and is dismissed by hand when the cursor leaves.
				popover.Autohide = false;
				popover.CanTarget = false;
				popover.HasArrow = false; // Reads as a tooltip, not as a menu anchored to the entry.
				popover.AddCssClass (cssClass);
				label = Gtk.Label.New (text);
				label.Halign = Gtk.Align.Start;
				label.Justify = Gtk.Justification.Left;
				label.Wrap = true;
				label.MaxWidthChars = maxWidthChars;
				popover.SetChild (label);
			} else {
				label?.SetText (text);
			}

			// Parent on every show, not just creation: the first show used to skip it entirely,
			// popping an orphaned popover - "realize() on a widget that isn't inside a toplevel",
			// then gtk_widget_get_native / gdk_surface_new_popup assertion failures on screen.
			// Each swatch owns its own CaptionPopover, so palette hovers hit this once per swatch.
			if (popover.GetParent () != anchor) {
				popover.Popdown ();
				popover.Unparent ();
				popover.SetParent (anchor);
			}

			popover.Position = position;
			popover.Popup ();
		}

		public void Hide ()
		{
			popover?.Popdown ();
		}

		/// <summary>Hides and releases the popover (e.g. on widget destruction).</summary>
		public void Dispose ()
		{
			Hide ();
			if (popover is not null) {
				popover.Unparent ();
				popover = null;
			}
			label = null;
		}
	}
}
