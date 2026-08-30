using System.Globalization;
using System.Text;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Lectura de los formatos en los que viaja una letra. Es codigo puro, sin plataforma: quien abre
/// el fichero es el servicio de Android, aqui solo se interpretan los bytes.
/// </summary>
public static class LyricsFormats
{
    /// <summary>
    /// Texto en formato LRC: <c>[mm:ss.xx]linea</c>. Una linea puede llevar varias marcas de
    /// tiempo (estribillos) y el fichero suele empezar con etiquetas como <c>[ar:]</c>, que no son
    /// letra y se descartan.
    /// </summary>
    public static Lyrics ParseLrc(string content, string source)
    {
        var lines = new List<LyricLine>();
        var synced = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            var times = new List<TimeSpan>();
            var index = 0;

            while (index < line.Length && line[index] == '[')
            {
                var close = line.IndexOf(']', index);
                if (close < 0)
                    break;

                var tag = line[(index + 1)..close];
                if (TryParseTimestamp(tag, out var time))
                    times.Add(time);
                else if (!IsMetadataTag(tag))
                    break;   // no es marca ni etiqueta conocida: a partir de aqui es texto

                index = close + 1;
            }

            var text = line[index..].Trim();

            if (times.Count == 0)
            {
                // Sin marca de tiempo: es una letra plana (o la cabecera del fichero).
                if (text.Length > 0)
                    lines.Add(new LyricLine(null, text));
                continue;
            }

            synced = true;
            foreach (var time in times)
                lines.Add(new LyricLine(time, text));
        }

        if (synced)
            lines = lines.Where(l => l.Time is not null).OrderBy(l => l.Time!.Value).ToList();

        return lines.Count == 0 ? Lyrics.Empty : new Lyrics(lines, synced, source);
    }

    /// <summary>Letra sin sincronizar: una linea por salto de linea.</summary>
    public static Lyrics ParsePlain(string content, string source)
    {
        // Un texto plano puede venir en realidad en formato LRC dentro de la etiqueta.
        if (content.Contains("["))
        {
            var parsed = ParseLrc(content, source);
            if (parsed.IsSynced)
                return parsed;
        }

        var lines = content
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Select(line => new LyricLine(null, line))
            .ToList();

        // Se recortan los blancos del principio y del final, pero no los de en medio: separan
        // estrofas y sin ellos la letra se lee como un ladrillo.
        while (lines.Count > 0 && lines[0].Text.Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Text.Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines.Count == 0 ? Lyrics.Empty : new Lyrics(lines, false, source);
    }

    private static bool IsMetadataTag(string tag) =>
        tag.Length > 3 && tag[2] == ':' && !char.IsDigit(tag[0]);

    /// <summary>Marca de tiempo LRC: <c>mm:ss</c>, <c>mm:ss.xx</c> o <c>mm:ss:xx</c>.</summary>
    private static bool TryParseTimestamp(string tag, out TimeSpan time)
    {
        time = default;

        var colon = tag.IndexOf(':');
        if (colon <= 0 || !int.TryParse(tag[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return false;

        var rest = tag[(colon + 1)..].Replace(':', '.');
        if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return false;

        time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    // ==================================================================================
    //  ID3v2 (MP3): USLT es la letra plana y SYLT la sincronizada
    // ==================================================================================

    /// <summary>
    /// Busca la letra en la cabecera ID3v2 del fichero. Se lee a mano en vez de con una biblioteca
    /// de etiquetas porque las disponibles son LGPL y la regla es MIT y uso comercial
    /// (constitucion 4); ademas aqui solo hacen falta dos marcos de los cientos que existen.
    /// </summary>
    public static Lyrics ReadId3(Stream stream, string source)
    {
        var header = new byte[10];
        if (stream.Read(header, 0, 10) != 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
            return Lyrics.Empty;

        var version = header[3];
        if (version is < 2 or > 4)
            return Lyrics.Empty;

        var tagSize = SyncSafe(header, 6);
        var body = new byte[tagSize];
        var read = ReadExactly(stream, body);
        if (read <= 0)
            return Lyrics.Empty;

        var position = 0;
        var frameHeaderSize = version == 2 ? 6 : 10;
        Lyrics? unsynced = null;

        while (position + frameHeaderSize <= read)
        {
            string frameId;
            int frameSize;

            if (version == 2)
            {
                frameId = Encoding.ASCII.GetString(body, position, 3);
                frameSize = (body[position + 3] << 16) | (body[position + 4] << 8) | body[position + 5];
            }
            else
            {
                frameId = Encoding.ASCII.GetString(body, position, 4);
                frameSize = version == 4
                    ? SyncSafe(body, position + 4)
                    : (body[position + 4] << 24) | (body[position + 5] << 16) | (body[position + 6] << 8) | body[position + 7];
            }

            // Relleno de ceros al final de la etiqueta: ya no hay mas marcos.
            if (frameId.Length == 0 || frameId[0] == '\0' || frameSize <= 0)
                break;

            var contentStart = position + frameHeaderSize;
            if (contentStart + frameSize > read)
                break;

            var content = new ReadOnlySpan<byte>(body, contentStart, frameSize);

            // La sincronizada manda: es la que permite seguir la letra mientras suena.
            if (frameId is "SYLT" or "SLT")
            {
                var synced = ReadSylt(content, source);
                if (synced.HasLyrics)
                    return synced;
            }
            else if (frameId is "USLT" or "ULT")
            {
                var plain = ReadUslt(content, source);
                if (plain.HasLyrics)
                    unsynced ??= plain;
            }

            position = contentStart + frameSize;
        }

        return unsynced ?? Lyrics.Empty;
    }

    /// <summary>USLT: codificacion, idioma (3), descriptor terminado en nulo y la letra.</summary>
    private static Lyrics ReadUslt(ReadOnlySpan<byte> content, string source)
    {
        if (content.Length < 5)
            return Lyrics.Empty;

        var encoding = EncodingFor(content[0]);
        var body = content[4..];

        var descriptorEnd = TerminatorLength(content[0]) == 2
            ? FindDoubleNull(body)
            : body.IndexOf((byte)0);

        if (descriptorEnd < 0)
            return Lyrics.Empty;

        var text = encoding.GetString(body[(descriptorEnd + TerminatorLength(content[0]))..]).Trim('﻿');
        return text.Length == 0 ? Lyrics.Empty : ParsePlain(text, source);
    }

    /// <summary>
    /// SYLT: tras la cabecera van pares de (texto terminado en nulo + marca de tiempo de 4 bytes).
    /// Solo se acepta la marca en milisegundos; en fotogramas MPEG haria falta el bitrate y no
    /// merece la pena para lo poco que se usa.
    /// </summary>
    private static Lyrics ReadSylt(ReadOnlySpan<byte> content, string source)
    {
        if (content.Length < 7)
            return Lyrics.Empty;

        var encoding = EncodingFor(content[0]);
        var terminator = TerminatorLength(content[0]);
        var timestampFormat = content[4];
        if (timestampFormat != 2)
            return Lyrics.Empty;

        var body = content[6..];
        var descriptorEnd = terminator == 2 ? FindDoubleNull(body) : body.IndexOf((byte)0);
        if (descriptorEnd < 0)
            return Lyrics.Empty;

        var position = descriptorEnd + terminator;
        var lines = new List<LyricLine>();

        while (position + terminator + 4 <= body.Length)
        {
            var rest = body[position..];
            var end = terminator == 2 ? FindDoubleNull(rest) : rest.IndexOf((byte)0);
            if (end < 0)
                break;

            var text = encoding.GetString(rest[..end]).Trim('﻿').Trim();
            var stampAt = position + end + terminator;
            if (stampAt + 4 > body.Length)
                break;

            var milliseconds = (body[stampAt] << 24) | (body[stampAt + 1] << 16)
                | (body[stampAt + 2] << 8) | body[stampAt + 3];

            if (text.Length > 0)
                lines.Add(new LyricLine(TimeSpan.FromMilliseconds(milliseconds), text));

            position = stampAt + 4;
        }

        return lines.Count == 0
            ? Lyrics.Empty
            : new Lyrics(lines.OrderBy(l => l.Time!.Value).ToList(), true, source);
    }

    // ==================================================================================
    //  FLAC: comentarios Vorbis
    // ==================================================================================

    /// <summary>
    /// Letra en los comentarios Vorbis de un FLAC (claves <c>LYRICS</c>, <c>UNSYNCEDLYRICS</c> o
    /// <c>SYNCEDLYRICS</c>, que es la costumbre entre los programas de etiquetado).
    /// </summary>
    public static Lyrics ReadFlac(Stream stream, string source)
    {
        var marker = new byte[4];
        if (stream.Read(marker, 0, 4) != 4 || marker[0] != 'f' || marker[1] != 'L' || marker[2] != 'a' || marker[3] != 'C')
            return Lyrics.Empty;

        while (true)
        {
            var header = new byte[4];
            if (ReadExactly(stream, header) != 4)
                return Lyrics.Empty;

            var isLast = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7F;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];

            if (type == 4)
            {
                var block = new byte[length];
                if (ReadExactly(stream, block) != length)
                    return Lyrics.Empty;

                return ReadVorbisComment(block, source);
            }

            if (isLast)
                return Lyrics.Empty;

            // Bloque que no interesa (STREAMINFO, PICTURE...): se salta entero.
            if (stream.CanSeek)
                stream.Seek(length, SeekOrigin.Current);
            else if (ReadExactly(stream, new byte[length]) != length)
                return Lyrics.Empty;
        }
    }

    private static Lyrics ReadVorbisComment(byte[] block, string source)
    {
        var position = 0;

        var vendorLength = ReadLittleEndian(block, ref position);
        position += vendorLength;
        if (position + 4 > block.Length)
            return Lyrics.Empty;

        var count = ReadLittleEndian(block, ref position);

        for (var i = 0; i < count && position + 4 <= block.Length; i++)
        {
            var length = ReadLittleEndian(block, ref position);
            if (length < 0 || position + length > block.Length)
                break;

            var entry = Encoding.UTF8.GetString(block, position, length);
            position += length;

            var separator = entry.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = entry[..separator].ToUpperInvariant();
            var value = entry[(separator + 1)..];

            if (key is "LYRICS" or "UNSYNCEDLYRICS" or "SYNCEDLYRICS" && value.Trim().Length > 0)
                return ParsePlain(value, source);
        }

        return Lyrics.Empty;
    }

    // ==================================================================================

    private static int ReadLittleEndian(byte[] data, ref int position)
    {
        if (position + 4 > data.Length)
        {
            position = data.Length;
            return 0;
        }

        var value = data[position] | (data[position + 1] << 8) | (data[position + 2] << 16) | (data[position + 3] << 24);
        position += 4;
        return value;
    }

    private static int SyncSafe(byte[] data, int offset) =>
        ((data[offset] & 0x7F) << 21) | ((data[offset + 1] & 0x7F) << 14)
        | ((data[offset + 2] & 0x7F) << 7) | (data[offset + 3] & 0x7F);

    private static Encoding EncodingFor(byte code) => code switch
    {
        1 => Encoding.Unicode,       // UTF-16 con BOM
        2 => Encoding.BigEndianUnicode,
        3 => Encoding.UTF8,
        _ => Encoding.Latin1,
    };

    private static int TerminatorLength(byte encoding) => encoding is 1 or 2 ? 2 : 1;

    private static int FindDoubleNull(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
                return i;
        }

        return -1;
    }

    private static int ReadExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
                break;

            total += read;
        }

        return total;
    }
}
