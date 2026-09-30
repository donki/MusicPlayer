using MusicPlayer.Helpers;
using MusicPlayer.Models;

namespace MusicPlayer.Tests;

public class SongTests
{
    [Theory]
    [InlineData("/music/a.mp3", "MP3")]
    [InlineData("/music/a.b.flac", "FLAC")]
    [InlineData("/music/sin-extension", "")]
    [InlineData("", "")]
    public void Format_es_la_extension_en_mayusculas(string path, string expected) =>
        Assert.Equal(expected, new Song { FilePath = path }.Format);

    [Fact]
    public void Agrupa_por_artista_del_album_antes_que_por_el_de_la_pista()
    {
        var song = new Song { Artist = "Invitado", AlbumArtist = "  Grupo  ", Composer = "Autor" };
        Assert.Equal("Grupo", song.ResolveGroupName(preferComposer: false));
    }

    [Fact]
    public void Con_compositor_preferido_manda_el_compositor()
    {
        var song = new Song { Artist = "Orquesta", AlbumArtist = "Orquesta", Composer = "Bach" };
        Assert.Equal("Bach", song.ResolveGroupName(preferComposer: true));
    }

    [Fact]
    public void Unknown_y_blancos_no_son_nombre_de_grupo()
    {
        var song = new Song { AlbumArtist = "<UNKNOWN>", Artist = "   ", Composer = "Mozart" };
        Assert.Equal("Mozart", song.ResolveGroupName(preferComposer: false));
        Assert.Equal(string.Empty, new Song { Artist = "<unknown>" }.ResolveGroupName(false));
    }
}

public class ArtistGroupTests
{
    [Fact]
    public void Cuenta_canciones_y_suma_la_duracion()
    {
        var group = new ArtistGroup
        {
            Name = "G",
            Songs = [new Song { Duration = TimeSpan.FromSeconds(90) }, new Song { Duration = TimeSpan.FromSeconds(30) }],
        };

        Assert.Equal(2, group.SongCount);
        Assert.Equal(TimeSpan.FromMinutes(2), group.TotalDuration);
        Assert.Null(group.ImagePath);
        Assert.Null(group.Description);
    }
}

public class SongTagsTests
{
    private static readonly Song Original = new()
    {
        Id = 7, ContentUri = "content://7", FilePath = "/m/7.mp3", AlbumId = 3, Duration = TimeSpan.FromSeconds(200),
        Title = "T", Artist = "A", AlbumArtist = "AA", Album = "Al", Composer = "C", Track = 2, Year = 1999,
    };

    [Fact]
    public void From_copia_las_etiquetas_editables() =>
        Assert.Equal(new SongTags("T", "A", "AA", "Al", "C", 2, 1999), SongTags.From(Original));

    [Fact]
    public void ApplyTo_cambia_las_etiquetas_y_respeta_lo_demas()
    {
        var edited = new SongTags("T2", "A2", "AA2", "Al2", "C2", 5, 2001).ApplyTo(Original);

        Assert.Equal(7, edited.Id);
        Assert.Equal("content://7", edited.ContentUri);
        Assert.Equal("/m/7.mp3", edited.FilePath);
        Assert.Equal(3, edited.AlbumId);
        Assert.Equal(TimeSpan.FromSeconds(200), edited.Duration);
        Assert.Equal(("T2", "A2", "AA2", "Al2", "C2", 5, 2001),
            (edited.Title, edited.Artist, edited.AlbumArtist, edited.Album, edited.Composer, edited.Track, edited.Year));
    }
}

public class PlaylistTests
{
    [Fact]
    public void Solo_la_de_favoritas_es_favoritas()
    {
        Assert.True(new Playlist { Id = Playlist.FavoritesId }.IsFavorites);
        var other = new Playlist();
        Assert.False(other.IsFavorites);
        Assert.Equal(32, other.Id.Length);
        Assert.NotEqual(other.Id, new Playlist().Id);
        Assert.Empty(other.SongIds);
    }
}

public class LyricsTests
{
    private static readonly Lyrics Synced = new(
    [
        new LyricLine(TimeSpan.FromSeconds(5), "uno"),
        new LyricLine(TimeSpan.FromSeconds(10), "dos"),
        new LyricLine(TimeSpan.FromSeconds(20), "tres"),
    ], true, "lrc");

    [Theory]
    [InlineData(0, -1)]
    [InlineData(5, 0)]
    [InlineData(9.9, 0)]
    [InlineData(10, 1)]
    [InlineData(300, 2)]
    public void IndexAt_da_la_ultima_linea_empezada(double seconds, int expected) =>
        Assert.Equal(expected, Synced.IndexAt(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void IndexAt_sin_sincronizar_es_menos_uno()
    {
        var plain = new Lyrics([new LyricLine(null, "x")], false, "tag");
        Assert.Equal(-1, plain.IndexAt(TimeSpan.FromMinutes(1)));
        Assert.True(plain.HasLyrics);
        Assert.False(Lyrics.Empty.HasLyrics);
    }

    [Fact]
    public void IndexAt_salta_lineas_sin_marca()
    {
        var mixed = new Lyrics(
        [
            new LyricLine(null, "titulo"),
            new LyricLine(TimeSpan.FromSeconds(1), "a"),
            new LyricLine(TimeSpan.FromSeconds(3), "b"),
        ], true, "x");

        Assert.Equal(1, mixed.IndexAt(TimeSpan.FromSeconds(2)));
    }
}

public class TimeFormatterTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-10, "0:00")]
    public void Format_minutos_u_horas(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormatter.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Format_de_desconocido_es_cero() =>
        Assert.Equal("0:00", TimeFormatter.Format(TimeSpan.MaxValue));

    [Fact]
    public void FormatProgress_une_posicion_y_total() =>
        Assert.Equal("1:05 / 3:00", TimeFormatter.FormatProgress(TimeSpan.FromSeconds(65), TimeSpan.FromMinutes(3)));
}
