using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Google;

namespace Widgets.Servicios;

// Traduce los errores de Google a un mensaje corto que diga qué hacer.
public static class Errores
{
    public static string Texto(Exception ex, string servicio) => ex switch
    {
        FileNotFoundException => "Falta client_secret.json junto a Widgets.exe",
        GoogleApiException g when g.HttpStatusCode == HttpStatusCode.Forbidden && g.Message.Contains("has not been used") =>
            $"Falta activar la API de Google {servicio} en Google Cloud Console (APIs y servicios → Biblioteca).",
        GoogleApiException g when g.HttpStatusCode == HttpStatusCode.Forbidden =>
            $"Google no dio permiso para {servicio}. Volvé a autorizar la app.",
        HttpRequestException => "Sin conexión. Se reintenta en 5 minutos.",
        _ => $"No se pudo leer {servicio}: {ex.Message}",
    };
}
