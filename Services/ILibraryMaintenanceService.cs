namespace MusicPlayer.Services;

/// <summary>
/// Trabajos de mantenimiento de la biblioteca que van mas alla de leer el indice del sistema:
/// pedirle que reindexe el telefono entero, sacar las caratulas que llevan los ficheros y volver a
/// leer sus etiquetas.
/// </summary>
/// <remarks>
/// <para>Son tres botones de Configuracion y no parte del escaneo normal porque cuestan tiempo:
/// abrir cada fichero de audio para leerle la imagen o las etiquetas tarda segundos con cientos de
/// canciones, y el escaneo normal tiene que ser instantaneo. Se lanzan a proposito y avisan de
/// como van.</para>
///
/// <para>Todo lo que encuentran se guarda en el almacenamiento propio de la aplicacion, nunca en
/// el fichero de audio: la imagen como imagen puesta a mano y las etiquetas como correccion (ver
/// <see cref="ICustomArtService"/> y <see cref="ISongTagsService"/>).</para>
/// </remarks>
public interface ILibraryMaintenanceService
{
    /// <summary>Si hay un trabajo en marcha; no se lanzan dos a la vez.</summary>
    bool IsBusy { get; }

    /// <summary>
    /// Pide al sistema que vuelva a indexar todo el almacenamiento —la memoria interna y las
    /// tarjetas— y despues relee la biblioteca. Devuelve cuantas canciones nuevas han aparecido.
    /// </summary>
    Task<int> RescanDeviceAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Para cada cancion sin caratula, saca la imagen que lleve dentro el fichero o, si no lleva,
    /// la de su carpeta (cover.jpg, folder.jpg…). Devuelve cuantas caratulas se han encontrado.
    /// </summary>
    Task<int> ExtractEmbeddedArtAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vuelve a leer las etiquetas de cada fichero y rellena lo que el indice del sistema dejo
    /// vacio. Lo que el usuario corrigio a mano no se toca. Devuelve cuantas canciones han
    /// cambiado.
    /// </summary>
    Task<int> RefreshTagsAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default);
}
