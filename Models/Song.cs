namespace MusicPlayer.Models;

/// <summary>
/// Una pista de audio del dispositivo, tal y como la describe el indice de medios del sistema.
/// Es un modelo de datos puro: no contiene logica de presentacion (constitucion 5).
/// </summary>
public sealed class Song
{
    /// <summary>Identificador de la pista en el indice de medios del sistema.</summary>
    public long Id { get; init; }

    /// <summary>URI de contenido de la pista; es lo que se entrega al reproductor.</summary>
    public string ContentUri { get; init; } = string.Empty;

    /// <summary>Ruta del fichero. Solo informativa: la reproduccion usa <see cref="ContentUri"/>.</summary>
    public string FilePath { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string AlbumArtist { get; init; } = string.Empty;

    /// <summary>Compositor de la obra. Es lo unico que identifica a un autor en musica clasica,
    /// donde el campo de artista suele traer al interprete o directamente nada.</summary>
    public string Composer { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    /// <summary>Identificador del album, con el que el sistema resuelve la caratula.</summary>
    public long AlbumId { get; init; }

    public TimeSpan Duration { get; init; }

    public int Track { get; init; }

    public int Year { get; init; }

    /// <summary>Extension del fichero en mayusculas (MP3, FLAC, OGG...), para mostrar el formato.</summary>
    public string Format =>
        Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension
            ? extension
            : string.Empty;

    /// <summary>
    /// Nombre del grupo bajo el que se agrupa la cancion. Se elige el primer campo con contenido
    /// real: el artista del album manda sobre el de la pista para que un disco de varios
    /// interpretes no se rompa en una entrada por cancion.
    /// </summary>
    /// <param name="preferComposer">
    /// Cuando esta activo, el compositor manda sobre el interprete. Es lo que espera una biblioteca
    /// de musica clasica, donde agrupar por interprete dispersa la obra de un mismo autor.
    /// </param>
    public string ResolveGroupName(bool preferComposer)
    {
        var candidates = preferComposer
            ? new[] { Composer, AlbumArtist, Artist }
            : new[] { AlbumArtist, Artist, Composer };

        foreach (var candidate in candidates)
        {
            if (IsMeaningful(candidate))
                return candidate.Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// El indice de medios de Android rellena los campos vacios con el literal
    /// <c>&lt;unknown&gt;</c>, que no es un nombre de grupo sino la ausencia de uno.
    /// </summary>
    private static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value.Trim(), "<unknown>", StringComparison.OrdinalIgnoreCase);
}
