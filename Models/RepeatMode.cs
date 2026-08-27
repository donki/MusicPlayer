namespace MusicPlayer.Models;

/// <summary>Que hace la cola al terminar la cancion actual.</summary>
public enum RepeatMode
{
    /// <summary>Al llegar al final de la cola, la reproduccion se detiene.</summary>
    Off = 0,

    /// <summary>Al llegar al final de la cola, vuelve a empezar por la primera.</summary>
    All = 1,

    /// <summary>Repite indefinidamente la cancion actual.</summary>
    One = 2,
}
