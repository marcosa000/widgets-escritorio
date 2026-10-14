using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Services;
using Microsoft.UI.Xaml.Media;
using Widgets.Auth;
using GDrive = Google.Apis.Drive.v3;

namespace Widgets.Servicios;

public record Archivo(string Nombre, string Cuando, string Icono, SolidColorBrush Color, string Link);

// Los últimos archivos que abriste en Drive (solo metadatos: nombre, tipo, fecha y link).
public static class DriveService
{
    private static readonly CultureInfo EsAR = new("es-AR");

    static DriveService() => Autoprueba();

    public static event Action<IReadOnlyList<Archivo>>? Actualizado;
    public static IReadOnlyList<Archivo> Recientes { get; private set; } = Array.Empty<Archivo>();
    public static event Action<string>? Fallo;

    public static async Task RefrescarAsync()
    {
        try
        {
            var pedido = (await Api()).Files.List();
            pedido.Q = "trashed = false and mimeType != 'application/vnd.google-apps.folder' and viewedByMeTime > '2000-01-01T00:00:00'";
            pedido.OrderBy = "viewedByMeTime desc";
            pedido.PageSize = 15;
            pedido.Fields = "files(name,mimeType,viewedByMeTime,webViewLink)";
            Publicar(((await pedido.ExecuteAsync()).Files ?? new List<GDrive.Data.File>()).Select(Mapear).ToList());
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Errores.Texto(ex, "Drive")); // el próximo refresco reintenta
        }
    }

    // También lo usa el modo demostración (Demo.cs).
    internal static void Publicar(IReadOnlyList<Archivo> archivos)
    {
        Recientes = archivos;
        Actualizado?.Invoke(archivos);
    }

    // Archivos y carpetas cuyo nombre contiene el texto. Devuelve null si falló (el error va por Fallo).
    public static async Task<IReadOnlyList<Archivo>?> BuscarAsync(string texto)
    {
        try
        {
            var pedido = (await Api()).Files.List();
            pedido.Q = $"trashed = false and name contains '{Escapar(texto)}'";
            pedido.OrderBy = "modifiedTime desc";
            pedido.PageSize = 15;
            pedido.Fields = "files(name,mimeType,viewedByMeTime,modifiedTime,webViewLink)";
            return ((await pedido.ExecuteAsync()).Files ?? new List<GDrive.Data.File>()).Select(Mapear).ToList();
        }
        catch (Exception ex)
        {
            Fallo?.Invoke(Errores.Texto(ex, "Drive"));
            return null;
        }
    }

    // Sube un archivo a "Mi unidad" y refresca los recientes. Devuelve el error, o null si salió bien.
    public static async Task<string?> SubirAsync(string nombre, System.IO.Stream contenido, string tipo)
    {
        try
        {
            // Con "visto ahora" el archivo aparece primero en los recientes (que se ordenan por viewedByMeTime).
            var archivo = new GDrive.Data.File { Name = nombre, ViewedByMeTimeDateTimeOffset = DateTimeOffset.Now };
            var subida = (await Api()).Files.Create(archivo, contenido, tipo);
            var resultado = await subida.UploadAsync();
            if (resultado.Exception != null) throw resultado.Exception;
            _ = RefrescarAsync();
            return null;
        }
        catch (Exception ex)
        {
            return Errores.Texto(ex, "Drive");
        }
    }

    // Comillas y barras invertidas escapadas: el texto va dentro de la consulta de Drive.
    public static string Escapar(string texto) => texto.Replace("\\", "\\\\").Replace("'", "\\'");

    private static async Task<GDrive.DriveService> Api() => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = await GoogleAuth.ObtenerAsync(),
        ApplicationName = "Widgets",
    });

    internal static Archivo Mapear(GDrive.Data.File f)
    {
        // Ícono y color por tipo, como en Drive: Docs azul, Sheets verde, Slides amarillo, PDF rojo.
        var (icono, color) = f.MimeType switch
        {
            "application/vnd.google-apps.document" => ("", 0x8AB4F8u),
            "application/vnd.google-apps.spreadsheet" => ("", 0x81C995u),
            "application/vnd.google-apps.presentation" => ("", 0xFDD663u),
            "application/pdf" => ("", 0xF28B82u),
            "application/vnd.google-apps.folder" => ("", 0xCAC4CFu),
            _ => ("", 0xCAC4CFu),
        };
        // En búsquedas puede haber archivos que nunca abriste: se usa la última modificación.
        var visto = (f.ViewedByMeTimeDateTimeOffset ?? f.ModifiedTimeDateTimeOffset)?.LocalDateTime ?? DateTime.Now;
        return new Archivo(f.Name, Cuando(visto, DateTime.Now), icono, App.Pincel(color), f.WebViewLink ?? "https://drive.google.com");
    }

    public static string Cuando(DateTime visto, DateTime ahora)
    {
        var dif = ahora - visto;
        if (dif.TotalMinutes < 1) return "recién";
        if (dif.TotalHours < 1) return $"hace {(int)dif.TotalMinutes} min";
        if (visto.Date == ahora.Date) return $"hace {(int)dif.TotalHours} h";
        if (visto.Date == ahora.Date.AddDays(-1)) return "ayer";
        return visto.ToString(visto.Year == ahora.Year ? "d MMM" : "d MMM yyyy", EsAR);
    }

    [Conditional("DEBUG")]
    private static void Autoprueba()
    {
        var ahora = new DateTime(2026, 10, 14, 12, 0, 0);
        Debug.Assert(Cuando(ahora.AddSeconds(-20), ahora) == "recién");
        Debug.Assert(Cuando(ahora.AddMinutes(-5), ahora) == "hace 5 min");
        Debug.Assert(Cuando(ahora.AddHours(-3), ahora) == "hace 3 h");
        Debug.Assert(Cuando(ahora.AddHours(-13), ahora) == "ayer"); // 23:00 de ayer: menos de un día pero otra fecha
        Debug.Assert(Cuando(new DateTime(2026, 10, 12, 9, 0, 0), ahora) == "12 oct");
        Debug.Assert(Cuando(new DateTime(2025, 3, 1), ahora) == "1 mar 2025");
        Debug.Assert(Escapar(@"O'Brien\x") == @"O\'Brien\\x");
    }
}
