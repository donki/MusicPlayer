using System.Security.Cryptography;
using System.Text;

namespace MusicPlayer.Services;

/// <inheritdoc cref="ICustomArtService"/>
public sealed class CustomArtService : ICustomArtService
{
    /// <summary>Lo mas grande que se acepta de una direccion de internet: 8 MB.</summary>
    private const int MaxBytes = 8 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _folder;

    public CustomArtService(HttpClient http)
    {
        _http = http;
        _folder = Path.Combine(FileSystem.AppDataDirectory, "custom-art");
        Directory.CreateDirectory(_folder);
    }

    public string? ForArtist(string artistName) => Existente(ArtistFile(artistName));

    public string? ForSong(long songId) => Existente(SongFile(songId));

    public Task<string> SetArtistAsync(string artistName, Stream image, CancellationToken cancellationToken = default) =>
        GuardarAsync(ArtistFile(artistName), image, cancellationToken);

    public Task<string> SetSongAsync(long songId, Stream image, CancellationToken cancellationToken = default) =>
        GuardarAsync(SongFile(songId), image, cancellationToken);

    public void ClearArtist(string artistName) => Borrar(ArtistFile(artistName));

    public void ClearSong(long songId) => Borrar(SongFile(songId));

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("La dirección no es válida.");
        }

        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tipo = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            // Pegar la direccion de una pagina en vez de la de la imagen es el error de siempre:
            // se dice, en vez de guardar un HTML que luego no se puede pintar.
            throw new InvalidOperationException("Esa dirección no devuelve una imagen.");
        }

        if (response.Content.Headers.ContentLength is > MaxBytes)
        {
            throw new InvalidOperationException("La imagen pesa demasiado.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return bytes.Length <= MaxBytes
            ? bytes
            : throw new InvalidOperationException("La imagen pesa demasiado.");
    }

    // -----------------------------------------------------------------------

    private static string? Existente(string path) => File.Exists(path) ? path : null;

    private async Task<string> GuardarAsync(string path, Stream image, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_folder);

        await using (var destino = File.Create(path))
        {
            await image.CopyToAsync(destino, cancellationToken).ConfigureAwait(false);
        }

        // Se toca la fecha a proposito: quien haga copias de esta imagen —el coche, por ejemplo—
        // se entera de que ha cambiado comparando fechas.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return path;
    }

    private static void Borrar(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Si no se deja borrar ahora, se quedara la imagen puesta: no es motivo para reventar.
        }
    }

    private string SongFile(long songId) => Path.Combine(_folder, $"song-{songId}.img");

    /// <summary>
    /// El nombre de un grupo no sirve como nombre de fichero —barras, dos puntos, mayusculas que
    /// cambian segun el sistema—, asi que se usa su huella. Se normaliza a minusculas para que
    /// «Queen» y «queen» sean el mismo grupo, igual que en el resto de la aplicacion.
    /// </summary>
    private string ArtistFile(string artistName)
    {
        var huella = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(artistName.Trim().ToLowerInvariant())))[..16];

        return Path.Combine(_folder, $"artist-{huella}.img");
    }
}
