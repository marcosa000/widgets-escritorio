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

TOPE_CUERPO = 46           # en el cuadro de la IA, de esta fila para abajo son patas
SUELO = 61                 # fila donde apoyan las patitas
GIRO = 0.38                # cuánto gira cada pata desde el hombro/cadera al caminar (rad, ~22°)
ALZADA = 3                 # cuánto se levanta la patita que avanza (px)
JY = 43                    # fila de hombros y caderas: dentro del cuerpo, así no queda un hueco arriba de la pata
# Forma de las patas en reposo, relativa al hombro/cadera (y hacia abajo): (punto, ancho en px).
# Delantera: hombro, codo, muñeca, patita. Trasera en "Z" como la de un gato: cadera y muslo ancho,
# rodilla hacia adelante, garrón ("talón") alto hacia atrás, patita.
DELANTERA = [((0, 0), 5.0), ((-1, 7), 4.5), ((0, 14), 3.6), ((1, 18), 3.6)]
TRASERA = [((0, 0), 7.5), ((3, 7), 5.5), ((-2, 12), 3.6), ((0, 18), 3.6)]
# Cerca = lado izquierdo de Simón (el que se ve). Pares diagonales en fase: delantera cerca + trasera lejos.
PATAS = [                  # x del hombro/cadera, fase, delantera, cercana  (en orden de dibujo dentro de cada lado)
    (21, 0.0, False, False),
    (47, np.pi, True, False),
    (26, np.pi, False, True),
    (52, 0.0, True, True),
]


def oscurecer(color, f=0.78):
    return tuple(int(v * f) for v in color)


def puntos_pata(jx, fase, delantera):
    """Articulaciones de la pata en este momento del paso: gira desde el hombro/cadera; la apoyada se estira
    hasta el piso y la que avanza se dobla y se levanta."""
    forma = DELANTERA if delantera else TRASERA
    giro = GIRO * np.cos(fase)                           # >0: la patita va adelante
    avanza = max(0.0, -np.sin(fase))                     # 0 apoyada … 1 en el aire
    c, s = np.cos(giro), np.sin(giro)
    puntos = []
    for i, ((x, y), ancho) in enumerate(forma):
        if i >= 2 and avanza:                            # la parte de abajo se dobla hacia adelante al levantarla
            x, y = x + 2 * avanza, y - ALZADA * avanza * (0.6 if i == 2 else 1)
        puntos.append([jx + x * c + y * s, JY - x * s + y * c, ancho])
    if not avanza:                                       # apoyada: estira muñeca/garrón y patita hasta el piso
        falta = SUELO - puntos[-1][1]
        puntos[-1][1] += falta
        puntos[-2][1] += falta * 0.5
    return puntos


def dibujar_pata(lienzo, cuerpo, puntos, delantera, cercana):
    """Pata con grosor variable a lo largo de la línea, patita ovalada, colores de Simón y contorno."""
    h, w = lienzo.shape[:2]
    mascara = np.zeros((h, w), bool)
    yy, xx = np.mgrid[0:h, 0:w]
    for (ax, ay, aw), (bx, by, bw) in zip(puntos, puntos[1:]):
        pasos = int(max(abs(bx - ax), abs(by - ay)) * 3) + 1
        for t in np.linspace(0, 1, pasos):
            x, y, r = ax + (bx - ax) * t, ay + (by - ay) * t, (aw + (bw - aw) * t) / 2
            mascara |= (xx + 0.5 - x) ** 2 + (yy + 0.5 - y) ** 2 <= r * r
    px, py = puntos[-1][0], puntos[-1][1]
    mascara |= ((xx + 0.5 - (px + 1)) / 2.6) ** 2 + ((yy + 0.5 - (py - 0.8)) / 1.4) ** 2 <= 1   # patita
    mascara[int(py) + 1:] = False                        # nada por debajo del piso

    blanca_entera = delantera and cercana                # pata delantera izquierda de Simón
    sombra = (lambda col: col) if cercana else oscurecer
    for y, x in zip(*np.nonzero(mascara)):
        if blanca_entera or y >= py - 2:                 # patita blanca (3 filas); la otra pata, blanca entera
            atras = x < px - 0.5 and not mascara[y, max(x - 1, 0)]
            color = BLANCO_SOMBRA if atras else BLANCO
        else:
            color = RAYA if y % 3 == 0 else ATIGRADO
        lienzo[y, x] = (*sombra(color), 255)
    # contorno de 1 px, salvo sobre el cuerpo (ahí la pata se funde con el pelaje)
    borde = np.zeros_like(mascara)
    borde[1:] |= mascara[:-1]; borde[:-1] |= mascara[1:]; borde[:, 1:] |= mascara[:, :-1]; borde[:, :-1] |= mascara[:, 1:]
    borde &= ~mascara & ~cuerpo
    lienzo[borde] = (*CONTORNO, 255)


def caminata(base, cuadros=6):
    """Cuerpo, cabeza y cola de un cuadro de la IA; las patas, dibujadas en un ciclo de `cuadros` pasos."""
    cuerpo = base.copy()
    cuerpo[TOPE_CUERPO:, 12:] = 0                                         # sin patas (la punta de la cola, x<12, queda)
    cuerpo[TOPE_CUERPO + 4:, :12] = 0                                     # debajo de la cola: restos de la pata de la IA
    corte = (cuerpo[TOPE_CUERPO - 1, :, 3] > 0) & (np.arange(N) >= 12)   # contorno de la panza donde quedó cortado
    cuerpo[TOPE_CUERPO, corte] = (*CONTORNO, 255)
    linea_lomo(cuerpo, 18, 50)
    resultado = []
    for k in range(cuadros):
        rebote = 1 if k % (cuadros // 2) == cuadros // 4 else 0           # sube 1 px a mitad de cada paso
        movido = np.zeros_like(cuerpo)
        movido[:N - rebote] = cuerpo[rebote:]
        es_cuerpo = movido[..., 3] > 0
        lienzo = np.zeros_like(cuerpo)
        for cercanas in (False, True):                                    # las del otro lado, detrás del cuerpo
            if cercanas:
                lienzo[es_cuerpo] = movido[es_cuerpo]
            for jx, fase, delantera, cercana in PATAS:
                if cercana == cercanas:
                    puntos = [[x, y - rebote, a] for x, y, a in puntos_pata(jx, 2 * np.pi * k / cuadros + fase, delantera)]
                    puntos[-1][1] = min(puntos[-1][1] + rebote, SUELO)    # las patitas no suben con el rebote
                    dibujar_pata(lienzo, es_cuerpo, puntos, delantera, cercana)
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
