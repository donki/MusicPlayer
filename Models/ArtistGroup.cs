namespace MusicPlayer.Models;

/// <summary>
/// Un grupo o compositor con sus canciones. Es la unidad de navegacion principal de la biblioteca
/// y tambien la primera rama del arbol que ve Android Auto.
/// </summary>
public sealed class ArtistGroup
{
    public required string Name { get; init; }

    public required IReadOnlyList<Song> Songs { get; init; }

    /// <summary>
    /// Ruta local de la imagen del grupo, descargada solo si el usuario activo la busqueda en
    /// linea. <c>null</c> mientras no haya imagen: la interfaz muestra el marcador de posicion.
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>Resena breve del grupo o compositor, de la misma consulta que la imagen.</summary>
    public string? Description { get; set; }

    public int SongCount => Songs.Count;

    public TimeSpan TotalDuration =>
        TimeSpan.FromTicks(Songs.Sum(song => song.Duration.Ticks));
}
