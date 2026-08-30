using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>Lo que se ha encontrado de una cancion. Los campos vacios es que no venian.</summary>
public sealed record SongLookupResult(
    string Title,
    string Artist,
    string Album,
    int Year,
    int Track)
{
    public static readonly SongLookupResult None = new(string.Empty, string.Empty, string.Empty, 0, 0);

    public bool Found => Title.Length > 0 || Artist.Length > 0 || Album.Length > 0;

    /// <summary>Resumen legible para enseñarle al usuario que se ha encontrado antes de aplicarlo.</summary>
    public string Describe(Func<string, string> label)
    {
        var lines = new List<string>();

        if (Title.Length > 0)
            lines.Add($"{label("TagTitle")}: {Title}");
        if (Artist.Length > 0)
            lines.Add($"{label("TagArtist")}: {Artist}");
        if (Album.Length > 0)
            lines.Add($"{label("TagAlbum")}: {Album}");
        if (Year > 0)
            lines.Add($"{label("TagYear")}: {Year}");
        if (Track > 0)
            lines.Add($"{label("TagTrack")}: {Track}");

        return string.Join('\n', lines);
    }
}

/// <summary>
/// Busca en internet los datos de una cancion (titulo, grupo, album, año y numero de pista) para
/// corregir las etiquetas de una biblioteca que llega con nombres a medias.
/// </summary>
/// <remarks>
/// Misma regla que <see cref="IArtistInfoService"/>: apagado por defecto y solo con el permiso
/// explicito del usuario (constitucion 3). Lo unico que sale del dispositivo es el titulo y el
/// nombre del grupo que ya estan escritos en el formulario. La fuente es MusicBrainz, cuyos datos
/// son de dominio publico (CC0).
/// </remarks>
public interface ISongLookupService
{
    /// <summary>Si la busqueda en linea esta permitida por el usuario ahora mismo.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Datos de la cancion, o <see cref="SongLookupResult.None"/> si no hay coincidencia clara,
    /// no hay red o el usuario no ha dado permiso. Nunca lanza.
    /// </summary>
    Task<SongLookupResult> LookupAsync(SongTags tags, CancellationToken cancellationToken = default);
}
