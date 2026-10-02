using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>Lo que el arbol del coche lee de la biblioteca (sin imagenes ni escaneo).</summary>
public interface IBrowseLibrary
{
    IReadOnlyList<Song> Songs { get; }
    IReadOnlyList<ArtistGroup> Artists { get; }
    Song? FindById(long id);
    IReadOnlyList<Song> FindByIds(IEnumerable<long> ids);
    ArtistGroup? FindArtist(string name);
}

/// <summary>Que imagen lleva un nodo del arbol del coche (la pone el servicio de Android).</summary>
public enum BrowseIcon
{
    None,
    Favorites,
    /// <summary>Foto del grupo; si no, la caratula de su primera cancion; si no, el icono de grupo.</summary>
    Artist,
    /// <summary>La caratula de su primera cancion; si no, el icono de lista.</summary>
    Playlist,
    /// <summary>Su caratula; si no, la foto del grupo; si no, el icono de cancion.</summary>
    Song,
}

/// <summary>Un elemento del arbol de Android Auto, sin tipos de Android.</summary>
public sealed record BrowseNode(string MediaId, string Title, string? Subtitle, bool Playable, BrowseIcon Icon,
    ArtistGroup? Artist = null, IReadOnlyList<Song>? Songs = null, Song? Song = null);

/// <summary>
/// El arbol de navegacion de Android Auto (antes dentro de MusicService): raiz, grupos, listas,
/// todas las canciones, y los identificadores con los que el coche pide reproducir. Es codigo puro
/// y se prueba sin coche; el servicio solo lo convierte en MediaItem con sus imagenes.
/// </summary>
public static class BrowseTree
{
    public const string RootId = "root";
    public const string ArtistsId = "artists";
    public const string PlaylistsId = "playlists";
    public const string AllSongsId = "allsongs";
    public const string ArtistPrefix = "artist|";
    public const string PlaylistPrefix = "playlist|";
    public const string SongPrefix = "song|";

    /// <summary>Favoritas cuelga de la raiz con su propio corazon.</summary>
    public const string FavoritesId = PlaylistPrefix + Playlist.FavoritesId;

    /// <summary>Tope de elementos por carpeta: el coche no necesita mas y una lista enorme tarda.</summary>
    public const int MaxChildren = 500;

    /// <summary>Hijos de un nodo; vacio si el nodo no existe.</summary>
    public static List<BrowseNode> Children(string? parentId, IBrowseLibrary library,
        IPlaylistService? playlists, ILocalizationService? localization)
    {
        if (parentId is null)
            return [];

        if (parentId == RootId)
        {
            return
            [
                new(FavoritesId, localization?["Favorites"] ?? "Favorites", null, false, BrowseIcon.Favorites),
                new(ArtistsId, localization?["TabArtists"] ?? "Artists", null, false, BrowseIcon.None),
                new(PlaylistsId, localization?["TabPlaylists"] ?? "Playlists", null, false, BrowseIcon.None),
                new(AllSongsId, localization?["TabSongs"] ?? "Songs", null, false, BrowseIcon.None),
            ];
        }

        if (parentId == ArtistsId)
        {
            return library.Artists.Take(MaxChildren)
                .Select(a => new BrowseNode(ArtistPrefix + a.Name, a.Name, SongCountText(localization, a.SongCount),
                    false, BrowseIcon.Artist, Artist: a, Songs: a.Songs))
                .ToList();
        }

        if (parentId == PlaylistsId)
        {
            // Sin imagen el coche pinta un triangulo de aviso, como si fuera un error: cada lista
            // lleva la caratula de su primera cancion, o un icono.
            return (playlists?.Playlists ?? [])
                .Select(p => new BrowseNode(PlaylistPrefix + p.Id, p.Name, SongCountText(localization, p.SongIds.Count),
                    false, p.IsFavorites ? BrowseIcon.Favorites : BrowseIcon.Playlist,
                    Songs: p.IsFavorites ? null : library.FindByIds(p.SongIds)))
                .ToList();
        }

        if (parentId == AllSongsId)
            return Playables(library.Songs, AllSongsId);

        if (parentId.StartsWith(ArtistPrefix, StringComparison.Ordinal))
        {
            var artist = library.FindArtist(parentId[ArtistPrefix.Length..]);
            return artist is null ? [] : Playables(artist.Songs, parentId);
        }

        if (parentId.StartsWith(PlaylistPrefix, StringComparison.Ordinal))
        {
            var playlist = playlists?.Find(parentId[PlaylistPrefix.Length..]);
            return playlist is null ? [] : Playables(library.FindByIds(playlist.SongIds), parentId);
        }

        return [];
    }

    private static List<BrowseNode> Playables(IEnumerable<Song> songs, string contextId) =>
        songs.Take(MaxChildren)
            .Select(s => new BrowseNode(SongMediaId(s.Id, contextId), s.Title, s.ResolveGroupName(preferComposer: false),
                true, BrowseIcon.Song, Song: s))
            .ToList();

    /// <summary>«song|42|artist|Queen»: la cancion y la lista desde la que se eligio (para la cola).</summary>
    public static string SongMediaId(long songId, string contextId) => $"{SongPrefix}{songId}|{contextId}";

    /// <summary>Lee un identificador de cancion; null si no lo es o esta mal formado.</summary>
    public static (long SongId, string ContextId)? ParseSongMediaId(string? mediaId)
    {
        if (mediaId is null || !mediaId.StartsWith(SongPrefix, StringComparison.Ordinal))
            return null;

        var rest = mediaId[SongPrefix.Length..];
        var separator = rest.IndexOf('|');
        if (separator <= 0 || !long.TryParse(rest[..separator], out var songId))
            return null;

        return (songId, rest[(separator + 1)..]);
    }

    /// <summary>
    /// La cola que toca al elegir una cancion en el coche: la del grupo o la lista desde la que se
    /// eligio, empezando por ella. Si ya no esta ahi (se borro), solo esa cancion; null si no existe.
    /// </summary>
    public static (List<Song> Queue, int Index)? QueueFor(string? mediaId, IBrowseLibrary library, IPlaylistService? playlists)
    {
        if (ParseSongMediaId(mediaId) is not { } parsed)
            return null;

        var queue = ContextQueue(parsed.ContextId, library, playlists);
        var index = queue.FindIndex(song => song.Id == parsed.SongId);
        if (index >= 0)
            return (queue, index);

        return library.FindById(parsed.SongId) is { } single ? ([single], 0) : null;
    }

    private static List<Song> ContextQueue(string contextId, IBrowseLibrary library, IPlaylistService? playlists)
    {
        if (contextId.StartsWith(ArtistPrefix, StringComparison.Ordinal))
            return library.FindArtist(contextId[ArtistPrefix.Length..])?.Songs.ToList() ?? [];

        if (contextId.StartsWith(PlaylistPrefix, StringComparison.Ordinal))
        {
            var playlist = playlists?.Find(contextId[PlaylistPrefix.Length..]);
            return playlist is null ? [] : library.FindByIds(playlist.SongIds).ToList();
        }

        return library.Songs.ToList();
    }

    /// <summary>«1 cancion» / «12 canciones».</summary>
    public static string SongCountText(ILocalizationService? localization, int count)
    {
        if (localization is null)
            return count == 1 ? "1 song" : $"{count} songs";

        return count == 1 ? localization["SongCountOne"] : localization.Format("SongCountMany", count);
    }

    /// <summary>
    /// Controladores de medios que pueden navegar la biblioteca ademas de la propia aplicacion:
    /// Android Auto, su simulador, el asistente y el puente de Bluetooth del coche.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownCallers =
    [
        "com.google.android.projection.gearhead",
        "com.google.android.autosimulator",
        "com.google.android.carassistant",
        "com.google.android.googlequicksearchbox",
        "com.google.android.wearable.app",
        "com.android.bluetooth",
        "com.android.systemui",
    ];

    /// <summary>
    /// Si un llamante puede navegar: la propia app; quien tenga MEDIA_CONTENT_CONTROL; o un
    /// controlador conocido que ademas sea del sistema o venga de Play (un nombre de paquete a
    /// secas lo puede poner cualquier APK de fuera; pasando por Play, no).
    /// </summary>
    public static bool IsCallerAllowed(string caller, string ownPackage, Func<bool> hasMediaControl, Func<bool> isSystemOrFromPlay)
    {
        if (string.Equals(caller, ownPackage, StringComparison.Ordinal))
            return true;
        if (hasMediaControl())
            return true;
        return KnownCallers.Contains(caller, StringComparer.Ordinal) && isSystemOrFromPlay();
    }

    /// <summary>Instaladores que cuentan como «de Play».</summary>
    public static bool IsPlayInstaller(string? installer) => installer is "com.android.vending" or "com.google.android.feedback";
}
