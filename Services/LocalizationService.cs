using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

/// <inheritdoc cref="ILocalizationService"/>
/// <remarks>
/// El catalogo vive aqui, en un unico servicio, igual que en el resto de apps sOCratic. La
/// constitucion (seccion 8) admite servicio de localizacion o ficheros de recursos; se elige el
/// servicio para que un texto que falte se detecte al compilar la lista, no en tiempo de ejecucion.
/// El ingles es el idioma por defecto y hace de respaldo: al usuario nunca se le muestra la clave.
/// </remarks>
public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "en";

    private static readonly string[] SupportedLanguages = ["en", "es"];

    private readonly ISettingsService _settings;
    private readonly ILogger<LocalizationService> _logger;
    private string _current = DefaultLanguage;

    public LocalizationService(ISettingsService settings, ILogger<LocalizationService> logger)
    {
        _settings = settings;
        _logger = logger;
        SetLanguage(_settings.Language);
    }

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _current;

    public string SelectedLanguage => _settings.Language;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    public string this[string key]
    {
        get
        {
            var table = _current == "es" ? Spanish : English;
            if (table.TryGetValue(key, out var value))
                return value;

            if (English.TryGetValue(key, out var fallback))
            {
                _logger.LogWarning("Missing {Language} translation for key {Key}", _current, key);
                return fallback;
            }

            // Seccion 8: nunca se muestra la clave interna. Vale mas un hueco que una fuga.
            _logger.LogError("Unknown translation key {Key}", key);
            return string.Empty;
        }
    }

    public string Format(string key, params object?[] arguments)
    {
        var template = this[key];
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        try
        {
            return string.Format(CurrentCulture, template, arguments);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Malformed translation template for key {Key}", key);
            return template;
        }
    }

    public void SetLanguage(string? languageCode)
    {
        _settings.Language = languageCode ?? string.Empty;

        var resolved = Resolve(languageCode);
        _current = resolved;
        CurrentCulture = CultureInfo.GetCultureInfo(resolved);

        // Fechas, numeros y duraciones siguen el idioma elegido (constitucion 8).
        CultureInfo.DefaultThreadCurrentCulture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Idioma efectivo: el elegido si esta soportado; si se pide seguir al sistema, el del sistema
    /// cuando lo este; en cualquier otro caso, ingles.
    /// </summary>
    private static string Resolve(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            return IsSupported(languageCode) ? languageCode : DefaultLanguage;

        try
        {
            var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return IsSupported(system) ? system : DefaultLanguage;
        }
        catch (CultureNotFoundException)
        {
            return DefaultLanguage;
        }
    }

    private static bool IsSupported(string languageCode) =>
        SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase);

    // ======================================================================================
    //  Catalogo. Las dos tablas tienen exactamente las mismas claves (constitucion 8).
    // ======================================================================================

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // --- Identidad y menu ---
        ["AppName"] = "Music Player",
        ["AppTagline"] = "Your music, grouped by artist",
        ["MenuLibrary"] = "Library",
        ["MenuNowPlaying"] = "Now playing",
        ["MenuSettings"] = "Settings",
        ["MenuAbout"] = "About",

        // --- Comunes ---
        ["Accept"] = "Accept",
        ["Cancel"] = "Cancel",
        ["Close"] = "Close",
        ["Create"] = "Create",
        ["Delete"] = "Delete",
        ["Understood"] = "Got it",
        ["Save"] = "Save",
        ["Back"] = "Back",
        ["Error"] = "Error",

        // --- Biblioteca ---
        ["LibraryTitle"] = "Library",
        ["TabArtists"] = "Artists",
        ["TabSongs"] = "Songs",
        ["TabPlaylists"] = "Playlists",
        ["SearchPlaceholder"] = "Search artist, song or album",
        ["ScanningLibrary"] = "Scanning the device…",
        ["EmptyLibraryTitle"] = "No music found",
        ["EmptyLibraryMessage"] = "Copy some audio files to the device and scan again.",
        ["NoResultsTitle"] = "Nothing matches",
        ["NoResultsMessage"] = "No artist, song or album matches your search.",
        ["NoPlaylistsTitle"] = "No playlists yet",
        ["NoPlaylistsMessage"] = "Create a playlist and add songs to it from any song menu.",
        ["EmptyPlaylistMessage"] = "This playlist has no songs yet.",
        ["PermissionTitle"] = "Access to your music",
        ["PermissionMessage"] = "Music Player needs permission to read the audio files stored on this device. Nothing is uploaded anywhere.",
        ["PermissionDeniedTitle"] = "Permission denied",
        ["PermissionDeniedMessage"] = "Without access to your audio files there is nothing to play. You can grant it later from the system settings.",
        ["GrantAccess"] = "Grant access",
        ["SongCountOne"] = "1 song",
        ["SongCountMany"] = "{0} songs",
        ["ArtistCountMany"] = "{0} artists",
        ["ScanCompleteFormat"] = "{0} songs · {1} artists",

        // --- Acciones sobre una cancion ---
        ["SongActionsTitle"] = "Song",
        ["ActionPlay"] = "Play",
        ["ActionAddToPlaylist"] = "Add to playlists",
        ["ActionGoToArtist"] = "Go to artist",
        ["ActionDelete"] = "Delete from device",
        ["DeleteSongTitle"] = "Delete song",
        ["DeleteSongMessage"] = "\"{0}\" will be deleted from this device. This cannot be undone.",
        ["SongDeleted"] = "Song deleted",
        ["DeleteFailed"] = "The song could not be deleted",
        ["DeleteCancelled"] = "Nothing was deleted",

        // --- Listas de reproduccion ---
        ["NewPlaylist"] = "New playlist",
        ["PlaylistNameTitle"] = "Playlist name",
        ["PlaylistNameMessage"] = "Give the new playlist a name.",
        ["PlaylistNamePlaceholder"] = "My playlist",
        ["PlaylistExists"] = "There is already a playlist with that name",
        ["RenamePlaylistTitle"] = "Rename playlist",
        ["DeletePlaylistTitle"] = "Delete playlist",
        ["DeletePlaylistMessage"] = "\"{0}\" will be deleted. The songs stay on the device.",
        ["PlaylistDeleted"] = "Playlist deleted",
        ["RemoveFromPlaylist"] = "Remove from this playlist",
        ["SelectPlaylistsTitle"] = "Add to playlists",
        ["SelectPlaylistsHint"] = "Pick every playlist this song should belong to.",
        ["AddedToPlaylistsOne"] = "Added to 1 playlist",
        ["AddedToPlaylistsMany"] = "Added to {0} playlists",
        ["RemovedFromPlaylists"] = "Removed from every playlist",
        ["PlaylistActionsTitle"] = "Playlist",

        // --- Reproduccion ---
        ["NowPlayingTitle"] = "Now playing",
        ["NothingPlayingTitle"] = "Nothing is playing",
        ["NothingPlayingMessage"] = "Pick a song from your library to start.",
        ["UnknownArtist"] = "Unknown artist",
        ["UnknownTitle"] = "Untitled",
        ["UnknownAlbum"] = "Unknown album",
        ["PlayAll"] = "Play all",
        ["ShufflePlay"] = "Shuffle",
        ["QueuePositionFormat"] = "{0} of {1}",
        ["ShuffleOn"] = "Shuffle on",
        ["ShuffleOff"] = "Shuffle off",
        ["RepeatOff"] = "Repeat off",
        ["RepeatAll"] = "Repeat all",
        ["RepeatOne"] = "Repeat one",
        ["PlaybackFailed"] = "This file could not be played",

        // --- Grupo / compositor ---
        ["ArtistLookupRunning"] = "Looking up artist information…",
        ["ArtistLookupNotFound"] = "No information found for this artist",
        ["ArtistLookupDisabled"] = "Online lookup is off. Turn it on in Settings to show artist photos and biographies.",
        ["EnableOnlineLookup"] = "Turn on online lookup",
        ["ArtistImageSource"] = "Image and text: Wikipedia / Wikidata (CC BY-SA), artist matched with MusicBrainz.",

        // --- Configuracion ---
        ["SettingsTitle"] = "Settings",
        ["SectionLibrary"] = "Library",
        ["RescanLibrary"] = "Scan the device again",
        ["RescanHint"] = "Reads the audio files indexed by the system. Run it after copying new music.",
        ["GroupByComposer"] = "Group by composer",
        ["GroupByComposerHint"] = "Uses the composer instead of the performer to group songs. Useful for classical music.",
        ["SectionOnline"] = "Online information",
        ["OnlineArtistInfo"] = "Look up artist photos and biographies",
        ["OnlineArtistInfoHint"] = "Off by default. When you turn it on, only the artist name is sent to MusicBrainz and Wikidata to find a photo and a short biography. No song, file name or personal data ever leaves the device.",
        ["ClearImageCache"] = "Delete downloaded images",
        ["ImageCacheCleared"] = "Downloaded images deleted",
        ["SectionAndroidAuto"] = "Android Auto",
        ["AndroidAutoHint"] = "The library is available in your car: artists, playlists and all songs, with steering-wheel and voice controls. Nothing to configure here.",
        ["SectionLanguage"] = "Language",
        ["LanguageHint"] = "The language applies right away.",
        ["SpanishButton"] = "🇪🇸 Español",
        ["EnglishButton"] = "🇺🇸 English",

        // --- Acerca de ---
        ["AboutTitle"] = "About",
        ["AppDescription"] = "Plays the music stored on your device, grouped by artist or composer, with playlists and Android Auto support.",
        ["VersionFormat"] = "Version {0}",
        ["ContactTitle"] = "Contact",
        ["ContactHint"] = "Questions, bugs and ideas are welcome.",
        ["PrivacyTitle"] = "Privacy",
        ["PrivacyText"] = "Music Player reads the audio files on your device and keeps your playlists in the app's own storage. There are no accounts, no ads and no analytics. Artist photos and biographies are only looked up if you turn that option on, and even then the only thing sent is the artist name.",
        ["LicenseTitle"] = "License",
        ["LicenseText"] = "Free software released under the MIT license. The source code can be used, studied and modified by anyone.",
        ["LegalTitle"] = "Legal notice",
        ["LegalText1"] = "This software is provided \"as is\", without warranty of any kind, express or implied.",
        ["LegalText2"] = "In no event shall the authors be liable for any claim, damages or other liability arising from the use of this software.",
        ["WarningText"] = "⚠️ Use at your own risk",

        // --- Comprobacion de version ---
        ["UpdateAvailableTitle"] = "Update available",
        ["UpdateAvailableMessage"] = "Version {0} is available. You have {1}. Do you want to open the download page?",
        ["UpdateNow"] = "Open",
        ["UpdateLater"] = "Later",
    };

    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal)
    {
        // --- Identidad y menu ---
        ["AppName"] = "Music Player",
        ["AppTagline"] = "Tu música, agrupada por grupo",
        ["MenuLibrary"] = "Biblioteca",
        ["MenuNowPlaying"] = "Reproduciendo",
        ["MenuSettings"] = "Configuración",
        ["MenuAbout"] = "Acerca de",

        // --- Comunes ---
        ["Accept"] = "Aceptar",
        ["Cancel"] = "Cancelar",
        ["Close"] = "Cerrar",
        ["Create"] = "Crear",
        ["Delete"] = "Eliminar",
        ["Understood"] = "Entendido",
        ["Save"] = "Guardar",
        ["Back"] = "Volver",
        ["Error"] = "Error",

        // --- Biblioteca ---
        ["LibraryTitle"] = "Biblioteca",
        ["TabArtists"] = "Grupos",
        ["TabSongs"] = "Canciones",
        ["TabPlaylists"] = "Listas",
        ["SearchPlaceholder"] = "Buscar grupo, canción o álbum",
        ["ScanningLibrary"] = "Explorando el dispositivo…",
        ["EmptyLibraryTitle"] = "No se ha encontrado música",
        ["EmptyLibraryMessage"] = "Copia archivos de audio al dispositivo y vuelve a explorar.",
        ["NoResultsTitle"] = "Sin resultados",
        ["NoResultsMessage"] = "Ningún grupo, canción o álbum coincide con la búsqueda.",
        ["NoPlaylistsTitle"] = "Todavía no hay listas",
        ["NoPlaylistsMessage"] = "Crea una lista y añade canciones desde el menú de cualquier canción.",
        ["EmptyPlaylistMessage"] = "Esta lista aún no tiene canciones.",
        ["PermissionTitle"] = "Acceso a tu música",
        ["PermissionMessage"] = "Music Player necesita permiso para leer los archivos de audio guardados en este dispositivo. No se sube nada a ninguna parte.",
        ["PermissionDeniedTitle"] = "Permiso denegado",
        ["PermissionDeniedMessage"] = "Sin acceso a tus archivos de audio no hay nada que reproducir. Puedes concederlo más tarde desde los ajustes del sistema.",
        ["GrantAccess"] = "Conceder acceso",
        ["SongCountOne"] = "1 canción",
        ["SongCountMany"] = "{0} canciones",
        ["ArtistCountMany"] = "{0} grupos",
        ["ScanCompleteFormat"] = "{0} canciones · {1} grupos",

        // --- Acciones sobre una cancion ---
        ["SongActionsTitle"] = "Canción",
        ["ActionPlay"] = "Reproducir",
        ["ActionAddToPlaylist"] = "Añadir a listas",
        ["ActionGoToArtist"] = "Ir al grupo",
        ["ActionDelete"] = "Eliminar del dispositivo",
        ["DeleteSongTitle"] = "Eliminar canción",
        ["DeleteSongMessage"] = "«{0}» se eliminará de este dispositivo. No se puede deshacer.",
        ["SongDeleted"] = "Canción eliminada",
        ["DeleteFailed"] = "No se ha podido eliminar la canción",
        ["DeleteCancelled"] = "No se ha eliminado nada",

        // --- Listas de reproduccion ---
        ["NewPlaylist"] = "Nueva lista",
        ["PlaylistNameTitle"] = "Nombre de la lista",
        ["PlaylistNameMessage"] = "Pon un nombre a la nueva lista.",
        ["PlaylistNamePlaceholder"] = "Mi lista",
        ["PlaylistExists"] = "Ya existe una lista con ese nombre",
        ["RenamePlaylistTitle"] = "Renombrar lista",
        ["DeletePlaylistTitle"] = "Eliminar lista",
        ["DeletePlaylistMessage"] = "«{0}» se eliminará. Las canciones seguirán en el dispositivo.",
        ["PlaylistDeleted"] = "Lista eliminada",
        ["RemoveFromPlaylist"] = "Quitar de esta lista",
        ["SelectPlaylistsTitle"] = "Añadir a listas",
        ["SelectPlaylistsHint"] = "Marca todas las listas a las que quieras añadir la canción.",
        ["AddedToPlaylistsOne"] = "Añadida a 1 lista",
        ["AddedToPlaylistsMany"] = "Añadida a {0} listas",
        ["RemovedFromPlaylists"] = "Quitada de todas las listas",
        ["PlaylistActionsTitle"] = "Lista",

        // --- Reproduccion ---
        ["NowPlayingTitle"] = "Reproduciendo",
        ["NothingPlayingTitle"] = "No hay nada en reproducción",
        ["NothingPlayingMessage"] = "Elige una canción de la biblioteca para empezar.",
        ["UnknownArtist"] = "Grupo desconocido",
        ["UnknownTitle"] = "Sin título",
        ["UnknownAlbum"] = "Álbum desconocido",
        ["PlayAll"] = "Reproducir todo",
        ["ShufflePlay"] = "Aleatorio",
        ["QueuePositionFormat"] = "{0} de {1}",
        ["ShuffleOn"] = "Reproducción aleatoria activada",
        ["ShuffleOff"] = "Reproducción aleatoria desactivada",
        ["RepeatOff"] = "Sin repetición",
        ["RepeatAll"] = "Repetir todo",
        ["RepeatOne"] = "Repetir una",
        ["PlaybackFailed"] = "No se ha podido reproducir este archivo",

        // --- Grupo / compositor ---
        ["ArtistLookupRunning"] = "Buscando información del grupo…",
        ["ArtistLookupNotFound"] = "No se ha encontrado información de este grupo",
        ["ArtistLookupDisabled"] = "La búsqueda en línea está desactivada. Actívala en Configuración para ver fotos y biografías de los grupos.",
        ["EnableOnlineLookup"] = "Activar búsqueda en línea",
        ["ArtistImageSource"] = "Imagen y texto: Wikipedia / Wikidata (CC BY-SA); grupo identificado con MusicBrainz.",

        // --- Configuracion ---
        ["SettingsTitle"] = "Configuración",
        ["SectionLibrary"] = "Biblioteca",
        ["RescanLibrary"] = "Volver a explorar el dispositivo",
        ["RescanHint"] = "Lee los archivos de audio indexados por el sistema. Úsalo después de copiar música nueva.",
        ["GroupByComposer"] = "Agrupar por compositor",
        ["GroupByComposerHint"] = "Usa el compositor en lugar del intérprete para agrupar las canciones. Útil para música clásica.",
        ["SectionOnline"] = "Información en línea",
        ["OnlineArtistInfo"] = "Buscar fotos y biografías de los grupos",
        ["OnlineArtistInfoHint"] = "Desactivado por defecto. Si lo activas, solo se envía el nombre del grupo a MusicBrainz y Wikidata para buscar una foto y una biografía breve. Ninguna canción, nombre de archivo ni dato personal sale del dispositivo.",
        ["ClearImageCache"] = "Borrar las imágenes descargadas",
        ["ImageCacheCleared"] = "Imágenes descargadas borradas",
        ["SectionAndroidAuto"] = "Android Auto",
        ["AndroidAutoHint"] = "La biblioteca está disponible en el coche: grupos, listas y todas las canciones, con los controles del volante y por voz. Aquí no hay nada que configurar.",
        ["SectionLanguage"] = "Idioma",
        ["LanguageHint"] = "El idioma se aplica de inmediato.",
        ["SpanishButton"] = "🇪🇸 Español",
        ["EnglishButton"] = "🇺🇸 English",

        // --- Acerca de ---
        ["AboutTitle"] = "Acerca de",
        ["AppDescription"] = "Reproduce la música guardada en tu dispositivo, agrupada por grupo o compositor, con listas de reproducción y compatible con Android Auto.",
        ["VersionFormat"] = "Versión {0}",
        ["ContactTitle"] = "Contacto",
        ["ContactHint"] = "Dudas, fallos e ideas son bienvenidos.",
        ["PrivacyTitle"] = "Privacidad",
        ["PrivacyText"] = "Music Player lee los archivos de audio de tu dispositivo y guarda tus listas en el almacenamiento propio de la aplicación. No hay cuentas, ni anuncios, ni analítica. Las fotos y biografías de los grupos solo se buscan si activas esa opción y, aun así, lo único que se envía es el nombre del grupo.",
        ["LicenseTitle"] = "Licencia",
        ["LicenseText"] = "Software libre publicado bajo la licencia MIT. Cualquiera puede usar, estudiar y modificar el código fuente.",
        ["LegalTitle"] = "Aviso legal",
        ["LegalText1"] = "Este software se entrega «tal cual», sin garantías de ningún tipo, expresas o implícitas.",
        ["LegalText2"] = "En ningún caso los autores serán responsables de reclamaciones, daños u otras responsabilidades derivadas del uso de este software.",
        ["WarningText"] = "⚠️ Uso bajo su propio riesgo",

        // --- Comprobacion de version ---
        ["UpdateAvailableTitle"] = "Actualización disponible",
        ["UpdateAvailableMessage"] = "Está disponible la versión {0}. Tienes la {1}. ¿Quieres abrir la página de descarga?",
        ["UpdateNow"] = "Abrir",
        ["UpdateLater"] = "Más tarde",
    };
}
