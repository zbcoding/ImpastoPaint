using Pinta.Core;

namespace Pinta.Tools;

/// <summary>Bindings whose key is a lone modifier, such as Left Shift, held while dragging.</summary>
internal static class ModifierKeys
{
	/// <summary>The modifier bit a modifier key sets, or none for any other key.</summary>
	public static Gdk.ModifierType MaskOf (Gdk.Key key)
		=> key.Value switch {
			Gdk.Constants.KEY_Control_L or Gdk.Constants.KEY_Control_R => Gdk.ModifierType.ControlMask,
			Gdk.Constants.KEY_Shift_L or Gdk.Constants.KEY_Shift_R => Gdk.ModifierType.ShiftMask,
			Gdk.Constants.KEY_Alt_L or Gdk.Constants.KEY_Alt_R => Gdk.ModifierType.AltMask,
			Gdk.Constants.KEY_Meta_L or Gdk.Constants.KEY_Meta_R => Gdk.ModifierType.MetaMask,
			Gdk.Constants.KEY_Super_L or Gdk.Constants.KEY_Super_R => Gdk.ModifierType.SuperMask,
			Gdk.Constants.KEY_Hyper_L or Gdk.Constants.KEY_Hyper_R => Gdk.ModifierType.HyperMask,
			_ => default,
		};

	/// <summary>
	/// Whether a pointer event's <paramref name="state"/> holds every modifier
	/// <paramref name="gesture"/> needs. Either side's key counts, since the state cannot tell them apart.
	/// </summary>
	public static bool IsHeld (KeyGesture gesture, Gdk.ModifierType state)
	{
		Gdk.ModifierType required = gesture.Modifiers | MaskOf (gesture.Key);
		return required != default && (state & required) == required;
	}
}
