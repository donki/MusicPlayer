namespace MusicPlayer.Models;

/// <summary>
/// Etiquetas editables de una cancion. Es lo que el usuario puede corregir para que la busqueda y
/// la agrupacion acierten: una biblioteca real llega llena de «Unknown Artist», nombres mal
/// escritos y albumes partidos por una tilde de diferencia.
/// </summary>
public sealed record SongTags(
    string Title,
    string Artist,
    string AlbumArtist,
    string Album,
    string Composer,
    int Track,
    int Year)
{
    public static SongTags From(Song song) => new(
        song.Title,
        song.Artist,
        song.AlbumArtist,
        song.Album,
        song.Composer,
        song.Track,
        song.Year);

    /// <summary>Devuelve la cancion con estas etiquetas aplicadas; lo demas no se toca.</summary>
    public Song ApplyTo(Song song) => new()
    {
        Id = song.Id,
        ContentUri = song.ContentUri,
        FilePath = song.FilePath,
        AlbumId = song.AlbumId,
        Duration = song.Duration,
        Title = Title,
        Artist = Artist,
        AlbumArtist = AlbumArtist,
        Album = Album,
        Composer = Composer,
        Track = Track,
        Year = Year,
    };
}
