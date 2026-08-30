namespace MusicPlayer.Models;

/// <summary>Una linea de la letra. <see cref="Time"/> es null si la letra no va sincronizada.</summary>
public sealed record LyricLine(TimeSpan? Time, string Text);

/// <summary>
/// Letra de una cancion, leida de la propia biblioteca del usuario: de la etiqueta del fichero o
/// de un <c>.lrc</c> al lado. Music Player no descarga letras ni las inventa (constitucion 3 y 4:
/// privacidad primero y nada de contenido de terceros sin licencia).
/// </summary>
/// <param name="Lines">Lineas en orden. Vacio si la cancion no trae letra.</param>
/// <param name="IsSynced">Si cada linea tiene su momento, para poder seguirla mientras suena.</param>
/// <param name="Source">De donde salio, para poder decirselo al usuario (etiqueta o fichero .lrc).</param>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool IsSynced, string Source)
{
    public static readonly Lyrics Empty = new([], false, string.Empty);

    public bool HasLyrics => Lines.Count > 0;

    /// <summary>
    /// Indice de la linea que corresponde a la posicion dada, o -1 si todavia no ha empezado
    /// ninguna. Solo tiene sentido con letra sincronizada.
    /// </summary>
    public int IndexAt(TimeSpan position)
    {
        if (!IsSynced)
            return -1;

        var found = -1;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time is { } time && time <= position)
                found = i;
            else if (Lines[i].Time is not null)
                break;
        }

        return found;
    }
}
