namespace MusicPlayer.Services;

/// <summary>
/// Imagenes puestas <b>a mano</b> por el usuario: la de un grupo o la de una cancion.
/// </summary>
/// <remarks>
/// <para><b>Por que hace falta.</b> Una biblioteca de verdad llega con canciones sin caratula,
/// grupos que la busqueda en linea no encuentra —o encuentra mal— y recopilatorios donde la
/// caratula del album no dice nada. Hasta ahora la unica imagen posible era la que trajera el
/// fichero o la que se descargara sola, y no habia forma de arreglarlo.</para>
///
/// <para><b>Manda sobre lo demas.</b> Si hay imagen puesta a mano, se usa esa: es una decision
/// explicita del usuario y no la puede pisar una descarga posterior.</para>
///
/// <para>Se guarda en el almacenamiento propio de la aplicacion, con nombre de fichero deducible de
/// la clave: sin indice que mantener, y borrar el fichero es olvidar la imagen.</para>
/// </remarks>
public interface ICustomArtService
{
    /// <summary>Imagen elegida para el grupo, o <c>null</c> si no hay ninguna.</summary>
    string? ForArtist(string artistName);

    /// <summary>Imagen elegida para la cancion, o <c>null</c> si no hay ninguna.</summary>
    string? ForSong(long songId);

    /// <summary>Guarda la imagen de un grupo y devuelve donde ha quedado.</summary>
    Task<string> SetArtistAsync(string artistName, Stream image, CancellationToken cancellationToken = default);

    /// <summary>Guarda la imagen de una cancion y devuelve donde ha quedado.</summary>
    Task<string> SetSongAsync(long songId, Stream image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarga una imagen de una direccion de internet.
    /// </summary>
    /// <remarks>
    /// Comprueba que lo que llega es <b>una imagen</b> y que no es descomunal: una direccion
    /// cualquiera puede devolver una pagina web o un fichero de cien megas, y ni una cosa ni la
    /// otra se pueden pintar.
    /// </remarks>
    Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Quita la imagen puesta a mano; vuelve a mandar la que hubiera antes.</summary>
    void ClearArtist(string artistName);

    /// <summary>Quita la imagen puesta a mano de una cancion.</summary>
    void ClearSong(long songId);
}
