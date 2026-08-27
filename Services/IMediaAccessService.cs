namespace MusicPlayer.Services;

/// <summary>
/// Permiso de lectura de los archivos de audio del dispositivo. Es el unico permiso sensible que
/// pide la aplicacion y sin el no hay nada que reproducir (constitucion A.3).
/// </summary>
public interface IMediaAccessService
{
    Task<bool> IsGrantedAsync();

    /// <summary>Pide el permiso al usuario. Devuelve si quedo concedido.</summary>
    Task<bool> RequestAsync();

    /// <summary>
    /// Abre la ficha de la aplicacion en los ajustes del sistema, para cuando el usuario ya ha
    /// denegado el permiso de forma permanente y el dialogo ya no vuelve a salir.
    /// </summary>
    void OpenSystemSettings();
}
