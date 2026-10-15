using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Widgets.Servicios;
using Windows.System;
using Windows.UI.Text;

namespace Widgets.Vistas;

// Lista de tareas (pendientes ordenadas por fecha, completadas a pedido) y editor en el mismo lugar.
public sealed partial class TareasView : UserControl
{
    private const double ZonaMinima = 164; // título, unas líneas de notas, fecha y botones
    private static readonly SolidColorBrush Rojo = App.Pincel(0xF28B82);

    private bool _verCompletadas;
    private Tarea? _editando;              // null en el editor: tarea nueva

    public TareasView()
    {
        InitializeComponent();
        TasksService.Actualizado += Mostrar;
        TasksService.Fallo += texto =>
        {
            // Con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea.
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            Flexible.Opacity = 0;
            Flexible.IsHitTestVisible = false;
        };
    }

    // ── Lista ──

    private void Mostrar(IReadOnlyList<Tarea> tareas)
    {
        // Vencidas primero, después por fecha; las sin fecha al final, en el orden de Tasks.
        var pendientes = tareas.Where(t => !t.Hecha).OrderBy(t => t.Vence ?? DateTime.MaxValue).ToList();
        var completadas = tareas.Where(t => t.Hecha).ToList();

        Lista.Children.Clear();
        foreach (var t in pendientes) Lista.Children.Add(Fila(t));
        if (_verCompletadas && completadas.Count > 0)
        {
            Lista.Children.Add(new TextBlock
            {
                Text = "Completadas", Style = (Style)Application.Current.Resources["Meta"], Margin = new Thickness(4, 10, 0, 2),
            });
            foreach (var t in completadas) Lista.Children.Add(Fila(t));
        }

        Contador.Text = pendientes.Count == 1 ? "1 pendiente" : $"{pendientes.Count} pendientes";
        EtiquetaContador.Visibility = pendientes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Mensaje.Text = "Nada pendiente";
        Mensaje.Visibility = pendientes.Count == 0 && !(_verCompletadas && completadas.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        Flexible.Opacity = 1;
        Flexible.IsHitTestVisible = true;
    }

    // Casilla para completar y, al lado, el título con su fecha; tocar el título abre el editor.
    private UIElement Fila(Tarea t)
    {
        var res = Application.Current.Resources;
        var casilla = new CheckBox { IsChecked = t.Hecha, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(casilla, (t.Hecha ? "Desmarcar " : "Completar ") + t.Titulo);
        casilla.Click += async (_, _) =>
        {
            // Si Google rechaza el cambio, la casilla vuelve a como estaba; si no, se relee (pasa de sección).
            if (await TasksService.MarcarAsync(t.Id, casilla.IsChecked == true)) await TasksService.RefrescarAsync();
            else casilla.IsChecked = t.Hecha;
        };

        var titulo = new TextBlock
        {
            Text = t.Titulo, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)res[t.Hecha ? "TextoSecundario" : "TextoPrimario"],
            TextDecorations = t.Hecha ? TextDecorations.Strikethrough : TextDecorations.None,
        };
        var contenido = new StackPanel { Spacing = 1, Children = { titulo } };
        if (t.Vence != null && !t.Hecha)
            contenido.Children.Add(new TextBlock
            {
                Text = t.Vencida ? $"venció {t.Cuando}" : t.Cuando, FontSize = 11,
                Foreground = t.Vencida ? Rojo : (Brush)res["TextoSecundario"],
            });

        // Botón (y no texto suelto) para que el clic abra el editor y no arranque el arrastre del bloque.
        var abrir = new Button
        {
            Content = contenido, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 4, 4, 4), CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(abrir, t.Titulo);
        abrir.Click += (_, _) => Editar(t);

        var fila = new Grid { ColumnSpacing = 4, ColumnDefinitions = { new() { Width = GridLength.Auto }, new() } };
        Grid.SetColumn(abrir, 1);
        fila.Children.Add(casilla);
        fila.Children.Add(abrir);
        return fila;
    }

    private void AlternarCompletadas(object sender, RoutedEventArgs e)
    {
        _verCompletadas = !_verCompletadas;
        var texto = _verCompletadas ? "Ocultar completadas" : "Mostrar completadas";
        ToolTipService.SetToolTip(BotonCompletadas, texto);
        AutomationProperties.SetName(BotonCompletadas, texto);
        IconoCompletadas.Foreground = (Brush)Application.Current.Resources[_verCompletadas ? "Acento" : "TextoSecundario"];
        Mostrar(TasksService.Tareas);
    }

    // ── Editor ──

    private void NuevaTarea(object sender, RoutedEventArgs e)
    {
        if (Editor.Visibility == Visibility.Visible && _editando == null) { Cancelar(sender, e); return; } // el "+" también cierra
        _editando = null;
        AbrirEditor("", "", null);
    }

    private void Editar(Tarea t)
    {
        _editando = t;
        AbrirEditor(t.Titulo, t.Notas, t.Vence);
    }

    private void AbrirEditor(string titulo, string notas, DateTime? vence)
    {
        // Mismo alto que tenía la lista: el widget no cambia de tamaño ni se reacomoda el bloque.
        if (Editor.Visibility != Visibility.Visible) Editor.Height = Math.Max(Flexible.ActualHeight, ZonaMinima);
        TituloTarea.Text = titulo;
        NotasTarea.Text = notas.Replace('\n', '\r'); // el TextBox separa líneas con \r
        FechaTarea.Date = vence;
        EstadoTarea.Text = "";
        Flexible.Visibility = Mensaje.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        TituloTarea.Focus(FocusState.Programmatic);
        TituloTarea.SelectionStart = TituloTarea.Text.Length;
    }

    private void Cancelar(object sender, RoutedEventArgs e)
    {
        _editando = null;
        Editor.Visibility = Visibility.Collapsed;
        Flexible.Visibility = Visibility.Visible;
        Mostrar(TasksService.Tareas);
    }

    private void SinFecha(object sender, RoutedEventArgs e) => FechaTarea.Date = null;

    // Enter en el título guarda; en las notas hace un salto de línea. Esc cancela.
    private void TeclaEditor(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; Cancelar(sender, e); }
        else if (e.Key == VirtualKey.Enter && ReferenceEquals(sender, TituloTarea)) { e.Handled = true; Guardar(sender, e); }
    }

    private async void Guardar(object sender, RoutedEventArgs e)
    {
        var titulo = TituloTarea.Text.Trim();
        if (titulo == "" || !GuardarTarea.IsEnabled) return;
        var notas = NotasTarea.Text.Trim().Replace('\r', '\n'); // Tasks separa líneas con \n
        var vence = FechaTarea.Date?.Date;

        // Sin cambios no se guarda.
        if (_editando is { } t && t.Titulo == titulo && t.Notas.Trim() == notas && t.Vence == vence) { Cancelar(sender, e); return; }

        GuardarTarea.IsEnabled = false;
        EstadoTarea.Text = "Guardando…";
        var error = await TasksService.GuardarAsync(_editando?.Id, titulo, notas, vence);
        GuardarTarea.IsEnabled = true;
        if (error != null) { EstadoTarea.Text = error; return; } // lo escrito queda, para reintentar
        Cancelar(sender, e);
    }
}
