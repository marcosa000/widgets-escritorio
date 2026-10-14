# Widgets de escritorio

Widgets para el escritorio de Windows 11 con estética Material You, hechos en WinUI 3 (C#):
reloj con accesos rápidos, agenda y calendario mensual (Google Calendar), tareas (Google Tasks),
archivos recientes con búsqueda y subida (Google Drive) y notas (Google Keep, vía [gkeepapi](https://github.com/kiwiz/gkeepapi), no oficial).

Forman un bloque de dos columnas anclado detrás de las ventanas, que se mueve como una sola pieza
y se puede redimensionar desde los bordes.

## Requisitos

- Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Python 3.13 con `pip install gkeepapi keyring` (solo para Keep)
- Un cliente OAuth propio de Google ("App de escritorio") con las APIs de Calendar, Tasks y Drive activadas.
  Su JSON va en `Widgets\client_secret.json` (está en `.gitignore`: nunca se sube).

## Uso

```powershell
powershell -ExecutionPolicy Bypass -File .\publicar.ps1          # compila, publica y registra el arranque con Windows
python .\Widgets\keep\configurar_keep.py                         # una vez por computadora, para Keep
```

Los tokens de Google y de Keep se guardan en el Administrador de credenciales de Windows, no en archivos.

`index.md` y `privacidad.md` son la página y la política de privacidad que pide la pantalla de consentimiento de Google
(publicadas con GitHub Pages).
