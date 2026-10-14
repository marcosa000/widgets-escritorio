"""Configura el acceso a Google Keep para el widget de Notas. Se corre una sola vez, a mano.

Keep no tiene API para cuentas @gmail.com: gkeepapi (no oficial) necesita un "master token" de tu cuenta.
Este script lo obtiene a partir de una cookie de inicio de sesión y lo guarda en el Administrador de
credenciales de Windows. El token no se muestra ni se escribe en ningún archivo.
"""
import getpass
import json
import os
import secrets
import sys

import gkeepapi
import gpsoauth
import keyring

SERVICIO = "Widgets.Keep"
CONFIG = os.path.join(os.environ["LOCALAPPDATA"], "Widgets", "keep.json")


def main():
    print(__doc__)
    email = input("Tu cuenta de Google (correo): ").strip()
    print("""
1. Abrí una ventana de incógnito y entrá a https://accounts.google.com/EmbeddedSetup
2. Iniciá sesión con esa cuenta y tocá "Acepto". La página se queda cargando: está bien.
3. Apretá F12 → pestaña Aplicación (Application) → Cookies → https://accounts.google.com
4. Copiá el VALOR de la cookie "oauth_token" (empieza con oauth2_4/).
""")
    cookie = getpass.getpass("Pegá el valor de oauth_token (no se va a ver mientras lo pegás): ").strip()

    dispositivo = secrets.token_hex(8)  # identifica a "este widget" como dispositivo ante Google
    respuesta = gpsoauth.exchange_token(email, cookie, dispositivo)
    master = respuesta.get("Token")
    if not master:
        sys.exit(f"Google no devolvió el token ({respuesta.get('Error', 'sin detalle')}). "
                 "La cookie dura pocos minutos: repetí los pasos 1 a 4 y pegala enseguida.")

    # Prueba antes de guardar: si no anda, no queda nada a medias.
    keep = gkeepapi.Keep()
    keep.authenticate(email, master, device_id=dispositivo)
    cantidad = sum(1 for n in keep.all() if not n.trashed)

    keyring.set_password(SERVICIO, email, master)
    os.makedirs(os.path.dirname(CONFIG), exist_ok=True)
    with open(CONFIG, "w", encoding="utf-8") as f:
        json.dump({"email": email, "dispositivo": dispositivo}, f)
    print(f"Listo: Keep conectado ({cantidad} notas). Ya podés cerrar esta ventana.")


if __name__ == "__main__":
    main()
