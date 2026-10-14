using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Widgets.Servicios;
using Windows.UI.Text;

namespace Widgets.Vistas;

public sealed partial class TareasView : UserControl
{
    public TareasView()
    {
        InitializeComponent();
        TasksService.Actualizado += Mostrar;
        TasksService.Fallo += texto =>
        {
            // Con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea.
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            { Flexible.Opacity = 0; Flexible.IsHitTestVisible = false; }
        };
    }

    private void Mostrar(IReadOnlyList<Tarea> tareas)
    {
        Lista.Children.Clear();
        foreach (var t in tareas)
        {
            var casilla = new CheckBox { Content = new TextBlock { Text = t.Titulo, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis } };
            casilla.Click += async (_, _) =>
            {
                Pintar();
                // Si Google rechaza el cambio, la casilla vuelve a como estaba.
                if (!await TasksService.MarcarAsync(t.Id, casilla.IsChecked == true))
                {
                    casilla.IsChecked = !casilla.IsChecked;
                    Pintar();
                }
            };
            Lista.Children.Add(casilla);
        }
        Mensaje.Text = "Nada pendiente";
        Mensaje.Visibility = tareas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        { Flexible.Opacity = 1; Flexible.IsHitTestVisible = true; }
        Pintar();
    }

    // Las completadas quedan tachadas hasta el próximo refresco, donde desaparecen.
    private void Pintar()
    {
        var res = Application.Current.Resources;
        foreach (CheckBox c in Lista.Children)
        {
            var texto = (TextBlock)c.Content;
            bool hecha = c.IsChecked == true;
            texto.TextDecorations = hecha ? TextDecorations.Strikethrough : TextDecorations.None;
            texto.Foreground = (Brush)res[hecha ? "TextoSecundario" : "TextoPrimario"];
        }
        int pendientes = Lista.Children.Cast<CheckBox>().Count(c => c.IsChecked != true);
        Contador.Text = pendientes == 1 ? "1 pendiente" : $"{pendientes} pendientes";
        EtiquetaContador.Visibility = pendientes > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NuevaTarea(object sender, RoutedEventArgs e) => App.Abrir("https://tasks.google.com");
}
