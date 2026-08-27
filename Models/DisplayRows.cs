namespace MusicPlayer.Models;

/// <summary>
/// Fila de cancion tal y como se pinta en una lista. Es un modelo de presentacion: lleva los
/// textos ya resueltos y formateados para que la plantilla no tenga que calcular nada.
/// </summary>
public sealed class SongRow
{
    public required Song Song { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Duration { get; init; }

    public ImageSource? Artwork { get; init; }
}

/// <summary>Celda de la rejilla de grupos y compositores.</summary>
public sealed class ArtistRow
{
    public required string Name { get; init; }

    public required string Subtitle { get; init; }

    public ImageSource? Image { get; init; }
}

/// <summary>Fila de la lista de listas de reproduccion.</summary>
public sealed class PlaylistRow
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Subtitle { get; init; }
}

/// <summary>Fila del selector de listas: una lista y si la cancion pertenece a ella.</summary>
public sealed class PlaylistChoiceRow
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsSelected { get; set; }
}
