using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>Resultado de intentar borrar una cancion del dispositivo.</summary>
public enum DeleteOutcome
{
    Deleted,

    /// <summary>El usuario rechazo la confirmacion del sistema.</summary>
    Cancelled,

    Failed,
}

/// <summary>
/// Biblioteca musical del dispositivo: escaneo, agrupacion y borrado. Toda la logica vive aqui;
/// las paginas solo la orquestan (constitucion 7).
/// </summary>
public interface IMusicLibraryService
{
    /// <summary>Se dispara cuando cambia el contenido de la biblioteca (escaneo o borrado).</summary>
    event EventHandler? LibraryChanged;

    IReadOnlyList<Song> Songs { get; }

    /// <summary>Grupos o compositores, ordenados por nombre.</summary>
    IReadOnlyList<ArtistGroup> Artists { get; }

    /// <summary>Si ya se ha completado al menos un escaneo en esta ejecucion.</summary>
    bool HasScanned { get; }

    /// <summary>
    /// Recorre el indice de medios del sistema y reconstruye la biblioteca. Devuelve <c>false</c>
    /// si no hay permiso de acceso a los archivos de audio.
    /// </summary>
    Task<bool> ScanAsync(CancellationToken cancellationToken = default);

    Song? FindById(long id);

    /// <summary>Canciones de los identificadores dados, en el mismo orden y sin las que ya no existan.</summary>
    IReadOnlyList<Song> FindByIds(IEnumerable<long> ids);

    ArtistGroup? FindArtist(string name);

    /// <summary>URI de la caratula del album de la cancion, o <c>null</c> si el album no tiene.</summary>
    string? GetAlbumArtUri(Song song);

    /// <summary>
    /// Caratula lista para pintar. Devuelve <c>null</c> cuando el album no tiene, que es lo
    /// normal en buena parte de una biblioteca: la interfaz muestra entonces el marcador.
    /// </summary>
    ImageSource? GetAlbumArt(Song song);

    /// <summary>
    /// Borra la cancion del dispositivo. En Android moderno la confirmacion la pide el sistema,
    /// no la aplicacion, asi que el usuario siempre tiene la ultima palabra.
    /// </summary>
    Task<DeleteOutcome> DeleteAsync(Song song);
}
