using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

public class QueueOrderTests
{
    [Fact]
    public void Cola_vacia_no_tiene_orden()
    {
        var (order, index) = QueueOrder.Build(0, 0, shuffle: true, new Random(1));
        Assert.Empty(order);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void En_orden_se_empieza_en_la_elegida()
    {
        var (order, index) = QueueOrder.Build(5, 3, shuffle: false, new Random(1));
        Assert.Equal([0, 1, 2, 3, 4], order);
        Assert.Equal(3, index);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Aleatorio_es_una_permutacion_que_empieza_por_la_elegida(int seed)
    {
        var (order, index) = QueueOrder.Build(20, 13, shuffle: true, new Random(seed));

        Assert.Equal(0, index);
        Assert.Equal(13, order[0]);
        Assert.Equal(Enumerable.Range(0, 20), order.Order());
    }

    [Fact]
    public void Aleatorio_baraja_de_verdad()
    {
        var distinct = Enumerable.Range(0, 10)
            .Select(seed => string.Join(',', QueueOrder.Build(10, 0, true, new Random(seed)).Order))
            .Distinct()
            .Count();

        Assert.True(distinct > 1);
    }

    [Fact]
    public void Aleatorio_con_una_sola_cancion()
    {
        var (order, index) = QueueOrder.Build(1, 0, true, new Random(3));
        Assert.Equal([0], order);
        Assert.Equal(0, index);
    }

    [Theory]
    [InlineData(0, 3, RepeatMode.Off, false, 1)]
    [InlineData(1, 3, RepeatMode.Off, false, 2)]
    [InlineData(2, 3, RepeatMode.Off, false, null)] // fin de cola: se para
    [InlineData(2, 3, RepeatMode.Off, true, 0)]     // el usuario pide siguiente: vuelta
    [InlineData(2, 3, RepeatMode.All, false, 0)]
    [InlineData(2, 3, RepeatMode.One, false, 0)]
    [InlineData(0, 1, RepeatMode.Off, false, null)]
    public void Next_respeta_la_repeticion(int index, int count, RepeatMode repeat, bool user, int? expected) =>
        Assert.Equal(expected, QueueOrder.Next(index, count, repeat, user));

    [Theory]
    [InlineData(2, 3, 1)]
    [InlineData(0, 3, 2)]
    [InlineData(-1, 3, 2)]
    public void Previous_da_la_vuelta(int index, int count, int expected) =>
        Assert.Equal(expected, QueueOrder.Previous(index, count));
}

public class LibraryRulesTests
{
    private static Song S(long id, string title, string artist = "", string album = "", int track = 0,
        string composer = "", string albumArtist = "") =>
        new() { Id = id, Title = title, Artist = artist, Album = album, Track = track, Composer = composer, AlbumArtist = albumArtist };

    [Fact]
    public void Group_agrupa_ordena_y_descarta_sin_nombre()
    {
        var songs = new[]
        {
            S(1, "Zeta", "Beta", "B", 2),
            S(2, "Alfa", "Beta", "B", 1),
            S(3, "Otra", "alfa", "A"),
            S(4, "Sin grupo", "<unknown>"),
            S(5, "Primera", "Beta", "A", 9),
        };

        var groups = LibraryRules.Group(songs, preferComposer: false);

        Assert.Equal(["alfa", "Beta"], groups.Select(g => g.Name));
        var beta = groups[1];
        Assert.Equal([5L, 2L, 1L], beta.Songs.Select(s => s.Id)); // album A antes que B; en B por pista
        Assert.DoesNotContain(groups, g => g.Songs.Any(s => s.Id == 4));
    }

    [Fact]
    public void Group_no_separa_por_mayusculas_y_usa_la_grafia_mas_comun()
    {
        // Fallo encontrado: «beta» y «Beta» salian como dos grupos, pero FindArtist no distingue
        // mayusculas y siempre abria el primero; las canciones del otro quedaban inalcanzables.
        var groups = LibraryRules.Group([S(1, "a", "beta"), S(2, "b", "Beta"), S(3, "c", "Beta")], false);

        var group = Assert.Single(groups);
        Assert.Equal("Beta", group.Name);
        Assert.Equal(3, group.SongCount);
    }

    [Fact]
    public void Group_dentro_del_album_por_pista_y_titulo()
    {
        var songs = new[] { S(1, "b", "G", "X", 2), S(2, "a", "G", "X", 2), S(3, "z", "G", "X", 1) };
        var group = Assert.Single(LibraryRules.Group(songs, false));
        Assert.Equal([3L, 2L, 1L], group.Songs.Select(s => s.Id));
    }

    [Fact]
    public void Group_por_compositor()
    {
        var songs = new[] { S(1, "a", "Orquesta", composer: "Bach"), S(2, "b", "Orquesta", composer: "Bach") };
        Assert.Equal("Bach", Assert.Single(LibraryRules.Group(songs, true)).Name);
        Assert.Equal("Orquesta", Assert.Single(LibraryRules.Group(songs, false)).Name);
    }

    [Theory]
    [InlineData("titulo")]
    [InlineData("ARTISTA")]
    [InlineData("disco")]
    [InlineData("autor")]
    [InlineData("album artist")]
    public void MatchesSearch_mira_todos_los_campos(string term)
    {
        var song = S(1, "El Titulo", "Artista", "Disco", composer: "Autor", albumArtist: "Album Artist");
        Assert.True(LibraryRules.MatchesSearch(song, term));
    }

    [Fact]
    public void MatchesSearch_no_casa_lo_que_no_esta() =>
        Assert.False(LibraryRules.MatchesSearch(S(1, "a", "b", "c"), "zzz"));

    [Fact]
    public void Voz_sin_biblioteca_no_reproduce_nada() =>
        Assert.Empty(LibraryRules.ResolveVoiceQuery([], [], "algo"));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Voz_sin_texto_es_toda_la_biblioteca(string? query)
    {
        var songs = new[] { S(1, "a"), S(2, "b") };
        Assert.Same(songs, LibraryRules.ResolveVoiceQuery([], songs, query));
    }

    [Fact]
    public void Voz_prefiere_el_grupo_y_luego_titulo_o_album()
    {
        var songs = new[] { S(1, "Bohemian", "Queen", "Opera"), S(2, "Radio", "Other", "Kings Hits"), S(3, "X", "Y", "Z") };
        var groups = LibraryRules.Group(songs, false);

        Assert.Equal([1L], LibraryRules.ResolveVoiceQuery(groups, songs, " queen ").Select(s => s.Id));
        Assert.Equal([2L], LibraryRules.ResolveVoiceQuery(groups, songs, "radio").Select(s => s.Id));
        Assert.Equal([2L], LibraryRules.ResolveVoiceQuery(groups, songs, "kings").Select(s => s.Id));
        Assert.Empty(LibraryRules.ResolveVoiceQuery(groups, songs, "nada"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData(" <Unknown> ", "")]
    [InlineData("  Queen ", "Queen")]
    public void CleanTag(string? value, string expected) => Assert.Equal(expected, LibraryRules.CleanTag(value));

    [Theory]
    [InlineData("3/12", 3)]
    [InlineData("2024-05-01", 2024)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("99999999999", null)]
    public void ParseLeadingNumber(string? value, int? expected) =>
        Assert.Equal(expected, LibraryRules.ParseLeadingNumber(value));

    [Fact]
    public void CompleteTags_rellena_solo_lo_que_falta()
    {
        var song = new Song { FilePath = "/m/cancion.mp3", Title = "cancion", Artist = "", AlbumArtist = "AA", Album = "", Composer = "", Track = 0, Year = 1990 };

        var tags = LibraryRules.CompleteTags(song, "Titulo real", "Grupo", "Otro AA", "Disco", "Autor", 4, 2000);

        Assert.Equal(new SongTags("Titulo real", "Grupo", "AA", "Disco", "Autor", 4, 1990), tags);
    }

    [Fact]
    public void CompleteTags_no_pisa_un_titulo_de_verdad_ni_rellena_con_vacio()
    {
        var song = new Song { FilePath = "/m/01.mp3", Title = "Mi titulo", Track = 0, Year = 0 };
        Assert.Equal(new SongTags("Mi titulo", "", "", "", "", 0, 0),
            LibraryRules.CompleteTags(song, "Otro", "", "", "", "", null, null));

        var fileNamed = new Song { FilePath = "/m/01.mp3", Title = "01" };
        Assert.Equal("01", LibraryRules.CompleteTags(fileNamed, "", "", "", "", "", null, null).Title);
    }
}
