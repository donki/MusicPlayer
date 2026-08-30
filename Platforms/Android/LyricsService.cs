using Microsoft.Extensions.Logging;
using MusicPlayer.Models;
using MusicPlayer.Services;
using AndroidUri = Android.Net.Uri;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="ILyricsService"/>
/// <remarks>
/// Todo sale del dispositivo: la etiqueta de la propia cancion o un <c>.lrc</c> con su mismo
/// nombre. No hay ninguna consulta de red, asi que la letra funciona igual en el monte sin
/// cobertura, que es justo donde se escucha musica descargada.
/// </remarks>
public sealed class LyricsService : ILyricsService
{
    /// <summary>Etiqueta a leer del principio del fichero. Con esto sobra para ID3v2 y FLAC.</summary>
    private const int MaxHeaderBytes = 2 * 1024 * 1024;

    private readonly ILogger<LyricsService> _logger;

    // Una biblioteca se recorre muchas veces; releer el fichero en cada vuelta a la pantalla no
    // aporta nada porque la etiqueta no cambia sola.
    private readonly Dictionary<long, Lyrics> _cache = [];
    private readonly Lock _cacheGate = new();

    public LyricsService(ILogger<LyricsService> logger) => _logger = logger;

    public async Task<Lyrics> GetAsync(Song song, CancellationToken cancellationToken = default)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(song.Id, out var cached))
                return cached;
        }

        var lyrics = await Task.Run(() => Read(song), cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            _cache[song.Id] = lyrics;
        }

        return lyrics;
    }

    private Lyrics Read(Song song)
    {
        // El .lrc al lado de la cancion manda: si el usuario se ha molestado en ponerlo, es porque
        // quiere ese y no el de la etiqueta.
        var sidecar = ReadSidecar(song);
        if (sidecar.HasLyrics)
            return sidecar;

        try
        {
            using var stream = OpenSong(song);
            if (stream is null)
                return Lyrics.Empty;

            var format = song.Format;
            return format switch
            {
                "FLAC" => LyricsFormats.ReadFlac(stream, "etiqueta"),
                _ => LyricsFormats.ReadId3(stream, "etiqueta"),
            };
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.SecurityException or UnauthorizedAccessException)
        {
            // Una cancion sin letra legible es lo normal, no un fallo que deba interrumpir a nadie.
            _logger.LogInformation(ex, "Lyrics could not be read for song {SongId}.", song.Id);
            return Lyrics.Empty;
        }
    }

    /// <summary>
    /// Fichero <c>.lrc</c> con el mismo nombre que la cancion. Con almacenamiento delimitado puede
    /// no ser legible (el permiso de la app cubre audio, no ficheros de texto sueltos); si no se
    /// puede, se sigue con la etiqueta sin dar guerra.
    /// </summary>
    private Lyrics ReadSidecar(Song song)
    {
        if (song.FilePath.Length == 0)
            return Lyrics.Empty;

        try
        {
            var path = Path.ChangeExtension(song.FilePath, ".lrc");
            if (!File.Exists(path))
                return Lyrics.Empty;

            return LyricsFormats.ParseLrc(File.ReadAllText(path), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogInformation(ex, "The .lrc file next to song {SongId} could not be read.", song.Id);
            return Lyrics.Empty;
        }
    }

    /// <summary>
    /// Solo la cabecera del fichero: una etiqueta con la letra ocupa unos kilobytes y no hace falta
    /// traerse a memoria una cancion entera, que puede ser un FLAC de cien megas.
    /// </summary>
    private static Stream? OpenSong(Song song)
    {
        var uri = AndroidUri.Parse(song.ContentUri);
        if (uri is null)
            return null;

        using var source = global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        if (source is null)
            return null;

        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while (buffer.Length < MaxHeaderBytes && (read = source.Read(chunk, 0, chunk.Length)) > 0)
            buffer.Write(chunk, 0, read);

        buffer.Position = 0;
        return buffer;
    }
}
