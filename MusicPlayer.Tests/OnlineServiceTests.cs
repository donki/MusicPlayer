using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

/// <summary>
/// Busqueda de etiquetas en linea contra un servidor de mentira. Nada sale a la red. Cada busqueda
/// respeta el limite de MusicBrainz (algo mas de un segundo), asi que estas pruebas tardan.
/// </summary>
public abstract class SongLookupTestBase
{
    protected static readonly SongTags Tags = new("Bohemian Rhapsody", "Queen", "", "", "", 0, 0);

    protected static (SongLookupService Service, FakeHttpHandler Http) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool enabled = true)
    {
        var service = new SongLookupService(new FakeSettings { OnlineArtistInfo = enabled }, NullLogger<SongLookupService>.Instance);
        var http = new FakeHttpHandler(respond);
        Reflect.ReplaceHttp(service, http);
        return (service, http);
    }

    protected const string MusicBrainz = """
        { "recordings": [
            { "score": 60, "title": "Mala", "artist-credit": [{ "name": "Otro" }] },
            { "score": 100, "title": "Bohemian Rhapsody",
              "artist-credit": [{ "name": "Queen", "joinphrase": " feat. " }, { "artist": { "name": "Nadie" } }, { }],
              "releases": [
                { "title": "Greatest Hits", "date": "1981-10-26", "media": [{ "track": [{ "number": "1" }] }] },
                { "title": "A Night at the Opera", "date": "1975", "media": [{ "track": [{ "number": "11" }] }] },
                { "title": "", "date": "1970" },
                { "title": "Sin fecha" }
              ] }
        ] }
        """;
}

public sealed class SongLookupSinPermisoOSinTituloNoSeTocaLaRedTests : SongLookupTestBase
{    [Fact]
    public async Task Sin_permiso_o_sin_titulo_no_se_toca_la_red()
    {
        var (off, http) = Create(_ => throw new InvalidOperationException("no deberia pedir"), enabled: false);
        Assert.False(off.IsEnabled);
        Assert.Same(SongLookupResult.None, await off.LookupAsync(Tags));

        var (on, http2) = Create(_ => throw new InvalidOperationException("no deberia pedir"));
        Assert.Same(SongLookupResult.None, await on.LookupAsync(Tags with { Title = "  " }));
        Assert.Empty(http.Requests);
        Assert.Empty(http2.Requests);
    }
}

public sealed class SongLookupMusicbrainzEligeLaEdicionOriginalTests : SongLookupTestBase
{
    [Fact]
    public async Task MusicBrainz_elige_la_edicion_original()
    {
        var (service, http) = Create(_ => FakeHttpHandler.Json(MusicBrainz));

        var result = await service.LookupAsync(Tags);

        Assert.Equal(new SongLookupResult("Bohemian Rhapsody", "Queen feat. Nadie", "A Night at the Opera", 1975, 11), result);
        var request = Assert.Single(http.Requests);
        Assert.Equal("musicbrainz.org", request.Host);
        Assert.Contains("artist", Uri.UnescapeDataString(request.Query));
    }
}

public sealed class SongLookupSinGrupoSeBuscaSoloPorTituloYSeLimpianCoTests : SongLookupTestBase
{
    [Fact]
    public async Task Sin_grupo_se_busca_solo_por_titulo_y_se_limpian_comillas()
    {
        var (service, http) = Create(_ => FakeHttpHandler.Json(MusicBrainz));
        await service.LookupAsync(new SongTags("Say \"hi\": now\\", "", "", "", "", 0, 0));

        var query = Uri.UnescapeDataString(Assert.Single(http.Requests).Query);
        Assert.DoesNotContain("artist:", query);
        Assert.Contains("recording:\"Say  hi   now\"", query);
    }
}

public sealed class SongLookupSiMusicbrainzNoSabeSePreguntaAItunesTests : SongLookupTestBase
{
    [Fact]
    public async Task Si_MusicBrainz_no_sabe_se_pregunta_a_iTunes()
    {
        var (service, http) = Create(request => request.RequestUri!.Host switch
        {
            "musicbrainz.org" => FakeHttpHandler.Json("""{ "recordings": [ { "score": 90, "title": "" } ] }"""),
            "itunes.apple.com" => FakeHttpHandler.Json("""
                { "results": [ { "trackName": "" },
                  { "trackName": "Bohemian Rhapsody", "artistName": "Queen", "collectionName": "Opera",
                    "releaseDate": "1975-10-31T08:00:00Z", "trackNumber": 11 } ] }
                """),
            _ => throw new InvalidOperationException(),
        });

        var result = await service.LookupAsync(Tags with { Artist = "", Composer = "Mercury" });

        Assert.Equal(new SongLookupResult("Bohemian Rhapsody", "Queen", "Opera", 1975, 11), result);
        Assert.Contains("Mercury", Uri.UnescapeDataString(http.Requests[1].Query));
    }
}

public sealed class SongLookupDeezerEsElUltimoRecursoYLosErroresNoCortTests : SongLookupTestBase
{
    [Fact]
    public async Task Deezer_es_el_ultimo_recurso_y_los_errores_no_cortan_la_cascada()
    {
        var (service, http) = Create(request => request.RequestUri!.Host switch
        {
            "musicbrainz.org" => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable),
            "itunes.apple.com" => FakeHttpHandler.Json("no es json"),
            "api.deezer.com" => FakeHttpHandler.Json("""
                { "data": [ { "title": "" }, { "title": "Bohemian Rhapsody", "artist": { "name": "Queen" }, "album": { "title": "Opera" } } ] }
                """),
            _ => throw new InvalidOperationException(),
        });

        var result = await service.LookupAsync(Tags);

        Assert.Equal(new SongLookupResult("Bohemian Rhapsody", "Queen", "Opera", 0, 0), result);
        Assert.Equal(3, http.Requests.Count);
    }
}

public sealed class SongLookupNadieSabeNadaTests : SongLookupTestBase
{
    [Fact]
    public async Task Nadie_sabe_nada()
    {
        var (service, _) = Create(request => request.RequestUri!.Host switch
        {
            "musicbrainz.org" => FakeHttpHandler.Json("""{ "recordings": [] }"""),
            "itunes.apple.com" => FakeHttpHandler.Json("""{ "resultCount": 0 }"""),
            _ => FakeHttpHandler.Json("""{ "data": [ { "title": "" } ] }"""),
        });

        Assert.False((await service.LookupAsync(Tags)).Found);
    }
}

public sealed class SongLookupSinRedDevuelveNadaYCancelarSeRespetaTests : SongLookupTestBase
{
    [Fact]
    public async Task Sin_red_devuelve_nada_y_cancelar_se_respeta()
    {
        var (service, _) = Create(_ => throw new HttpRequestException("sin red"));
        Assert.Same(SongLookupResult.None, await service.LookupAsync(Tags));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LookupAsync(Tags, cancelled.Token));
        service.Dispose();
    }
}

public sealed class SongLookupPlazoAgotadoNoSeConfundeConCancelarTests : SongLookupTestBase
{
    [Fact]
    public async Task Plazo_agotado_no_se_confunde_con_cancelar()
    {
        // Fallo encontrado: el plazo agotado de HttpClient llega como TaskCanceledException y se
        // propagaba como si el usuario hubiera cancelado.
        var (service, _) = Create(_ => throw new TaskCanceledException("timeout"));
        Assert.Same(SongLookupResult.None, await service.LookupAsync(Tags));
    }
}

public sealed class SongLookupItunesSinTitulosPasaADeezerTests : SongLookupTestBase
{
    [Fact]
    public async Task iTunes_sin_titulos_pasa_a_Deezer()
    {
        var (service, http) = Create(request => request.RequestUri!.Host switch
        {
            "musicbrainz.org" => FakeHttpHandler.Json("""{ }"""),
            "itunes.apple.com" => FakeHttpHandler.Json("""{ "results": [ { "trackName": "" } ] }"""),
            _ => FakeHttpHandler.Json("""{ }"""),
        });

        Assert.False((await service.LookupAsync(Tags)).Found);
        Assert.Equal(3, http.Requests.Count);
    }
}

public sealed class SongLookupMusicbrainzSinEdicionesNiPistasTests : SongLookupTestBase
{
    [Fact]
    public async Task MusicBrainz_sin_ediciones_ni_pistas()
    {
        var (service, _) = Create(_ => FakeHttpHandler.Json("""
            { "recordings": [ { "title": "T", "artist-credit": [],
                "releases": [ { "title": "Disco", "date": "19x", "media": [ { "track": [] } ] },
                              { "title": "Otro", "media": [] } ] } ] }
            """));

        Assert.Equal(new SongLookupResult("T", "", "Disco", 0, 0), await service.LookupAsync(Tags));
    }
}

public sealed class SongLookupCancelarAMediaBusquedaSePropagaTests : SongLookupTestBase
{
    [Fact]
    public async Task Cancelar_a_media_busqueda_se_propaga()
    {
        using var cancel = new CancellationTokenSource();
        var (service, _) = Create(_ =>
        {
            cancel.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LookupAsync(Tags, cancel.Token));
    }
}

public sealed class SongLookupDescribeSoloLoQueHayTests : SongLookupTestBase
{
    [Fact]
    public void Describe_solo_lo_que_hay()
    {
        var text = new SongLookupResult("T", "", "Al", 1999, 0).Describe(key => key);
        Assert.Equal("TagTitle: T\nTagAlbum: Al\nTagYear: 1999", text);
        Assert.Equal("TagArtist: A\nTagTrack: 3", new SongLookupResult("", "A", "", 0, 3).Describe(k => k));
        Assert.False(SongLookupResult.None.Found);
        Assert.True(new SongLookupResult("", "", "x", 0, 0).Found);
    }
}

/// <summary>Fotos y biografias de los grupos contra un servidor de mentira.</summary>
public abstract class ArtistInfoTestBase
{
    protected const string Search = """{ "artists": [ { "id": "mbid-1", "score": 100 } ] }""";
    protected const string Relations = """
        { "relations": [ { "type": "discogs", "url": { "resource": "https://discogs.com/x" } },
                         { "type": "wikidata", "url": { "resource": "https://www.wikidata.org/wiki/Q15862" } } ] }
        """;
    protected const string Entity = """
        { "entities": { "Q15862": {
            "claims": { "P18": [ { "mainsnak": { "datavalue": { "value": "Queen 1984.jpg" } } } ] },
            "sitelinks": { "eswiki": { "title": "Queen" }, "enwiki": { "title": "Queen (band)" } },
            "descriptions": { "es": { "value": "banda britanica" }, "en": { "value": "British band" } } } } }
        """;

    protected static HttpResponseMessage Image() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 1]) };

    protected static Func<HttpRequestMessage, HttpResponseMessage> Server(
        string? summaryEs = "Queen es una banda", string? summaryEn = "Queen are a band", bool image = true) => request =>
    {
        var uri = request.RequestUri!;
        return uri.Host switch
        {
            "musicbrainz.org" when uri.AbsolutePath.EndsWith("/artist/") => FakeHttpHandler.Json(Search),
            "musicbrainz.org" => FakeHttpHandler.Json(Relations),
            "www.wikidata.org" => FakeHttpHandler.Json(Entity),
            "es.wikipedia.org" => summaryEs is null ? FakeHttpHandler.Status(HttpStatusCode.NotFound) : FakeHttpHandler.Json($$"""{ "extract": "{{summaryEs}}" }"""),
            "en.wikipedia.org" => summaryEn is null ? FakeHttpHandler.Json("{}") : FakeHttpHandler.Json($$"""{ "extract": "{{summaryEn}}" }"""),
            "commons.wikimedia.org" => image ? Image() : FakeHttpHandler.Status(HttpStatusCode.NotFound),
            _ => throw new InvalidOperationException(uri.ToString()),
        };
    };

    protected static (ArtistInfoService Service, FakeHttpHandler Http) Create(
        TempAppData data, Func<HttpRequestMessage, HttpResponseMessage> respond, bool enabled = true, string language = "es")
    {
        FileSystem.AppDataDirectory = data.Path;
        var service = new ArtistInfoService(new FakeSettings { OnlineArtistInfo = enabled, Language = language }, NullLogger<ArtistInfoService>.Instance);
        var http = new FakeHttpHandler(respond);
        Reflect.ReplaceHttp(service, http);
        return (service, http);
    }
}

public sealed class ArtistInfoSinPermisoTests : ArtistInfoTestBase
{    [Fact]
    public async Task Sin_permiso_no_se_consulta_y_sin_nombre_tampoco()
    {
        using var data = new TempAppData();
        var (service, http) = Create(data, _ => throw new InvalidOperationException(), enabled: false);

        Assert.False(service.IsEnabled);
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("  "));
        Assert.Null(service.GetCachedImagePath(" "));
        Assert.Null(service.GetCachedImagePath("Queen"));
        Assert.Empty(http.Requests);
    }
}

public sealed class ArtistInfoFotoYResenaTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Foto_y_resena_en_castellano_y_luego_de_la_cache()
    {
        using var data = new TempAppData();
        var (service, http) = Create(data, Server());

        var info = await service.GetAsync(" Queen ");

        Assert.Equal("Queen es una banda", info.Description);
        Assert.NotNull(info.ImagePath);
        Assert.Equal([0xFF, 0xD8, 1], File.ReadAllBytes(info.ImagePath!));
        Assert.Equal(info.ImagePath, service.GetCachedImagePath("QUEEN"));
        var requests = http.Requests.Count;

        // Otra instancia lee el indice del disco y no vuelve a la red.
        var (again, http2) = Create(data, _ => throw new InvalidOperationException());
        Assert.Equal(info, await again.GetAsync("queen"));
        Assert.Empty(http2.Requests);
        Assert.Equal(5, requests);

        again.ClearCache();
        Assert.Null(again.GetCachedImagePath("Queen"));
        Assert.False(Directory.Exists(Path.Combine(data.Path, "artists")));
        again.Dispose();
    }
}

public sealed class ArtistInfoInglesTests : ArtistInfoTestBase
{
    [Fact]
    public async Task En_ingles_y_sin_foto_con_la_descripcion_corta_de_respaldo()
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, Server(summaryEs: null, summaryEn: null, image: false), language: "en");

        var info = await service.GetAsync("Queen");

        Assert.Null(info.ImagePath);
        Assert.Equal("British band", info.Description);
    }
}

public sealed class ArtistInfoCoincidenciaFlojaTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Coincidencia_floja_no_se_ensena_y_se_recuerda_el_vacio()
    {
        using var data = new TempAppData();
        var (service, http) = Create(data, _ => FakeHttpHandler.Json("""{ "artists": [ { "id": "x", "score": 40 } ] }"""));

        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
        Assert.Single(http.Requests);   // el «no hay nada» tambien se guarda

        await service.GetAsync("Queen", forceRefresh: true);
        Assert.Equal(2, http.Requests.Count);
    }
}

public sealed class ArtistInfoBusquedaSinGrupoTests : ArtistInfoTestBase
{
    [Theory]
    [InlineData("""{ "artists": [] }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "artists": [ { "score": 100 } ] }""")]
    public async Task Busqueda_sin_grupo_util(string search)
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, _ => FakeHttpHandler.Json(search));
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Nadie"));
    }
}

public sealed class ArtistInfoSinWikidataTests : ArtistInfoTestBase
{
    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "relations": [ { "type": "wikidata", "url": { "resource": "https://x/P123" } } ] }""")]
    public async Task Sin_enlace_a_wikidata_no_hay_ficha(string relations)
    {
        using var data = new TempAppData();
        var (service, http) = Create(data, request => request.RequestUri!.AbsolutePath.EndsWith("/artist/")
            ? FakeHttpHandler.Json(Search)
            : FakeHttpHandler.Json(relations));

        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
        Assert.Equal(2, http.Requests.Count);
    }
}

public sealed class ArtistInfoEntidadAusenteTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Entidad_de_wikidata_ausente()
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, request => request.RequestUri!.Host == "www.wikidata.org"
            ? FakeHttpHandler.Status(HttpStatusCode.NotFound)
            : Server()(request));

        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
    }
}

public sealed class ArtistInfoSinRedTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Sin_red_no_revienta_y_cancelar_se_respeta()
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, _ => throw new HttpRequestException("sin red"));
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync("Otro", cancellationToken: cancelled.Token));
    }
}

public sealed class ArtistInfoIndiceCorruptoTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Indice_corrupto_se_reconstruye_y_foto_borrada_no_se_devuelve()
    {
        using var data = new TempAppData();
        Directory.CreateDirectory(Path.Combine(data.Path, "artists"));
        File.WriteAllText(Path.Combine(data.Path, "artists", "index.json"), "roto");

        var (service, _) = Create(data, Server());
        var info = await service.GetAsync("Queen");
        File.Delete(info.ImagePath!);

        Assert.Null(service.GetCachedImagePath("Queen"));
        var (again, _) = Create(data, _ => throw new InvalidOperationException());
        Assert.Equal(new ArtistInfo(null, "Queen es una banda"), await again.GetAsync("Queen"));
    }
}

public sealed class ArtistInfoPlazoAgotadoTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Plazo_agotado_no_revienta()
    {
        // Fallo encontrado: un plazo agotado se propagaba y cortaba la precarga de fotos de la
        // biblioteca y el boton de actualizar la ficha del grupo.
        using var data = new TempAppData();
        var (service, _) = Create(data, _ => throw new TaskCanceledException("timeout"));
        Assert.Equal(new ArtistInfo(null, null), await service.GetAsync("Queen"));
    }
}

public sealed class ArtistInfoConsultasALaVezTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Dos_consultas_a_la_vez_del_mismo_grupo_van_una_vez_a_la_red()
    {
        using var data = new TempAppData();
        var (service, http) = Create(data, Server());

        var both = await Task.WhenAll(service.GetAsync("Queen"), service.GetAsync("Queen"));

        Assert.Equal(both[0], both[1]);
        Assert.Equal(5, http.Requests.Count);
    }
}

public sealed class ArtistInfoVacioCaducadoTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Vacio_caducado_se_vuelve_a_consultar()
    {
        using var data = new TempAppData();
        var folder = Path.Combine(data.Path, "artists");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.json"),
            """{ "Queen": { "ImageFile": null, "Description": null, "FetchedAt": "2020-01-01T00:00:00+00:00" } }""");

        var (service, http) = Create(data, _ => FakeHttpHandler.Json("""{ "artists": [] }"""));
        await service.GetAsync("Queen");

        Assert.Single(http.Requests);
    }
}

public sealed class ArtistInfoImagenVaciaTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Imagen_vacia_no_se_guarda_y_sin_titulo_en_el_idioma_se_usa_el_otro()
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, request =>
        {
            var uri = request.RequestUri!;
            return uri.Host switch
            {
                "www.wikidata.org" => FakeHttpHandler.Json("""
                    { "entities": { "Q15862": {
                        "claims": { "P18": [ { "mainsnak": { "datavalue": { "value": "x.jpg" } } } ] },
                        "sitelinks": { "enwiki": { "title": "Queen (band)" } } } } }
                    """),
                "commons.wikimedia.org" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) },
                _ => Server()(request),
            };
        });

        var info = await service.GetAsync("Queen");

        Assert.Null(info.ImagePath);
        Assert.Equal("Queen are a band", info.Description);
    }
}

public sealed class ArtistInfoIdiomaDelSistemaTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Idioma_del_sistema_cuando_no_se_ha_elegido()
    {
        using var data = new TempAppData();
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        try
        {
            var (service, _) = Create(data, Server(), language: "");
            Assert.Equal("Queen es una banda", (await service.GetAsync("Queen")).Description);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}

public sealed class ArtistInfoIndiceSinGuardarTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Si_no_se_puede_guardar_el_indice_sigue_funcionando()
    {
        using var data = new TempAppData();
        Directory.CreateDirectory(Path.Combine(data.Path, "artists", "index.json"));   // una carpeta en su sitio
        var (service, _) = Create(data, Server());

        Assert.Equal("Queen es una banda", (await service.GetAsync("Queen")).Description);
    }
}

public sealed class ArtistInfoCacheAbiertaTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Borrar_la_cache_con_un_fichero_abierto_no_revienta()
    {
        using var data = new TempAppData();
        var (service, _) = Create(data, Server());
        var info = await service.GetAsync("Queen");

        using (File.Open(info.ImagePath!, FileMode.Open, FileAccess.Read, FileShare.None))
            service.ClearCache();

        Assert.Null(service.GetCachedImagePath("Queen"));   // el indice se vacia igualmente
    }
}

public sealed class ArtistInfoCancelarAMediasTests : ArtistInfoTestBase
{
    [Fact]
    public async Task Cancelar_a_media_consulta_se_propaga()
    {
        using var data = new TempAppData();
        using var cancel = new CancellationTokenSource();
        var (service, _) = Create(data, _ =>
        {
            cancel.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync("Queen", cancellationToken: cancel.Token));
    }
}
