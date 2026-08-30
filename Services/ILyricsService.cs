using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Letra de la cancion que se esta escuchando.
/// </summary>
/// <remarks>
/// La letra sale **de los ficheros del propio usuario**: de la etiqueta de la cancion (USLT/SYLT en
/// MP3, comentarios Vorbis en FLAC) o de un fichero <c>.lrc</c> con el mismo nombre. No se consulta
/// ningun servicio ni se genera texto: una letra es obra ajena con derechos, y un modelo de lenguaje
/// pequeno lo unico que haria seria inventarsela.
/// </remarks>
public interface ILyricsService
{
    /// <summary>
    /// Letra de la cancion, o <see cref="Lyrics.Empty"/> si el fichero no trae ninguna. Nunca
    /// lanza: una cancion sin letra es lo normal, no un error.
    /// </summary>
    Task<Lyrics> GetAsync(Song song, CancellationToken cancellationToken = default);
}
