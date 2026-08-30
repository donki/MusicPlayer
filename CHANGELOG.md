# Changelog — Music Player

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.N` (constitución 11).

## 2026.08.29.1

- **Ficha de la canción con letra**: nueva pantalla con los datos de la pista, la reseña del grupo y
  la **letra, que va marcando la línea que suena** cuando el fichero trae la letra sincronizada. Se
  llega desde el botón de información de *Reproduciendo* y desde el menú de la canción.
- La letra sale de los ficheros del propio usuario: etiqueta USLT/SYLT del MP3, comentarios Vorbis
  del FLAC o un `.lrc` con el mismo nombre. No se descarga ni se genera nada.
- **Buscar información** al editar una canción: rellena título, grupo, álbum, año y pista con lo
  que devuelve MusicBrainz. Respeta el interruptor de búsqueda en línea, que sigue apagado por
  defecto.
- **Al abrir la aplicación se recupera la última canción escuchada**, cargada y en pausa. Antes se
  guardaba en los ajustes y no la leía nadie.
- **Corregido**: al pasar a una canción sin carátula se quedaba viéndose la portada de la anterior,
  tanto en *Reproduciendo* como en el mini reproductor.

---

## 2026.08.28.1 — 2026-08-28

### Anadido
- **Seleccion multiple** en las tres listas de canciones (biblioteca, grupo y lista de
  reproduccion): se entra manteniendo pulsada una cancion y desde la barra contextual se puede
  reproducir la seleccion, anadirla a listas, quitarla de la lista que se esta viendo o borrarla
  del dispositivo con una sola confirmacion del sistema.
- **Edicion de la informacion de una cancion** (titulo, artista, artista del album, album,
  compositor, pista y ano) desde su menu, para que la busqueda y la agrupacion acierten. La
  correccion la guarda la aplicacion y, si el sistema lo permite, se escribe tambien en el indice
  de medios; el archivo de audio no se reescribe.

### Cambiado
- La imagen de un grupo o compositor sin foto descargada usa ahora la **caratula de su primera
  cancion** que tenga una, en vez del marcador generico.

## 2026.08.27.0 — Primera versión

Versión inicial de la aplicación.

### Reproducción
- Reproductor sobre `android.media.MediaPlayer`: MP3, AAC/M4A, FLAC, OGG Vorbis, Opus, WAV, MIDI,
  AMR y audio en MKV, sin incorporar códecs de terceros.
- Controles de anterior, reproducir/pausa y siguiente. «Anterior» vuelve al principio de la canción
  si ya han pasado más de 3 segundos.
- Barra de progreso arrastrable con tiempo reproducido y duración.
- Modos aleatorio (barajado de Fisher-Yates) y de repetición (ninguna / todas / una).
- Foco de audio: la música se pausa ante otra aplicación y baja el volumen ante un aviso corto.
- Servicio en primer plano con notificación de medios, para que siga sonando con la pantalla
  apagada.

### Biblioteca
- Escaneo del índice de medios del sistema (`MediaStore`) con permiso acotado `READ_MEDIA_AUDIO`.
- Agrupación por grupo, con el artista del álbum por delante del de la pista para que un disco de
  varios intérpretes no se rompa en una entrada por canción.
- Opción **Agrupar por compositor** para bibliotecas de música clásica.
- Búsqueda por grupo, canción, álbum y compositor.
- Borrado de canciones del dispositivo, con la confirmación del sistema en Android 11+.

### Listas de reproducción
- Crear, renombrar y eliminar listas, guardadas en el almacenamiento privado de la aplicación.
- Selector de **varias listas a la vez** desde el menú de cualquier canción.
- Al borrar una canción del dispositivo se retira de todas las listas.

### Android Auto
- `MediaBrowserService` con el árbol **Grupos / Listas / Canciones**.
- Reproducción desde el coche, botones del volante y búsqueda por voz.
- La navegación de la biblioteca se limita a la propia aplicación y a los controladores de medios
  del sistema, que son los que tienen concedido `MEDIA_CONTENT_CONTROL`.

### Información de grupos
- Búsqueda opcional de foto y reseña en MusicBrainz + Wikidata + Wikipedia.
- **Desactivada por defecto**: sin activarla, nada sale del dispositivo. Al activarla, lo único que
  se envía es el nombre del grupo.
- Caché en disco, con caducidad de 30 días para los grupos sin resultado, y botón para borrarla.

### Interfaz
- Paleta índigo unificada de las aplicaciones sOCratic, con tema claro y oscuro.
- Menú hamburguesa con logo, nombre, iconos por opción y versión en el pie.
- Botones con iconos vectoriales de línea, sin emoji.
- Diálogos propios (`ModernDialog`), nunca los del sistema.
- Español e inglés, cambiables en caliente desde Configuración y desde Acerca de.
- Pantalla «Acerca de» con la estructura canónica: cabecera, contacto, idioma, privacidad, licencia
  y aviso legal.

### Notas técnicas
- `AndroidX.Core.Core.Ktx` fijado a la misma versión que `AndroidX.Core` para evitar el fallo de
  empaquetado por duplicado de `androidx.core.animation.AnimatorKt`.
- `androidx.media` se usa a pesar de estar marcada como obsoleta: es la interfaz que Android Auto
  sigue exigiendo. La migración a Media3 queda anotada como mejora futura.
