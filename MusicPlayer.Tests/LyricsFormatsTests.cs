using System.Text;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

public class LrcTests
{
    [Fact]
    public void Lrc_con_cabecera_y_varias_marcas_por_linea()
    {
        const string lrc = "[ar:Grupo]\r\n[ti:Titulo]\n[00:12.50]Segunda\n[00:01.00][00:30:00]Estribillo\n\n[00:20]Tercera";

        var lyrics = LyricsFormats.ParseLrc(lrc, "fichero.lrc");

        Assert.True(lyrics.IsSynced);
        Assert.Equal("fichero.lrc", lyrics.Source);
        Assert.Equal(
            [(1.0, "Estribillo"), (12.5, "Segunda"), (20.0, "Tercera"), (30.0, "Estribillo")],
            lyrics.Lines.Select(l => (l.Time!.Value.TotalSeconds, l.Text)));
    }

    [Fact]
    public void Lrc_sin_marcas_es_letra_plana()
    {
        var lyrics = LyricsFormats.ParseLrc("uno\n  dos  \n", "x");
        Assert.False(lyrics.IsSynced);
        Assert.Equal(["uno", "dos"], lyrics.Lines.Select(l => l.Text));
        Assert.All(lyrics.Lines, l => Assert.Null(l.Time));
    }

    [Fact]
    public void Lrc_corchete_que_no_es_marca_se_queda_como_texto()
    {
        var lyrics = LyricsFormats.ParseLrc("[Coro] la la\n[sin cerrar", "x");
        Assert.Equal(["[Coro] la la", "[sin cerrar"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Lrc_marca_con_segundos_malos_no_cuenta()
    {
        var lyrics = LyricsFormats.ParseLrc("[01:xx]hola", "x");
        Assert.False(lyrics.IsSynced);
        Assert.Equal("[01:xx]hola", Assert.Single(lyrics.Lines).Text);
    }

    [Fact]
    public void Lrc_vacio_o_solo_cabecera_es_vacio()
    {
        Assert.Same(Lyrics.Empty, LyricsFormats.ParseLrc("", "x"));
        Assert.Same(Lyrics.Empty, LyricsFormats.ParseLrc("[ar:Grupo]\n[al:Disco]", "x"));
    }

    [Fact]
    public void Lrc_sincronizado_descarta_lineas_sin_marca()
    {
        var lyrics = LyricsFormats.ParseLrc("Creditos\n[00:02]a\n[00:01]b", "x");
        Assert.Equal(["b", "a"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Plain_recorta_blancos_de_los_extremos_pero_no_entre_estrofas()
    {
        var lyrics = LyricsFormats.ParsePlain("\r\n\nuno\r\n\r\ndos\n\n", "tag");
        Assert.False(lyrics.IsSynced);
        Assert.Equal(["uno", "", "dos"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Plain_que_en_realidad_es_lrc()
    {
        var lyrics = LyricsFormats.ParsePlain("[00:03.00]hola", "tag");
        Assert.True(lyrics.IsSynced);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(lyrics.Lines).Time);
    }

    [Fact]
    public void Plain_con_corchetes_no_sincronizado_sigue_siendo_plano()
    {
        var lyrics = LyricsFormats.ParsePlain("[Coro]\nla la", "tag");
        Assert.False(lyrics.IsSynced);
        Assert.Equal(["[Coro]", "la la"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Plain_en_blanco_es_vacio() => Assert.Same(Lyrics.Empty, LyricsFormats.ParsePlain(" \n \n", "x"));
}

public class Id3Tests
{
    // ---- Construccion de etiquetas ID3v2 de prueba ------------------------------------------

    private static byte[] SyncSafe(int value) =>
        [(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];

    private static byte[] Frame(int version, string id, byte[] content)
    {
        var header = new List<byte>(Encoding.ASCII.GetBytes(id));
        if (version == 2)
            header.AddRange([(byte)(content.Length >> 16), (byte)(content.Length >> 8), (byte)content.Length]);
        else
        {
            header.AddRange(version == 4
                ? SyncSafe(content.Length)
                : [(byte)(content.Length >> 24), (byte)(content.Length >> 16), (byte)(content.Length >> 8), (byte)content.Length]);
            header.AddRange([0, 0]); // flags
        }

        return [.. header, .. content];
    }

    private static MemoryStream Tag(int version, params byte[][] frames)
    {
        var body = frames.SelectMany(f => f).Concat(new byte[16]).ToArray(); // con relleno final
        return new MemoryStream([(byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0, .. SyncSafe(body.Length), .. body]);
    }

    private static byte[] Uslt(byte encoding, string descriptor, string text)
    {
        var enc = Enc(encoding);
        var terminator = encoding is 1 or 2 ? new byte[2] : new byte[1];
        return [encoding, (byte)'s', (byte)'p', (byte)'a', .. enc.GetBytes(descriptor), .. terminator, .. enc.GetBytes(text)];
    }

    private static byte[] Sylt(byte encoding, byte format, params (string Text, int Ms)[] lines)
    {
        var enc = Enc(encoding);
        var terminator = encoding is 1 or 2 ? new byte[2] : new byte[1];
        var data = new List<byte> { encoding, (byte)'e', (byte)'n', (byte)'g', format, 1 };
        data.AddRange(terminator); // descriptor vacio
        foreach (var (text, ms) in lines)
        {
            data.AddRange(enc.GetBytes(text));
            data.AddRange(terminator);
            data.AddRange([(byte)(ms >> 24), (byte)(ms >> 16), (byte)(ms >> 8), (byte)ms]);
        }

        return [.. data];
    }

    private static Encoding Enc(byte code) => code switch
    {
        1 => new UnicodeEncoding(false, false),
        2 => Encoding.BigEndianUnicode,
        3 => Encoding.UTF8,
        _ => Encoding.Latin1,
    };

    // ---- Pruebas ------------------------------------------------------------------------------

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    public void Uslt_se_lee_en_cada_codificacion(int version, byte encoding)
    {
        using var stream = Tag(version, Frame(version, "TIT2", [0, (byte)'x']), Frame(version, "USLT", Uslt(encoding, "d", "Canción\nsegunda")));

        var lyrics = LyricsFormats.ReadId3(stream, "tag");

        Assert.False(lyrics.IsSynced);
        Assert.Equal(["Canción", "segunda"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Id3v22_usa_marcos_de_tres_letras()
    {
        using var stream = Tag(2, Frame(2, "ULT", Uslt(0, "", "hola")));
        Assert.Equal("hola", Assert.Single(LyricsFormats.ReadId3(stream, "tag").Lines).Text);
    }

    [Fact]
    public void Sylt_manda_sobre_uslt_y_se_ordena()
    {
        using var stream = Tag(4,
            Frame(4, "USLT", Uslt(3, "", "plana")),
            Frame(4, "SYLT", Sylt(3, 2, ("dos", 2000), ("uno", 500), ("", 700))));

        var lyrics = LyricsFormats.ReadId3(stream, "tag");

        Assert.True(lyrics.IsSynced);
        Assert.Equal([("uno", 500.0), ("dos", 2000.0)], lyrics.Lines.Select(l => (l.Text, l.Time!.Value.TotalMilliseconds)));
    }

    [Fact]
    public void Sylt_en_utf16()
    {
        using var stream = Tag(3, Frame(3, "SYLT", Sylt(1, 2, ("ñu", 100))));
        Assert.Equal("ñu", Assert.Single(LyricsFormats.ReadId3(stream, "tag").Lines).Text);
    }

    [Fact]
    public void Sylt_en_fotogramas_se_ignora_y_queda_la_plana()
    {
        using var stream = Tag(3, Frame(3, "SYLT", Sylt(0, 1, ("x", 1))), Frame(3, "USLT", Uslt(0, "", "plana")));
        var lyrics = LyricsFormats.ReadId3(stream, "tag");
        Assert.False(lyrics.IsSynced);
        Assert.Equal("plana", Assert.Single(lyrics.Lines).Text);
    }

    [Fact]
    public void Sylt_con_lrc_dentro_de_uslt_se_sincroniza()
    {
        using var stream = Tag(3, Frame(3, "USLT", Uslt(0, "", "[00:01.00]a\n[00:02.00]b")));
        Assert.True(LyricsFormats.ReadId3(stream, "tag").IsSynced);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'T', (byte)'A', (byte)'G', 3, 0, 0, 0, 0, 0, 0 })] // no es ID3v2
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3', 5, 0, 0, 0, 0, 0, 10 })] // version desconocida
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3' })] // cabecera cortada
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 10 })] // sin cuerpo
    public void Id3_invalido_es_vacio(byte[] data) =>
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadId3(new MemoryStream(data), "tag"));

    [Fact]
    public void Id3_sin_letra_es_vacio()
    {
        using var stream = Tag(3, Frame(3, "TIT2", [0, (byte)'x']));
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadId3(stream, "tag"));
    }

    [Fact]
    public void Marco_que_se_sale_de_la_etiqueta_para_la_lectura()
    {
        var frame = Frame(3, "USLT", Uslt(0, "", "hola"));
        frame[7] = 200; // tamano mentiroso
        using var stream = Tag(3, frame);
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadId3(stream, "tag"));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2 })]                  // uslt demasiado corto
    [InlineData(new byte[] { 0, 1, 2, 3, 4, 5 })]         // descriptor sin terminar
    [InlineData(new byte[] { 0, (byte)'e', (byte)'n', (byte)'g', 0 })] // letra vacia
    [InlineData(new byte[] { 1, (byte)'e', (byte)'n', (byte)'g', 65, 0, 66 })] // descriptor utf16 sin terminar
    public void Uslt_roto_se_ignora(byte[] content)
    {
        using var stream = Tag(3, Frame(3, "USLT", content));
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadId3(stream, "tag"));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2, 3, 2, 1 })]             // sylt demasiado corto
    [InlineData(new byte[] { 0, 1, 2, 3, 2, 1, 5, 5, 5 })]    // descriptor sin terminar
    [InlineData(new byte[] { 0, 1, 2, 3, 2, 1, 0, 65, 0, 0 })] // marca de tiempo cortada
    [InlineData(new byte[] { 1, 1, 2, 3, 2, 1, 0, 0, 65, 0, 66, 0, 67, 0, 68, 0 })] // texto utf16 sin terminar
    [InlineData(new byte[] { 0, 1, 2, 3, 2, 1, 0, 65, 66, 0, 1, 2 })] // marca de tiempo que no cabe
    [InlineData(new byte[] { 1, 1, 2, 3, 2, 1, 65, 0, 66 })] // descriptor utf16 sin terminar
    public void Sylt_roto_se_ignora(byte[] content)
    {
        using var stream = Tag(3, Frame(3, "SYLT", content));
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadId3(stream, "tag"));
    }
}

public class FlacTests
{
    private static byte[] Le(int value) => BitConverter.GetBytes(value);

    private static byte[] VorbisBlock(params string[] comments)
    {
        var vendor = Encoding.UTF8.GetBytes("test");
        var data = new List<byte>();
        data.AddRange(Le(vendor.Length));
        data.AddRange(vendor);
        data.AddRange(Le(comments.Length));
        foreach (var comment in comments)
        {
            var bytes = Encoding.UTF8.GetBytes(comment);
            data.AddRange(Le(bytes.Length));
            data.AddRange(bytes);
        }

        return [.. data];
    }

    private static byte[] Block(int type, bool last, byte[] content) =>
        [(byte)((last ? 0x80 : 0) | type), (byte)(content.Length >> 16), (byte)(content.Length >> 8), (byte)content.Length, .. content];

    private static Stream Flac(bool seekable, params byte[][] blocks)
    {
        byte[] data = [(byte)'f', (byte)'L', (byte)'a', (byte)'C', .. blocks.SelectMany(b => b)];
        return seekable ? new MemoryStream(data) : new ForwardOnlyStream(data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lee_la_letra_de_los_comentarios_saltando_otros_bloques(bool seekable)
    {
        using var stream = Flac(seekable,
            Block(0, false, new byte[34]),
            Block(4, true, VorbisBlock("TITLE=x", "sin igual", "=vacio", "lyrics=  ", "UNSYNCEDLYRICS=hola\nadios")));

        var lyrics = LyricsFormats.ReadFlac(stream, "flac");

        Assert.Equal(["hola", "adios"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Syncedlyrics_en_lrc()
    {
        using var stream = Flac(true, Block(4, true, VorbisBlock("SYNCEDLYRICS=[00:01.00]a")));
        Assert.True(LyricsFormats.ReadFlac(stream, "flac").IsSynced);
    }

    [Fact]
    public void Sin_comentarios_es_vacio()
    {
        using var stream = Flac(true, Block(0, true, new byte[34]));
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(stream, "flac"));
    }

    [Fact]
    public void Comentarios_sin_letra_es_vacio()
    {
        using var stream = Flac(true, Block(4, true, VorbisBlock("TITLE=x")));
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(stream, "flac"));
    }

    [Fact]
    public void No_es_flac() =>
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(new MemoryStream("fLaX"u8.ToArray()), "flac"));

    [Fact]
    public void Fichero_cortado()
    {
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(new MemoryStream("fLaC\0\0"u8.ToArray()), "flac"));
        // Bloque de comentarios anunciado mas largo de lo que hay.
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(new MemoryStream([.. "fLaC"u8, 0x84, 0, 1, 0, 1, 2]), "flac"));
        // Bloque que se salta sin poder buscar y que no esta entero.
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(new ForwardOnlyStream([.. "fLaC"u8, 0, 0, 1, 0, 1]), "flac"));
    }

    [Fact]
    public void Comentarios_rotos_no_revientan()
    {
        // Vendedor que se sale del bloque.
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(Flac(true, Block(4, true, [.. Le(100), 1, 2])), "flac"));
        // Longitud de comentario que se sale del bloque.
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(Flac(true, Block(4, true, [.. Le(0), .. Le(1), .. Le(50), 65])), "flac"));
        // Cabecera de vendedor incompleta.
        Assert.Same(Lyrics.Empty, LyricsFormats.ReadFlac(Flac(true, Block(4, true, [1, 2])), "flac"));
    }

    /// <summary>Flujo que no admite buscar, como el de un content:// de Android.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
