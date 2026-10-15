using System;
using System.Linq;
using Widgets.Servicios;
using GDrive = Google.Apis.Drive.v3.Data;

namespace Widgets;

// Modo demostración: con la variable de entorno WIDGETS_DEMO=1 los widgets muestran datos inventados
// y no se conectan a Google. Sirve para sacar capturas sin exponer datos reales.
public static class Demo
{
    public static bool Activo => Environment.GetEnvironmentVariable("WIDGETS_DEMO") == "1";

    public static void Cargar()
    {
        var hoy = DateTime.Today;
        var facultad = App.Pincel(0xB39DDB);
        var trabajo = App.Pincel(0xFFB74D);
        var personal = App.Pincel(0x80CBC4);

        Evento Ev(int dias, double hora, double horas, string titulo, string lugar, string cal, Microsoft.UI.Xaml.Media.SolidColorBrush color) =>
            new(hoy.AddDays(dias).AddHours(hora), hoy.AddDays(dias).AddHours(hora + horas), false, titulo, lugar, cal, color);

        // Hoy, más algunos eventos repartidos en el mes para que el calendario muestre puntitos.
        var eventos = new[]
        {
            Ev(0, 8, 1, "Gimnasio", "", "personal", personal),
            Ev(0, 10, 2, "Análisis Matemático II", "Aula 12", "facultad", facultad),
            Ev(0, 14, 1.5, "Inglés técnico", "Aula 4", "facultad", facultad),
            Ev(0, 18, 3, "Turno en la oficina", "", "trabajo", trabajo),
            Ev(0, 21, 1, "Repaso para el parcial", "Biblioteca", "facultad", facultad),
        }.Concat(Enumerable.Range(-12, 30).Where(d => d != 0 && (d * 7 + 3) % 5 < 3).SelectMany(d => new[]
        {
            Ev(d, 9, 2, "Clase", "", "facultad", facultad),
            Ev(d, 18, 3, "Trabajo", "", "trabajo", trabajo),
        }.Take(1 + Math.Abs(d) % 2))).ToList();
        CalendarService.Publicar(eventos);

        TasksService.Publicar(new[]
        {
            new Tarea("1", "Entregar el TP de Sistemas Operativos", hoy.AddDays(1), "Subirlo al campus en PDF"),
            new Tarea("2", "Resolver la guía de ejercicios 3", hoy.AddDays(3)),
            new Tarea("3", "Pagar la cuota del gimnasio", hoy.AddDays(-2)),
            new Tarea("4", "Comprar apuntes", null),
            new Tarea("5", "Renovar la credencial de la biblioteca", null, "", true),
        });

        GDrive.File Archivo(string nombre, string tipo, double horasAtras) => new()
        {
            Name = nombre, MimeType = tipo, ViewedByMeTimeDateTimeOffset = DateTimeOffset.Now.AddHours(-horasAtras),
        };
        DriveService.Publicar(new[]
        {
            Archivo("Resumen de la unidad 2.pdf", "application/pdf", 1),
            Archivo("Presupuesto mensual", "application/vnd.google-apps.spreadsheet", 3),
            Archivo("Informe del TP", "application/vnd.google-apps.document", 20),
            Archivo("Presentación final", "application/vnd.google-apps.presentation", 30),
            Archivo("Apuntes de clase.pdf", "application/pdf", 80),
            Archivo("Fotos del viaje", "application/vnd.google-apps.folder", 120),
        }.Select(DriveService.Mapear).ToList());

        KeepService.Etiquetas = new[] { "Casa", "Facultad", "Lectura", "Proyectos" };
        KeepService.Publicar(new[]
        {
            new Nota("a", "Lista del súper", "Yerba, pan, frutas, café", "YELLOW", true, new[] { "Casa" }),
            new Nota("b", "Ideas para el proyecto", "Modo oscuro, atajos de teclado y exportar a PDF", "TEAL", true, new[] { "Proyectos" }),
            new Nota("c", "Fórmulas", "Área del círculo: π·r²\nVolumen de la esfera: 4/3·π·r³", "BLUE", false, new[] { "Facultad" }),
            new Nota("d", "Libros pendientes", "Rayuela, Ficciones, El túnel", "PINK", false, new[] { "Lectura" }),
        });
    }
}
