using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using Widgets.Servicios;

namespace Widgets.Vistas;

public sealed partial class RelojView : UserControl
{
    private static readonly CultureInfo EsAR = new("es-AR");
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<Evento> _eventos = Array.Empty<Evento>();

    public RelojView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Actualizar();
        Actualizar();
        _timer.Start();
        CalendarService.Actualizado += eventos => { _eventos = eventos; Actualizar(); }; // trae todo el mes: se filtra hoy abajo
    }

    private void Actualizar()
    {
        var ahora = DateTime.Now;
        Hora.Text = ahora.ToString("HH:mm");
        Fecha.Text = ahora.ToString("dddd, d 'de' MMMM", EsAR);
        var proximo = _eventos.FirstOrDefault(e => !e.TodoElDia && e.Inicio > ahora && e.Inicio.Date == ahora.Date);
        Proximo.Text = proximo == null ? "Sin más eventos hoy" : $"Próximo: {proximo.Titulo} · {proximo.Hora}";
        // Despierta justo al cambiar el minuto, no cada segundo.
        _timer.Interval = TimeSpan.FromSeconds(60 - ahora.Second);
    }

    private void Acceso(object sender, RoutedEventArgs e) => App.Abrir((string)((Button)sender).Tag);

    // ── Nueva nota / nueva tarea sin salir del widget ──
    private string? _modo; // "nota" o "tarea" mientras el panel está abierto

    private void Capturar(object sender, RoutedEventArgs e)
    {
        var modo = (string)((Button)sender).Tag;
        if (_modo == modo) { CerrarCaptura(sender, e); return; }
        _modo = modo;
        Entrada.AcceptsReturn = modo == "nota";
        Entrada.PlaceholderText = modo == "nota"
            ? "Nueva nota: la primera línea es el título (Ctrl+Enter guarda)"
            : "Nueva tarea (Enter guarda)";
        EstadoCaptura.Text = "";
        MostrarCaptura(true);
        Entrada.Focus(FocusState.Programmatic);
    }

    // Cerrar no borra lo escrito: si se vuelve a abrir, sigue ahí.
    private void CerrarCaptura(object sender, RoutedEventArgs e)
    {
        _modo = null;
        MostrarCaptura(false);
    }

    private void MostrarCaptura(bool visible)
    {
        Captura.Opacity = visible ? 1 : 0;
        Captura.IsHitTestVisible = Entrada.IsTabStop = visible;
        VistaReloj.Opacity = visible ? 0 : 1;
        VistaReloj.IsHitTestVisible = !visible;
    }

    private void TeclaEntrada(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { CerrarCaptura(sender, e); e.Handled = true; return; }
        if (e.Key != VirtualKey.Enter) return;
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (_modo == "tarea" || ctrl) { e.Handled = true; Guardar(sender, e); }
    }

    private async void Guardar(object sender, RoutedEventArgs e)
    {
        var texto = Entrada.Text.Trim();
        if (_modo == null || texto == "" || !BotonGuardar.IsEnabled) return;
        BotonGuardar.IsEnabled = false;
        EstadoCaptura.Text = "Guardando…";

        string? error;
        if (_modo == "tarea")
            error = await TasksService.CrearAsync(texto);
        else
        {
            // Varias líneas: la primera es el título y el resto el texto. Una sola: es el texto, sin título.
            var partes = texto.Split('\r', 2);
            error = partes.Length == 2
                ? await KeepService.GuardarAsync(null, partes[0].Trim(), partes[1].Trim().Replace('\r', '\n')) // el TextBox usa \r; Keep, \n
                : await KeepService.GuardarAsync(null, "", texto);
        }

        BotonGuardar.IsEnabled = true;
        if (error != null) { EstadoCaptura.Text = error; return; } // lo escrito queda, para reintentar
        Entrada.Text = "";
        CerrarCaptura(sender, e);
    }

    private void AbrirAlarmas(object sender, RoutedEventArgs e) => App.Abrir("ms-clock:");
}
