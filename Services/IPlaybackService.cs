using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Control de la reproduccion. La implementacion de plataforma habla con el servicio de medios
/// del sistema, que es el mismo que atiende a Android Auto y a la notificacion: hay una sola
/// reproduccion y un solo estado, se mande desde donde se mande.
/// </summary>
public interface IPlaybackService
{
    /// <summary>Cambio de pista, de estado (sonando/pausado) o de cola.</summary>
    event EventHandler? StateChanged;

    Song? Current { get; }

    IReadOnlyList<Song> Queue { get; }

    int QueueIndex { get; }

    bool IsPlaying { get; }

    /// <summary>Posicion actual. Se consulta bajo demanda; no genera eventos.</summary>
    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    bool Shuffle { get; set; }

    RepeatMode Repeat { get; set; }

    /// <summary>Sustituye la cola y empieza a reproducir por la posicion indicada.</summary>
    void Play(IReadOnlyList<Song> queue, int index);

    /// <summary>
    /// Deja la cola cargada por la posicion indicada pero **en pausa**, sin pedir el foco de audio.
    /// Es lo que usa el arranque para recuperar la ultima cancion escuchada sin ponerse a sonar
    /// solo ni callar lo que suene en otra aplicacion.
    /// </summary>
    void Prepare(IReadOnlyList<Song> queue, int index);

    void TogglePlayPause();

    void Next();

    /// <summary>
    /// Vuelve al principio de la cancion, o pasa a la anterior si acaba de empezar. Es el
    /// comportamiento que espera cualquiera que haya usado un reproductor.
    /// </summary>
    void Previous();

    void SeekTo(TimeSpan position);

    void Stop();
}
