using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Reglas de la biblioteca que no dependen de Android: como se agrupan las canciones, que casa con
/// una busqueda y como se completan las etiquetas con lo que trae el fichero.
/// </summary>
public static class LibraryRules
{
    /// <summary>
    /// Agrupa por grupo o compositor. Dentro de cada grupo, por album, pista y titulo; los grupos,
    /// por nombre. Las canciones sin ningun nombre util no forman grupo.
    /// </summary>
    /// <remarks>
    /// Las mayusculas no separan grupos: «Queen» y «queen» son el mismo, con el nombre escrito como
    /// lo escriben mas canciones. Antes salian dos tarjetas, pero al abrir cualquiera de las dos se
    /// buscaba el grupo sin distinguir mayusculas y siempre se abria la primera: las canciones de
    /// la segunda no se podian alcanzar ni desde la ficha del grupo ni desde el coche.
    /// </remarks>
    public static List<ArtistGroup> Group(IEnumerable<Song> songs, bool preferComposer) =>
        songs
            .Select(song => (Song: song, Name: song.ResolveGroupName(preferComposer)))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ArtistGroup
            {
                Name = group
                    .GroupBy(item => item.Name, StringComparer.Ordinal)
                    .OrderByDescending(spelling => spelling.Count())
                    .First().Key,
                Songs = group
                    .Select(item => item.Song)
                    .OrderBy(song => song.Album, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(song => song.Track)
                    .ThenBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            })
            .OrderBy(artist => artist.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Si la cancion casa con el texto del buscador (titulo, grupo, compositor o album).</summary>
    public static bool MatchesSearch(Song song, string term) =>
        song.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Artist.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.AlbumArtist.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Composer.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Album.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Lo que se reproduce cuando se pide musica por voz: sin texto, toda la biblioteca; si no, el
    /// primer grupo cuyo nombre lo contenga y, si no hay, las canciones por titulo o album. Vacio si
    /// nada casa.
    /// </summary>
    public static IReadOnlyList<Song> ResolveVoiceQuery(
        IReadOnlyList<ArtistGroup> artists, IReadOnlyList<Song> songs, string? query)
    {
        if (songs.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(query))
            return songs;

        var term = query.Trim();

        var artist = artists.FirstOrDefault(item =>
            item.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        if (artist is not null)
            return artist.Songs;

        return songs
            .Where(song => song.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                        || song.Album.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Valor de etiqueta limpio: sin blancos alrededor, y vacio si es el literal
    /// <c>&lt;unknown&gt;</c> con el que el indice de medios rellena lo que no sabe.
    /// </summary>
    public static string CleanTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return string.Equals(trimmed, "<unknown>", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }

    /// <summary>«3/12» es la pista 3; «2024-05-01» es el año 2024. Null si no empieza por un numero positivo.</summary>
    public static int? ParseLeadingNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) && number > 0 ? number : null;
    }

    /// <summary>
    /// Etiquetas completadas con lo leido del fichero: solo se rellena lo que falta, nunca se pisa
    /// lo que ya hay. El titulo cuenta como vacio si es el nombre del fichero, que es lo que pone el
    /// indice cuando la etiqueta no trae titulo.
    /// </summary>
    public static SongTags CompleteTags(
        Song song, string fileTitle, string fileArtist, string fileAlbumArtist, string fileAlbum,
        string fileComposer, int? fileTrack, int? fileYear)
    {
        var titleIsFileName = string.Equals(song.Title,
            Path.GetFileNameWithoutExtension(song.FilePath), StringComparison.Ordinal);

        return new SongTags(
            Title: titleIsFileName && fileTitle.Length > 0 ? fileTitle : song.Title,
            Artist: song.Artist.Length == 0 ? fileArtist : song.Artist,
            AlbumArtist: song.AlbumArtist.Length == 0 ? fileAlbumArtist : song.AlbumArtist,
            Album: song.Album.Length == 0 ? fileAlbum : song.Album,
            Composer: song.Composer.Length == 0 ? fileComposer : song.Composer,
            Track: song.Track == 0 ? fileTrack ?? 0 : song.Track,
            Year: song.Year == 0 ? fileYear ?? 0 : song.Year);
    }
}
