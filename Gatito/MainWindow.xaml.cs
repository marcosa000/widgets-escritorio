using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gatito;

// Simón camina por el borde de arriba de la barra de tareas y, al azar, se sienta, se lame, se estira, mira o duerme.
// Un clic lo asusta (MIAU!); doble clic es un mimo (mira de frente, suben corazones); arrastrándolo se lo cambia de
// lugar y cae de nuevo a la barra; clic derecho lo duerme un rato. Los efectos están en pixel art, como él.
public partial class MainWindow : Window
{
    private enum Estado { Caminar, Sentado, Lamiendo, Estirando, Mirando, Durmiendo, Susto, Mimo, Arrastrado, Cayendo }

    // Qué hace después de cada estado, con su peso (probabilidad relativa). Así el orden nunca es el mismo:
    // dormido → se estira → camina, o → se queda sentado → se lame → vuelve a dormir…
    private static readonly Dictionary<Estado, (Estado Hacia, int Peso)[]> Despues = new()
    {
        [Estado.Caminar] = new[] { (Estado.Caminar, 20), (Estado.Sentado, 22), (Estado.Lamiendo, 8), (Estado.Estirando, 20), (Estado.Mirando, 15), (Estado.Durmiendo, 15) },
        [Estado.Sentado] = new[] { (Estado.Lamiendo, 22), (Estado.Mirando, 15), (Estado.Durmiendo, 25), (Estado.Estirando, 15), (Estado.Caminar, 23) },
        [Estado.Lamiendo] = new[] { (Estado.Sentado, 35), (Estado.Caminar, 30), (Estado.Durmiendo, 15), (Estado.Estirando, 20) },
        [Estado.Estirando] = new[] { (Estado.Caminar, 45), (Estado.Sentado, 30), (Estado.Mirando, 10), (Estado.Lamiendo, 15) },
        [Estado.Mirando] = new[] { (Estado.Sentado, 25), (Estado.Caminar, 35), (Estado.Estirando, 20), (Estado.Durmiendo, 10), (Estado.Lamiendo, 10) },
        [Estado.Durmiendo] = new[] { (Estado.Estirando, 70), (Estado.Sentado, 30) },   // al despertar casi siempre se estira
        [Estado.Susto] = new[] { (Estado.Caminar, 55), (Estado.Sentado, 25), (Estado.Estirando, 10), (Estado.Mirando, 10) },
        [Estado.Mimo] = new[] { (Estado.Sentado, 45), (Estado.Caminar, 25), (Estado.Lamiendo, 15), (Estado.Durmiendo, 15) },
        [Estado.Cayendo] = new[] { (Estado.Sentado, 40), (Estado.Caminar, 40), (Estado.Estirando, 20) },
    };

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
    private static readonly string[] NombresEfectos =
        { "fx_z1", "fx_z2", "fx_z3", "fx_corazon1", "fx_corazon2", "fx_corazon3", "fx_prrr", "fx_miau" };
    private readonly Dictionary<string, BitmapImage> _poses = new();
    private readonly Dictionary<string, BitmapImage> _efectos = new();
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _vigia = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Random _azar = new();
    // Un clic suelto espera un instante por si llega el segundo (doble clic = mimo, no susto).
    private readonly DispatcherTimer _clicSuelto = new() { Interval = TimeSpan.FromMilliseconds(260) };

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
        foreach (var n in NombresEfectos)
            _efectos[n] = new BitmapImage(new Uri($"pack://application:,,,/efectos/{n}.png"));

        var area = SystemParameters.WorkArea;
        _x = area.Left + _azar.NextDouble() * (area.Width - Width);
        Posicionar();
        Mostrar("caminar1");

        _clicSuelto.Tick += (_, _) => { _clicSuelto.Stop(); Cambiar(Estado.Susto); };
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
        MoverParticulas();
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
                if (_cuadro % 9 == 4) EmitirZ();               // una "z" cada ~1 s, de a una y de distinto tamaño
                break;
            case Estado.Mimo:
                if (_cuadro % 4 == 1 && _cuadro < 28) EmitirCorazon();
                break;
            case Estado.Susto:
                _altura = Math.Max(0, _altura + _velocidadY);
                _velocidadY -= Gravedad;
                break;
            case Estado.Cayendo:
                _velocidadY -= Gravedad;
                _altura = Math.Max(0, _altura + _velocidadY);
                if (_altura == 0) Siguiente(Estado.Cayendo);
                break;
            case Estado.Arrastrado:
                return; // lo mueve el mouse
        }
        Posicionar();
        if (_cuadro >= _duracion && _estado is not (Estado.Cayendo or Estado.Arrastrado)) Siguiente(_estado);
    }

    // Elige al azar, con los pesos de la tabla, qué hace después de `desde`.
    private void Siguiente(Estado desde)
    {
        var opciones = Despues[desde];
        var r = _azar.Next(opciones.Sum(o => o.Peso));
        foreach (var (hacia, peso) in opciones)
        {
            if (r < peso) { Cambiar(hacia); return; }
            r -= peso;
        }
    }

    private void Cambiar(Estado nuevo)
    {
        _estado = nuevo;
        _cuadro = 0;
        QuitarEfectos();
        (_duracion, var pose) = nuevo switch
        {
            Estado.Caminar => (_azar.Next(50, 150), "caminar1"),
            Estado.Sentado => (_azar.Next(40, 100), "sentado"),
            Estado.Lamiendo => (_azar.Next(30, 60), "lamiendo"),
            Estado.Estirando => (20, "estirando"),
            Estado.Mirando => (_azar.Next(30, 60), "mirando"),
            Estado.Durmiendo => (_azar.Next(200, 450), "durmiendo1"),   // despierta solo entre 20 y 45 s después
            Estado.Susto => (12, "susto"),
            Estado.Mimo => (35, "mirando"),
            _ => (0, "colgando"), // arrastrado o cayendo: colgando en el aire
        };
        if (nuevo == Estado.Caminar && _azar.NextDouble() < 0.3) _direccion = -_direccion;
        if (nuevo == Estado.Susto) _velocidadY = 12;
        Mostrar(pose);
        // Los globitos se ponen después de elegir la pose, porque van encima de ella.
        if (nuevo == Estado.Susto) Emitir("fx_miau", Width / 2, ArribaSprite - 6, 0, 0, 12);
        if (nuevo == Estado.Mimo) Emitir("fx_prrr", Width / 2, ArribaSprite - 6, 0, 0, 34);
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

    // ── Efectos en pixel art: "z" azules, corazones rojos y globitos ──

    private sealed class Particula
    {
        public Image Imagen = null!;
        public double X, Y, Vx, Vy, Deriva;   // centro de la imagen (DIP), velocidad por tick y vaivén lateral
        public int Edad, Vida, Fase;
    }

    private readonly List<Particula> _particulas = new();

    // Arriba de la imagen de Simón, en coordenadas de la ventana.
    private double ArribaSprite => Height - Sprite.Margin.Bottom - Sprite.Height;

    private void Emitir(string nombre, double x, double y, double vx, double vy, int vida, double deriva = 0)
    {
        var fuente = _efectos[nombre];
        var imagen = new Image { Source = fuente, Width = fuente.PixelWidth, Height = fuente.PixelHeight };
        RenderOptions.SetBitmapScalingMode(imagen, BitmapScalingMode.NearestNeighbor);
        Efectos.Children.Add(imagen);
        var p = new Particula { Imagen = imagen, X = x, Y = y, Vx = vx, Vy = vy, Vida = vida, Deriva = deriva, Fase = _azar.Next(0, 6) };
        _particulas.Add(p);
        Colocar(p);
    }

    private void Colocar(Particula p)
    {
        // En píxeles enteros: el pixel art no se desliza entre píxeles.
        Canvas.SetLeft(p.Imagen, Math.Round(p.X + Math.Sin(p.Edad * 0.35 + p.Fase) * p.Deriva - p.Imagen.Width / 2));
        Canvas.SetTop(p.Imagen, Math.Round(p.Y - p.Imagen.Height / 2));
        var resta = p.Vida - p.Edad;
        p.Imagen.Opacity = resta <= 3 ? 0.3 : resta <= 6 ? 0.65 : 1;   // se desvanece en pasos, no de corrido
    }

    private void MoverParticulas()
    {
        for (int i = _particulas.Count - 1; i >= 0; i--)
        {
            var p = _particulas[i];
            p.Edad++;
            if (p.Edad >= p.Vida) { Efectos.Children.Remove(p.Imagen); _particulas.RemoveAt(i); continue; }
            p.X += p.Vx;
            p.Y += p.Vy;
            Colocar(p);
        }
    }

    private void QuitarEfectos()
    {
        Efectos.Children.Clear();
        _particulas.Clear();
    }

    // Salen de arriba de la cabeza (dormido, la cabeza queda del lado al que mira), flotan hacia arriba y se desvanecen.
    private void EmitirZ()
    {
        var cabeza = Width / 2 + _direccion * (Sprite.Width / 2 - 10);
        Emitir($"fx_z{_azar.Next(1, 4)}", cabeza, ArribaSprite + 5, 0.25 * _direccion, -0.55, 46, deriva: 2);
    }

    // Del costado de la cabeza (mirando de frente), de a uno y de distinto tamaño.
    private void EmitirCorazon()
    {
        var lado = _azar.Next(2) == 0 ? -1 : 1;
        Emitir($"fx_corazon{_azar.Next(1, 4)}", Width / 2 + lado * _azar.Next(21, 31), ArribaSprite + 12, 0, -0.8, 22, deriva: 2);
    }

    // ── Mouse: clic = susto, doble clic = mimo, arrastrar = moverlo, clic derecho = dormir ──

    private void Presionar(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _clicSuelto.Stop();
            _agarre = null;
            Cambiar(Estado.Mimo);
            return;
        }
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
        else _clicSuelto.Start();
    }

    // Se duerme un rato y despierta solo (ver la duración de Durmiendo en Cambiar).
    private void DormirConClicDerecho(object sender, MouseButtonEventArgs e)
    {
        if (_estado is Estado.Arrastrado or Estado.Cayendo) return;
        _clicSuelto.Stop();
        Cambiar(Estado.Durmiendo);
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
