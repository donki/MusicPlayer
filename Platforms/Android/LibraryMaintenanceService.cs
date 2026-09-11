using Android.Content;
using Android.Media;
using Android.OS.Storage;
using Android.Provider;
using Microsoft.Extensions.Logging;
using MusicPlayer.Models;
using MusicPlayer.Services;
using AndroidUri = Android.Net.Uri;
using Environment = Android.OS.Environment;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="ILibraryMaintenanceService"/>
public sealed class LibraryMaintenanceService : ILibraryMaintenanceService
{
    /// <summary>Nombres con los que se suele guardar la caratula al lado de las canciones.</summary>
    private static readonly string[] FolderArtNames =
        ["cover", "folder", "front", "album", "albumart", "albumartsmall", "caratula", "portada"];

    /// <summary>Cuanto se espera como maximo a que el sistema termine de reindexar un volumen.</summary>
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(5);

    private readonly IMusicLibraryService _library;
    private readonly ICustomArtService _customArt;
    private readonly ISongTagsService _tags;
    private readonly ILogger<LibraryMaintenanceService> _logger;

    private int _busy;

    public LibraryMaintenanceService(
        IMusicLibraryService library,
        ICustomArtService customArt,
        ISongTagsService tags,
        ILogger<LibraryMaintenanceService> logger)
    {
        _library = library;
        _customArt = customArt;
        _tags = tags;
        _logger = logger;
    }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    private static Context Context => global::Android.App.Application.Context;

    // ==================================================================================
    //  Reindexar el telefono entero
    // ==================================================================================

    public async Task<int> RescanDeviceAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            throw new InvalidOperationException("Ya hay un trabajo de biblioteca en marcha.");

        try
        {
            var before = _library.HasScanned ? _library.Songs.Count : 0;
            if (!_library.HasScanned)
                await _library.ScanAsync(cancellationToken).ConfigureAwait(false);
            before = _library.Songs.Count;

            // Se le pide al indexador del sistema que recorra cada volumen desde la raiz. Es el
            // que sabe leer ficheros que la aplicacion no puede ni ver (almacenamiento acotado):
            // nosotros solo le decimos por donde, y despues leemos lo que haya apuntado.
            foreach (var root in VolumeRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(root);
                await ScanPathAsync(root, cancellationToken).ConfigureAwait(false);
            }

            await _library.ScanAsync(cancellationToken).ConfigureAwait(false);
            return Math.Max(0, _library.Songs.Count - before);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Las raices de la memoria interna y de las tarjetas o discos USB montados.</summary>
    private static List<string> VolumeRoots()
    {
        var roots = new List<string>();

        try
        {
            if (Context.GetSystemService(Context.StorageService) is StorageManager manager)
            {
                foreach (var volume in manager.StorageVolumes)
                {
                    if (volume.State != Environment.MediaMounted)
                        continue;

                    // Directory es de Android 11; por debajo solo hay raiz para el volumen
                    // principal, que es el que devuelve ExternalStorageDirectory.
                    var path = OperatingSystem.IsAndroidVersionAtLeast(30)
                        ? volume.Directory?.AbsolutePath
                        : volume.IsPrimary ? Environment.ExternalStorageDirectory?.AbsolutePath : null;

                    if (!string.IsNullOrEmpty(path) && !roots.Contains(path))
                        roots.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            // Sin lista de volumenes se escanea al menos la memoria interna.
        }

        if (roots.Count == 0 && Environment.ExternalStorageDirectory?.AbsolutePath is { } primary)
            roots.Add(primary);

        return roots;
    }

    private static Task ScanPathAsync(string path, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new ScanListener(done);

        MediaScannerConnection.ScanFile(Context, [path], null, listener);

        // El indexador no avisa si el volumen desaparece a mitad: se espera con tope.
        return Task.WhenAny(done.Task, Task.Delay(ScanTimeout, cancellationToken));
    }

    private sealed class ScanListener(TaskCompletionSource<bool> done)
        : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
    {
        public void OnScanCompleted(string? path, AndroidUri? uri) => done.TrySetResult(true);
    }

    // ==================================================================================
    //  Caratulas que llevan los ficheros
    // ==================================================================================

    public async Task<int> ExtractEmbeddedArtAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            throw new InvalidOperationException("Ya hay un trabajo de biblioteca en marcha.");

        try
        {
            if (!_library.HasScanned)
                await _library.ScanAsync(cancellationToken).ConfigureAwait(false);

            // Solo las que no tienen nada: ni caratula en el indice ni imagen puesta a mano.
            var pending = _library.Songs
                .Where(song => _customArt.ForSong(song.Id) is null && !HasSystemArt(song))
                .ToList();

            var found = 0;
            var done = 0;

            // Las caratulas de carpeta se resuelven una vez por carpeta: en un album de doce
            // pistas no tiene sentido buscar el cover.jpg doce veces.
            var folderArt = new Dictionary<string, AndroidUri?>(StringComparer.OrdinalIgnoreCase);

            // Abrir cada fichero bloquea: fuera del hilo principal.
            await Task.Run(async () =>
            {
                foreach (var song in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (await SaveEmbeddedAsync(song, cancellationToken).ConfigureAwait(false) ||
                            await SaveFolderArtAsync(song, folderArt, cancellationToken).ConfigureAwait(false))
                        {
                            found++;
                        }
                    }
                    catch (Exception ex) when (ex is Java.Lang.Exception or IOException or UnauthorizedAccessException)
                    {
                        // Un fichero corrupto o inaccesible no para el recorrido.
                        _logger.LogWarning(ex, "No se pudo leer la caratula de {Title}.", song.Title);
                    }

                    progress?.Report((++done, pending.Count));
                }
            }, cancellationToken).ConfigureAwait(false);

            if (found > 0)
                await _library.ScanAsync(cancellationToken).ConfigureAwait(false);

            return found;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static bool HasSystemArt(Song song)
    {
        if (song.AlbumId <= 0)
            return false;

        try
        {
            using var stream = Context.ContentResolver?.OpenInputStream(
                AndroidUri.Parse($"content://media/external/audio/albumart/{song.AlbumId}")!);
            return stream is not null;
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.IO.IOException)
        {
            return false;
        }
    }

    private async Task<bool> SaveEmbeddedAsync(Song song, CancellationToken cancellationToken)
    {
        using var retriever = new MediaMetadataRetriever();
        retriever.SetDataSource(Context, AndroidUri.Parse(song.ContentUri)!);

        var picture = retriever.GetEmbeddedPicture();
        if (picture is null || picture.Length == 0)
            return false;

        using var stream = new MemoryStream(picture);
        await _customArt.SetSongAsync(song.Id, stream, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Busca en el indice de imagenes del sistema un cover.jpg (o parecido) en la carpeta de la
    /// cancion. Se pregunta al indice y no al disco porque el disco, con el almacenamiento
    /// acotado, no se puede recorrer; el indice si sabe que imagenes hay en cada carpeta.
    /// </summary>
    private async Task<bool> SaveFolderArtAsync(Song song, Dictionary<string, AndroidUri?> cache, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(song.FilePath);
        if (string.IsNullOrEmpty(folder))
            return false;

        if (!cache.TryGetValue(folder, out var image))
        {
            image = FindFolderArt(folder);
            cache[folder] = image;
        }

        if (image is null)
            return false;

        using var input = Context.ContentResolver?.OpenInputStream(image);
        if (input is null)
            return false;

        await _customArt.SetSongAsync(song.Id, input, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static AndroidUri? FindFolderArt(string folder)
    {
        var resolver = Context.ContentResolver;
        if (resolver is null)
            return null;

        string[] projection = [IBaseColumns.Id, MediaStore.IMediaColumns.DisplayName];
        var pattern = folder.TrimEnd('/') + "/%";

        try
        {
            using var cursor = resolver.Query(
                MediaStore.Images.Media.ExternalContentUri!, projection,
                $"{MediaStore.IMediaColumns.Data} LIKE ?", [pattern], null);

            if (cursor is null)
                return null;

            var idColumn = cursor.GetColumnIndex(IBaseColumns.Id);
            var nameColumn = cursor.GetColumnIndex(MediaStore.IMediaColumns.DisplayName);

            while (cursor.MoveToNext())
            {
                var name = cursor.GetString(nameColumn) ?? string.Empty;

                // Solo las de la carpeta misma, no las de subcarpetas: el LIKE trae todo el arbol.
                var stem = Path.GetFileNameWithoutExtension(name);
                if (!FolderArtNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
                    continue;

                return ContentUris.WithAppendedId(MediaStore.Images.Media.ExternalContentUri!, cursor.GetLong(idColumn));
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception)
        {
            // Sin permiso de imagenes (Android 13+ lo pide aparte) no hay caratulas de carpeta.
        }

        return null;
    }

    // ==================================================================================
    //  Releer etiquetas
    // ==================================================================================

    public async Task<int> RefreshTagsAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            throw new InvalidOperationException("Ya hay un trabajo de biblioteca en marcha.");

        try
        {
            if (!_library.HasScanned)
                await _library.ScanAsync(cancellationToken).ConfigureAwait(false);

            // Lo corregido a mano se respeta: solo se miran las canciones sin correccion.
            var pending = _library.Songs.Where(song => _tags.Find(song.Id) is null).ToList();
            var changed = 0;
            var done = 0;

            await Task.Run(() =>
            {
                foreach (var song in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (CompleteFromFile(song))
                            changed++;
                    }
                    catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
                    {
                        _logger.LogWarning(ex, "No se pudieron leer las etiquetas de {Title}.", song.Title);
                    }

                    progress?.Report((++done, pending.Count));
                }
            }, cancellationToken).ConfigureAwait(false);

            if (changed > 0)
                await _library.ScanAsync(cancellationToken).ConfigureAwait(false);

            return changed;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>
    /// Rellena con lo que diga el fichero cada campo que el indice dejo vacio. Si el indice ya
    /// tenia algo, se deja: el indice y el fichero suelen coincidir, y cuando no, no hay forma de
    /// saber cual de los dos esta bien.
    /// </summary>
    private bool CompleteFromFile(Song song)
    {
        using var retriever = new MediaMetadataRetriever();
        retriever.SetDataSource(Context, AndroidUri.Parse(song.ContentUri)!);

        var fileTitle = Clean(retriever.ExtractMetadata(MetadataKey.Title));
        var fileArtist = Clean(retriever.ExtractMetadata(MetadataKey.Artist));
        var fileAlbumArtist = Clean(retriever.ExtractMetadata(MetadataKey.Albumartist));
        var fileAlbum = Clean(retriever.ExtractMetadata(MetadataKey.Album));
        var fileComposer = Clean(retriever.ExtractMetadata(MetadataKey.Composer));
        var fileTrack = ParseLeadingNumber(retriever.ExtractMetadata(MetadataKey.CdTrackNumber));
        var fileYear = ParseLeadingNumber(retriever.ExtractMetadata(MetadataKey.Year))
            ?? ParseLeadingNumber(retriever.ExtractMetadata(MetadataKey.Date));

        // El indice pone el nombre del fichero como titulo cuando la etiqueta esta vacia: eso
        // cuenta como vacio.
        var titleIsFileName = string.Equals(song.Title,
            Path.GetFileNameWithoutExtension(song.FilePath), StringComparison.Ordinal);

        var tags = new SongTags(
            Title: titleIsFileName && fileTitle.Length > 0 ? fileTitle : song.Title,
            Artist: song.Artist.Length == 0 ? fileArtist : song.Artist,
            AlbumArtist: song.AlbumArtist.Length == 0 ? fileAlbumArtist : song.AlbumArtist,
            Album: song.Album.Length == 0 ? fileAlbum : song.Album,
            Composer: song.Composer.Length == 0 ? fileComposer : song.Composer,
            Track: song.Track == 0 ? fileTrack ?? 0 : song.Track,
            Year: song.Year == 0 ? fileYear ?? 0 : song.Year);

        if (tags == SongTags.From(song))
            return false;

        _tags.Save(song.Id, tags);
        return true;
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return string.Equals(trimmed, "<unknown>", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }

    /// <summary>«3/12» es la pista 3; «2024-05-01» es el año 2024.</summary>
    private static int? ParseLeadingNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) && number > 0 ? number : null;
    }
}
