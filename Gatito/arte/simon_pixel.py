"""Genera las poses de Simón en pixel art a partir de la hoja de Gemini (4×3 cuadros sobre fondo magenta).

    python simon_pixel.py [hoja.jpg] [carpeta de salida]     (por defecto: simon-pixel-v2.jpg → ../sprites)

1. Lleva cada cuadro a su grilla real de pixel art (80×80) tomando el color del centro de cada "píxel" de la IA,
   y lo limpia con una paleta fija (así no se pierden los ojos, el collar ni la nariz).
2. Corrige las marcas de Simón: pata delantera izquierda blanca entera, la derecha atigrada con patita blanca,
   las traseras atigradas con patitas blancas, y la línea oscura del lomo.
3. Rehace la caminata: cuerpo, cabeza y cola de un cuadro de la IA, y las cuatro patas dibujadas en un ciclo
   de 6 cuadros (pares diagonales, el cuerpo sube un píxel a mitad de paso).
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
    (38, 150, 140), (22, 105, 98),            # collar turquesa
    (135, 200, 90), (60, 120, 50),            # ojos verdes
    (232, 150, 150), (190, 110, 110),         # nariz y orejas rosadas
])


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
    q = PALETA[d.argmin(-1)]
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

TOPE_CUERPO = 46           # en el cuadro de la IA, de esta fila para abajo son patas
SUELO = 61                 # fila donde apoyan las patitas
AMPLITUD = 6               # cuánto adelanta/atrasa cada pata (px)
# Articulaciones (hombro/cadera) en la fila TOPE_CUERPO-1. Cerca = lado izquierdo de Simón (el que se ve).
PATAS = {                  # x, fase, delantera, cercana
    "trasera_lejos": (21, 0.0, False, False),
    "delantera_lejos": (47, np.pi, True, False),
    "trasera_cerca": (26, np.pi, False, True),
    "delantera_cerca": (52, 0.0, True, True),
}


def oscurecer(color, f=0.78):
    return tuple(int(v * f) for v in color)


def dibujar_pata(lienzo, jx, jy, fx, fy, delantera, cercana):
    """Pata de 4 px de ancho (las traseras, con el muslo más ancho arriba), de la articulación a la patita,
    con la rodilla marcada y contorno."""
    rodilla = (jx + (1 if delantera else -2), jy + (fy - jy) * 0.5)
    puntos = []
    for (ax, ay), (bx, by) in (((jx, jy), rodilla), (rodilla, (fx, fy))):
        pasos = int(max(abs(bx - ax), abs(by - ay))) * 2 + 1
        puntos += [(ax + (bx - ax) * t / pasos, ay + (by - ay) * t / pasos) for t in range(pasos + 1)]
    mascara = np.zeros(lienzo.shape[:2], bool)
    for i, (x, y) in enumerate(puntos):
        xi, yi = int(round(x)), int(round(y))
        t = i / max(len(puntos) - 1, 1)                                    # 0 en la articulación, 1 en la patita
        ancho = 4 + (round(2 * (1 - t / 0.45)) if not delantera and t < 0.45 else 0)
        izq = ancho // 2
        mascara[max(yi - 1, 0):yi + 1, max(xi - izq, 0):xi - izq + ancho] = True
    mascara[int(fy) - 1:int(fy) + 1, int(fx) - 1:int(fx) + 3] = True      # patita, apenas hacia adelante
    blanca_entera = delantera and cercana                                  # pata delantera izquierda de Simón
    sombra = (lambda c: c) if cercana else oscurecer
    for y, x in zip(*np.nonzero(mascara)):
        if blanca_entera or y >= fy - 2:                                   # patita blanca (3 filas)
            color = BLANCO_SOMBRA if (x <= fx - 1 and not blanca_entera) or (blanca_entera and x == int(fx) - 1) else BLANCO
        else:
            color = RAYA if y % 3 == 0 else ATIGRADO
        lienzo[y, x] = (*sombra(color), 255)
    # contorno: solo donde no tapa el cuerpo (debajo de la articulación)
    borde = np.zeros_like(mascara)
    borde[1:] |= mascara[:-1]; borde[:-1] |= mascara[1:]; borde[:, 1:] |= mascara[:, :-1]; borde[:, :-1] |= mascara[:, 1:]
    borde &= ~mascara
    borde[:jy + 2] = False
    lienzo[borde] = (*CONTORNO, 255)


def caminata(base, cuadros=6):
    """Cuerpo, cabeza y cola de un cuadro de la IA; las patas, dibujadas en un ciclo de `cuadros` pasos."""
    cuerpo = base.copy()
    cuerpo[TOPE_CUERPO:, 12:] = 0                                         # sin patas (la punta de la cola, x<12, queda)
    cuerpo[TOPE_CUERPO + 4:, :12] = 0                                     # debajo de la cola: restos de la pata de la IA
    # contorno de la panza donde quedó cortado
    corte = (cuerpo[TOPE_CUERPO - 1, :, 3] > 0) & (np.arange(N) >= 12)
    cuerpo[TOPE_CUERPO, corte] = (*CONTORNO, 255)
    linea_lomo(cuerpo, 18, 50)
    resultado = []
    for k in range(cuadros):
        rebote = 1 if k % (cuadros // 2) == cuadros // 4 else 0           # sube 1 px a mitad de cada paso
        lienzo = np.zeros_like(cuerpo)
        def patas(cercanas):
            for jx, fase, delantera, cercana in PATAS.values():
                if cercana != cercanas:
                    continue
                phi = 2 * np.pi * k / cuadros + fase
                fx = jx + AMPLITUD * np.cos(phi)
                fy = SUELO - 2 * max(0.0, -np.sin(phi))                    # la pata que avanza va levantada
                dibujar_pata(lienzo, jx, TOPE_CUERPO - 1 - rebote, fx, fy, delantera, cercana)
        patas(cercanas=False)                                              # las del otro lado, detrás del cuerpo
        movido = np.zeros_like(cuerpo)
        movido[:N - rebote] = cuerpo[rebote:] if rebote else cuerpo
        tapa = movido[..., 3] > 0
        lienzo[tapa] = movido[tapa]
        patas(cercanas=True)
        resultado.append(lienzo)
    return resultado


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
        "caminar": caminata(c["caminar_ia1"]),
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
