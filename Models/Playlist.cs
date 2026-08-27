namespace MusicPlayer.Models;

/// <summary>
/// Lista de reproduccion creada por el usuario. Guarda identificadores del indice de medios, no
/// rutas: si el usuario mueve un fichero de sitio la lista sigue apuntando a la misma cancion.
/// </summary>
public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<long> SongIds { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
