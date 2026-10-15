using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
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
        KeepService.Fallo += texto =>
        {
            // Con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea.
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            ConfigurarKeep.Visibility = KeepService.FaltaConfigurar ? Visibility.Visible : Visibility.Collapsed;
            if (!Editor.IsHitTestVisible) { Flexible.Opacity = 0; Flexible.IsHitTestVisible = false; }
        };
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
            nota.Click += (_, _) => AbrirNota(n);
            Grid.SetRow(nota, i / 2);
            Grid.SetColumn(nota, i % 2);
            Mosaico.Children.Add(nota);
        }
        ConfigurarKeep.Visibility = Visibility.Collapsed;
        Mensaje.Text = "Sin notas";
        Mensaje.Visibility = notas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!Editor.IsHitTestVisible) { Flexible.Opacity = 1; Flexible.IsHitTestVisible = true; }
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

    // ── Crear, leer y editar notas sin salir del widget: texto, color y etiquetas ──
    private Nota? _editando;                 // null: nota nueva
    private string _color = "DEFAULT";
    private readonly HashSet<string> _etiquetasElegidas = new();
    private string _borradorNueva = "";      // lo escrito en una nota nueva no se pierde al abrir otra

    // Abre una ventana de PowerShell con configurar_keep.py; al cerrarla, el widget vuelve a leer Keep.
    private async void Configurar(object sender, RoutedEventArgs e)
    {
        ConfigurarKeep.IsEnabled = false;
        Mensaje.Text = "Seguí los pasos en la ventana de PowerShell…";
        await KeepService.ConfigurarAsync();
        ConfigurarKeep.IsEnabled = true;
    }

    private void NuevaNota(object sender, RoutedEventArgs e)
    {
        if (Editor.IsHitTestVisible && _editando == null) { CerrarEditor(sender, e); return; }
        if (_editando != null) { _editando = null; _color = "DEFAULT"; _etiquetasElegidas.Clear(); }
        TextoNota.Text = _borradorNueva;
        AbrirEditor();
    }

    private void AbrirNota(Nota n)
    {
        if (_editando == null) _borradorNueva = TextoNota.Text;
        _editando = n;
        TextoNota.Text = n.Titulo != "" ? n.Titulo + "\r" + n.Texto : n.Texto;
        _color = n.ColorKeep;
        _etiquetasElegidas.Clear();
        _etiquetasElegidas.UnionWith(n.Etiquetas);
        AbrirEditor();
    }

    private void AbrirEditor()
    {
        bool soloLectura = _editando?.Lista == true;
        TextoNota.IsReadOnly = soloLectura;
        GuardarNota.IsEnabled = !soloLectura;
        Colores.IsHitTestVisible = EtiquetasEditor.IsHitTestVisible = !soloLectura;
        AbrirEnKeep.Visibility = _editando != null ? Visibility.Visible : Visibility.Collapsed;
        EstadoNota.Text = soloLectura ? "Lista con casillas: se edita en Keep" : "";
        PintarColores();
        PintarEtiquetas();
        MostrarEditor(true);
        TextoNota.Focus(FocusState.Programmatic);
        TextoNota.SelectionStart = TextoNota.Text.Length;
    }

    private void AbrirEnNavegador(object sender, RoutedEventArgs e)
    {
        if (_editando != null) App.Abrir(_editando.Link);
    }

    // Cerrar no borra lo escrito ni lo elegido: si se vuelve a abrir, sigue ahí.
    private void CerrarEditor(object sender, RoutedEventArgs e) => MostrarEditor(false);

    private void MostrarEditor(bool visible)
    {
        Editor.Opacity = visible ? 1 : 0;
        Editor.IsHitTestVisible = TextoNota.IsTabStop = visible;
        Flexible.Opacity = Aviso.Opacity = visible ? 0 : 1;
        Aviso.IsHitTestVisible = !visible;
        Flexible.IsHitTestVisible = !visible;
    }

    private void PintarColores()
    {
        Colores.Children.Clear();
        foreach (var c in KeepService.Paleta)
        {
            bool elegido = c == _color;
            var boton = new Button
            {
                Width = 22, Height = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(11), MinWidth = 0,
                Background = c == "DEFAULT" ? (Brush)Application.Current.Resources["FondoEtiqueta"] : KeepService.Color(c),
                BorderBrush = (Brush)Application.Current.Resources["TextoPrimario"],
                BorderThickness = new Thickness(elegido ? 2 : 0),
            };
            AutomationProperties.SetName(boton, c == "DEFAULT" ? "Sin color" : $"Color {c.ToLowerInvariant()}");
            boton.Click += (_, _) => { _color = c; PintarColores(); };
            Colores.Children.Add(boton);
        }
    }

    private void PintarEtiquetas()
    {
        EtiquetasEditor.Children.Clear();
        _etiquetasElegidas.IntersectWith(KeepService.Etiquetas); // por si alguna se borró en Keep
        foreach (var nombre in KeepService.Etiquetas)
        {
            var chip = new ToggleButton
            {
                Content = nombre, IsChecked = _etiquetasElegidas.Contains(nombre), FontSize = 11,
                Height = 26, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(6), MinWidth = 0,
            };
            chip.Click += (_, _) => { if (chip.IsChecked == true) _etiquetasElegidas.Add(nombre); else _etiquetasElegidas.Remove(nombre); };
            EtiquetasEditor.Children.Add(chip);
        }
    }

    private void TeclaNota(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { CerrarEditor(sender, e); e.Handled = true; return; }
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl) { e.Handled = true; Guardar(sender, e); }
    }

    private async void Guardar(object sender, RoutedEventArgs e)
    {
        var texto = TextoNota.Text.Trim();
        if (texto == "" || !GuardarNota.IsEnabled || _editando?.Lista == true) return;
        GuardarNota.IsEnabled = false;
        EstadoNota.Text = "Guardando…";

        // Varias líneas: la primera es el título y el resto el texto. Una sola: es el texto, sin título.
        var partes = texto.Split('\r', 2);
        var (titulo, cuerpo) = partes.Length == 2 ? (partes[0].Trim(), partes[1].Trim()) : ("", texto);
        cuerpo = cuerpo.Replace('\r', '\n'); // el TextBox separa líneas con \r; Keep, con \n
        var error = await KeepService.GuardarAsync(_editando?.Id, titulo, cuerpo, _color, _etiquetasElegidas.ToList());

        GuardarNota.IsEnabled = true;
        if (error != null) { EstadoNota.Text = error; return; } // lo escrito queda, para reintentar
        if (_editando == null) _borradorNueva = "";
        TextoNota.Text = "";
        _editando = null;
        _color = "DEFAULT";
        _etiquetasElegidas.Clear();
        MostrarEditor(false);
    }
}
