namespace MusicPlayer.Models;

/// <summary>
/// Lista de reproduccion creada por el usuario. Guarda identificadores del indice de medios, no
/// rutas: si el usuario mueve un fichero de sitio la lista sigue apuntando a la misma cancion.
/// </summary>
public sealed class Playlist
{
    /// <summary>
    /// Identificador fijo de la lista de favoritas. Es una lista mas —se reproduce, se navega
    /// desde el coche y se elige en el selector como cualquier otra—, pero existe siempre, no se
    /// puede renombrar ni borrar y va la primera. Con un identificador fijo, el corazon de la
    /// cancion sabe a que lista apuntar sin buscarla por nombre.
    /// </summary>
    public const string FavoritesId = "favorites";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<long> SongIds { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>La lista de favoritas: ni se renombra ni se borra.</summary>
    public bool IsFavorites => Id == FavoritesId;
}
