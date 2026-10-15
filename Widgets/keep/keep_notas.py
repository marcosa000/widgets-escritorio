"""Imprime en JSON las notas activas de Google Keep y todas las etiquetas, para el widget de Notas.
Lo ejecuta KeepService. Salida: {"notas": [...], "etiquetas": [...]}.

Con el argumento "crear" crea una nota con el JSON {"titulo", "texto", "color", "etiquetas"} que recibe por la
entrada estándar (color: un valor de Keep como "YELLOW"; etiquetas: nombres, se crean si no existen) e imprime
el listado actualizado (igual que sin argumentos). Con "editar" recibe además "id" y reemplaza título, texto, color y etiquetas de esa nota.
Las listas con casillas no se editan (su texto no es texto plano): se marcan con "lista": true.

Códigos de salida: 0 bien, 2 Keep sin configurar, 3 Google rechazó el token (hay que configurar de nuevo),
1 otro error (el detalle va por stderr).
"""
import json
import os
import sys

import gkeepapi
import keyring

SERVICIO = "Widgets.Keep"
CARPETA = os.path.join(os.environ["LOCALAPPDATA"], "Widgets")
CONFIG = os.path.join(CARPETA, "keep.json")
# Caché de la última sincronización: con ella, cada lectura baja solo los cambios y no todas las notas.
ESTADO = os.path.join(CARPETA, "keep_estado.json")
MAX_NOTAS = 30


def main():
    try:
        with open(CONFIG, encoding="utf-8") as f:
            config = json.load(f)
        master = keyring.get_password(SERVICIO, config["email"])
    except (OSError, KeyError, ValueError):
        master = None
    if not master:
        print("Keep no está configurado: corré keep\\configurar_keep.py", file=sys.stderr)
        sys.exit(2)

    estado = None
    if os.path.exists(ESTADO):
        try:
            with open(ESTADO, encoding="utf-8") as f:
                estado = json.load(f)
        except ValueError:
            estado = None  # caché roto: se baja todo de nuevo

    keep = gkeepapi.Keep()
    try:
        keep.authenticate(config["email"], master, state=estado, device_id=config["dispositivo"])
    except Exception:
        if estado is None:
            raise
        keep = gkeepapi.Keep()  # un caché viejo puede romper la sincronización: reintento sin él
        keep.authenticate(config["email"], master, device_id=config["dispositivo"])

    if sys.argv[1:] in (["crear"], ["editar"]):
        datos = json.load(sys.stdin)
        if sys.argv[1] == "crear":
            nota = keep.createNote(datos.get("titulo", ""), datos.get("texto", ""))
        else:
            nota = keep.get(datos["id"])
            if nota is None or nota.trashed:
                sys.exit("La nota ya no existe en Keep")
            if isinstance(nota, gkeepapi.node.List):
                sys.exit("Las listas con casillas se editan en Keep")
            nota.title, nota.text = datos.get("titulo", ""), datos.get("texto", "")
        nota.color = gkeepapi.node.ColorValue(datos.get("color") or "DEFAULT")
        elegidas = set(datos.get("etiquetas") or [])
        for etiqueta in list(nota.labels.all()):
            if etiqueta.name not in elegidas:
                nota.labels.remove(etiqueta)
        for nombre in elegidas:
            nota.labels.add(keep.findLabel(nombre, create=True))
        keep.sync()  # sube la nota; si falla, sale con error y el widget conserva lo escrito
        # Sigue de largo e imprime el listado actualizado: así el widget no corre el script (ni inicia sesión) otra vez.

    guardar_estado(keep)
    notas = [n for n in keep.all() if not n.archived and not n.trashed and (n.title or n.text)]
    notas.sort(key=lambda n: (not n.pinned, -n.timestamps.updated.timestamp()))
    print(json.dumps({
        "notas": [
            {"id": n.id, "titulo": n.title, "texto": n.text, "color": n.color.value, "fijada": n.pinned,
             "etiquetas": sorted(l.name for l in n.labels.all()), "lista": isinstance(n, gkeepapi.node.List)}
            for n in notas[:MAX_NOTAS]
        ],
        "etiquetas": sorted(l.name for l in keep.labels()),
    }))


def guardar_estado(keep):
    with open(ESTADO, "w", encoding="utf-8") as f:
        json.dump(keep.dump(), f)


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except gkeepapi.exception.LoginException as e:
        print(f"Google rechazó el token de Keep: {e}", file=sys.stderr)
        sys.exit(3)
    except Exception as e:  # el widget muestra este texto
        print(f"{type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
