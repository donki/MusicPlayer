using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

/// <inheritdoc cref="IArtistInfoService"/>
public sealed class ArtistInfoService : IArtistInfoService, IDisposable
{
    /// <summary>MusicBrainz exige identificarse y no pasar de una peticion por segundo.</summary>
    private const string UserAgent = "sOCraticMusicPlayer/2026.08.27 ( jsoladelarosa@gmail.com )";

    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1.1);

    /// <summary>Un grupo sin resultados no se vuelve a consultar hasta pasado este tiempo.</summary>
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromDays(30);

    private const string IndexFileName = "index.json";
    private const string CacheFolderName = "artists";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settings;
    private readonly ILogger<ArtistInfoService> _logger;
    private readonly HttpClient _http;
    private readonly string _cacheFolder;
    private readonly string _indexPath;

    // Las consultas se serializan: la red no es el cuello de botella, el limite de MusicBrainz si.
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Lock _indexGate = new();
    private readonly Stopwatch _sinceLastRequest = Stopwatch.StartNew();

    private Dictionary<string, CacheEntry> _index = new(StringComparer.OrdinalIgnoreCase);

    public ArtistInfoService(ISettingsService settings, ILogger<ArtistInfoService> logger)
    {
        _settings = settings;
        _logger = logger;

        _cacheFolder = Path.Combine(FileSystem.AppDataDirectory, CacheFolderName);
        _indexPath = Path.Combine(_cacheFolder, IndexFileName);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        LoadIndex();
    }

    public bool IsEnabled => _settings.OnlineArtistInfo;

    public string? GetCachedImagePath(string artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
            return null;

        lock (_indexGate)
        {
            if (!_index.TryGetValue(artistName.Trim(), out var entry) || entry.ImageFile is null)
                return null;

            var path = Path.Combine(_cacheFolder, entry.ImageFile);
            return File.Exists(path) ? path : null;
        }
    }

    public async Task<ArtistInfo> GetAsync(string artistName, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
            return new ArtistInfo(null, null);

        var name = artistName.Trim();

        if (!forceRefresh && TryReadCache(name, out var cached))
            return cached;

        // Sin permiso explicito no se toca la red (constitucion 3).
        if (!IsEnabled)
            return new ArtistInfo(null, null);

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Otra consulta pudo rellenar el cache mientras se esperaba el turno.
            if (!forceRefresh && TryReadCache(name, out cached))
                return cached;

            var fetched = await FetchAsync(name, cancellationToken).ConfigureAwait(false);
            StoreCache(name, fetched);
            return ToInfo(fetched);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            // Sin red, o con una respuesta que no se entiende, la app sigue funcionando: la foto
            // es un adorno, no un requisito (constitucion 10, nada de fallos silenciosos: queda
            // registrado, pero no se interrumpe al usuario).
            _logger.LogWarning(ex, "The artist lookup for {Artist} failed.", name);
            return new ArtistInfo(null, null);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void ClearCache()
    {
        lock (_indexGate)
        {
            try
            {
                if (Directory.Exists(_cacheFolder))
                    Directory.Delete(_cacheFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "The artist image cache could not be deleted.");
            }

            _index = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _requestGate.Dispose();
    }

    // ==================================================================================
    //  Consulta: MusicBrainz identifica al grupo, Wikidata da la imagen, Wikipedia el texto.
    // ==================================================================================

    private async Task<CacheEntry> FetchAsync(string name, CancellationToken cancellationToken)
    {
        var stamped = new CacheEntry { FetchedAt = DateTimeOffset.UtcNow };

        var wikidataId = await ResolveWikidataIdAsync(name, cancellationToken).ConfigureAwait(false);
        if (wikidataId is null)
            return stamped;

        var entity = await ReadWikidataEntityAsync(wikidataId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return stamped;

        stamped.Description = await ResolveDescriptionAsync(entity, cancellationToken).ConfigureAwait(false);

        if (entity.ImageFileName is not null)
            stamped.ImageFile = await DownloadImageAsync(name, entity.ImageFileName, cancellationToken).ConfigureAwait(false);

        return stamped;
    }

    /// <summary>
    /// Busca el grupo en MusicBrainz y sigue su enlace a Wikidata. Se descartan las coincidencias
    /// flojas: mas vale no ensenar foto que ensenar la de otro grupo.
    /// </summary>
    private async Task<string?> ResolveWikidataIdAsync(string name, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"artist:\"{name}\"");
        using var searchDocument = await GetJsonAsync(
            $"https://musicbrainz.org/ws/2/artist/?query={query}&limit=1&fmt=json", cancellationToken)
            .ConfigureAwait(false);

        if (searchDocument is null ||
            !searchDocument.RootElement.TryGetProperty("artists", out var artists) ||
            artists.GetArrayLength() == 0)
            return null;

        var best = artists[0];
        if (best.TryGetProperty("score", out var score) && score.TryGetInt32(out var value) && value < 85)
            return null;

        if (!best.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } mbid)
            return null;

        using var relationsDocument = await GetJsonAsync(
            $"https://musicbrainz.org/ws/2/artist/{mbid}?inc=url-rels&fmt=json", cancellationToken)
            .ConfigureAwait(false);

        if (relationsDocument is null ||
            !relationsDocument.RootElement.TryGetProperty("relations", out var relations))
            return null;

        foreach (var relation in relations.EnumerateArray())
        {
            if (!relation.TryGetProperty("type", out var type) || type.GetString() != "wikidata")
                continue;

            if (relation.TryGetProperty("url", out var url) &&
                url.TryGetProperty("resource", out var resource) &&
                resource.GetString() is { Length: > 0 } address)
            {
                var identifier = address[(address.LastIndexOf('/') + 1)..];
                if (identifier.StartsWith('Q'))
                    return identifier;
            }
        }

        return null;
    }

    private async Task<WikidataEntity?> ReadWikidataEntityAsync(string wikidataId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"https://www.wikidata.org/wiki/Special:EntityData/{wikidataId}.json", cancellationToken)
            .ConfigureAwait(false);

        if (document is null ||
            !document.RootElement.TryGetProperty("entities", out var entities) ||
            !entities.TryGetProperty(wikidataId, out var entity))
            return null;

        var result = new WikidataEntity();

        // P18 es la propiedad «imagen» de Wikidata; el valor es el nombre del fichero en Commons.
        if (entity.TryGetProperty("claims", out var claims) &&
            claims.TryGetProperty("P18", out var images) &&
            images.GetArrayLength() > 0 &&
            images[0].TryGetProperty("mainsnak", out var snak) &&
            snak.TryGetProperty("datavalue", out var datavalue) &&
            datavalue.TryGetProperty("value", out var fileName))
        {
            result.ImageFileName = fileName.GetString();
        }

        if (entity.TryGetProperty("sitelinks", out var sitelinks))
        {
            result.SpanishTitle = ReadSitelink(sitelinks, "eswiki");
            result.EnglishTitle = ReadSitelink(sitelinks, "enwiki");
        }

        if (entity.TryGetProperty("descriptions", out var descriptions))
        {
            result.SpanishDescription = ReadLanguageValue(descriptions, "es");
            result.EnglishDescription = ReadLanguageValue(descriptions, "en");
        }

        return result;
    }

    /// <summary>
    /// Resena: el resumen de Wikipedia en el idioma de la interfaz, con el otro idioma y la
    /// descripcion corta de Wikidata como respaldo.
    /// </summary>
    private async Task<string?> ResolveDescriptionAsync(WikidataEntity entity, CancellationToken cancellationToken)
    {
        var spanishFirst = _settings.Language == "es" ||
            (string.IsNullOrEmpty(_settings.Language) &&
             System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es");

        var candidates = spanishFirst
            ? new (string Language, string? Title)[] { ("es", entity.SpanishTitle), ("en", entity.EnglishTitle) }
            : new (string Language, string? Title)[] { ("en", entity.EnglishTitle), ("es", entity.SpanishTitle) };

        foreach (var (language, title) in candidates)
        {
            if (string.IsNullOrWhiteSpace(title))
                continue;

            using var document = await GetJsonAsync(
                $"https://{language}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}",
                cancellationToken).ConfigureAwait(false);

            if (document is not null &&
                document.RootElement.TryGetProperty("extract", out var extract) &&
                extract.GetString() is { Length: > 0 } summary)
                return summary;
        }

        return spanishFirst
            ? entity.SpanishDescription ?? entity.EnglishDescription
            : entity.EnglishDescription ?? entity.SpanishDescription;
    }

    private async Task<string?> DownloadImageAsync(string artistName, string commonsFileName, CancellationToken cancellationToken)
    {
        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

        // Special:FilePath sirve el fichero de Commons ya escalado, sin necesidad de clave de API.
        var address = $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(commonsFileName)}?width=600";

        using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
            return null;

        Directory.CreateDirectory(_cacheFolder);
        var fileName = $"{Hash(artistName)}.img";
        await File.WriteAllBytesAsync(Path.Combine(_cacheFolder, fileName), bytes, cancellationToken).ConfigureAwait(false);
        return fileName;
    }

    private async Task<JsonDocument?> GetJsonAsync(string address, CancellationToken cancellationToken)
    {
        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

        using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup request returned {Status} for {Address}", (int)response.StatusCode, address);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        var waited = _sinceLastRequest.Elapsed;
        if (waited < MinimumRequestInterval)
            await Task.Delay(MinimumRequestInterval - waited, cancellationToken).ConfigureAwait(false);

        _sinceLastRequest.Restart();
    }

    // ==================================================================================
    //  Cache en disco
    // ==================================================================================

    private bool TryReadCache(string name, out ArtistInfo info)
    {
        lock (_indexGate)
        {
            if (_index.TryGetValue(name, out var entry))
            {
                var hasContent = entry.ImageFile is not null || entry.Description is not null;
                if (hasContent || DateTimeOffset.UtcNow - entry.FetchedAt < NegativeCacheLifetime)
                {
                    info = ToInfo(entry);
                    return true;
                }
            }
        }

        info = new ArtistInfo(null, null);
        return false;
    }

    private void StoreCache(string name, CacheEntry entry)
    {
        lock (_indexGate)
        {
            _index[name] = entry;
            SaveIndex();
        }
    }

    private ArtistInfo ToInfo(CacheEntry entry)
    {
        if (entry.ImageFile is null)
            return new ArtistInfo(null, entry.Description);

        var path = Path.Combine(_cacheFolder, entry.ImageFile);
        return new ArtistInfo(File.Exists(path) ? path : null, entry.Description);
    }

    private void LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return;

            var stored = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_indexPath));
            if (stored is not null)
                _index = new Dictionary<string, CacheEntry>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The artist cache index could not be read; it will be rebuilt.");
        }
    }

    private void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            File.WriteAllText(_indexPath, JsonSerializer.Serialize(_index, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The artist cache index could not be saved.");
        }
    }

    /// <summary>Nombre de fichero estable a partir del nombre del grupo, sin caracteres raros.</summary>
    private static string Hash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(digest, 0, 12).ToLowerInvariant();
    }

    private static string? ReadSitelink(JsonElement sitelinks, string site) =>
        sitelinks.TryGetProperty(site, out var link) && link.TryGetProperty("title", out var title)
            ? title.GetString()
            : null;

    private static string? ReadLanguageValue(JsonElement descriptions, string language) =>
        descriptions.TryGetProperty(language, out var entry) && entry.TryGetProperty("value", out var value)
            ? value.GetString()
            : null;

    private sealed class CacheEntry
    {
        public string? ImageFile { get; set; }

        public string? Description { get; set; }

        public DateTimeOffset FetchedAt { get; set; }
    }

    private sealed class WikidataEntity
    {
        public string? ImageFileName { get; set; }

        public string? SpanishTitle { get; set; }

        public string? EnglishTitle { get; set; }

        public string? SpanishDescription { get; set; }

        public string? EnglishDescription { get; set; }
    }
}
