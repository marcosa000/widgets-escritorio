using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Widgets.Auth;

namespace Widgets.Servicios;

// Traduce los errores de Google a un mensaje corto que diga qué hacer.
public static class Errores
{
    public static string Texto(Exception ex, string servicio) => ex switch
    {
        FileNotFoundException => "Falta client_secret.json junto a Widgets.exe",
        TokenResponseException { Error.Error: "invalid_grant" } => SesionVencida(),
        GoogleApiException g when g.HttpStatusCode == HttpStatusCode.Forbidden && g.Message.Contains("has not been used") =>
            $"Falta activar la API de Google {servicio} en Google Cloud Console (APIs y servicios → Biblioteca).",
        GoogleApiException g when g.HttpStatusCode == HttpStatusCode.Forbidden =>
            $"Google no dio permiso para {servicio}. Volvé a autorizar la app.",
        HttpRequestException => "Sin conexión. Se reintenta en 5 minutos.",
        _ => $"No se pudo leer {servicio}: {ex.Message}",
    };

    private static string SesionVencida()
    {
        GoogleAuth.Olvidar();
        return "Google cerró la sesión. Tocá ↻ en la Agenda para volver a entrar (se abre el navegador).";
    }
}
