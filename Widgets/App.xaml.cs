using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Widgets.Vistas;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Widgets;

public partial class App : Application
{
    private readonly List<WidgetWindow> _ventanas = new();
    private PointInt32 _origen;                     // esquina superior izquierda visible del bloque
    private int _yElegida;                          // altura donde el usuario dejó el bloque (antes de ajustarlo a la pantalla)
    private bool _ubicado;
    private readonly DispatcherTimer _refresco = new() { Interval = TimeSpan.FromMinutes(5) };
    private (bool Derecha, int Y, int Alto)[] _lugares = Array.Empty<(bool, int, int)>();
    private int _reparto;                           // bit i = 1: el widget i va a la derecha
    private bool _repartoFijo;                      // se fija cuando el usuario toca un borde: nada salta de columna
    private readonly int[] _anchos = new int[2];    // ancho visible (px) de cada columna
    private int?[] _altosUsuario = Array.Empty<int?>(); // alto visible (px) que eligió el usuario, por widget
    private readonly DispatcherTimer _finArrastre = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private WidgetWindow? _arrastrando;             // ventana cuyo borde se está estirando ahora

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        FrameworkElement[] vistas = { new RelojView(), new AgendaView(), new TareasView(), new MesView(), new NotasView(), new DriveView() };
        _altosUsuario = new int?[vistas.Length];
        // Mientras se estira un borde no se toca esa ventana (si no, "pelea" con Windows y parpadea);
        // al soltar, se acomoda todo.
        _finArrastre.Tick += (_, _) => { _finArrastre.Stop(); _arrastrando = null; Posicionar(); };
        foreach (var v in vistas)
        {
            var w = new WidgetWindow(v);
            w.AltoCambiado += Acomodar;
            w.Arrastrado += (dx, dy) =>
            {
                _yElegida += dy;
                _origen = new PointInt32(_origen.X + dx, _yElegida);
                Posicionar();
            };
            w.BordeArrastrado += r => CambiarTamano(w, r);
            _ventanas.Add(w);
            w.Activate();
        }
        // Después de crear las vistas, que ya se suscribieron a los servicios.
        _refresco.Tick += (_, _) => RefrescarTodo();
        _refresco.Start();
        RefrescarTodo();
    }

    // Dos columnas que terminan a la misma altura: se elige el reparto con menor diferencia (hasta que el usuario
    // toca un borde, desde ahí queda fijo); la columna más corta estira sus widgets para cubrirla.
    // 16 px parejos entre bordes visibles.
    private void Acomodar() => Acomodar(_arrastrando);

    private void Acomodar(WidgetWindow? arrastrando)
    {
        if (!_ventanas.All(w => w.Medido)) return;
        int n = _ventanas.Count, espacio = (int)(16 * _ventanas[0].Escala);
        // Cada widget mide lo que eligió el usuario, pero nunca menos que su mínimo.
        var altos = _ventanas.Select((w, i) => Math.Max(w.AltoNatural, _altosUsuario[i] ?? 0)).ToArray();

        int Suma(int mascara, bool derecha)
        {
            int total = 0, cuantos = 0;
            for (int i = 0; i < n; i++)
                if (((mascara >> i & 1) == 1) == derecha) { total += altos[i]; cuantos++; }
            return total + Math.Max(0, cuantos - 1) * espacio;
        }

        // 6 widgets = 32 repartos posibles (el reloj fijo a la izquierda); probarlos todos es instantáneo.
        if (!_repartoFijo)
        {
            int m0 = 0;
            for (int m = 0; m < 1 << n; m += 2)
                if (Math.Abs(Suma(m, false) - Suma(m, true)) < Math.Abs(Suma(m0, false) - Suma(m0, true))) m0 = m;
            _reparto = m0;
        }
        int mejor = _reparto;

        // Si una columna no entra en pantalla, los widgets con lista se achican (en proporción a lo que
        // pueden ceder) hasta su piso; sus listas muestran menos filas y el resto se ve con la rueda.
        var area = DisplayArea.Primary.WorkArea;
        int maximo = area.Height - 2 * espacio;
        foreach (bool derecha in new[] { false, true })
        {
            int exceso = Suma(mejor, derecha) - maximo;
            var cedibles = Enumerable.Range(0, n)
                .Where(i => ((mejor >> i & 1) == 1) == derecha && altos[i] > _ventanas[i].AltoPiso).ToList();
            int holgura = cedibles.Sum(i => altos[i] - _ventanas[i].AltoPiso);
            if (exceso <= 0 || holgura == 0) continue;
            foreach (int i in cedibles)
            {
                int puede = altos[i] - _ventanas[i].AltoPiso;
                altos[i] -= Math.Min(puede, (int)Math.Ceiling((double)exceso * puede / holgura));
            }
        }

        int alto = Math.Max(Suma(mejor, false), Suma(mejor, true));

        // shortcut: el bloque arranca siempre arriba a la derecha; recordar dónde lo dejaste va con los ajustes del paso 6.
        if (!_ubicado)
        {
            _anchos[0] = _anchos[1] = _ventanas[0].Recuadro.Width;
            int anchoBloque = _anchos[0] + _anchos[1] + espacio;
            _yElegida = area.Y + espacio;
            _origen = new PointInt32(area.X + area.Width - anchoBloque - espacio, _yElegida);
            _ubicado = true;
        }
        // El bloque no pasa el borde de abajo (la barra de tareas); si es más alto que la pantalla, arranca arriba.
        // Se parte siempre de la altura elegida: si el bloque creció un momento (por ejemplo, mientras cargan los
        // datos), vuelve a su lugar cuando se achica.
        _origen = new PointInt32(_origen.X, Math.Max(area.Y, Math.Min(_yElegida, area.Y + area.Height - espacio - alto)));
        _lugares = new (bool, int, int)[n];
        foreach (bool derecha in new[] { false, true })
        {
            var indices = Enumerable.Range(0, n).Where(i => ((mejor >> i & 1) == 1) == derecha).ToList();
            int sobra = alto - Suma(mejor, derecha), y = 0;
            // El sobrante va a los widgets cuyo alto no eligió el usuario (si todos lo eligieron, al último),
            // parejo y con el resto de la división al último de ellos.
            var reciben = indices.Where(i => _altosUsuario[i] == null).DefaultIfEmpty(indices[^1]).ToList();
            for (int k = 0; k < indices.Count; k++)
            {
                int i = indices[k], r = reciben.IndexOf(i);
                int extra = r < 0 ? 0 : sobra / reciben.Count + (r == reciben.Count - 1 ? sobra % reciben.Count : 0);
                _lugares[i] = (derecha, y, altos[i] + extra);
                y += altos[i] + extra + espacio;
            }
        }
        Posicionar(arrastrando);
    }

    private void Posicionar(WidgetWindow? salvo = null)
    {
        if (_lugares.Length != _ventanas.Count) return;
        int espacio = (int)(16 * _ventanas[0].Escala);
        for (int i = 0; i < _ventanas.Count; i++)
        {
            if (_ventanas[i] == salvo) continue;
            var (derecha, y, alto) = _lugares[i];
            _ventanas[i].Ubicar(_origen.X + (derecha ? _anchos[0] + espacio : 0), _origen.Y + y, derecha ? _anchos[1] : _anchos[0], alto);
        }
    }

    // El usuario estiró un borde de una ventana.
    // Ancho: pasa a toda su columna. Alto: es de ese widget (nunca menos que su mínimo); los de abajo se corren
    // y la otra columna se estira para seguir terminando a la misma altura, sin que el bloque pase la pantalla.
    private void CambiarTamano(WidgetWindow w, RectInt32 r)
    {
        int i = _ventanas.IndexOf(w);
        if (_lugares.Length != _ventanas.Count) return;
        int col = _lugares[i].Derecha ? 1 : 0, espacio = (int)(16 * w.Escala);

        if (r.Width != _anchos[col])
        {
            int ancho = Math.Clamp(r.Width, (int)(300 * w.Escala), (int)(640 * w.Escala));
            // Si se estiró el borde izquierdo, el derecho queda fijo; si se estiró el derecho, el izquierdo queda fijo.
            int x = r.X == ColumnaX(col) ? r.X : r.X + r.Width - ancho;
            _anchos[col] = ancho;
            _origen = new PointInt32(col == 0 ? x : x - espacio - _anchos[0], _origen.Y);
        }
        if (r.Height != _lugares[i].Alto)
        {
            // Tope: lo que deja libre el resto de la columna hasta el borde de abajo del área de trabajo.
            var area = DisplayArea.Primary.WorkArea;
            int resto = Enumerable.Range(0, _ventanas.Count)
                .Where(j => j != i && _lugares[j].Derecha == _lugares[i].Derecha)
                .Sum(j => Math.Max(_ventanas[j].AltoNatural, _altosUsuario[j] ?? 0) + espacio);
            int tope = area.Y + area.Height - espacio - _origen.Y - resto;
            _altosUsuario[i] = Math.Clamp(r.Height, w.AltoNatural, Math.Max(w.AltoNatural, tope));
        }
        _arrastrando = w;
        _repartoFijo = true;
        Acomodar(w);
        _finArrastre.Stop();
        _finArrastre.Start();
    }

    private int ColumnaX(int col) => _origen.X + (col == 1 ? _anchos[0] + (int)(16 * _ventanas[0].Escala) : 0);

    public static void RefrescarTodo()
    {
        if (Demo.Activo) { Demo.Cargar(); return; }
        _ = Servicios.CalendarService.RefrescarAsync();
        _ = Servicios.TasksService.RefrescarAsync();
        _ = Servicios.DriveService.RefrescarAsync();
        _ = Servicios.KeepService.RefrescarAsync();
    }

    public static void Abrir(string uri) => _ = Launcher.LaunchUriAsync(new Uri(uri));

    public static SolidColorBrush Pincel(uint rgb) =>
        new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
