using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Drive.v3;
using Google.Apis.Tasks.v1;
using Google.Apis.Json;
using Google.Apis.Util.Store;
using Windows.Security.Credentials;

namespace Widgets.Auth;

public static class GoogleAuth
{
    // Tasks con escritura para completar y crear tareas. Drive: metadatos para listar y buscar (nombres y fechas,
    // no contenido) y drive.file para subir; drive.file solo da acceso a los archivos que sube esta app.
    private static readonly string[] Scopes =
    {
        CalendarService.Scope.CalendarReadonly,
        TasksService.Scope.Tasks,
        DriveService.Scope.DriveMetadataReadonly,
        DriveService.Scope.DriveFile,
    };

    // El token guardado no sabe qué scopes tiene: al cambiar Scopes hay que cambiar este nombre
    // para que Google vuelva a pedir permiso en vez de reusar un token sin los permisos nuevos.
    private const string Usuario = "usuario-calendar-tasks-drive-subida";

    public static readonly string RutaSecreto = Path.Combine(AppContext.BaseDirectory, "client_secret.json");

    private static Task<UserCredential>? _credencial;

    // La primera vez abre el navegador para autorizar; después usa el token guardado y lo renueva solo.
    public static Task<UserCredential> ObtenerAsync()
    {
        if (!File.Exists(RutaSecreto))
            throw new FileNotFoundException("Falta client_secret.json junto a Widgets.exe", RutaSecreto);
        return _credencial ??= Autorizar();
    }

    private static async Task<UserCredential> Autorizar()
    {
        try
        {
            using var stream = File.OpenRead(RutaSecreto);
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets, Scopes, Usuario, CancellationToken.None, new BovedaDataStore());
        }
        catch
        {
            _credencial = null; // si cancelás en el navegador, el próximo refresco vuelve a intentar
            throw;
        }
    }

    // Tokens en el Credential Locker de Windows (cifrado por usuario) en vez de un JSON plano en disco.
    private sealed class BovedaDataStore : IDataStore
    {
        private const string Recurso = "Widgets.Google";
        private readonly PasswordVault _boveda = new();

        public Task StoreAsync<T>(string key, T value)
        {
            _boveda.Add(new PasswordCredential(Recurso, key, NewtonsoftJsonSerializer.Instance.Serialize(value)));
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            var c = Buscar(key);
            if (c == null) return Task.FromResult(default(T)!);
            c.RetrievePassword();
            return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(c.Password));
        }

        public Task DeleteAsync<T>(string key)
        {
            if (Buscar(key) is { } c) _boveda.Remove(c);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            try { foreach (var c in _boveda.FindAllByResource(Recurso)) _boveda.Remove(c); }
            catch (Exception) { } // FindAllByResource tira si no hay nada guardado
            return Task.CompletedTask;
        }

        private PasswordCredential? Buscar(string key)
        {
            try { return _boveda.Retrieve(Recurso, key); }
            catch (Exception) { return null; } // Retrieve tira si no existe
        }
    }
}
