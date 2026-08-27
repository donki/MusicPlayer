# Política de privacidad — Music Player

**Última actualización: 27 de agosto de 2026**

Music Player es una aplicación de Socratic. Esta política explica qué datos usa la aplicación y qué
hace con ellos. En resumen: **tu música y tus listas no salen de tu dispositivo**.

## Qué datos usa la aplicación

| Dato | Para qué | Dónde se queda |
|---|---|---|
| Archivos de audio del dispositivo | Reproducirlos y agruparlos por grupo o compositor. | En tu dispositivo. Nunca se copian ni se suben. |
| Etiquetas de las canciones (título, grupo, álbum, compositor) | Ordenar y agrupar la biblioteca. | En tu dispositivo. |
| Tus listas de reproducción | Guardar qué canciones has puesto en cada lista. | En el almacenamiento privado de la aplicación, dentro de tu dispositivo. Se borran al desinstalarla. |
| Tus preferencias (idioma, modo aleatorio, agrupación) | Recordar cómo quieres usar la aplicación. | En el almacenamiento privado de la aplicación. |

## Qué no hace la aplicación

- **No hay cuentas de usuario.** No se pide correo, teléfono ni ningún dato personal.
- **No hay anuncios ni rastreadores.** No se incluye ningún SDK de publicidad ni de analítica.
- **No se recopila información de uso.** Nadie sabe qué escuchas, ni cuándo, ni cuánto.
- **No se sube tu música a ninguna parte.**

## Conexiones a internet

La aplicación solo se conecta a internet en dos casos, y ninguno envía datos personales:

1. **Comprobación de versión al arrancar.** Se descarga un pequeño fichero del repositorio del
   proyecto para saber si hay una versión más reciente. No se envía nada sobre ti ni sobre tu
   dispositivo.

2. **Fotos y biografías de grupos (opcional, desactivado por defecto).** Solo si lo activas en
   *Configuración › Información en línea*. En ese caso se envía **únicamente el nombre del grupo**
   a estos servicios públicos, que no requieren registro ni clave:
   - **MusicBrainz** (`musicbrainz.org`), para identificar al grupo.
   - **Wikidata** (`wikidata.org`) y **Wikimedia Commons** (`commons.wikimedia.org`), para obtener
     la fotografía.
   - **Wikipedia** (`wikipedia.org`), para obtener un resumen breve.

   No se envía ningún título de canción, ni nombre de archivo, ni identificador de dispositivo, ni
   dato personal. Las imágenes descargadas se guardan en tu dispositivo y puedes borrarlas en
   cualquier momento desde *Configuración › Borrar las imágenes descargadas*.

   Estos servicios reciben tu dirección IP, como en cualquier petición de internet. Sus propias
   políticas de privacidad son las que aplican a ese dato.

## Permisos

- **Acceso a los archivos de audio** (`READ_MEDIA_AUDIO`, o el permiso de almacenamiento en
  Android 12 y anteriores): sin él la aplicación no tiene nada que reproducir. Solo se leen archivos
  de audio.
- **Escritura en almacenamiento** (solo Android 9 y anteriores): únicamente para borrar una canción
  cuando tú lo pides. Desde Android 10 el borrado lo confirma el propio sistema.
- **Servicio en primer plano de reproducción y notificaciones**: para que la música siga sonando con
  la pantalla apagada y para mostrar los controles.
- **Mantener la CPU activa** (`WAKE_LOCK`): evita que la reproducción se corte con la pantalla
  apagada.
- **Internet**: para los dos usos descritos arriba.

## Android Auto

Cuando conectas el teléfono al coche, Android Auto puede consultar tu biblioteca para mostrarla en
la pantalla del vehículo. Esa consulta ocurre entre aplicaciones de tu propio dispositivo; no sale
nada a internet. La aplicación solo permite esa consulta a los controladores de medios del sistema,
no a cualquier aplicación instalada.

## Menores

La aplicación no está dirigida específicamente a menores y no recopila datos de nadie.

## Cambios en esta política

Si esta política cambia, la nueva versión se publicará junto a la aplicación y se actualizará la
fecha de la cabecera.

## Contacto

**jsoladelarosa@gmail.com**
