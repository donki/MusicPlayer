using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Microsoft.Extensions.Logging;
using MusicPlayer.Models;
using MusicPlayer.Services;
using AndroidUri = Android.Net.Uri;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="IMusicLibraryService"/>
/// <remarks>
/// Lee el indice de medios del sistema (MediaStore) en vez de recorrer el sistema de archivos: es
/// lo unico que funciona con el almacenamiento con alcance de Android moderno, ya trae las
/// etiquetas leidas y no exige el permiso amplio de almacenamiento (constitucion A.3).
/// </remarks>
public sealed class MusicLibraryService : IMusicLibraryService
{
    private const string UnknownColumnValue = "<unknown>";

    /// <summary>Columnas presentes en todas las versiones de Android soportadas.</summary>
    private static readonly string[] BaseProjection =
    [
        "_id", "title", "artist", "album", "album_id", "composer", "duration", "track", "year", "_data",
    ];

    /// <summary>Solo pistas marcadas como musica: deja fuera tonos, notificaciones y grabaciones.</summary>
    private const string MusicOnlySelection = "is_music != 0";

    private const string TitleSortOrder = "title COLLATE NOCASE ASC";

    private readonly ISettingsService _settings;
    private readonly IMediaAccessService _access;
    private readonly IArtistInfoService _artistInfo;
    private readonly ISongTagsService _tags;
    private readonly ILogger<MusicLibraryService> _logger;

    /// <summary>Tope de caratulas retenidas en memoria a la vez.</summary>
    private const int MaxCachedAlbumArt = 300;

    private readonly Lock _albumArtGate = new();
    private readonly Dictionary<long, ImageSource?> _albumArt = [];

    private IReadOnlyList<Song> _songs = [];
    private IReadOnlyList<ArtistGroup> _artists = [];
    private Dictionary<long, Song> _byId = [];

    public MusicLibraryService(
        ISettingsService settings,
        IMediaAccessService access,
        IArtistInfoService artistInfo,
        ISongTagsService tags,
        ILogger<MusicLibraryService> logger)
    {
        _settings = settings;
        _access = access;
        _artistInfo = artistInfo;
        _tags = tags;
        _logger = logger;
    }

    public event EventHandler? LibraryChanged;

    public IReadOnlyList<Song> Songs => _songs;

    public IReadOnlyList<ArtistGroup> Artists => _artists;

    public bool HasScanned { get; private set; }

    public async Task<bool> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!await _access.IsGrantedAsync().ConfigureAwait(false))
            return false;

        var songs = await Task.Run(() => Query(cancellationToken), cancellationToken).ConfigureAwait(false);

        _songs = songs;
        _byId = songs.ToDictionary(song => song.Id);
        _artists = Group(songs);
        HasScanned = true;

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Song? FindById(long id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<Song> FindByIds(IEnumerable<long> ids)
    {
        var result = new List<Song>();
        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var song))
                result.Add(song);
        }

        return result;
    }

    public ArtistGroup? FindArtist(string name) =>
        _artists.FirstOrDefault(artist => string.Equals(artist.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public string? GetAlbumArtUri(Song song) =>
        song.AlbumId > 0 ? $"content://media/external/audio/albumart/{song.AlbumId}" : null;

    /// <summary>
    /// Caratula del album. MAUI no sabe abrir una URI <c>content://</c>, asi que se le da un
    /// origen de flujo que la resuelve por el proveedor de contenidos. El resultado se cachea por
    /// album —incluida la ausencia de caratula— para no volver a intentar abrir lo que no existe
    /// cada vez que la lista se desplaza.
    /// </summary>
    public ImageSource? GetAlbumArt(Song song)
    {
        if (song.AlbumId <= 0)
            return null;

        lock (_albumArtGate)
        {
            if (_albumArt.TryGetValue(song.AlbumId, out var cached))
                return cached;
        }

        var uri = AndroidUri.Parse($"content://media/external/audio/albumart/{song.AlbumId}");
        ImageSource? source = null;

        if (uri is not null && HasAlbumArt(uri))
            source = ImageSource.FromStream(() => OpenAlbumArt(uri));

        lock (_albumArtGate)
        {
            // El cache no crece sin limite: una biblioteca grande con miles de albumes no puede
            // acabar con todas las caratulas retenidas en memoria.
            if (_albumArt.Count >= MaxCachedAlbumArt)
                _albumArt.Clear();

            _albumArt[song.AlbumId] = source;
        }

        return source;
    }

    private bool HasAlbumArt(AndroidUri uri)
    {
        try
        {
            using var stream = global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
            return stream is not null;
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.IO.IOException)
        {
            return false;
        }
    }

    private Stream? OpenAlbumArt(AndroidUri uri)
    {
        try
        {
            return global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.IO.IOException)
        {
            _logger.LogDebug(ex, "The album art at {Uri} could not be opened.", uri);
            return null;
        }
    }

    public async Task<TagsOutcome> UpdateTagsAsync(Song song, SongTags tags)
    {
        // Primero la correccion propia: es la que hace que la busqueda y la agrupacion acierten, y
        // la unica que sobrevive a un reindexado del sistema.
        _tags.Save(song.Id, tags);

        var inSystem = await TryUpdateMediaStoreAsync(song, tags).ConfigureAwait(false);
        await ScanAsync().ConfigureAwait(false);

        return inSystem ? TagsOutcome.Saved : TagsOutcome.SavedInAppOnly;
    }

    public async Task ResetTagsAsync(Song song)
    {
        _tags.Forget([song.Id]);
        await ScanAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Traslada la correccion al indice de medios para que la vean las demas aplicaciones. Desde
    /// Android 11 el fichero no es nuestro, asi que el sistema pide permiso de escritura al
    /// usuario; si lo niega no pasa nada grave: dentro de la aplicacion la correccion ya vale.
    /// </summary>
    private async Task<bool> TryUpdateMediaStoreAsync(Song song, SongTags tags)
    {
        var resolver = global::Android.App.Application.Context.ContentResolver;
        var collection = MediaStore.Audio.Media.ExternalContentUri;
        if (resolver is null || collection is null)
            return false;

        var uri = ContentUris.WithAppendedId(collection, song.Id);
        if (uri is null)
            return false;

        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !await RequestWriteAccessAsync(resolver, uri).ConfigureAwait(false))
            return false;

        try
        {
            var values = new ContentValues();
            values.Put("title", tags.Title);
            values.Put("artist", tags.Artist);
            values.Put("album_artist", tags.AlbumArtist);
            values.Put("album", tags.Album);
            values.Put("composer", tags.Composer);
            values.Put("track", tags.Track);
            values.Put("year", tags.Year);

            return resolver.Update(uri, values, null, null) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The media index could not be updated for song {SongId}.", song.Id);
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private async Task<bool> RequestWriteAccessAsync(ContentResolver resolver, AndroidUri uri)
    {
        var activity = MainActivity.Current;
        if (activity is null)
            return false;

        var request = MediaStore.CreateWriteRequest(resolver, new List<AndroidUri> { uri });
        if (request?.IntentSender is null)
            return false;

        return await activity.ConfirmSystemRequestAsync(request.IntentSender).ConfigureAwait(false);
    }

    public Task<DeleteOutcome> DeleteAsync(Song song) => DeleteAsync([song]);

    public ImageSource? GetArtistArt(ArtistGroup artist)
    {
        if (artist.ImagePath is not null)
            return ImageSource.FromFile(artist.ImagePath);

        // Se para en la primera que tenga caratula: el resultado esta cacheado por album, asi que
        // recorrer un grupo entero sin ninguna solo cuesta caro la primera vez.
        foreach (var song in artist.Songs)
        {
            if (GetAlbumArt(song) is { } art)
                return art;
        }

        return null;
    }

    public ImageSource? GetArtworkOrArtistArt(Song song)
    {
        if (GetAlbumArt(song) is { } own)
            return own;

        // Se busca por el nombre que se muestra, no por la etiqueta cruda: es el mismo con el que
        // se agrupa la biblioteca, asi que una cancion de un grupo renombrado sigue encontrandolo.
        var name = song.ResolveGroupName(preferComposer: false);
        if (name.Length == 0)
            return null;

        return FindArtist(name) is { } artist ? GetArtistArt(artist) : null;
    }

    public async Task<DeleteOutcome> DeleteAsync(IReadOnlyList<Song> songs)
    {
        if (songs.Count == 0)
            return DeleteOutcome.Failed;

        var context = global::Android.App.Application.Context;
        var resolver = context.ContentResolver;
        if (resolver is null)
            return DeleteOutcome.Failed;

        var collection = MediaStore.Audio.Media.ExternalContentUri;
        if (collection is null)
            return DeleteOutcome.Failed;

        var uris = songs
            .Select(song => ContentUris.WithAppendedId(collection, song.Id))
            .OfType<AndroidUri>()
            .ToList();

        if (uris.Count == 0)
            return DeleteOutcome.Failed;

        try
        {
            var outcome = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? await DeleteWithSystemConfirmationAsync(resolver, uris).ConfigureAwait(false)
                : DeleteDirectly(resolver, uris);

            if (outcome == DeleteOutcome.Deleted)
            {
                _tags.Forget(songs.Select(song => song.Id).ToList());
                await ScanAsync().ConfigureAwait(false);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Count} selected songs could not be deleted.", uris.Count);
            return DeleteOutcome.Failed;
        }
    }

    /// <summary>
    /// Android 11+: la confirmacion la pinta el sistema sobre la actividad. La aplicacion no puede
    /// borrar nada a espaldas del usuario, que es exactamente lo que queremos. El sistema admite
    /// varias URI en una sola peticion, asi que un lote se confirma de una vez.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private async Task<DeleteOutcome> DeleteWithSystemConfirmationAsync(ContentResolver resolver, IList<AndroidUri> uris)
    {
        var activity = MainActivity.Current;
        if (activity is null)
        {
            _logger.LogWarning("A delete was requested with no activity on screen; it was ignored.");
            return DeleteOutcome.Failed;
        }

        var request = MediaStore.CreateDeleteRequest(resolver, uris);
        if (request?.IntentSender is null)
            return DeleteOutcome.Failed;

        var confirmed = await activity.ConfirmSystemRequestAsync(request.IntentSender).ConfigureAwait(false);
        return confirmed ? DeleteOutcome.Deleted : DeleteOutcome.Cancelled;
    }

    /// <summary>
    /// Android 10 y anteriores: borrado directo, con el permiso de escritura del manifiesto. Se
    /// da por bueno si cae al menos una: el resto pueden ser pistas que ya no estaban.
    /// </summary>
    private DeleteOutcome DeleteDirectly(ContentResolver resolver, IEnumerable<AndroidUri> uris) =>
        uris.Sum(uri => resolver.Delete(uri, null, null)) > 0 ? DeleteOutcome.Deleted : DeleteOutcome.Failed;

    // ==================================================================================
    //  Lectura del indice de medios
    // ==================================================================================

    private List<Song> Query(CancellationToken cancellationToken)
    {
        var songs = new List<Song>();
        var resolver = global::Android.App.Application.Context.ContentResolver;
        var collection = MediaStore.Audio.Media.ExternalContentUri;
        if (resolver is null || collection is null)
            return songs;

        using var cursor = OpenCursor(resolver, collection);
        if (cursor is null)
            return songs;

        var idColumn = cursor.GetColumnIndex("_id");
        var titleColumn = cursor.GetColumnIndex("title");
        var artistColumn = cursor.GetColumnIndex("artist");
        var albumArtistColumn = cursor.GetColumnIndex("album_artist");
        var albumColumn = cursor.GetColumnIndex("album");
        var albumIdColumn = cursor.GetColumnIndex("album_id");
        var composerColumn = cursor.GetColumnIndex("composer");
        var durationColumn = cursor.GetColumnIndex("duration");
        var trackColumn = cursor.GetColumnIndex("track");
        var yearColumn = cursor.GetColumnIndex("year");
        var pathColumn = cursor.GetColumnIndex("_data");

        while (cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = ReadLong(cursor, idColumn);
            if (id <= 0)
                continue;

            var path = ReadText(cursor, pathColumn);
            var title = ReadText(cursor, titleColumn);
            if (title.Length == 0)
                title = Path.GetFileNameWithoutExtension(path);

            var contentUri = ContentUris.WithAppendedId(collection, id)?.ToString();
            if (string.IsNullOrEmpty(contentUri))
                continue;

            // La correccion del usuario manda sobre lo que diga el indice: es justo lo que se
            // pidio al editarla, y sin esto un reindexado la desharia.
            songs.Add(_tags.Apply(new Song
            {
                Id = id,
                ContentUri = contentUri,
                FilePath = path,
                Title = title,
                Artist = ReadText(cursor, artistColumn),
                AlbumArtist = ReadText(cursor, albumArtistColumn),
                Album = ReadText(cursor, albumColumn),
                AlbumId = ReadLong(cursor, albumIdColumn),
                Composer = ReadText(cursor, composerColumn),
                Duration = TimeSpan.FromMilliseconds(ReadLong(cursor, durationColumn)),
                // El numero de pista viene como DDD (disco * 1000 + pista) en discos multiples.
                Track = (int)(ReadLong(cursor, trackColumn) % 1000),
                Year = (int)ReadLong(cursor, yearColumn),
            }));
        }

        return songs;
    }

    /// <summary>
    /// Abre el cursor con la columna de artista del album cuando la version de Android la
    /// garantiza, y sin ella en las anteriores: pedir una columna inexistente hace fallar la
    /// consulta entera, no solo esa columna.
    /// </summary>
    private ICursor? OpenCursor(ContentResolver resolver, AndroidUri collection)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            try
            {
                string[] extended = [.. BaseProjection, "album_artist"];
                return resolver.Query(collection, extended, MusicOnlySelection, null, TitleSortOrder);
            }
            catch (Java.Lang.IllegalArgumentException ex)
            {
                _logger.LogWarning(ex, "This device does not expose the album_artist column; falling back.");
            }
        }

        return resolver.Query(collection, BaseProjection, MusicOnlySelection, null, TitleSortOrder);
    }

    private static string ReadText(ICursor cursor, int column)
    {
        if (column < 0 || cursor.IsNull(column))
            return string.Empty;

        var value = cursor.GetString(column);
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), UnknownColumnValue, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return value.Trim();
    }

    private static long ReadLong(ICursor cursor, int column) =>
        column < 0 || cursor.IsNull(column) ? 0 : cursor.GetLong(column);

    // ==================================================================================
    //  Agrupacion por grupo o compositor
    // ==================================================================================

    private List<ArtistGroup> Group(IReadOnlyList<Song> songs)
    {
        var preferComposer = _settings.PreferComposer;

        return songs
            .GroupBy(song => song.ResolveGroupName(preferComposer))
            .Where(group => group.Key.Length > 0)
            .Select(group => new ArtistGroup
            {
                Name = group.Key,
                Songs = group
                    .OrderBy(song => song.Album, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(song => song.Track)
                    .ThenBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                // La foto solo se pinta si ya estaba descargada: la rejilla no dispara consultas
                // de red al desplazarse.
                ImagePath = _artistInfo.GetCachedImagePath(group.Key),
            })
            .OrderBy(artist => artist.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
