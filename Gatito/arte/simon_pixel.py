"""Genera las poses de Simón en pixel art a partir de la hoja de Gemini (4×3 cuadros sobre fondo magenta).

    python simon_pixel.py [hoja.jpg] [carpeta de salida]     (por defecto: simon-pixel-v2.jpg → ../sprites)

1. Lleva cada cuadro a su grilla real de pixel art (80×80) tomando el color del centro de cada "píxel" de la IA,
   y lo limpia con una paleta fija (así no se pierden los ojos, el collar ni la nariz).
2. Corrige las marcas de Simón: pata delantera izquierda blanca entera, la derecha atigrada con patita blanca,
   las traseras atigradas con patitas blancas, y la línea oscura del lomo.
3. Caminata: los 4 cuadros de la IA con los colores de las patas corregidos; la pata delantera blanca cambia
   de lugar en cada cuadro para que se lean como pasos alternados.
Las poses miran a la derecha: se ve el lado izquierdo de Simón, así que la pata delantera cercana es la blanca.
"""
import os
import sys

import numpy as np
from PIL import Image

AQUI = os.path.dirname(os.path.abspath(__file__))
N = 80  # grilla de cada cuadro

NOMBRES = [
    ["caminar_ia1", "caminar_ia2", "caminar_ia3", "caminar_ia4"],
    ["sentado", "lamiendo", "estirando", "susto"],
    ["durmiendo1", "durmiendo2", "colgando", "mirando"],
]

# Colores de Simón. Los de acento son fijos: en la grilla ocupan pocos píxeles y una paleta automática los pierde.
CONTORNO = (24, 20, 22)
BLANCO, BLANCO_SOMBRA = (249, 249, 245), (214, 211, 205)
ATIGRADO, ATIGRADO_CLARO, RAYA, RAYA_OSCURA = (112, 99, 82), (138, 124, 106), (69, 61, 51), (46, 38, 37)
PALETA = np.array([
    CONTORNO, BLANCO, BLANCO_SOMBRA, ATIGRADO, ATIGRADO_CLARO, RAYA, RAYA_OSCURA,
    (87, 78, 66), (155, 138, 128),            # tonos intermedios del pelaje
    (38, 150, 140), (22, 105, 98),            # collar (la IA lo dibujó turquesa; se repinta de amarillo)
    (135, 200, 90), (60, 120, 50),            # ojos verdes
    (232, 150, 150), (190, 110, 110),         # nariz y orejas rosadas
])


# El collar de Simón es amarillo: el turquesa de la IA se reconoce con la paleta y se repinta.
COLLAR = {(38, 150, 140): (240, 198, 46), (22, 105, 98): (178, 138, 22)}


def a_grilla(celda_rgb):
    """Fondo magenta → transparente; un color por "píxel" de la IA (el del centro), con la paleta fija."""
    c = celda_rgb.astype(np.float32)
    magenta = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    h, w = magenta.shape
    ys = ((np.arange(N) + 0.5) * h / N).astype(int)
    xs = ((np.arange(N) + 0.5) * w / N).astype(int)
    color = c[ys][:, xs]
    opaco = magenta[ys][:, xs] < 110
    d = ((color[:, :, None, :] - PALETA[None, None]) ** 2).sum(-1)
    q = PALETA[d.argmin(-1)].copy()
    for ia, propio in COLLAR.items():
        q[np.all(q == ia, axis=-1)] = propio
    return np.dstack([q, np.where(opaco, 255, 0)]).astype(np.uint8)


def cargar(hoja):
    im = np.asarray(Image.open(hoja).convert("RGB"))
    alto, ancho = im.shape[:2]
    return {nombre: a_grilla(im[f * alto // 3:(f + 1) * alto // 3, c * ancho // 4:(c + 1) * ancho // 4])
            for f, fila in enumerate(NOMBRES) for c, nombre in enumerate(fila)}


# ── Retoques ──

def es(px, color):
    return np.all(px[..., :3] == color, axis=-1) & (px[..., 3] > 0)


def pata_atigrada(px, x0, x1, y0, y1):
    """Pinta de atigrado (con rayas horizontales) lo blanco de una pata, dentro del recuadro [x0,x1]×[y0,y1]."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if es(px[y:y + 1, x:x + 1], BLANCO)[0, 0] or es(px[y:y + 1, x:x + 1], BLANCO_SOMBRA)[0, 0]:
                px[y, x, :3] = RAYA if y % 3 == 0 else ATIGRADO


def linea_lomo(px, desde, hasta, lado="arriba"):
    """La raya oscura de Simón sobre la columna: 2 px justo debajo del contorno de la espalda.
    lado="arriba": recorre columnas x en [desde, hasta]; lado="izquierda": filas y en [desde, hasta] (sentado)."""
    pelaje = [ATIGRADO, ATIGRADO_CLARO, RAYA, (87, 78, 66), (155, 138, 128)]
    for i in range(desde, hasta + 1):
        linea = px[:, i] if lado == "arriba" else px[i, :]
        opacos = np.nonzero(linea[:, 3] > 0)[0]
        if len(opacos) == 0:
            continue
        borde = opacos[0]
        for j in (borde + 2, borde + 3):
            if j < len(linea) and any(tuple(linea[j, :3]) == c for c in pelaje):
                linea[j, :3] = RAYA_OSCURA


# ── Caminata ──
# Se usan los 4 cuadros de la IA (la forma de las patas es buena) y se corrigen los colores. La IA hizo los
# cuadros 1≈3 y 2≈4: pintar de blanco una pata delantera distinta en cada uno hace que se lean como pasos alternados.
PELAJE = [ATIGRADO, ATIGRADO_CLARO, RAYA, RAYA_OSCURA, (87, 78, 66), (155, 138, 128)]
PIERNAS = 48                     # de esta fila para abajo, patas (más arriba, el cuerpo y el pecho blanco)
TRASERAS = (4, 40)               # columnas de las patas traseras
DELANTERA_BLANCA = {             # recuadro (x0, x1) de la pata delantera cercana de cada cuadro
    "caminar_ia1": (52, 70),     # la que va adelante
    "caminar_ia2": (49, 56),     # la vertical
    "caminar_ia3": (43, 51),     # la de atrás
    "caminar_ia4": (56, 70),     # la que va adelante
}


def pintar(px, x0, x1, y0, y1, de, a_color):
    """Repinta los píxeles de los colores `de` dentro del recuadro; a_color(y) da el color según la fila."""
    for y in range(y0, min(y1, px.shape[0] - 1) + 1):
        for x in range(x0, x1 + 1):
            if px[y, x, 3] and any(tuple(px[y, x, :3]) == c for c in de):
                px[y, x, :3] = a_color(y)


def pata_blanca(px, x0, x1):
    pintar(px, x0, x1, PIERNAS, N - 1, PELAJE + [BLANCO_SOMBRA], lambda y: BLANCO)  # la IA la dejó gris en algunos


def patas_atigradas(px, x0, x1):
    """Blanco → atigrado con rayas, salvo las 3 filas de abajo de la pata (la patita sigue blanca)."""
    ys = np.nonzero(px[PIERNAS:, x0:x1 + 1, 3].any(axis=1))[0]
    piso = PIERNAS + ys.max() if len(ys) else N - 1
    pintar(px, x0, x1, PIERNAS, piso - 3, [BLANCO, BLANCO_SOMBRA], lambda y: RAYA if y % 3 == 0 else ATIGRADO)


def otras_delanteras_atigradas(px, blanca):
    """La pata delantera lejana: atigrada con patita blanca (fuera del recuadro de la blanca)."""
    for x0, x1 in ((TRASERAS[1] + 1, blanca[0] - 1), (blanca[1] + 1, N - 1)):
        if x0 <= x1:
            patas_atigradas(px, x0, x1)


def caminata(c):
    c["caminar_ia4"][:PIERNAS - 4] = c["caminar_ia2"][:PIERNAS - 4]   # el lomo erizado del cuadro 4, del cuadro 2
    cuadros = []
    for nombre, blanca in DELANTERA_BLANCA.items():
        px = c[nombre].copy()
        patas_atigradas(px, *TRASERAS)
        otras_delanteras_atigradas(px, blanca)
        pata_blanca(px, *blanca)
        linea_lomo(px, 18, 50)
        cuadros.append(px)
    return cuadros


def recortar_juntos(cuadros):
    """Recorta un grupo de cuadros con el mismo recuadro (así no tiemblan al alternarse)."""
    opaco = np.any([c[..., 3] > 0 for c in cuadros], axis=0)
    ys, xs = np.nonzero(opaco)
    return [c[ys.min():ys.max() + 1, xs.min():xs.max() + 1] for c in cuadros]


if __name__ == "__main__":
    hoja = sys.argv[1] if len(sys.argv) > 1 else os.path.join(AQUI, "simon-pixel-v2.jpg")
    salida = sys.argv[2] if len(sys.argv) > 2 else os.path.join(AQUI, "..", "sprites")
    c = cargar(hoja)

    # Patas traseras atigradas (la IA las hizo blancas); las patitas quedan blancas.
    pata_atigrada(c["estirando"], 0, 7, 46, 58)
    pata_atigrada(c["susto"], 15, 31, 37, 52)
    pata_atigrada(c["colgando"], 17, 31, 39, 57)
    # Línea oscura del lomo
    linea_lomo(c["estirando"], 10, 38)
    linea_lomo(c["susto"], 12, 44)
    linea_lomo(c["colgando"], 14, 50)
    linea_lomo(c["durmiendo1"], 12, 40)
    linea_lomo(c["durmiendo2"], 12, 40)
    linea_lomo(c["sentado"], 26, 52, lado="izquierda")
    linea_lomo(c["lamiendo"], 26, 52, lado="izquierda")

    grupos = {
        "caminar": caminata(c),
        "durmiendo": [c["durmiendo1"], c["durmiendo2"]],
        "sentado": [c["sentado"], c["lamiendo"]],
    }
    finales = {f"caminar{i + 1}": px for i, px in enumerate(recortar_juntos(grupos["caminar"]))}
    finales.update(zip(["durmiendo1", "durmiendo2"], recortar_juntos(grupos["durmiendo"])))
    finales.update(zip(["sentado", "lamiendo"], recortar_juntos(grupos["sentado"])))
    for nombre in ("estirando", "susto", "colgando", "mirando"):
        finales[nombre] = recortar_juntos([c[nombre]])[0]

    os.makedirs(salida, exist_ok=True)
    for viejo in os.listdir(salida):
        if viejo.endswith(".png"):
            os.remove(os.path.join(salida, viejo))
    for nombre, px in finales.items():
        Image.fromarray(px).save(os.path.join(salida, nombre + ".png"))
        print(f"{nombre:11} {px.shape[1]}×{px.shape[0]}")
