using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>Resultado de guardar las etiquetas corregidas de una cancion.</summary>
public enum TagsOutcome
{
    /// <summary>Guardadas en la aplicacion y tambien en el indice de medios del sistema.</summary>
    Saved,

    /// <summary>
    /// Guardadas solo en la aplicacion, porque el sistema no dejo tocar su indice. La correccion
    /// vale igual dentro de la aplicacion y en Android Auto; lo unico que no se entera es el resto
    /// de aplicaciones del telefono.
    /// </summary>
    SavedInAppOnly,
}

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
    /// Imagen con la que se representa el grupo o compositor. Si no hay foto descargada se usa la
    /// caratula de la primera de sus canciones que tenga una: una caratula suya dice mucho mas
    /// que el marcador generico, y no hace falta permiso ni conexion para tenerla.
    /// </summary>
    ImageSource? GetArtistArt(ArtistGroup artist);

    /// <summary>
    /// Corrige las etiquetas de una cancion. La correccion se guarda siempre en la aplicacion, que
    /// es lo que hace que la busqueda y la agrupacion acierten a partir de ese momento, y ademas se
    /// intenta trasladar al indice de medios del sistema para que la vean las demas aplicaciones.
    /// </summary>
    Task<TagsOutcome> UpdateTagsAsync(Song song, SongTags tags);

    /// <summary>
    /// Olvida la correccion y vuelve a las etiquetas que trae el fichero. Lo que se escribiera en
    /// el indice del sistema no se deshace: eso ya es del sistema, no nuestro.
    /// </summary>
    Task ResetTagsAsync(Song song);

    /// <summary>
    /// Borra la cancion del dispositivo. En Android moderno la confirmacion la pide el sistema,
    /// no la aplicacion, asi que el usuario siempre tiene la ultima palabra.
    /// </summary>
    Task<DeleteOutcome> DeleteAsync(Song song);

    /// <summary>
    /// Borra varias canciones de una vez. El sistema pide <b>una sola</b> confirmacion para todo
    /// el lote, que es lo que espera quien acaba de marcar veinte canciones: encadenar veinte
    /// dialogos seria inaceptable.
    /// </summary>
    Task<DeleteOutcome> DeleteAsync(IReadOnlyList<Song> songs);
}
