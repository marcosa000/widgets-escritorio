using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gatito;

// Simón camina por el borde de arriba de la barra de tareas y a veces descansa.
// Un clic lo asusta (¡miau!); arrastrándolo se lo cambia de lugar y cae de nuevo a la barra.
public partial class MainWindow : Window
{
    private enum Estado { Caminar, Sentado, Lamiendo, Estirando, Mirando, Durmiendo, Susto, Arrastrado, Cayendo }

    private const double Escala = 1;          // pixel art 1:1: cada píxel del dibujo es un píxel de pantalla (al 100 %)
    private const double Velocidad = 2.5;     // DIP por tick (10 por segundo); cada cuadro de la caminata dura 2 ticks
    private const int CuadrosCaminata = 4;
    // La ventana baja este tanto sobre la barra de tareas: caminando, las patas quedan en el borde; en las poses de
    // descanso la imagen baja y la cola y la panza se apoyan apenas sobre la barra.
    private const double Posado = 5;
    private static readonly HashSet<string> PosesPosado = new() { "sentado", "lamiendo", "mirando", "durmiendo1", "durmiendo2" };
    private const double Gravedad = 2.5;

    private static readonly string[] Nombres =
        { "caminar1", "caminar2", "caminar3", "caminar4", "sentado", "lamiendo", "estirando", "susto", "durmiendo1", "durmiendo2", "colgando", "mirando" };
    private readonly Dictionary<string, BitmapImage> _poses = new();
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _vigia = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Random _azar = new();

    private Estado _estado = Estado.Caminar;
    private int _cuadro;                      // cuadros desde que empezó el estado actual
    private int _duracion = 60;               // cuadros que dura el estado actual
    private int _direccion = 1;               // 1: derecha, -1: izquierda
    private double _x, _altura, _velocidadY;  // _altura: cuánto está por encima de la barra (DIP)
    private Point? _agarre;                   // dónde se lo agarró, relativo a la ventana
    private Point _inicioClic;
    private bool _movido;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var n in Nombres)
            _poses[n] = new BitmapImage(new Uri($"pack://application:,,,/sprites/{n}.png"));

        var area = SystemParameters.WorkArea;
        _x = area.Left + _azar.NextDouble() * (area.Width - Width);
        Posicionar();
        Mostrar("caminar1");

        _reloj.Tick += (_, _) => Avanzar();
        _reloj.Start();
        // Si hay un video o un juego en pantalla completa, Simón se esconde. Si no, se vuelve a poner adelante:
        // la barra de tareas también está "siempre arriba" y, si no, al tocarla lo taparía.
        _vigia.Tick += (_, _) =>
        {
            Visibility = PantallaCompleta() ? Visibility.Hidden : Visibility.Visible;
            if (Visibility == Visibility.Visible)
                SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _vigia.Start();
    }

    // Fuera de Alt+Tab y sin robar el foco al tocarlo.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    // ── Animación ──

    private void Avanzar()
    {
        _cuadro++;
        switch (_estado)
        {
            case Estado.Caminar:
                Mostrar($"caminar{_cuadro / 2 % CuadrosCaminata + 1}"); // un paso cada 2 ticks
                _x += Velocidad * _direccion;
                var area = SystemParameters.WorkArea;
                if (_x < area.Left || _x + Width > area.Right)
                {
                    _direccion = -_direccion;
                    _x = Math.Clamp(_x, area.Left, area.Right - Width);
                }
                break;
            case Estado.Lamiendo:
                Mostrar(_cuadro / 5 % 2 == 0 ? "lamiendo" : "sentado");
                break;
            case Estado.Durmiendo:
                Mostrar(_cuadro / 15 % 2 == 0 ? "durmiendo1" : "durmiendo2");
                Zzz.Text = new string('z', _cuadro / 6 % 3 + 1);
                break;
            case Estado.Susto:
                _altura = Math.Max(0, _altura + _velocidadY);
                _velocidadY -= Gravedad;
                break;
            case Estado.Cayendo:
                _velocidadY -= Gravedad;
                _altura = Math.Max(0, _altura + _velocidadY);
                if (_altura == 0) Cambiar(Estado.Sentado);
                break;
            case Estado.Arrastrado:
                return; // lo mueve el mouse
        }
        Posicionar();
        if (_cuadro >= _duracion && _estado is not (Estado.Cayendo or Estado.Arrastrado)) Siguiente();
    }

    // Qué hace después: casi siempre vuelve a caminar; a veces descansa un rato.
    private void Siguiente()
    {
        if (_estado != Estado.Caminar) { Cambiar(Estado.Caminar); return; }
        var r = _azar.NextDouble();
        Cambiar(r switch
        {
            < 0.25 => Estado.Sentado,
            < 0.40 => Estado.Lamiendo,
            < 0.50 => Estado.Estirando,
            < 0.62 => Estado.Mirando,
            < 0.75 => Estado.Durmiendo,
            _ => Estado.Caminar,
        });
    }

    private void Cambiar(Estado nuevo)
    {
        if (_estado == Estado.Durmiendo && nuevo == Estado.Caminar) nuevo = Estado.Estirando; // al despertar se estira
        _estado = nuevo;
        _cuadro = 0;
        Zzz.Visibility = nuevo == Estado.Durmiendo ? Visibility.Visible : Visibility.Collapsed;
        Globo.Visibility = nuevo == Estado.Susto ? Visibility.Visible : Visibility.Collapsed;
        (_duracion, var pose) = nuevo switch
        {
            Estado.Caminar => (_azar.Next(50, 150), "caminar1"),
            Estado.Sentado => (_azar.Next(40, 100), "sentado"),
            Estado.Lamiendo => (_azar.Next(30, 60), "lamiendo"),
            Estado.Estirando => (20, "estirando"),
            Estado.Mirando => (_azar.Next(30, 60), "mirando"),
            Estado.Durmiendo => (_azar.Next(200, 600), "durmiendo1"),
            Estado.Susto => (12, "susto"),
            _ => (0, "colgando"), // arrastrado o cayendo: colgando en el aire
        };
        if (nuevo == Estado.Caminar && _azar.NextDouble() < 0.3) _direccion = -_direccion;
        if (nuevo == Estado.Susto) _velocidadY = 12;
        Mostrar(pose);
    }

    private void Mostrar(string pose)
    {
        var imagen = _poses[pose];
        Sprite.Source = imagen;
        Sprite.Width = imagen.PixelWidth * Escala;
        Sprite.Height = imagen.PixelHeight * Escala;
        Sprite.Margin = new Thickness(0, 0, 0, PosesPosado.Contains(pose) ? 0 : Posado);
        Volteo.ScaleX = _direccion;
    }

    // Las patas apoyadas en el borde de arriba de la barra de tareas (abajo del área de trabajo).
    private void Posicionar()
    {
        Left = _x;
        Top = SystemParameters.WorkArea.Bottom + Posado - Height - _altura;
    }

    // ── Mouse: clic = susto; arrastrar = moverlo ──

    private void Presionar(object sender, MouseButtonEventArgs e)
    {
        _agarre = e.GetPosition(this);
        _inicioClic = PointToScreen(_agarre.Value);
        _movido = false;
        CaptureMouse();
    }

    private void Mover(object sender, MouseEventArgs e)
    {
        if (_agarre is not { } agarre || !IsMouseCaptured) return;
        var cursor = PointToScreen(e.GetPosition(this));
        if (!_movido && (cursor - _inicioClic).Length < 6) return;
        if (!_movido) { _movido = true; Cambiar(Estado.Arrastrado); }

        // PointToScreen da píxeles físicos; la ventana se ubica en DIP.
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var enDip = dpi.Transform(cursor);
        _x = enDip.X - agarre.X;
        _altura = Math.Max(0, SystemParameters.WorkArea.Bottom + Posado - Height - (enDip.Y - agarre.Y));
        Posicionar();
    }

    private void Soltar(object sender, MouseButtonEventArgs e)
    {
        if (_agarre == null) return;
        _agarre = null;
        ReleaseMouseCapture();
        if (_movido) { _velocidadY = 0; Cambiar(Estado.Cayendo); }
        else Cambiar(Estado.Susto);
    }

    // ── Pantalla completa ──

    private static bool PantallaCompleta() =>
        SHQueryUserNotificationState(out var estado) == 0 && estado is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int estado);
}
