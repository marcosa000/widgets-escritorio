using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Servicios;

namespace Widgets.Vistas;

public sealed partial class NotasView : UserControl
{
    public NotasView()
    {
        InitializeComponent();
        KeepService.Actualizado += Mostrar;
        KeepService.Fallo += texto => { Mensaje.Text = texto; Mensaje.Visibility = Visibility.Visible; };
    }

    private void Mostrar(IReadOnlyList<Nota> notas)
    {
        var texto = (Brush)Application.Current.Resources["TextoPrimario"];
        var meta = (Brush)Application.Current.Resources["TextoSecundario"];
        Mosaico.Children.Clear();
        Mosaico.RowDefinitions.Clear();
        for (int i = 0; i < notas.Count; i++)
        {
            var n = notas[i];
            if (i % 2 == 0) Mosaico.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var encabezado = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    // Mismo criterio que las etiquetas: fondo neutro, el color de Keep solo en el punto.
                    new Ellipse { Width = 6, Height = 6, Fill = n.Color, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = n.Titulo != "" ? n.Titulo : "Sin título", FontSize = 13, FontWeight = FontWeights.SemiBold,
                                    Foreground = texto, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            if (n.Fijada) encabezado.Children.Add(new FontIcon { Glyph = "", FontSize = 10, Foreground = meta });
            // Botón (y no Border) para que el clic abra la nota y no arranque el arrastre del bloque.
            var nota = new Button
            {
                Background = (Brush)Application.Current.Resources["FondoEtiqueta"], BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(16), Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        encabezado,
                        new TextBlock { Text = n.Texto, FontSize = 12, Foreground = meta, TextWrapping = TextWrapping.Wrap,
                                        MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis },
                        Etiquetas(n.Etiquetas, meta),
                    },
                },
            };
            AutomationProperties.SetName(nota, n.Titulo != "" ? n.Titulo : n.Texto);
            nota.Click += (_, _) => App.Abrir(n.Link);
            Grid.SetRow(nota, i / 2);
            Grid.SetColumn(nota, i % 2);
            Mosaico.Children.Add(nota);
        }
        Mensaje.Text = "Sin notas";
        Mensaje.Visibility = notas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Etiquetas de Keep con el mismo estilo gris de la Agenda; si no entran en el ancho, se recortan.
    private static UIElement Etiquetas(IReadOnlyList<string> etiquetas, Brush meta)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var e in etiquetas)
            fila.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["Etiqueta"],
                Child = new TextBlock { Text = e, FontSize = 11, Foreground = meta, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 },
            });
        fila.Visibility = etiquetas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return fila;
    }

    private void NuevaNota(object sender, RoutedEventArgs e) => App.Abrir("https://keep.new");
}
