using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Widgets.Servicios;

namespace Widgets.Vistas;

public sealed partial class AgendaView : UserControl
{
    public AgendaView()
    {
        InitializeComponent();
        CalendarService.Actualizado += _ => Mostrar();
        CalendarService.DiaCambiado += Mostrar;
        CalendarService.Fallo += texto =>
        {
            // Con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea.
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            { Flexible.Opacity = 0; Flexible.IsHitTestVisible = false; }
        };
    }

    // Muestra el día elegido en el calendario mensual (hoy, si no se eligió otro).
    private void Mostrar()
    {
        var dia = CalendarService.DiaElegido;
        // Los que ya terminaron van al final, en gris.
        var eventos = CalendarService.Eventos.Where(e => e.Ocurre(dia)).OrderBy(e => e.Paso).ToList();
        int dif = (dia - DateTime.Today).Days;
        var titulo = dif switch { 0 => "Hoy", 1 => "Mañana", -1 => "Ayer", _ => dia.ToString("ddd d MMM", new CultureInfo("es-AR")) };
        Encabezado.Text = char.ToUpper(titulo[0]) + titulo[1..];

        ListaEventos.ItemsSource = eventos;
        Cantidad.Text = eventos.Count == 1 ? "1 evento" : $"{eventos.Count} eventos";
        EtiquetaCantidad.Visibility = eventos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Mensaje.Text = dif == 0 ? "Nada más por hoy" : "Sin eventos";
        Mensaje.Visibility = eventos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        { Flexible.Opacity = 1; Flexible.IsHitTestVisible = true; }
    }

    private void Refrescar(object sender, RoutedEventArgs e) => App.RefrescarTodo();

    // Crear eventos desde el widget necesita permiso de escritura; por ahora abre el formulario web.
    private void NuevoEvento(object sender, RoutedEventArgs e) => App.Abrir("https://calendar.google.com/calendar/r/eventedit");
}
