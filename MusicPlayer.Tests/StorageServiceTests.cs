using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

public sealed class PlaylistServiceTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly LocalizationService _localization = new(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance);

    public void Dispose() => _data.Dispose();

    private PlaylistService Create() => new(NullLogger<PlaylistService>.Instance, _localization);

    private string FilePath => Path.Combine(_data.Path, "playlists.json");

    [Fact]
    public void Sin_fichero_solo_hay_favoritas_con_nombre_traducido()
    {
        var service = Create();

        var favorites = Assert.Single(service.Playlists);
        Assert.True(favorites.IsFavorites);
        Assert.Equal(_localization["Favorites"], favorites.Name);
        Assert.Equal(_localization["Favorites"], service.Find(Playlist.FavoritesId)!.Name);
    }

    [Fact]
    public void Crear_renombrar_borrar_y_persistir()
    {
        var service = Create();
        var changes = 0;
        service.PlaylistsChanged += (_, _) => changes++;

        var created = service.Create("  Viaje  ")!;
        Assert.Equal("Viaje", created.Name);
        Assert.Null(service.Create("viaje"));   // repetida sin mirar mayusculas
        Assert.Null(service.Create("   "));
        Assert.Null(service.Create(null!));

        var other = service.Create("Gimnasio")!;
        Assert.False(service.Rename(other.Id, "VIAJE"));   // chocaria con otra
        Assert.False(service.Rename(other.Id, " "));
        Assert.False(service.Rename("no-existe", "X"));
        Assert.False(service.Rename(Playlist.FavoritesId, "Mias"));
        Assert.True(service.Rename(other.Id, "Correr"));
        Assert.True(service.Rename(other.Id, "correr"));   // la misma, cambiando mayusculas

        service.Delete(Playlist.FavoritesId);   // no se borra
        service.Delete("no-existe");
        service.Delete(created.Id);

        Assert.Equal(5, changes);

        var reloaded = Create();
        Assert.Equal([Playlist.FavoritesId, other.Id], reloaded.Playlists.Select(p => p.Id));
        Assert.Equal("correr", reloaded.Find(other.Id)!.Name);
        Assert.Null(reloaded.Find("no-existe"));
    }

    [Fact]
    public void Favoritas_se_alterna()
    {
        var service = Create();

        Assert.False(service.IsFavorite(5));
        Assert.True(service.ToggleFavorite(5));
        Assert.True(service.IsFavorite(5));
        Assert.True(Create().IsFavorite(5));
        Assert.False(service.ToggleFavorite(5));
        Assert.False(service.IsFavorite(5));
    }

    [Fact]
    public void Pertenencia_y_anadir_sin_repetir()
    {
        var service = Create();
        var a = service.Create("A")!;
        var b = service.Create("B")!;

        service.SetMembership(1, [a.Id, Playlist.FavoritesId]);
        Assert.Equal([Playlist.FavoritesId, a.Id], service.PlaylistIdsContaining(1));

        service.SetMembership(1, [b.Id]);
        Assert.Equal([b.Id], service.PlaylistIdsContaining(1));

        var changes = 0;
        service.PlaylistsChanged += (_, _) => changes++;
        service.AddSongs([1, 2, 2], [b.Id, a.Id]);
        Assert.Equal([1L, 2L], service.Find(b.Id)!.SongIds);
        Assert.Equal([1L, 2L], service.Find(a.Id)!.SongIds);
        Assert.Equal(1, changes);

        service.AddSongs([1, 2], [a.Id]);   // ya estaban: no cambia nada
        service.AddSongs([], [a.Id]);
        service.AddSongs([3], []);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Quitar_de_una_lista_y_de_todas()
    {
        var service = Create();
        var a = service.Create("A")!;
        service.AddSongs([1, 2, 3], [a.Id, Playlist.FavoritesId]);

        var changes = 0;
        service.PlaylistsChanged += (_, _) => changes++;

        service.RemoveSong(a.Id, 1);
        Assert.Equal([2L, 3L], service.Find(a.Id)!.SongIds);
        service.RemoveSongs(a.Id, []);
        service.RemoveSongs("no-existe", [2]);
        service.RemoveSongs(a.Id, [99]);
        Assert.Equal(1, changes);

        service.RemoveSongEverywhere(2);
        Assert.Equal([3L], service.Find(a.Id)!.SongIds);
        Assert.Equal([1L, 3L], service.Find(Playlist.FavoritesId)!.SongIds);
        service.RemoveSongsEverywhere([]);
        service.RemoveSongsEverywhere([99]);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Fichero_corrupto_arranca_vacio()
    {
        File.WriteAllText(FilePath, "{ esto no es json");
        Assert.Single(Create().Playlists);
    }

    [Fact]
    public void Fichero_antiguo_sin_favoritas_la_crea_y_la_pone_primera_y_descarta_sin_nombre()
    {
        var stored = new List<Playlist>
        {
            new() { Id = "x", Name = "Vieja", SongIds = [4] },
            new() { Id = "y", Name = "" },
            new() { Id = Playlist.FavoritesId, Name = "Favorites", SongIds = [9] },
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));

        var service = Create();

        Assert.Equal([Playlist.FavoritesId, "x"], service.Playlists.Select(p => p.Id));
        Assert.True(service.IsFavorite(9));

        File.WriteAllText(FilePath, JsonSerializer.Serialize(new List<Playlist> { new() { Id = "x", Name = "Sola" } }));
        Assert.Equal([Playlist.FavoritesId, "x"], Create().Playlists.Select(p => p.Id));

        File.WriteAllText(FilePath, "null");
        Assert.Single(Create().Playlists);
    }

    [Fact]
    public void Si_no_se_puede_guardar_sigue_funcionando_en_memoria()
    {
        var service = Create();
        Directory.CreateDirectory(FilePath); // el fichero no se puede escribir: hay una carpeta
        Assert.NotNull(service.Create("A"));
        Assert.Equal(2, service.Playlists.Count);
    }
}

public sealed class SongTagsServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SongTagsService Create() => new(NullLogger<SongTagsService>.Instance);

    [Fact]
    public void Guardar_aplicar_y_olvidar()
    {
        var service = Create();
        var song = new Song { Id = 3, Title = "viejo", Duration = TimeSpan.FromSeconds(9) };
        var tags = new SongTags("nuevo", "A", "AA", "Al", "C", 1, 2000);

        Assert.Null(service.Find(3));
        Assert.Same(song, service.Apply(song));

        service.Save(3, tags);
        Assert.Equal(tags, Create().Find(3));   // persistido
        Assert.Equal("nuevo", service.Apply(song).Title);
        Assert.Equal(TimeSpan.FromSeconds(9), service.Apply(song).Duration);

        service.Forget([]);
        service.Forget([99]);
        Assert.NotNull(Create().Find(3));

        service.Forget([3, 99]);
        Assert.Null(service.Find(3));
        Assert.Null(Create().Find(3));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("null")]
    public void Fichero_corrupto_o_nulo_empieza_de_cero(string content)
    {
        File.WriteAllText(Path.Combine(_data.Path, "song_tags.json"), content);
        Assert.Null(Create().Find(1));
    }

    [Fact]
    public void Si_no_se_puede_guardar_no_revienta()
    {
        var service = Create();
        Directory.CreateDirectory(Path.Combine(_data.Path, "song_tags.json"));
        service.Save(1, new SongTags("t", "", "", "", "", 0, 0));
        Assert.NotNull(service.Find(1));
        Assert.Null(Create().Find(1));
    }
}

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Valores_por_defecto_prudentes()
    {
        var settings = new SettingsService();
        Assert.Equal(string.Empty, settings.Language);
        Assert.False(settings.OnlineArtistInfo);   // la red, apagada por defecto
        Assert.False(settings.PreferComposer);
        Assert.Equal(0, settings.LastSongId);
        Assert.False(settings.Shuffle);
        Assert.Equal(0, settings.RepeatMode);
        Assert.False(settings.IncludeAllAudio);
    }

    [Fact]
    public void Se_guardan_y_se_leen()
    {
        var settings = new SettingsService
        {
            Language = null!,
            OnlineArtistInfo = true,
            PreferComposer = true,
            LastSongId = 77,
            Shuffle = true,
            RepeatMode = 2,
            IncludeAllAudio = true,
        };

        var again = new SettingsService();
        Assert.Equal(string.Empty, again.Language);
        again.Language = "es";
        Assert.Equal("es", new SettingsService().Language);
        Assert.True(again.OnlineArtistInfo);
        Assert.True(again.PreferComposer);
        Assert.Equal(77, again.LastSongId);
        Assert.True(again.Shuffle);
        Assert.Equal(2, again.RepeatMode);
        Assert.True(again.IncludeAllAudio);
    }
}

public sealed class CustomArtServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static CustomArtService Create(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) =>
        new(new HttpClient(new FakeHttpHandler(respond ?? (_ => FakeHttpHandler.Status(HttpStatusCode.NotFound)))));

    [Fact]
    public async Task Imagen_de_cancion_y_de_grupo()
    {
        var service = Create();
        Assert.Null(service.ForSong(1));
        Assert.Null(service.ForArtist("Queen"));

        var songPath = await service.SetSongAsync(1, new MemoryStream([1, 2, 3]));
        var artistPath = await service.SetArtistAsync("Queen", new MemoryStream([4]));

        Assert.Equal(songPath, service.ForSong(1));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(songPath));
        Assert.Equal(artistPath, service.ForArtist("  queen "));   // mismo grupo sin mirar mayusculas
        Assert.Null(service.ForArtist("AC/DC"));
        Assert.Equal(artistPath, await service.SetArtistAsync("QUEEN", new MemoryStream([5])));
        Assert.Equal([5], File.ReadAllBytes(artistPath));

        service.ClearSong(1);
        service.ClearArtist("Queen");
        service.ClearSong(2);   // no existia: no pasa nada
        Assert.Null(service.ForSong(1));
        Assert.Null(service.ForArtist("Queen"));
    }

    [Fact]
    public async Task Borrar_una_imagen_abierta_no_revienta()
    {
        var service = Create();
        var path = await service.SetSongAsync(1, new MemoryStream([1]));
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            service.ClearSong(1);   // en Windows el fichero esta bloqueado
        Assert.True(File.Exists(path) || service.ForSong(1) is null);
    }

    [Theory]
    [InlineData("no es url")]
    [InlineData("ftp://servidor/imagen.png")]
    [InlineData("file:///c:/imagen.png")]
    public async Task Descargar_rechaza_direcciones_que_no_son_web(string url) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().DownloadAsync(url));

    [Fact]
    public async Task Descargar_una_imagen()
    {
        var service = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([9, 9]) { Headers = { ContentType = new("image/png") } },
        });

        Assert.Equal([9, 9], await service.DownloadAsync("  https://ejemplo.test/a.png "));
    }

    [Fact]
    public async Task Descargar_una_pagina_en_vez_de_una_imagen()
    {
        var service = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>", System.Text.Encoding.UTF8, "text/html") });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync("https://ejemplo.test/"));
        Assert.Contains("imagen", error.Message);
    }

    [Fact]
    public async Task Descargar_sin_tipo_ni_exito()
    {
        var sinTipo = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinTipo.DownloadAsync("https://ejemplo.test/"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create().DownloadAsync("https://ejemplo.test/"));
    }

    [Fact]
    public async Task Descargar_una_imagen_demasiado_grande()
    {
        var anunciada = Create(_ =>
        {
            var content = new ByteArrayContent([1]) { Headers = { ContentType = new("image/jpeg") } };
            content.Headers.ContentLength = 9L * 1024 * 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => anunciada.DownloadAsync("https://ejemplo.test/"));

        // Sin tamano anunciado: se descubre al leerla.
        var escondida = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new byte[8 * 1024 * 1024 + 1])) { Headers = { ContentType = new("image/jpeg") } },
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => escondida.DownloadAsync("https://ejemplo.test/"));
    }
}
