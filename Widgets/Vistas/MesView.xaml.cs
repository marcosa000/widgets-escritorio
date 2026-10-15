using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Servicios;

namespace Widgets.Vistas;

public sealed partial class MesView : UserControl
{
    private static readonly CultureInfo EsAR = new("es-AR");
    private const int MaxPuntos = 3;

    public MesView()
    {
        InitializeComponent();
        CalendarService.Actualizado += _ => Pintar();
        CalendarService.DiaCambiado += Pintar;
        TasksService.Actualizado += _ => Pintar();
        Pintar();
    }

    private void Pintar()
    {
        var mes = CalendarService.Mes;
        var titulo = mes.ToString("MMMM yyyy", EsAR);
        Titulo.Text = char.ToUpper(titulo[0]) + titulo[1..];

        Dias.Children.Clear();
        Dias.RowDefinitions.Clear();
        int desfase = ((int)mes.DayOfWeek + 6) % 7; // lunes = 0
        int cantidad = DateTime.DaysInMonth(mes.Year, mes.Month);
        int filas = 1 + (desfase + cantidad + 6) / 7;
        for (int i = 0; i < filas; i++) Dias.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var res = Application.Current.Resources;
        var letras = "LMMJVSD";
        for (int c = 0; c < 7; c++)
            Agregar(new TextBlock
            {
                Text = letras[c].ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)res["TextoSecundario"], Margin = new Thickness(0, 0, 0, 4),
            }, 0, c);

        for (int d = 1; d <= cantidad; d++)
        {
            var dia = mes.AddDays(d - 1);
            int i = desfase + d - 1;
            Agregar(Dia(dia, res), 1 + i / 7, i % 7);
        }
    }

    private Button Dia(DateTime dia, ResourceDictionary res)
    {
        bool hoy = dia == DateTime.Today;
        bool elegido = dia == CalendarService.DiaElegido;
        var eventos = CalendarService.Eventos.Where(e => e.Ocurre(dia)).ToList();
        var tareas = TasksService.Tareas.Count(t => t.Vence == dia && !t.Hecha);

        // Un punto por evento (color de su calendario) y uno lavanda por tarea, hasta 3; el total exacto va en el tooltip.
        var colores = eventos.Select(e => (Brush)e.Color).Concat(Enumerable.Repeat((Brush)res["Acento"], tareas)).Take(MaxPuntos);
        var puntos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Height = 4 };
        foreach (var color in colores) puntos.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = color });

        var boton = new Button
        {
            Width = 40, Height = 44, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Border
                    {
                        Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                        Background = hoy ? (Brush)res["Acento"] : null,
                        // El día elegido (si no es hoy) lleva un aro del color de acento.
                        BorderBrush = (Brush)res["Acento"], BorderThickness = new Thickness(elegido && !hoy ? 1.5 : 0),
                        Child = new TextBlock
                        {
                            Text = dia.Day.ToString(), FontSize = 13,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                            FontWeight = hoy ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = (Brush)res[hoy ? "SobreAcento" : "TextoPrimario"],
                        },
                    },
                    puntos,
                },
            },
        };
        if (eventos.Count + tareas > 0)
            ToolTipService.SetToolTip(boton, string.Join(", ", new[]
            {
                eventos.Count switch { 0 => null, 1 => "1 evento", var n => $"{n} eventos" },
                tareas switch { 0 => null, 1 => "1 tarea", var n => $"{n} tareas" },
            }.Where(t => t != null)));
        boton.Click += (_, _) => CalendarService.ElegirDia(dia);
        return boton;
    }

    private void Agregar(FrameworkElement e, int fila, int columna)
    {
        Grid.SetRow(e, fila);
        Grid.SetColumn(e, columna);
        Dias.Children.Add(e);
    }

    private void Anterior(object sender, RoutedEventArgs e) => IrAlMes(-1);
    private void Siguiente(object sender, RoutedEventArgs e) => IrAlMes(1);

    // Pinta el mes nuevo enseguida (sin puntos) y otra vez cuando llegan sus eventos.
    private async void IrAlMes(int delta)
    {
        var pedido = CalendarService.CambiarMesAsync(CalendarService.Mes.AddMonths(delta));
        Pintar();
        await pedido;
    }
    private void NuevoEvento(object sender, RoutedEventArgs e) => App.Abrir("https://calendar.google.com/calendar/r/eventedit");
}
