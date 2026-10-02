using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

/// <summary>Arbol de Android Auto, cola de reproduccion y comprobacion de version, sin movil ni coche.</summary>
public class BrowseAndQueueTests
{
    private static Song S(long id, string title, string artist = "Queen") =>
        new() { Id = id, Title = title, Artist = artist, ContentUri = $"content://media/{id}" };

    private sealed class FakeLibrary : IBrowseLibrary
    {
        public List<Song> All { get; } = [];
        public List<ArtistGroup> Groups { get; } = [];
        public IReadOnlyList<Song> Songs => All;
        public IReadOnlyList<ArtistGroup> Artists => Groups;
        public Song? FindById(long id) => All.FirstOrDefault(s => s.Id == id);
        public IReadOnlyList<Song> FindByIds(IEnumerable<long> ids) => ids.Select(FindById).OfType<Song>().ToList();
        public ArtistGroup? FindArtist(string name) => Groups.FirstOrDefault(g => g.Name == name);
    }

    private sealed class FakePlaylists : IPlaylistService
    {
        public List<Playlist> Lists { get; } = [];
        public event EventHandler? PlaylistsChanged { add { } remove { } }
        public IReadOnlyList<Playlist> Playlists => Lists;
        public Playlist? Find(string playlistId) => Lists.FirstOrDefault(p => p.Id == playlistId);
        public Playlist? Create(string name) => throw new NotSupportedException();
        public bool Rename(string playlistId, string name) => throw new NotSupportedException();
        public void Delete(string playlistId) => throw new NotSupportedException();
        public IReadOnlyList<string> PlaylistIdsContaining(long songId) => throw new NotSupportedException();
        public void SetMembership(long songId, IReadOnlyCollection<string> playlistIds) => throw new NotSupportedException();
        public void AddSongs(IReadOnlyCollection<long> songIds, IReadOnlyCollection<string> playlistIds) => throw new NotSupportedException();
        public void RemoveSong(string playlistId, long songId) => throw new NotSupportedException();
        public void RemoveSongs(string playlistId, IReadOnlyCollection<long> songIds) => throw new NotSupportedException();
        public void RemoveSongEverywhere(long songId) => throw new NotSupportedException();
        public void RemoveSongsEverywhere(IReadOnlyCollection<long> songIds) => throw new NotSupportedException();
        public bool IsFavorite(long songId) => throw new NotSupportedException();
        public bool ToggleFavorite(long songId) => throw new NotSupportedException();
    }

    private static (FakeLibrary Library, FakePlaylists Playlists) Data()
    {
        var library = new FakeLibrary();
        library.All.AddRange([S(1, "Bohemian"), S(2, "Radio Ga Ga"), S(3, "Yesterday", "Beatles")]);
        library.Groups.Add(new ArtistGroup { Name = "Queen", Songs = [library.All[0], library.All[1]], ImagePath = "/fotos/queen.jpg" });
        library.Groups.Add(new ArtistGroup { Name = "Beatles", Songs = [library.All[2]] });
        var playlists = new FakePlaylists();
        playlists.Lists.Add(new Playlist { Id = Playlist.FavoritesId, Name = "Favoritas", SongIds = [3] });
        playlists.Lists.Add(new Playlist { Id = "p1", Name = "Coche", SongIds = [2, 99, 1] });
        return (library, playlists);
    }

    // -----------------------------------------------------------------------
    // Arbol de Android Auto
    // -----------------------------------------------------------------------

    [Fact]
    public void LaRaizTieneFavoritasGruposListasYCanciones()
    {
        var (library, playlists) = Data();
        var root = BrowseTree.Children(BrowseTree.RootId, library, playlists, null);

        Assert.Equal([BrowseTree.FavoritesId, BrowseTree.ArtistsId, BrowseTree.PlaylistsId, BrowseTree.AllSongsId], root.Select(n => n.MediaId));
        Assert.Equal(["Favorites", "Artists", "Playlists", "Songs"], root.Select(n => n.Title));
        Assert.All(root, n => Assert.False(n.Playable));
        Assert.Equal(BrowseIcon.Favorites, root[0].Icon);
        Assert.Equal("playlist|favorites", BrowseTree.FavoritesId);
    }

    [Fact]
    public void LaRaizSaleEnElIdiomaDeLaApp()
    {
        var (library, playlists) = Data();
        var es = new LocalizationService(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance);
        var root = BrowseTree.Children(BrowseTree.RootId, library, playlists, es);
        Assert.Equal(["Favoritas", "Grupos"], root.Take(2).Select(n => n.Title));
        Assert.Equal("1 canción", BrowseTree.SongCountText(es, 1));
        Assert.Equal("12 canciones", BrowseTree.SongCountText(es, 12));
        Assert.Equal(("1 song", "3 songs"), (BrowseTree.SongCountText(null, 1), BrowseTree.SongCountText(null, 3)));
    }

    [Fact]
    public void GruposYListasConSuImagen()
    {
        var (library, playlists) = Data();

        var artists = BrowseTree.Children(BrowseTree.ArtistsId, library, playlists, null);
        Assert.Equal(["artist|Queen", "artist|Beatles"], artists.Select(n => n.MediaId));
        Assert.Equal("2 songs", artists[0].Subtitle);
        Assert.Equal("/fotos/queen.jpg", artists[0].Artist!.ImagePath);
        Assert.Equal(BrowseIcon.Artist, artists[0].Icon);

        var lists = BrowseTree.Children(BrowseTree.PlaylistsId, library, playlists, null);
        Assert.Equal((BrowseIcon.Favorites, (IReadOnlyList<Song>?)null), (lists[0].Icon, lists[0].Songs));
        Assert.Equal(BrowseIcon.Playlist, lists[1].Icon);
        Assert.Equal([2L, 1L], lists[1].Songs!.Select(s => s.Id));   // la que ya no existe no sale
        Assert.Equal("3 songs", lists[1].Subtitle);                  // el recuento es el de la lista
        Assert.Empty(BrowseTree.Children(BrowseTree.PlaylistsId, library, null, null));
    }

    [Fact]
    public void LasCancionesSeReproducenConSuContexto()
    {
        var (library, playlists) = Data();

        var all = BrowseTree.Children(BrowseTree.AllSongsId, library, playlists, null);
        Assert.Equal("song|1|allsongs", all[0].MediaId);
        Assert.All(all, n => Assert.True(n.Playable && n.Icon == BrowseIcon.Song && n.Song is not null));
        Assert.Equal("Queen", all[0].Subtitle);

        Assert.Equal(["song|1|artist|Queen", "song|2|artist|Queen"], BrowseTree.Children("artist|Queen", library, playlists, null).Select(n => n.MediaId));
        Assert.Equal(["song|2|playlist|p1", "song|1|playlist|p1"], BrowseTree.Children("playlist|p1", library, playlists, null).Select(n => n.MediaId));
        Assert.Empty(BrowseTree.Children("artist|Nadie", library, playlists, null));
        Assert.Empty(BrowseTree.Children("playlist|no", library, playlists, null));
        Assert.Empty(BrowseTree.Children("otra|cosa", library, playlists, null));
        Assert.Empty(BrowseTree.Children(null, library, playlists, null));
    }

    [Fact]
    public void NoMasDeQuinientosPorCarpeta()
    {
        var library = new FakeLibrary();
        library.All.AddRange(Enumerable.Range(1, 600).Select(i => S(i, $"T{i}")));
        Assert.Equal(BrowseTree.MaxChildren, BrowseTree.Children(BrowseTree.AllSongsId, library, null, null).Count);
    }

    [Theory]
    [InlineData("song|42|artist|Queen", 42L, "artist|Queen")]
    [InlineData("song|7|allsongs", 7L, "allsongs")]
    [InlineData("song|7|", 7L, "")]
    public void SeLeeElIdentificadorDeCancion(string mediaId, long id, string context) =>
        Assert.Equal((id, context), BrowseTree.ParseSongMediaId(mediaId));

    [Theory]
    [InlineData(null)]
    [InlineData("artist|Queen")]
    [InlineData("song|")]
    [InlineData("song||x")]
    [InlineData("song|abc|x")]
    [InlineData("song|42")]
    public void IdentificadoresQueNoSonCancion(string? mediaId) => Assert.Null(BrowseTree.ParseSongMediaId(mediaId));

    [Fact]
    public void LaColaEsLaDelSitioDesdeElQueSeEligio()
    {
        var (library, playlists) = Data();

        var fromArtist = BrowseTree.QueueFor("song|2|artist|Queen", library, playlists)!.Value;
        Assert.Equal("1,2|1", string.Join(",", fromArtist.Queue.Select(s => s.Id)) + "|" + fromArtist.Index);

        var fromList = BrowseTree.QueueFor("song|1|playlist|p1", library, playlists)!.Value;
        Assert.Equal("2,1|1", string.Join(",", fromList.Queue.Select(s => s.Id)) + "|" + fromList.Index);

        var fromAll = BrowseTree.QueueFor(BrowseTree.SongMediaId(3, BrowseTree.AllSongsId), library, playlists)!.Value;
        Assert.Equal(2, fromAll.Index);

        // Ya no esta en la lista (o la lista no existe): suena sola.
        var alone = BrowseTree.QueueFor("song|3|playlist|p1", library, playlists)!.Value;
        Assert.Equal("3|0", string.Join(",", alone.Queue.Select(s => s.Id)) + "|" + alone.Index);
        Assert.Single(BrowseTree.QueueFor("song|3|playlist|borrada", library, null)!.Value.Queue);
        Assert.Single(BrowseTree.QueueFor("song|1|artist|Nadie", library, playlists)!.Value.Queue);

        Assert.Null(BrowseTree.QueueFor("song|99|allsongs", library, playlists));
        Assert.Null(BrowseTree.QueueFor("basura", library, playlists));
    }

    [Theory]
    [InlineData("com.socratic.musicplayer", false, false, true)]       // la propia app
    [InlineData("com.otra.app", true, false, true)]                    // con MEDIA_CONTENT_CONTROL
    [InlineData("com.google.android.projection.gearhead", false, true, true)]   // Auto desde Play
    [InlineData("com.google.android.projection.gearhead", false, false, false)] // Auto de fuera
    [InlineData("com.desconocida", false, true, false)]
    public void QuienPuedeNavegar(string caller, bool mediaControl, bool fromPlay, bool allowed) =>
        Assert.Equal(allowed, BrowseTree.IsCallerAllowed(caller, "com.socratic.musicplayer", () => mediaControl, () => fromPlay));

    [Theory]
    [InlineData("com.android.vending", true)]
    [InlineData("com.google.android.feedback", true)]
    [InlineData("com.amazon.venezia", false)]
    [InlineData(null, false)]
    public void InstaladoresDePlay(string? installer, bool fromPlay) => Assert.Equal(fromPlay, BrowseTree.IsPlayInstaller(installer));

    // -----------------------------------------------------------------------
    // Cola de reproduccion
    // -----------------------------------------------------------------------

    private static List<Song> Five() => Enumerable.Range(1, 5).Select(i => S(i, $"T{i}")).ToList();

    [Fact]
    public void ColaVaciaNoTieneActualNiSeMueve()
    {
        var queue = new PlaybackQueue(new Random(1));
        Assert.True(queue.IsEmpty);
        Assert.Null(queue.Current);
        Assert.Equal(-1, queue.QueueIndex);
        Assert.False(queue.MoveNext(userRequested: true));
        Assert.False(queue.MovePrevious());

        queue.Load([], 3);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void CargarEmpiezaDondeSeDiceAcotado()
    {
        var queue = new PlaybackQueue(new Random(1));
        queue.Load(Five(), 2);
        Assert.Equal((3L, 2), (queue.Current!.Id, queue.QueueIndex));

        queue.Load(Five(), 99);
        Assert.Equal(5L, queue.Current!.Id);
        queue.Load(Five(), -4);
        Assert.Equal(1L, queue.Current!.Id);
        Assert.Equal(5, queue.Songs.Count);
    }

    [Fact]
    public void SiguienteYAnteriorSegunLaRepeticion()
    {
        var queue = new PlaybackQueue(new Random(1));
        queue.Load(Five(), 3);

        Assert.True(queue.MoveNext(userRequested: false));
        Assert.Equal(5L, queue.Current!.Id);
        Assert.False(queue.MoveNext(userRequested: false));   // fin sin repetir: se para
        Assert.Equal(5L, queue.Current!.Id);

        queue.Repeat = RepeatMode.All;
        Assert.True(queue.MoveNext(userRequested: false));
        Assert.Equal(1L, queue.Current!.Id);

        Assert.True(queue.MovePrevious());
        Assert.Equal(5L, queue.Current!.Id);

        queue.Stop();
        Assert.Null(queue.Current);
        Assert.False(queue.IsEmpty);   // la cola se conserva
    }

    [Fact]
    public void AleatorioNoCortaLaQueSuena()
    {
        var queue = new PlaybackQueue(new Random(7));
        queue.Load(Five(), 2);

        Assert.True(queue.SetShuffle(true));
        Assert.False(queue.SetShuffle(true));   // ya estaba
        Assert.True(queue.Shuffle);
        Assert.Equal(3L, queue.Current!.Id);

        var seen = new HashSet<long> { queue.Current.Id };
        while (queue.MoveNext(userRequested: false))
            seen.Add(queue.Current!.Id);
        Assert.Equal(5, seen.Count);   // barajada, pero pasan todas una vez

        Assert.True(queue.SetShuffle(false));
        Assert.False(queue.Shuffle);

        queue.Stop();
        Assert.True(queue.SetShuffle(true));   // sin actual: empieza por el principio
        Assert.NotNull(queue.Current);
    }

    // -----------------------------------------------------------------------
    // Version
    // -----------------------------------------------------------------------

    private static UpdateService Updates(string json, List<string> opened, Func<string>? version = null) =>
        new(new LocalizationService(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance),
            NullLogger<UpdateService>.Instance, () => Task.FromResult(json), version ?? (() => "2026.10.02.0"),
            url => { opened.Add(url); return Task.CompletedTask; });

    [Fact]
    public async Task VersionNuevaSePreguntaUnaVezYAbreElEnlace()
    {
        var opened = new List<string>();
        var asked = new List<string>();
        var updates = Updates("""{"version":"2026.10.3.0","url":"https://example.org/mp"}""", opened);

        await updates.CheckAndPromptAsync((t, m, a, c) => { asked.Add(m); return Task.FromResult(true); });
        await updates.CheckAndPromptAsync((t, m, a, c) => { asked.Add(m); return Task.FromResult(true); });

        Assert.Single(asked);
        Assert.Contains("2026.10.3.0", asked[0]);
        Assert.Equal(["https://example.org/mp"], opened);
    }

    [Theory]
    [InlineData("""{"version":"2026.10.02.0","url":"x"}""", false)]
    [InlineData("""{"url":"x"}""", false)]
    [InlineData("no es json", false)]
    [InlineData("""{"version":"2099.1"}""", true)]
    public async Task SinVersionNuevaOSinEnlaceNoSeAbreNada(string json, bool asks)
    {
        var opened = new List<string>();
        var asked = 0;
        await Updates(json, opened).CheckAndPromptAsync((t, m, a, c) => { asked++; return Task.FromResult(true); });
        Assert.Equal(asks ? 1 : 0, asked);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task SinRedNoMolesta()
    {
        var updates = new UpdateService(new LocalizationService(new FakeSettings(), NullLogger<LocalizationService>.Instance),
            NullLogger<UpdateService>.Instance, () => throw new HttpRequestException("sin red"), () => "1", _ => Task.CompletedTask);
        await updates.CheckAndPromptAsync((t, m, a, c) => throw new InvalidOperationException("no deberia preguntar"));
        Assert.NotNull(new UpdateService(new LocalizationService(new FakeSettings(), NullLogger<LocalizationService>.Instance), NullLogger<UpdateService>.Instance));
    }

    [Theory]
    [InlineData("2026.10.1.0", "2026.9.30.0", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.x", "1.1", -1)]
    public void CompararVersiones(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(UpdateService.CompareVersions(a, b)));
}
