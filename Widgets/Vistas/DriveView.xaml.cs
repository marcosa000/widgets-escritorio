using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Widgets.Servicios;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Widgets.Vistas;

public sealed partial class DriveView : UserControl
{
    private bool Buscando => Busqueda.Visibility == Visibility.Visible;

    public DriveView()
    {
        InitializeComponent();
        // El refresco de recientes no pisa una búsqueda en curso.
        DriveService.Actualizado += archivos => { if (!Buscando) Mostrar(archivos, "Sin archivos recientes"); };
        DriveService.Fallo += texto =>
        {
            // Con error, la lista vieja se oculta (sin cambiar el alto) para que el mensaje se lea.
            Mensaje.Text = texto;
            Mensaje.Visibility = Visibility.Visible;
            { Flexible.Opacity = 0; Flexible.IsHitTestVisible = false; }
        };
    }

    private void Mostrar(IReadOnlyList<Archivo> archivos, string siVacio)
    {
        ListaArchivos.ItemsSource = archivos;
        Mensaje.Text = siVacio;
        Mensaje.Visibility = archivos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        { Flexible.Opacity = 1; Flexible.IsHitTestVisible = true; }
    }

    private void AlternarBusqueda(object sender, RoutedEventArgs e)
    {
        if (Buscando) { Cerrar(); return; }
        Busqueda.Visibility = Visibility.Visible;
        IconoBuscar.Glyph = ""; // la lupa pasa a ser una X para cerrar
        Busqueda.Focus(FocusState.Programmatic);
    }

    private void Cerrar()
    {
        Busqueda.Text = "";
        Busqueda.Visibility = Visibility.Collapsed;
        IconoBuscar.Glyph = "";
        Mostrar(DriveService.Recientes, "Sin archivos recientes");
    }

    private async void TeclaBusqueda(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { Cerrar(); return; }
        if (e.Key != VirtualKey.Enter || string.IsNullOrWhiteSpace(Busqueda.Text)) return;
        e.Handled = true;
        Mensaje.Text = "Buscando…";
        Mensaje.Visibility = Visibility.Visible;
        var resultados = await DriveService.BuscarAsync(Busqueda.Text.Trim());
        if (Buscando && resultados != null) Mostrar(resultados, "Sin resultados");
    }

    private void AbrirArchivo(object sender, RoutedEventArgs e) => App.Abrir((string)((Button)sender).Tag);

    // Subir pega: si hay archivos o una imagen copiados, los guarda en 00 INBOX; si no, abre "Mi unidad".
    private async void Subir(object sender, RoutedEventArgs e)
    {
        var copiado = Clipboard.GetContent();
        if (!TieneAlgoParaSubir(copiado)) { App.Abrir("https://drive.google.com/drive/my-drive"); return; }
        await SubirDesde(copiado);
    }

    private void ArrastreEncima(object sender, DragEventArgs e)
    {
        if (!TieneAlgoParaSubir(e.DataView)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Guardar en 00 INBOX";
    }

    private async void Soltar(object sender, DragEventArgs e)
    {
        var demora = e.GetDeferral(); // los datos del arrastre solo valen mientras dure el evento
        try { await SubirDesde(e.DataView); }
        finally { demora.Complete(); }
    }

    private static bool TieneAlgoParaSubir(DataPackageView datos) =>
        datos.Contains(StandardDataFormats.StorageItems) || datos.Contains(StandardDataFormats.Bitmap);

    private async Task SubirDesde(DataPackageView datos)
    {
        Mensaje.Visibility = Visibility.Visible;
        var errores = new List<string>();
        int subidos = 0;

        if (datos.Contains(StandardDataFormats.StorageItems))
        {
            var archivos = (await datos.GetStorageItemsAsync()).OfType<StorageFile>().ToList(); // las carpetas no se suben
            foreach (var a in archivos)
            {
                Mensaje.Text = $"Guardando {a.Name}…";
                using var flujo = await a.OpenStreamForReadAsync();
                var tipo = string.IsNullOrEmpty(a.ContentType) ? "application/octet-stream" : a.ContentType;
                if (await DriveService.GuardarAsync(a.Name, flujo, tipo) is { } error) errores.Add($"{a.Name}: {error}"); else subidos++;
            }
        }
        else
        {
            // Imagen copiada (captura de pantalla, imagen copiada del navegador…): se sube como PNG con fecha y hora.
            Mensaje.Text = "Guardando imagen…";
            var nombre = $"Imagen pegada {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png";
            using var origen = await (await datos.GetBitmapAsync()).OpenReadAsync();
            using var png = new MemoryStream();
            var decodificador = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(origen);
            var codificador = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, png.AsRandomAccessStream());
            codificador.SetSoftwareBitmap(await decodificador.GetSoftwareBitmapAsync());
            await codificador.FlushAsync();
            png.Position = 0;
            if (await DriveService.GuardarAsync(nombre, png, "image/png") is { } error) errores.Add(error); else subidos++;
        }

        Mensaje.Text = errores.Count == 0 ? (subidos == 1 ? "Guardado en 00 INBOX" : $"{subidos} archivos guardados en 00 INBOX") : string.Join("\n", errores);
        if (errores.Count == 0) _ = OcultarMensajeLuego();
    }

    private async Task OcultarMensajeLuego()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (ListaArchivos.Items.Count > 0) Mensaje.Visibility = Visibility.Collapsed;
    }
}
