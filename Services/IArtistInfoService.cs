namespace MusicPlayer.Services;

/// <summary>Foto y resena de un grupo o compositor.</summary>
/// <param name="ImagePath">Ruta local de la imagen descargada, o <c>null</c> si no se encontro.</param>
/// <param name="Description">Resena breve, o <c>null</c> si no se encontro.</param>
public sealed record ArtistInfo(string? ImagePath, string? Description);

/// <summary>
/// Busca en internet la foto y una resena breve de un grupo o compositor.
/// </summary>
/// <remarks>
/// Choca de frente con «privacidad primero» (constitucion 3), asi que esta apagado por defecto y
/// solo se activa desde Configuracion. Lo unico que sale del dispositivo es el nombre del grupo:
/// ni titulos de cancion, ni nombres de fichero, ni identificadores. Las fuentes son publicas y de
/// licencia compatible: MusicBrainz (CC0) para identificar al grupo y Wikidata/Wikimedia Commons
/// (CC BY-SA) para la imagen y el texto.
/// </remarks>
public interface IArtistInfoService
{
    /// <summary>Si la busqueda en linea esta permitida por el usuario ahora mismo.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Foto y resena del grupo, del cache si ya se consulto. Devuelve un resultado vacio, sin
    /// tocar la red, si el usuario no ha activado la busqueda en linea.
    /// </summary>
    /// <param name="forceRefresh">
    /// Salta el cache y vuelve a preguntar. Es lo que hace falta cuando el usuario pide la busqueda
    /// a proposito: si la primera vez no se encontro nada, el cache negativo dura 30 dias y sin esto
    /// el boton no haria absolutamente nada.
    /// </param>
    Task<ArtistInfo> GetAsync(string artistName, bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Imagen ya descargada de un grupo, sin consultar la red. Para pintar la rejilla.</summary>
    string? GetCachedImagePath(string artistName);

    /// <summary>Borra las imagenes y los textos descargados.</summary>
    void ClearCache();
}
