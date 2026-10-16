using System.Windows;

namespace Gatito;

public partial class App : Application
{
    // Un solo Simón: si ya hay uno corriendo (por ejemplo, el del arranque con Windows), esta copia se cierra.
    private static Mutex? _unico;

    protected override void OnStartup(StartupEventArgs e)
    {
        _unico = new Mutex(true, "Gatito.Simon", out bool primero);
        if (!primero) { Shutdown(); return; }
        base.OnStartup(e);
    }
}
