"""Dibuja los efectos de Simón en pixel art, con el mismo tamaño de píxel que él (1 px = 1 px de pantalla):
las "z" azules del sueño (3 tamaños), los corazones rojos del mimo (3 tamaños) y los globitos "PRRR" y "MIAU!"
con una fuente de 5 px de alto. Todos llevan el contorno oscuro de Simón.

    python efectos.py [carpeta de salida]      (por defecto: ../efectos)
"""
import os
import sys

import numpy as np
from PIL import Image

AQUI = os.path.dirname(os.path.abspath(__file__))
CONTORNO = (24, 20, 22)


def dibujar(filas, colores):
    """filas: texto con un carácter por píxel ('.' = vacío); colores: carácter → color. Agrega 1 px de contorno."""
    h, w = len(filas), len(filas[0])
    marca = np.zeros((h + 2, w + 2), bool)
    arte = np.zeros((h + 2, w + 2, 4), np.uint8)
    for y, fila in enumerate(filas):
        for x, ch in enumerate(fila):
            if ch != ".":
                marca[y + 1, x + 1] = True
                arte[y + 1, x + 1] = (*colores[ch], 255)
    cerca = np.zeros_like(marca)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cerca |= np.roll(np.roll(marca, dy, 0), dx, 1)
    arte[cerca & ~marca] = (*CONTORNO, 255)
    return Image.fromarray(arte)


# ── "z" azules: tapa clara, diagonal azul y base más oscura ──
AZUL = {"o": (170, 214, 255), "#": (96, 165, 255), "d": (58, 108, 214)}


def z(n, grueso):
    filas = ["o" * n]
    for r in range(1, n - 1):
        c = n - 1 - r
        filas.append("".join("#" if c - grueso < x <= c else "." for x in range(n)))
    filas.append("d" * n)
    return filas


# ── corazones rojos ──
ROJO = {"o": (255, 150, 160), "#": (228, 52, 72), "d": (170, 28, 52)}
CORAZONES = {
    "corazon1": ["o#.#.", "#####", "#####", ".###.", "..d.."],
    "corazon2": [".##.##.", "o######", "#######", ".#####.", "..###..", "...d..."],
    "corazon3": [".###.###.", "o########", "#########", "#########", ".#######.", "..#####..", "...###...", "....d...."],
}

# ── fuente de 5 px de alto (mayúsculas) ──
LETRAS = {
    "P": ["####.", "#...#", "####.", "#....", "#...."],
    "R": ["####.", "#...#", "####.", "#..#.", "#...#"],
    "M": ["#...#", "##.##", "#.#.#", "#...#", "#...#"],
    "I": ["###", ".#.", ".#.", ".#.", "###"],
    "A": [".###.", "#...#", "#####", "#...#", "#...#"],
    "U": ["#...#", "#...#", "#...#", "#...#", ".###."],
    "!": ["#", "#", "#", ".", "#"],
}


def texto(palabra):
    filas = ["" for _ in range(5)]
    for i, letra in enumerate(palabra):
        for y in range(5):
            filas[y] += ("." if i else "") + LETRAS[letra][y]
    return filas


if __name__ == "__main__":
    salida = sys.argv[1] if len(sys.argv) > 1 else os.path.join(AQUI, "..", "efectos")
    os.makedirs(salida, exist_ok=True)
    imagenes = {
        "fx_z1": dibujar(z(5, 1), AZUL),
        "fx_z2": dibujar(z(7, 2), AZUL),
        "fx_z3": dibujar(z(9, 2), AZUL),
        "fx_prrr": dibujar(texto("PRRR"), {"#": (255, 178, 196)}),
        "fx_miau": dibujar(texto("MIAU!"), {"#": (250, 250, 245)}),
    }
    for nombre, filas in CORAZONES.items():
        imagenes["fx_" + nombre] = dibujar(filas, ROJO)
    for nombre, im in imagenes.items():
        im.save(os.path.join(salida, nombre + ".png"))
        print(f"{nombre:12} {im.width}×{im.height}")
