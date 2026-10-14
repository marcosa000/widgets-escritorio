using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Services;
using Microsoft.UI.Xaml.Media;
using Widgets.Auth;
using GCal = Google.Apis.Calendar.v3;

namespace Widgets.Servicios;

// Fin es exclusivo: un evento de todo el día del 14 termina el 15 a las 00:00.
public record Evento(DateTime Inicio, DateTime Fin, bool TodoElDia, string Titulo, string Lugar, string Calendario, SolidColorBrush Color)
{
    public string Hora => TodoElDia ? "Todo el día" : Inicio.ToString("HH:mm");
    public bool TieneLugar => Lugar != "";
    public bool Ocurre(DateTime dia) => Inicio < dia.AddDays(1) && Fin > dia;
    // Se evalúa al mostrarse; la Agenda se vuelve a pintar en cada refresco (5 min).
    public bool Paso => !TodoElDia && Fin <= DateTime.Now;
    public double Opacidad => Paso ? 0.45 : 1;
}

// Eventos del mes que muestra el calendario (y de hoy, para el reloj) de todos los calendarios visibles.
// También guarda el día elegido en el calendario mensual, que es el que muestra la Agenda.
public static class CalendarService
{
    public static event Action<IReadOnlyList<Evento>>? Actualizado;
    public static event Action<string>? Fallo;
    public static event Action? DiaCambiado;

    public static DateTime Mes { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    // shortcut: si la app queda abierta pasada la medianoche, sigue elegido el día anterior hasta tocar otro.
    public static DateTime DiaElegido { get; private set; } = DateTime.Today;
    public static IReadOnlyList<Evento> Eventos { get; private set; } = Array.Empty<Evento>();

    public static void ElegirDia(DateTime dia)
    {
        DiaElegido = dia.Date;
        DiaCambiado?.Invoke();
    }

    public static Task CambiarMesAsync(DateTime mes)
    {
        Mes = new DateTime(mes.Year, mes.Month, 1);
        return RefrescarAsync();
    }

    public static async Task RefrescarAsync()
    {
        try
        {
            Publicar(await LeerAsync());
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Errores.Texto(ex, "Calendar")); // el próximo refresco reintenta
        }
    }

    // También lo usa el modo demostración (Demo.cs).
    internal static void Publicar(IReadOnlyList<Evento> eventos)
    {
        Eventos = eventos;
        Actualizado?.Invoke(eventos);
    }

    private static async Task<IReadOnlyList<Evento>> LeerAsync()
    {
        var api = new GCal.CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = await GoogleAuth.ObtenerAsync(),
            ApplicationName = "Widgets",
        });

        // El mes visible y, si hoy cae fuera de él, también hoy (lo usa el reloj).
        var rangos = new List<(DateTime Desde, DateTime Hasta)> { (Mes, Mes.AddMonths(1)) };
        if (DateTime.Today < Mes || DateTime.Today >= Mes.AddMonths(1)) rangos.Add((DateTime.Today, DateTime.Today.AddDays(1)));

        var calendarios = (await api.CalendarList.List().ExecuteAsync()).Items.Where(c => c.Selected == true).ToList();
        var consultas = from cal in calendarios from r in rangos select LeerCalendarioAsync(api, cal, r.Desde, r.Hasta);

        return (await Task.WhenAll(consultas)).SelectMany(x => x)
            .DistinctBy(e => (e.Titulo, e.Inicio, e.Calendario))
            .OrderBy(e => !e.TodoElDia).ThenBy(e => e.Inicio).ToList();
    }

    private static async Task<IEnumerable<Evento>> LeerCalendarioAsync(GCal.CalendarService api, GCal.Data.CalendarListEntry cal, DateTime desde, DateTime hasta)
    {
        var pedido = api.Events.List(cal.Id);
        pedido.TimeMinDateTimeOffset = desde;
        pedido.TimeMaxDateTimeOffset = hasta;
        pedido.SingleEvents = true; // expande los eventos recurrentes
        pedido.MaxResults = 2500;
        var color = App.Pincel(Convert.ToUInt32(cal.BackgroundColor.TrimStart('#'), 16));
        // El calendario principal se llama como tu correo; en la etiqueta queda "personal".
        var nombre = cal.Primary == true && cal.SummaryOverride == null ? "personal" : (cal.SummaryOverride ?? cal.Summary).ToLowerInvariant();

        return ((await pedido.ExecuteAsync()).Items ?? new List<GCal.Data.Event>())
            .Where(e => e.Status != "cancelled")
            .Select(e =>
            {
                bool todoElDia = e.Start.DateTimeDateTimeOffset == null;
                var inicio = e.Start.DateTimeDateTimeOffset?.LocalDateTime ?? DateTime.Parse(e.Start.Date);
                var fin = e.End.DateTimeDateTimeOffset?.LocalDateTime ?? DateTime.Parse(e.End.Date);
                return new Evento(inicio, fin, todoElDia, e.Summary ?? "(sin título)", e.Location ?? "", nombre, color);
            });
    }
}
