using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Servicios;
using Windows.System;
using Windows.UI.Core;

namespace Widgets.Vistas;

// Tres modos en el mismo lugar: la lista de notas, la lectura de una nota y el editor (nueva o existente).
public sealed partial class NotasView : UserControl
{
    private enum Modo { Lista, Lectura, Edicion }

    private Modo _modo = Modo.Lista;
    private IReadOnlyList<Nota> _notas = Array.Empty<Nota>();
    private Nota? _abierta;                  // la nota en lectura (o la que se está editando)
    private Nota? _editando;                 // null en el editor: nota nueva
    private string _color = "DEFAULT";
    private readonly HashSet<string> _etiquetasElegidas = new();
    // Lo escrito en una nota nueva no se pierde al abrir otra nota o cancelar.
    private (string Texto, string Color, string[] Etiquetas) _borrador = ("", "DEFAULT", Array.Empty<string>());


    public NotasView()
    {
        InitializeComponent();
        KeepService.Actualizado += Mostrar;
        KeepService.Fallo += texto =>
        {
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            ConfigurarKeep.Visibility = KeepService.FaltaConfigurar ? Visibility.Visible : Visibility.Collapsed;
            Flexible.Opacity = 0; // con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea
            Flexible.IsHitTestVisible = false;
        };
    }

    // Lo mínimo que ocupan la lectura y el editor (colores, etiquetas y botones, más unas líneas de texto).
    private const double ZonaMinima = 200;

    private void IrA(Modo modo)
    {
        // La lectura y el editor ocupan exactamente el lugar que tenía la lista: el widget no cambia de alto
        // ni se reacomoda el bloque al pasar de un modo a otro.
        if (_modo == Modo.Lista && modo != Modo.Lista)
            Vista.Height = Editor.Height = Math.Max(Flexible.ActualHeight, ZonaMinima);
        _modo = modo;
        Aviso.Visibility = Flexible.Visibility = modo == Modo.Lista ? Visibility.Visible : Visibility.Collapsed;
        Vista.Visibility = modo == Modo.Lectura ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = modo == Modo.Edicion ? Visibility.Visible : Visibility.Collapsed;
        if (modo == Modo.Edicion)
        {
            TextoNota.Focus(FocusState.Programmatic);
            TextoNota.SelectionStart = TextoNota.Text.Length;
        }
    }

    // ── Lista ──

    private void Mostrar(IReadOnlyList<Nota> notas)
    {
        _notas = notas;
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
        Flexible.Opacity = 1;
        Flexible.IsHitTestVisible = true;

        // Si se está leyendo una nota, se muestra su versión nueva.
        if (_modo == Modo.Lectura && _abierta != null && notas.FirstOrDefault(n => n.Id == _abierta.Id) is { } actual)
            AbrirNota(actual);
    }

    // Etiquetas de Keep con el mismo estilo gris de la Agenda; si no entran en el ancho, se recortan.
    private static StackPanel Etiquetas(IReadOnlyList<string> etiquetas, Brush meta)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var e in etiquetas) fila.Children.Add(Chip(e, meta));
        fila.Visibility = etiquetas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return fila;
    }

    private static Border Chip(string etiqueta, Brush meta) => new()
    {
        Style = (Style)Application.Current.Resources["Etiqueta"],
        Child = new TextBlock { Text = etiqueta, FontSize = 11, Foreground = meta, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 },
    };

    // Abre una ventana de PowerShell con configurar_keep.py; al cerrarla, el widget vuelve a leer Keep.
    private async void Configurar(object sender, RoutedEventArgs e)
    {
        ConfigurarKeep.IsEnabled = false;
        Mensaje.Text = "Seguí los pasos en la ventana de PowerShell…";
        await KeepService.ConfigurarAsync();
        ConfigurarKeep.IsEnabled = true;
    }

    // ── Lectura ──

    private void AbrirNota(Nota n)
    {
        _abierta = n;
        VistaColor.Fill = n.Color;
        VistaTitulo.Text = n.Titulo != "" ? n.Titulo : "Sin título";
        VistaTexto.Text = n.Texto;
        VistaEtiquetas.Children.Clear();
        foreach (var e in n.Etiquetas) VistaEtiquetas.Children.Add(Chip(e, (Brush)Application.Current.Resources["TextoSecundario"]));
        // Las listas con casillas no son texto plano: editarlas como texto las rompería. Se editan en Keep.
        BotonEditar.Visibility = n.Lista ? Visibility.Collapsed : Visibility.Visible;
        IrA(Modo.Lectura);
    }

    private void VolverALista(object sender, RoutedEventArgs e)
    {
        _abierta = null;
        IrA(Modo.Lista);
    }

    private void AbrirEnNavegador(object sender, RoutedEventArgs e)
    {
        if (_abierta != null) App.Abrir(_abierta.Link);
    }

    private void EditarConDobleClic(object sender, DoubleTappedRoutedEventArgs e) => Editar(sender, e);

    // ── Edición ──

    private void Editar(object sender, RoutedEventArgs e)
    {
        if (_abierta == null || _abierta.Lista) return;
        if (_editando == null) GuardarBorrador();
        _editando = _abierta;
        CargarEditor(_abierta.Titulo != "" ? _abierta.Titulo + "\r" + _abierta.Texto : _abierta.Texto, _abierta.ColorKeep, _abierta.Etiquetas);
        IrA(Modo.Edicion);
    }

    private void NuevaNota(object sender, RoutedEventArgs e)
    {
        if (_modo == Modo.Edicion && _editando == null) { Cancelar(sender, e); return; } // el "+" también cierra
        _editando = null;
        CargarEditor(_borrador.Texto, _borrador.Color, _borrador.Etiquetas);
        IrA(Modo.Edicion);
    }

    private void CargarEditor(string texto, string color, IEnumerable<string> etiquetas)
    {
        TextoNota.Text = texto;
        _color = color;
        _etiquetasElegidas.Clear();
        _etiquetasElegidas.UnionWith(etiquetas);
        EstadoNota.Text = "";
        PintarColores();
        PintarEtiquetas();
    }

    private void GuardarBorrador() => _borrador = (TextoNota.Text, _color, _etiquetasElegidas.ToArray());

    // Cancelar: de una nota existente vuelve a su lectura; de una nueva, a la lista (el borrador queda guardado).
    private void Cancelar(object sender, RoutedEventArgs e)
    {
        if (_editando == null) { GuardarBorrador(); IrA(Modo.Lista); }
        else { _editando = null; IrA(Modo.Lectura); }
    }

    private void PintarColores()
    {
        Colores.Children.Clear();
        foreach (var c in KeepService.Paleta)
        {
            var boton = new Button
            {
                Width = 22, Height = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(11), MinWidth = 0,
                Background = c == "DEFAULT" ? (Brush)Application.Current.Resources["FondoEtiqueta"] : KeepService.Color(c),
                BorderBrush = (Brush)Application.Current.Resources["TextoPrimario"],
                BorderThickness = new Thickness(c == _color ? 2 : 0),
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
        if (e.Key == VirtualKey.Escape) { Cancelar(sender, e); e.Handled = true; return; }
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl) { e.Handled = true; Guardar(sender, e); }
    }

    // Guardado optimista: la nota se ve cambiada al instante y Keep se actualiza por detrás (unos segundos).
    // Si falla, vuelve el editor con lo escrito y el error, y la lista vuelve a como estaba.
    private async void Guardar(object sender, RoutedEventArgs e)
    {
        var escrito = TextoNota.Text;
        var texto = escrito.Trim();
        if (texto == "") return;

        // Varias líneas: la primera es el título y el resto el texto. Una sola: es el texto, sin título.
        var partes = texto.Split('\r', 2);
        var (titulo, cuerpo) = partes.Length == 2 ? (partes[0].Trim(), partes[1].Trim()) : ("", texto);
        cuerpo = cuerpo.Replace('\r', '\n'); // el TextBox separa líneas con \r; Keep, con \n

        var original = _editando;
        var (color, etiquetas) = (_color, _etiquetasElegidas.ToArray());
        var anteriores = _notas;
        var nueva = new Nota(original?.Id ?? "nueva", titulo, cuerpo, color, original?.Fijada ?? false, etiquetas);
        Mostrar(original == null ? anteriores.Prepend(nueva).ToList() : anteriores.Select(n => n.Id == original.Id ? nueva : n).ToList());
        _editando = null;
        if (original == null) { _borrador = ("", "DEFAULT", Array.Empty<string>()); IrA(Modo.Lista); }
        else AbrirNota(nueva);

        EstadoGuardado.Text = "Guardando…";
        var error = await KeepService.GuardarAsync(original?.Id, titulo, cuerpo, color, etiquetas);
        if (error == null)
        {
            EstadoGuardado.Text = "Guardado";
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (EstadoGuardado.Text == "Guardado") EstadoGuardado.Text = "";
            return;
        }

        EstadoGuardado.Text = "";
        Mostrar(anteriores);
        _abierta = original;
        _editando = original;
        CargarEditor(escrito, color, etiquetas);
        EstadoNota.Text = error;
        IrA(Modo.Edicion);
    }
}
