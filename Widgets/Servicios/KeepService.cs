using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;

namespace Widgets.Servicios;

public record Nota(string Id, string Titulo, string Texto, SolidColorBrush Color, bool Fijada, IReadOnlyList<string> Etiquetas)
{
    public string Link => $"https://keep.google.com/#NOTE/{Id}";
}

// Notas de Google Keep vía keep\keep_notas.py (gkeepapi, no oficial: Keep no tiene API para cuentas @gmail.com).
// El script corre, imprime JSON y termina: no queda Python abierto entre refrescos.
public static class KeepService
{
    private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "keep", "keep_notas.py");

    public static event Action<IReadOnlyList<Nota>>? Actualizado;
    public static event Action<string>? Fallo;

    // Colores de Keep llevados al acento del widget (punto junto al título).
    private static readonly Dictionary<string, uint> Colores = new()
    {
        ["RED"] = 0xF28B82, ["ORANGE"] = 0xFBBC04, ["YELLOW"] = 0xFFF475, ["GREEN"] = 0xCCFF90,
        ["TEAL"] = 0xA7FFEB, ["BLUE"] = 0xCBF0F8, ["CERULEAN"] = 0xAECBFA, ["PURPLE"] = 0xD7AEFB,
        ["PINK"] = 0xFDCFE8, ["BROWN"] = 0xE6C9A8, ["GRAY"] = 0xE8EAED,
    };

    private record NotaJson(string id, string titulo, string texto, string color, bool fijada, List<string>? etiquetas);

    public static async Task RefrescarAsync()
    {
        try
        {
            var notas = JsonSerializer.Deserialize<List<NotaJson>>(await EjecutarAsync()) ?? new();
            Actualizado?.Invoke(notas.Select(n => new Nota(
                n.id, n.titulo ?? "", n.texto ?? "",
                App.Pincel(Colores.TryGetValue(n.color ?? "", out var c) ? c : 0xCAC4CF),
                n.fijada, n.etiquetas ?? new())).ToList());
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Explicar(ex));
        }
    }

    // Crea la nota en Keep y refresca. Devuelve el error, o null si salió bien.
    public static async Task<string?> CrearAsync(string titulo, string texto)
    {
        try
        {
            await EjecutarAsync("crear", JsonSerializer.Serialize(new { titulo, texto }));
            _ = RefrescarAsync();
            return null;
        }
        catch (Exception ex)
        {
            return Explicar(ex);
        }
    }

    private static string Explicar(Exception ex) => ex switch
    {
        System.ComponentModel.Win32Exception => "No encontré Python. Instalalo o agregalo al PATH.",
        ErrorKeep { Codigo: 2 } => "Keep no está configurado: corré keep\\configurar_keep.py",
        _ => "No se pudo usar Keep: " + ex.Message,
    };

    private sealed class ErrorKeep(int codigo, string mensaje) : Exception(mensaje)
    {
        public int Codigo => codigo;
    }

    // Corre keep_notas.py y devuelve lo que imprime; si sale con error, tira ErrorKeep con la última línea de stderr.
    private static async Task<string> EjecutarAsync(string? argumento = null, string? entrada = null)
    {
        var inicio = new ProcessStartInfo("python")
        {
            ArgumentList = { "-I", Script },
            RedirectStandardInput = entrada != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        if (argumento != null) inicio.ArgumentList.Add(argumento);
        if (entrada != null) inicio.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        var proceso = Process.Start(inicio) ?? throw new InvalidOperationException("no se pudo ejecutar python");

        if (entrada != null)
        {
            await proceso.StandardInput.WriteAsync(entrada);
            proceso.StandardInput.Close();
        }
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        var fin = proceso.WaitForExitAsync();
        if (await Task.WhenAny(fin, Task.Delay(TimeSpan.FromSeconds(90))) != fin)
        {
            proceso.Kill(true);
            throw new TimeoutException("Keep tardó demasiado en responder");
        }
        if (proceso.ExitCode != 0)
            // Solo la última línea: el mensaje, sin el traceback.
            throw new ErrorKeep(proceso.ExitCode, (await error).Trim().Split('\n').LastOrDefault()?.Trim() ?? "");
        return await salida;
    }
}
