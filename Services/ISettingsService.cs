namespace MusicPlayer.Services;

/// <summary>
/// Preferencias del usuario. Configuracion ligera, por lo que va en el almacen clave-valor del
/// sistema y no en un fichero estructurado (constitucion 9).
/// </summary>
public interface ISettingsService
{
    /// <summary>Codigo de idioma elegido, o cadena vacia para seguir al del sistema.</summary>
    string Language { get; set; }

    /// <summary>
    /// Permiso explicito del usuario para consultar informacion de grupos en internet. Apagado por
    /// defecto: sin el, nada sale del dispositivo (constitucion 3, privacidad primero).
    /// </summary>
    bool OnlineArtistInfo { get; set; }

    /// <summary>Agrupar por compositor en vez de por interprete (bibliotecas de musica clasica).</summary>
    bool PreferComposer { get; set; }

    /// <summary>Ultima cancion reproducida, para restaurar el reproductor al volver a abrir.</summary>
    long LastSongId { get; set; }

    bool Shuffle { get; set; }

    int RepeatMode { get; set; }
}
