using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <inheritdoc cref="ISongLookupService"/>
public sealed class SongLookupService : ISongLookupService, IDisposable
{
    /// <summary>MusicBrainz exige identificarse y no pasar de una peticion por segundo.</summary>
    private const string UserAgent = "sOCraticMusicPlayer/2026.08.29 ( jsoladelarosa@gmail.com )";

    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1.1);

    /// <summary>Por debajo de esta puntuacion la coincidencia es dudosa y no se ofrece nada.</summary>
    private const int MinimumScore = 80;

    private readonly ISettingsService _settings;
    private readonly ILogger<SongLookupService> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Stopwatch _sinceLastRequest = Stopwatch.StartNew();

    public SongLookupService(ISettingsService settings, ILogger<SongLookupService> logger)
    {
        _settings = settings;
        _logger = logger;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsEnabled => _settings.OnlineArtistInfo;

    public async Task<SongLookupResult> LookupAsync(SongTags tags, CancellationToken cancellationToken = default)
    {
        // Sin permiso explicito no se toca la red (constitucion 3).
        if (!IsEnabled || tags.Title.Trim().Length == 0)
            return SongLookupResult.None;

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

            var address = BuildQuery(tags);
            using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Song lookup returned {Status}.", (int)response.StatusCode);
                return SongLookupResult.None;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return ReadBest(document.RootElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Sin red no se puede buscar, pero la edicion a mano sigue funcionando igual.
            _logger.LogWarning(ex, "The song lookup failed.");
            return SongLookupResult.None;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    /// <summary>
    /// Se busca por titulo y, si se conoce, por grupo: con las dos cosas la coincidencia es mucho
    /// mas fiable que con un titulo suelto, que en musica se repite constantemente.
    /// </summary>
    private static string BuildQuery(SongTags tags)
    {
        var title = Escape(tags.Title.Trim());
        var artist = Escape(FirstNonEmpty(tags.Artist, tags.AlbumArtist, tags.Composer));

        var query = artist.Length > 0
            ? $"recording:\"{title}\" AND artist:\"{artist}\""
            : $"recording:\"{title}\"";

        return $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";
    }

    private SongLookupResult ReadBest(JsonElement root)
    {
        if (!root.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            return SongLookupResult.None;

        foreach (var recording in recordings.EnumerateArray())
        {
            if (recording.TryGetProperty("score", out var score) &&
                score.TryGetInt32(out var value) && value < MinimumScore)
                continue;

            var title = ReadString(recording, "title");
            var artist = ReadArtistCredit(recording);
            var (album, year, track) = ReadRelease(recording);

            var result = new SongLookupResult(title, artist, album, year, track);
            if (result.Found)
                return result;
        }

        return SongLookupResult.None;
    }

    private static string ReadArtistCredit(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits) || credits.GetArrayLength() == 0)
            return string.Empty;

        // Una colaboracion viene troceada en varios creditos con su union ("feat.", "&"): se
        // reconstruye tal cual, que es como se escribe en la etiqueta.
        var name = string.Empty;
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.TryGetProperty("name", out var artistName))
                name += artistName.GetString();
            else if (credit.TryGetProperty("artist", out var artist) && artist.TryGetProperty("name", out var inner))
                name += inner.GetString();

            if (credit.TryGetProperty("joinphrase", out var join))
                name += join.GetString();
        }

        return name.Trim();
    }

    /// <summary>
    /// Album, año y numero de pista de la primera edicion. Se prefiere la mas antigua: es la
    /// edicion original, no una recopilacion posterior.
    /// </summary>
    private static (string Album, int Year, int Track) ReadRelease(JsonElement recording)
    {
        if (!recording.TryGetProperty("releases", out var releases) || releases.GetArrayLength() == 0)
            return (string.Empty, 0, 0);

        var bestAlbum = string.Empty;
        var bestYear = 0;
        var bestTrack = 0;

        foreach (var release in releases.EnumerateArray())
        {
            var album = ReadString(release, "title");
            var year = ReadYear(release);
            var track = ReadTrack(release);

            if (album.Length == 0)
                continue;

            if (bestAlbum.Length == 0 || (year > 0 && (bestYear == 0 || year < bestYear)))
            {
                bestAlbum = album;
                bestYear = year;
                bestTrack = track;
            }
        }

        return (bestAlbum, bestYear, bestTrack);
    }

    private static int ReadYear(JsonElement release)
    {
        var date = ReadString(release, "date");
        return date.Length >= 4 && int.TryParse(date[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : 0;
    }

    private static int ReadTrack(JsonElement release)
    {
        if (!release.TryGetProperty("media", out var media) || media.GetArrayLength() == 0)
            return 0;

        var first = media[0];
        if (!first.TryGetProperty("track", out var tracks) || tracks.GetArrayLength() == 0)
            return 0;

        var number = ReadString(tracks[0], "number");
        return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (value.Trim().Length > 0)
                return value.Trim();
        }

        return string.Empty;
    }

    /// <summary>Las comillas y las barras romperian la consulta de Lucene que usa MusicBrainz.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", " ").Replace("\"", " ").Replace(":", " ").Trim();

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        var waited = _sinceLastRequest.Elapsed;
        if (waited < MinimumRequestInterval)
            await Task.Delay(MinimumRequestInterval - waited, cancellationToken).ConfigureAwait(false);

        _sinceLastRequest.Restart();
    }

    public void Dispose()
    {
        _http.Dispose();
        _requestGate.Dispose();
    }
}
