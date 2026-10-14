# Design system — Widgets de escritorio (Windows 11, WinUI 3)

## Producto
Conjunto de widgets propios anclados al fondo del escritorio de Windows 11 (detrás de las ventanas, sin Alt+Tab). Uso diario de un estudiante universitario argentino. Interfaz en español (es-AR). Se implementará en WinUI 3, así que todo debe ser reproducible con controles XAML: sin efectos web exóticos, sin animaciones complejas.

Widgets:
1. Agenda del día (Google Calendar): eventos de hoy con chip de color por calendario, horario, lugar; contador de tareas pendientes; botón "+" píldora.
2. Calendario mensual: grilla de mes, día actual resaltado (círculo relleno de acento), flechas < > para navegar, botón "+".
3. Tareas (Google Tasks): lista con casilla circular para completar, contador.
4. Notas (estilo Keep): tarjetitas de nota con colores suaves tipo Keep.
5. Drive reciente: lista de archivos con ícono por tipo (Docs azul, Sheets verde, Slides amarillo, PDF rojo), fecha relativa, botones subir y buscar.
6. Reloj: hora grande, fecha, próxima alarma/evento.
7. Barra de accesos rápidos: nueva nota, nueva tarea, nota de voz, dibujo, foto — íconos en botones cuadrados con esquinas redondeadas (12px).

## Estética: Material You (Material 3) oscuro sobre Mica/Acrylic
- Fondo de escritorio: wallpaper oscuro desenfocado (para mostrar la translucidez).
- Tarjetas: fondo translúcido oscuro `rgba(32, 33, 38, 0.72)` con backdrop blur 30px (simula DesktopAcrylic), borde 1px `rgba(255,255,255,0.06)`.
- Radio de esquina: tarjetas 28px; elementos internos 16–20px; botones "+" en píldora (999px). Etiquetas, contadores y barra de accesos rápidos: cuadrados con esquinas redondeadas (6px etiquetas, 12px botones de acceso, 16px contenedor de la barra). NUNCA píldora en etiquetas.
- Sin sombras duras: sombra suave `0 8px 32px rgba(0,0,0,0.35)`.
- Separación entre widgets: 16px. Padding interno de tarjeta: 20px.

## Color (tonal, Material 3 dark)
- Texto primario `#E6E1E9`, secundario `#CAC4CF`, terciario `#938F99`.
- Acento primario (lavanda M3): `#D0BCFF`, contenedor `#4F378B`, sobre-contenedor `#EADDFF`.
- Superficie elevada interna (filas, celdas): `rgba(255,255,255,0.05)`.
- Colores por calendario (el usuario rechazó los fondos saturados tonales de las etiquetas por verse "muy IA"; usar estos colores solo como acento: punto, borde o texto, nunca como fondo relleno):
  - Facultad: azul `#A8C7FA` sobre `#0B3A6B`
  - Personal: verde `#A6D9A0` sobre `#1F4D1E`
  - Trabajo: ámbar `#FFD28A` sobre `#5A3E00`
  - Feriados: rosa `#FFB1C8` sobre `#6A1B3A`
- Notas: fondo neutro rgba(255,255,255,0.08) y punto de 6px de color (ámbar, verde, azul, rosa) junto al título; nada de fondos tonales.

## Tipografía
- Familia única: "Segoe UI Variable" (fallback "Segoe UI", sans-serif). NO usar otras fuentes.
- Reloj: 64px, peso 300 (Display). Título de widget: 16px peso 600. Cuerpo 14px. Meta/caption 12px color secundario.

## Componentes
- Encabezado de widget: título a la izquierda, a la derecha botón "+" píldora (fondo `#D0BCFF`, texto `#381E72`, 32px alto) y/o íconos.
- Fila de evento: barra vertical de color de 4px redondeada + hora (12px) + título (14px 500) + lugar con ícono de ubicación (12px secundario) + chip de calendario.
- Contador: etiqueta cuadrada con esquinas redondeadas "3 pendientes", sin fondo lavanda saturado.
- Casilla de tarea: círculo 20px borde 2px `#938F99`; completada = relleno acento con check y texto tachado.
- Íconos: lineales estilo Fluent System Icons / Material Symbols Rounded, 20px.
- Estados hover: superficie +4% blanco. Sin animaciones salvo transiciones de 150ms.

## Layout
Pantalla 1920×1080. Widgets dispuestos en el lado derecho del escritorio en dos columnas (~360px de ancho cada una), dejando el resto del wallpaper libre. La barra de accesos rápidos es horizontal, contenedor con esquinas de 16px, debajo del reloj.
