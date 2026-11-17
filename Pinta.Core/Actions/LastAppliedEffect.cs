// LastAppliedEffect.cs
//
// Impasto: the Repeat entry of the Adjustments or Effects menu. Remembers, for this session, the
// last effect applied from that menu together with a copy of the settings it was applied with.

namespace Pinta.Core;

public sealed class LastAppliedEffect
{
	private readonly Gio.Menu section = Gio.Menu.New ();
	private bool documents_open;

	public Command Command { get; }

	/// <summary>
	/// A private copy of the last applied effect, or null until one is applied. Its settings are
	/// frozen at apply time, so reopening and cancelling the effect's dialog does not change them.
	/// </summary>
	public BaseEffect? Effect { get; private set; }

	public LastAppliedEffect (string name, string label, string iconName, string shortcut)
	{
		Command = new Command (name, label, null, iconName, shortcuts: [shortcut]) {
			Sensitive = false,
		};
		section.AppendItem (Command.CreateMenuItem ());
	}

	/// <summary>Adds the command to the app and its own section at the bottom of <paramref name="menu"/>.</summary>
	public void RegisterActions (Gtk.Application app, Gio.Menu menu)
	{
		app.AddCommand (Command);
		menu.AppendSection (null, section);
	}

	public void Remember (BaseEffect effect)
	{
		Effect = effect.Clone ();

		section.RemoveAll ();
		// Translators: Adjustments or Effects menu item that applies the last used adjustment or effect again, with the same settings; {0} is its name, e.g. "Gaussian Blur".
		section.AppendItem (Gio.MenuItem.New (Translations.GetString ("Repeat {0}", effect.Name), Command.FullName));

		UpdateSensitive ();
	}

	public void SetDocumentsOpen (bool open)
	{
		documents_open = open;
		UpdateSensitive ();
	}

	private void UpdateSensitive ()
		=> Command.Sensitive = documents_open && Effect is not null;
}
