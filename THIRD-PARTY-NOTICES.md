# Avisos de terceros — Music Player

Inventario de dependencias (constitución 4). **Todas** son de licencia permisiva y aptas para uso
comercial. Antes de añadir una dependencia nueva hay que verificar su licencia y registrarla aquí.

---

## Paquetes NuGet

| Dependencia | Uso | Licencia | Titular |
|---|---|---|---|
| `Microsoft.Maui.Controls` | Framework de interfaz de la aplicación. | MIT | Microsoft |
| `Microsoft.Extensions.Logging.Debug` | Trazas de depuración, solo en configuración Debug. | MIT | Microsoft |
| `Xamarin.AndroidX.Media` | Enlaces .NET de `androidx.media`: `MediaSessionCompat`, `MediaBrowserServiceCompat` y el estilo de notificación de medios. Es la interfaz que Android Auto exige para exponer una biblioteca navegable. | MIT AND Apache-2.0 | Microsoft (enlaces) / The Android Open Source Project (biblioteca) |
| `Xamarin.AndroidX.Core.Core.Ktx` | Fijado explícitamente para que coincida con la versión de `AndroidX.Core` que arrastra `AndroidX.Media`. Sin esa alineación, ambas definen `androidx.core.animation.AnimatorKt` y el empaquetado falla. | MIT AND Apache-2.0 | Microsoft / The Android Open Source Project |

Las dependencias transitivas de estos paquetes (AndroidX Annotation, Collection, Core, JSpecify,
Kotlin StdLib) son todas MIT o Apache-2.0.

## APIs del sistema operativo

El uso de las APIs de Android **no contamina la licencia del proyecto** (constitución 4). Se usan:

- `android.media.MediaPlayer` — decodificación y reproducción. Es lo que da soporte a MP3, AAC,
  FLAC, OGG Vorbis, Opus, WAV, MIDI y AMR sin incorporar ningún códec de terceros.
- `android.provider.MediaStore` — índice de medios del dispositivo y borrado de ficheros.
- `android.media.AudioManager` — foco de audio.
- `android.app.NotificationManager` — notificación de reproducción.

## Servicios en línea (opcionales, desactivados por defecto)

Solo se consultan si el usuario activa *Buscar fotos y biografías de los grupos* en Configuración.
Ninguno requiere clave de API ni registro.

| Servicio | Qué se envía | Qué se obtiene | Licencia de los datos |
|---|---|---|---|
| [MusicBrainz](https://musicbrainz.org/) | El nombre del grupo. | El identificador del grupo y su enlace a Wikidata. | CC0 (datos básicos) |
| [Wikidata](https://www.wikidata.org/) | El identificador del grupo. | El nombre del fichero de la foto y los enlaces a Wikipedia. | CC0 |
| [Wikimedia Commons](https://commons.wikimedia.org/) | El nombre del fichero de la foto. | La imagen del grupo. | CC BY-SA / dominio público según el fichero |
| [Wikipedia](https://wikipedia.org/) | El título del artículo. | El resumen del artículo. | CC BY-SA 4.0 |

La aplicación se identifica ante MusicBrainz con un *User-Agent* propio y respeta su límite de una
petición por segundo, como exigen sus condiciones de uso.

Las imágenes y los textos de Wikipedia/Commons son de licencia **CC BY-SA**, que obliga a citar la
fuente: la pantalla de grupo muestra siempre la atribución bajo la reseña.

## Recursos propios

Los iconos, el icono de aplicación, la pantalla de arranque y todo el código de la aplicación son de
desarrollo propio y se publican bajo la licencia MIT del proyecto.
