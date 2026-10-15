using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Services;
using Widgets.Auth;
using GTasks = Google.Apis.Tasks.v1;

namespace Widgets.Servicios;

// Vence: solo la fecha (Tasks no guarda hora). Hecha: completada (se muestran las de los últimos días).
public record Tarea(string Id, string Titulo, DateTime? Vence, string Notas = "", bool Hecha = false)
{
    public bool Vencida => !Hecha && Vence < DateTime.Today;

    // "hoy", "mañana", "ayer", "vie 17", "17 oct 2027"; vacío si no tiene fecha.
    public string Cuando => Vence is not { } v ? "" : (v - DateTime.Today).Days switch
    {
        0 => "hoy",
        1 => "mañana",
        -1 => "ayer",
        > 1 and < 7 => v.ToString("ddd d", EsAR),
        _ => v.ToString(v.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", EsAR),
    };

    private static readonly CultureInfo EsAR = new("es-AR");
}

// Tareas de la lista principal de Google Tasks: pendientes y completadas de los últimos días.
// Crear, editar y completar escriben en Google.
public static class TasksService
{
    private const string Lista = "@default";
    private const int DiasCompletadas = 14;

    public static event Action<IReadOnlyList<Tarea>>? Actualizado;
    public static IReadOnlyList<Tarea> Tareas { get; private set; } = Array.Empty<Tarea>();
    public static event Action<string>? Fallo;

    public static async Task RefrescarAsync()
    {
        try
        {
            var pedido = (await Api()).Tasks.List(Lista);
            pedido.ShowCompleted = true;
            pedido.ShowHidden = true; // las completadas pasan a "ocultas" cuando se limpian en Tasks
            pedido.CompletedMin = DateTime.UtcNow.AddDays(-DiasCompletadas).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"); // RFC 3339
            pedido.MaxResults = 100;
            var tareas = (await pedido.ExecuteAsync()).Items ?? new List<GTasks.Data.Task>();
            Publicar(tareas
                .Where(t => !string.IsNullOrWhiteSpace(t.Title) && t.Deleted != true)
                .OrderBy(t => t.Position)
                .Select(Mapear)
                .ToList());
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Errores.Texto(ex, "Tasks")); // el próximo refresco reintenta
        }
    }

    // Tasks guarda solo la fecha de vencimiento (la hora llega siempre en 00:00 UTC): se toma el día tal cual.
    private static Tarea Mapear(GTasks.Data.Task t) => new(
        t.Id, t.Title, t.Due is { Length: >= 10 } d ? DateTime.Parse(d[..10]) : null, t.Notes ?? "", t.Status == "completed");

    private static string? FormatoTasks(DateTime? dia) => dia?.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");

    // También lo usa el modo demostración (Demo.cs).
    internal static void Publicar(IReadOnlyList<Tarea> tareas)
    {
        Tareas = tareas;
        Actualizado?.Invoke(tareas);
    }

    // Devuelve false si Google rechazó el cambio, para que la vista vuelva la casilla atrás.
    public static async Task<bool> MarcarAsync(string id, bool hecha)
    {
        if (Demo.Activo) return true; // en demo la casilla cambia, pero no se escribe en Google
        try
        {
            await ReemplazarAsync(id, t => { t.Status = hecha ? "completed" : "needsAction"; if (!hecha) t.Completed = null; });
            return true;
        }
        catch (Exception ex)
        {
            Fallo?.Invoke("No se guardó en Tasks. " + Errores.Texto(ex, "Tasks"));
            return false;
        }
    }

    // Sin id crea la tarea; con id la edita. Vence null = sin fecha. Después refresca.
    // Devuelve el error, o null si salió bien.
    public static async Task<string?> GuardarAsync(string? id, string titulo, string notas = "", DateTime? vence = null)
    {
        if (Demo.Activo) return "Modo demostración: no se guarda nada";
        try
        {
            if (id == null)
                await (await Api()).Tasks.Insert(new GTasks.Data.Task { Title = titulo, Notes = notas, Due = FormatoTasks(vence) }, Lista).ExecuteAsync();
            else
                await ReemplazarAsync(id, t => { t.Title = titulo; t.Notes = notas; t.Due = FormatoTasks(vence); });
            await RefrescarAsync();
            return null;
        }
        catch (Exception ex)
        {
            return Errores.Texto(ex, "Tasks");
        }
    }

    // Trae la tarea, la cambia y la reemplaza entera: es la única forma de borrar campos (fecha, completada),
    // porque un cambio parcial omite los vacíos.
    private static async Task ReemplazarAsync(string id, Action<GTasks.Data.Task> cambiar)
    {
        var api = await Api();
        var tarea = await api.Tasks.Get(Lista, id).ExecuteAsync();
        cambiar(tarea);
        await api.Tasks.Update(tarea, Lista, id).ExecuteAsync();
    }

    private static async Task<GTasks.TasksService> Api() => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = await GoogleAuth.ObtenerAsync(),
        ApplicationName = "Widgets",
    });
}
