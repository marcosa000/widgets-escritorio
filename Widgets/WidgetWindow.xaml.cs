using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinRT.Interop;

namespace Widgets;

public sealed partial class WidgetWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly DesktopAcrylicController _acrilico = new();
    private double _natural, _piso;
    private POINT? _ultimoCursor;

    public readonly double Escala;
    public event Action? AltoCambiado;
    public event Action<int, int>? Arrastrado;
    public event Action<RectInt32>? BordeArrastrado;
    // Último tamaño de ventana que pidió App y cuánto miden los bordes invisibles (para pasar a recuadro visible).
    private SizeInt32 _tamPedido;
    private RectInt32 _bordes;

    public WidgetWindow(FrameworkElement contenido)
    {
        InitializeComponent();
        // Medidor calcula el alto mínimo del contenido; App le da a la ventana al menos ese alto.
        Raiz.Children.Add(new Medidor(contenido, (natural, piso) =>
            // Fuera de la pasada de layout: App mueve y redimensiona ventanas.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (Math.Abs(natural - _natural) < 0.5 && Math.Abs(piso - _piso) < 0.5) return;
                (_natural, _piso) = (natural, piso);
                AltoCambiado?.Invoke();
            })));
        _hwnd = WindowNative.GetWindowHandle(this);

        // Sin barra de título, fuera de Alt+Tab y de la barra de tareas.
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.IsShownInSwitchers = false;

        // Ancho fijo: tarjeta de 360 + 12 de margen por lado. El alto lo pone App.
        Escala = GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)(384 * Escala), (int)(200 * Escala)));

        // Acrílico manual con IsInputActive fijo: el widget nunca tiene foco y si no se vería el color liso de reserva.
        _acrilico.AddSystemBackdropTarget(WinRT.CastExtensions.As<ICompositionSupportsSystemBackdrop>(this));
        _acrilico.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark });
        Closed += (_, _) => _acrilico.Dispose();

        // Un tamaño distinto del que pidió App vino del usuario estirando un borde. Se compara con AppWindow.Size
        // (exacto al instante) y no con Recuadro, que DWM actualiza un momento después y daría falsos arrastres.
        AppWindow.Changed += (_, a) =>
        {
            var t = AppWindow.Size;
            if (!a.DidSizeChange || _tamPedido.Width == 0 || (t.Width == _tamPedido.Width && t.Height == _tamPedido.Height)) return;
            var p = AppWindow.Position;
            BordeArrastrado?.Invoke(new RectInt32(p.X + _bordes.X, p.Y + _bordes.Y, t.Width - _bordes.Width, t.Height - _bordes.Height));
        };

        // Siempre detrás: se reaplica cada vez que la ventana gana foco.
        Activated += (_, _) => AlFondo();
    }

    // Rectángulo que se ve en pantalla; AppWindow.Size incluye bordes invisibles de redimensión que no cuentan.
    public RectInt32 Recuadro
    {
        get
        {
            DwmGetWindowAttribute(_hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>());
            return new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    public bool Medido => _natural > 0;

    // Alto visible (px) mínimo: el contenido entero, con las listas largas topeadas en ~5 filas.
    public int AltoNatural => APixeles(_natural);

    // Lo menos que puede medir si el bloque no entra en pantalla: su lista (si tiene) con lugar para 1-2 filas.
    public int AltoPiso => APixeles(_piso);

    private int APixeles(double contenido) => (int)Math.Ceiling((contenido + 24) * Escala) + (Recuadro.Height - AppWindow.ClientSize.Height);

    public void Ubicar(int x, int y, int anchoVisible, int altoVisible)
    {
        var v = Recuadro;
        var p = AppWindow.Position;
        // X, Y: borde invisible izquierdo y superior; Width, Height: lo que suman los bordes invisibles.
        _bordes = new RectInt32(v.X - p.X, v.Y - p.Y, AppWindow.Size.Width - v.Width, AppWindow.Size.Height - v.Height);
        _tamPedido = new SizeInt32(anchoVisible + _bordes.Width, altoVisible + _bordes.Height);
        AppWindow.MoveAndResize(new RectInt32(x - _bordes.X, y - _bordes.Y, _tamPedido.Width, _tamPedido.Height));
    }

    // Arrastre de todo el bloque: cada ventana informa cuánto se movió el cursor y App mueve las seis.
    private void Presionar(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        Raiz.CapturePointer(e.Pointer);
        GetCursorPos(out var p);
        _ultimoCursor = p;
    }

    private void Mover(object sender, PointerRoutedEventArgs e)
    {
        if (_ultimoCursor is not { } u) return;
        GetCursorPos(out var p);
        _ultimoCursor = p;
        Arrastrado?.Invoke(p.X - u.X, p.Y - u.Y);
    }

    private void Soltar(object sender, PointerRoutedEventArgs e)
    {
        _ultimoCursor = null;
        Raiz.ReleasePointerCaptures();
        AlFondo();
    }

    private void AlFondo() =>
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private void Cerrar(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private struct RECT { public int Left, Top, Right, Bottom; }
    private struct POINT { public int X, Y; }
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private static readonly IntPtr HWND_BOTTOM = 1;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
}

// Mide el contenido dos veces por pasada:
// 1) sin límite de alto, para saber cuánto ocupa de verdad (si no, WinUI recorta la medida al tamaño actual
//    de la ventana y nunca se entera de que el contenido creció). La lista "Flexible" de la vista, si tiene,
//    cuenta con un tope de Tag px: el resto se ve con la rueda del mouse.
// 2) con el alto real, para que esa lista ocupe el espacio que sobre si la ventana es más alta que el mínimo.
internal sealed class Medidor : Microsoft.UI.Xaml.Controls.Panel
{
    private readonly FrameworkElement _contenido;
    private readonly Action<double, double> _alMedir;
    private const double PisoFlexible = 80;
    private FrameworkElement? _flexible;
    private double _tope;

    public Medidor(FrameworkElement contenido, Action<double, double> alMedir)
    {
        (_contenido, _alMedir) = (contenido, alMedir);
        Children.Add(contenido);
        contenido.Loaded += (_, _) =>
        {
            _flexible = contenido.FindName("Flexible") as FrameworkElement;
            _tope = double.TryParse(_flexible?.Tag as string, out var t) ? t : 0;
            InvalidateMeasure();
        };
    }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size disponible)
    {
        _contenido.Measure(new Windows.Foundation.Size(disponible.Width, double.PositiveInfinity));
        double total = _contenido.DesiredSize.Height, piso = total;
        if (_flexible != null)
        {
            double lista = _flexible.DesiredSize.Height;
            total += Math.Min(lista, _tope) - lista;
            piso += Math.Min(lista, PisoFlexible) - lista;
        }
        _alMedir(total, piso);

        _contenido.Measure(disponible);
        return new Windows.Foundation.Size(disponible.Width, Math.Min(total, disponible.Height));
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        _contenido.Arrange(new Windows.Foundation.Rect(0, 0, final.Width, final.Height));
        return final;
    }
}
