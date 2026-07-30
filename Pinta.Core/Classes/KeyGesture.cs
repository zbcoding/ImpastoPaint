using System;

namespace Pinta.Core;

public readonly struct KeyGesture : IEquatable<KeyGesture>
{
	public const Gdk.ModifierType AcceleratorMask =
		Gdk.ModifierType.ControlMask | Gdk.ModifierType.ShiftMask | Gdk.ModifierType.AltMask |
		Gdk.ModifierType.SuperMask | Gdk.ModifierType.MetaMask | Gdk.ModifierType.HyperMask;

	public Gdk.Key Key { get; }
	public Gdk.ModifierType Modifiers { get; }

	public KeyGesture (Gdk.Key key, Gdk.ModifierType modifiers = default)
	{
		Key = key;
		Modifiers = modifiers & AcceleratorMask;
	}

	public static KeyGesture None { get; } = new (Gdk.Key.Invalid);

	public bool IsValid
		=> Key.Value is not (0 or Gdk.Constants.KEY_VoidSymbol);

	public string ToAcceleratorName ()
		=> IsValid ? Gtk.Functions.AcceleratorName (Key.Value, Modifiers) : string.Empty;

	public string ToLabel ()
		=> IsValid
			? Gtk.Functions.AcceleratorGetLabel (Key.Value, Modifiers)
			: Translations.GetString ("None");

	public static KeyGesture? TryParse (string accelerator)
	{
		if (string.IsNullOrEmpty (accelerator))
			return null;

		if (!GtkExtensions.TryParseAccelerator (accelerator, out uint keyval, out var mods))
			return null;

		return new KeyGesture (new Gdk.Key (keyval), mods);
	}

	public bool Equals (KeyGesture other)
		=> Key.ToUpper ().Value == other.Key.ToUpper ().Value && Modifiers == other.Modifiers;

	public override bool Equals (object? obj)
		=> obj is KeyGesture other && Equals (other);

	public override int GetHashCode ()
		=> HashCode.Combine (Key.ToUpper ().Value, Modifiers);

	public static bool operator == (KeyGesture left, KeyGesture right) => left.Equals (right);
	public static bool operator != (KeyGesture left, KeyGesture right) => !left.Equals (right);
}
