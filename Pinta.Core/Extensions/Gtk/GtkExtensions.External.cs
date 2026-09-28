using System;
using System.Runtime.InteropServices;

namespace Pinta.Core;

partial class GtkExtensions
{
	// TODO-GTK4 (bindings, unsubmitted) - need support for 'out' enum parameters.
	[LibraryImport (GTK_LIBRARY_NAME, EntryPoint = "gtk_accelerator_parse")]
	[return: MarshalAs (UnmanagedType.Bool)]
	private static partial bool AcceleratorParse (
		[MarshalAs (UnmanagedType.LPUTF8Str)] string accelerator,
		out uint accelerator_key,
		out Gdk.ModifierType accelerator_mods);

	/// <summary>
	/// Public entry point for gtk_accelerator_parse, since GirCore's generated binding
	/// doesn't support the 'out' enum parameter (see TODO above).
	/// </summary>
	public static bool TryParseAccelerator (
		string accelerator,
		out uint accelerator_key,
		out Gdk.ModifierType accelerator_mods)
		=> AcceleratorParse (accelerator, out accelerator_key, out accelerator_mods);

	// Manual binding for GetPreeditString
	// TODO-GTK4 (bindings) - missing from gir.core: "opaque record parameter 'attrs' with direction != in not yet supported"
	[DllImport (GTK_LIBRARY_NAME, EntryPoint = "gtk_im_context_get_preedit_string")]
	private static extern void IMContextGetPreeditString (
		IntPtr handle,
		out GLib.Internal.NonNullableUtf8StringOwnedHandle str,
		out Pango.Internal.AttrListOwnedHandle attrs,
		out int cursor_pos);

	/// <summary>
	/// Sets the dropdown's search expression to a <see cref="Gtk.StringObject"/>'s "string"
	/// property, which the popup's search entry filters on.
	/// TODO-GTK4 (bindings) - gir.core 0.8 has no public Gtk.PropertyExpression constructor.
	/// </summary>
	public static void SetStringObjectExpression (this Gtk.DropDown dropDown)
	{
		using var property = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create ("string");
		IntPtr expression = Gtk.Internal.PropertyExpression.New (Gtk.StringObject.GetGType (), IntPtr.Zero, property);
		Gtk.Internal.DropDown.SetExpression (dropDown.Handle.DangerousGetHandle (), expression);
		Gtk.Internal.Expression.Unref (expression); // the dropdown holds its own reference
	}
}
