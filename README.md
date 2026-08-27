# Music Player

Reproductor de música para Android hecho en .NET MAUI. Reproduce la música guardada en el
dispositivo, la agrupa por grupo o compositor, permite hacer listas de reproducción y funciona en
Android Auto.

Forma parte de las aplicaciones **sOCratic** y cumple la [constitución del repositorio](../../CONSTITUCION.md),
la [de móvil](../CONSTITUCION-MOBILE.md) y el submódulo de gobernanza `constitution/`.

---

## Qué hace

| Función | Detalle |
|---|---|
| **Reproducción** | Todos los formatos que soporta Android: MP3, AAC/M4A, FLAC, OGG Vorbis, Opus, WAV, MIDI, AMR y audio en MKV. |
| **Controles** | Anterior, reproducir/pausa y siguiente, con barra de progreso arrastrable y tiempo reproducido / duración. |
| **Biblioteca** | Escanea el índice de medios del sistema y agrupa las canciones por grupo (o por compositor, opcional). |
| **Fotos de grupo** | Busca la foto y una reseña del grupo en internet. **Desactivado por defecto** (ver *Privacidad*). |
| **Listas** | Crear, renombrar y borrar listas; desde una canción se puede marcar en varias listas a la vez. |
| **Borrar canciones** | Elimina el fichero del dispositivo con la confirmación del sistema. |
| **Android Auto** | La biblioteca completa —grupos, listas y todas las canciones— navegable en el coche, con los controles del volante y por voz. |
| **Idiomas** | Español e inglés, cambiables en caliente. |
| **Temas** | Claro y oscuro, siguiendo al sistema. |

## Arquitectura

```
MusicPlayer/
├── Models/          Song, ArtistGroup, Playlist, RepeatMode y los modelos de fila
├── Services/        Contratos e implementaciones independientes de plataforma
├── Helpers/         Utilidades puras (formato de tiempo, menú de canción, acceso al contenedor)
├── Pages/           Interfaces XAML con code-behind delgado
├── Controls/        MiniPlayerView, la barra de reproducción compartida
├── Resources/       Iconos SVG, estilos, splash e icono de aplicación
└── Platforms/Android/
    ├── MusicService.cs          Motor de reproducción + MediaBrowserService (Android Auto)
    ├── PlaybackService.cs       Puente entre la interfaz y el servicio
    ├── MusicLibraryService.cs   Escaneo del MediaStore, agrupación y borrado
    ├── MediaAccessService.cs    Permiso de lectura de audio
    └── Resources/xml/automotive_app_desc.xml
```

**Una sola reproducción.** El coche, la notificación, los botones del volante y la interfaz de la
aplicación mandan sobre la misma `MediaSessionCompat`. No hay dos reproductores que puedan
discrepar sobre qué está sonando.

## Privacidad

- La música y las listas **no salen del dispositivo**. Las listas se guardan en `playlists.json`,
  dentro del almacenamiento privado de la aplicación.
- No hay cuentas, ni anuncios, ni analítica.
- La búsqueda de fotos y reseñas de grupos está **apagada por defecto**. Al activarla en
  *Configuración*, lo único que sale del dispositivo es **el nombre del grupo**, que se consulta en
  MusicBrainz (para identificarlo) y en Wikidata/Wikipedia (para la foto y el texto). Ni títulos de
  canción, ni nombres de fichero, ni identificadores.
- La única otra salida de red es la comprobación de versión al arrancar (constitución 15).

## Permisos

| Permiso | Para qué |
|---|---|
| `READ_MEDIA_AUDIO` | Leer los archivos de audio del dispositivo. Es la función principal. |
| `READ_EXTERNAL_STORAGE` (`maxSdkVersion=32`) | Lo mismo en Android 12 y anteriores, donde el permiso acotado no existe. |
| `WRITE_EXTERNAL_STORAGE` (`maxSdkVersion=28`) | Borrar canciones en Android 9 y anteriores. Desde Android 10 lo gestiona MediaStore sin permiso. |
| `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_MEDIA_PLAYBACK` | Seguir sonando con la pantalla apagada y en el coche. |
| `POST_NOTIFICATIONS` | La notificación con los controles de reproducción. |
| `WAKE_LOCK` | Mantener la CPU despierta mientras suena la música. |
| `INTERNET`, `ACCESS_NETWORK_STATE` | Comprobación de versión y, si el usuario la activa, la búsqueda de fotos de grupo. |

## Compilar

```powershell
# APK de depuración
dotnet build MusicPlayer.csproj -c Debug -f net10.0-android36.0

# AAB de release firmado (la contraseña se pasa por CLI, nunca va en el repositorio)
dotnet publish MusicPlayer.csproj -c Release -f net10.0-android36.0 `
  -p:AndroidPackageFormat=aab `
  -p:AndroidSigningStorePass=<pass> -p:AndroidSigningKeyPass=<pass>
```

## Probar

```powershell
# Genera pistas sintéticas con etiquetas ID3 (nunca música real, constitución A.8.2)
..\..\testing\MusicPlayer\make_test_audio.ps1

# Instala y lanza en MuMu Player
.\install_mumu.ps1 -BuildFirst -Launch -PushTestAudio
```

### Probar Android Auto

Android Auto no funciona en MuMu. Para validarlo hace falta un dispositivo real con la aplicación
*Android Auto* instalada y su **Desktop Head Unit** (DHU), o un coche compatible:

1. Activar las opciones de desarrollador de Android Auto y marcar *Unknown sources*.
2. `adb forward tcp:5277 tcp:5277` y arrancar el DHU.
3. Music Player debe aparecer en la lista de aplicaciones de medios con tres carpetas: **Grupos**,
   **Listas** y **Canciones**.

## Limitaciones conocidas

- Las listas de Android Auto se acotan a 500 elementos por nodo: el coche no pagina y una lista
  enorme se corta sola.
- El identificador de medios usa `|` como separador; un grupo con `|` en el nombre se reproduce
  igual, pero la cola se arma con toda la biblioteca en vez de con la del grupo.
- `androidx.media` está marcada como obsoleta en favor de Media3. Se usa a propósito porque sigue
  siendo la interfaz que Android Auto exige; la migración a Media3 está anotada como mejora futura.

## Licencia

MIT. Ver [LICENSE](LICENSE) y [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
