using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Correcciones de etiquetas hechas por el usuario, guardadas en el almacenamiento propio de la
/// aplicacion.
/// </summary>
/// <remarks>
/// Existen porque el indice de medios del sistema no es un sitio fiable donde guardar una
/// correccion: al reindexar, Android vuelve a leer las etiquetas del fichero y deshace lo que se
/// haya escrito en sus columnas. Guardar ademas la correccion aqui hace que lo que el usuario
/// arreglo siga arreglado, tambien en Android Auto, pase lo que pase con el indice.
/// </remarks>
public interface ISongTagsService
{
    /// <summary>Correccion guardada para esa cancion, o <c>null</c> si no se ha tocado.</summary>
    SongTags? Find(long songId);

    void Save(long songId, SongTags tags);

    /// <summary>Aplica la correccion guardada, si la hay. Si no, devuelve la cancion tal cual.</summary>
    Song Apply(Song song);

    /// <summary>Olvida la correccion; se usa al borrar la cancion del dispositivo.</summary>
    void Forget(IReadOnlyCollection<long> songIds);
}
