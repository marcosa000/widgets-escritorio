using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Services;
using Widgets.Auth;
using GTasks = Google.Apis.Tasks.v1;

namespace Widgets.Servicios;

public record Tarea(string Id, string Titulo, DateTime? Vence);

// Tareas pendientes de la lista principal de Google Tasks; completar o desmarcar escribe en Google.
public static class TasksService
{
    private const string Lista = "@default";

    public static event Action<IReadOnlyList<Tarea>>? Actualizado;
    public static IReadOnlyList<Tarea> Tareas { get; private set; } = Array.Empty<Tarea>();
    public static event Action<string>? Fallo;

    public static async Task RefrescarAsync()
    {
        try
        {
            var pedido = (await Api()).Tasks.List(Lista);
            pedido.ShowCompleted = false;
            pedido.MaxResults = 100;
            var tareas = (await pedido.ExecuteAsync()).Items ?? new List<GTasks.Data.Task>();
            Tareas = tareas
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .OrderBy(t => t.Position)
                // Tasks guarda solo la fecha de vencimiento (la hora llega siempre en 00:00 UTC): se toma el día tal cual.
                .Select(t => new Tarea(t.Id, t.Title, t.Due is { Length: >= 10 } d ? DateTime.Parse(d[..10]) : null))
                .ToList();
            Actualizado?.Invoke(Tareas);
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Errores.Texto(ex, "Tasks")); // el próximo refresco reintenta
        }
    }

    // Devuelve false si Google rechazó el cambio, para que la vista vuelva la casilla atrás.
    public static async Task<bool> MarcarAsync(string id, bool hecha)
    {
        try
        {
            // Al volver a "needsAction", Google borra solo la fecha de completada.
            var cambio = new GTasks.Data.Task { Status = hecha ? "completed" : "needsAction" };
            await (await Api()).Tasks.Patch(cambio, Lista, id).ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            Fallo?.Invoke("No se guardó en Tasks. " + Errores.Texto(ex, "Tasks"));
            return false;
        }
    }

    // Crea la tarea en la lista principal y refresca. Devuelve el error, o null si salió bien.
    public static async Task<string?> CrearAsync(string titulo)
    {
        try
        {
            await (await Api()).Tasks.Insert(new GTasks.Data.Task { Title = titulo }, Lista).ExecuteAsync();
            _ = RefrescarAsync();
            return null;
        }
        catch (Exception ex)
        {
            return Errores.Texto(ex, "Tasks");
        }
    }

    private static async Task<GTasks.TasksService> Api() => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = await GoogleAuth.ObtenerAsync(),
        ApplicationName = "Widgets",
    });
}
