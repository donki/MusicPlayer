using System.Net;
using System.Reflection;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

/// <summary>
/// Las pruebas que tocan estado global de verdad (las tablas de traduccion, la cultura por defecto)
/// van en esta coleccion para no pisarse entre si.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppStateCollection
{
    public const string Name = "Estado de la app";
}

/// <summary>Carpeta temporal propia que hace de AppDataDirectory y se borra al acabar.</summary>
public sealed class TempAppData : IDisposable
{
    public TempAppData()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        FileSystem.AppDataDirectory = Path;
        Preferences.Clear();
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Ajustes en memoria, para no depender de Preferences.</summary>
public sealed class FakeSettings : ISettingsService
{
    public string Language { get; set; } = string.Empty;
    public bool OnlineArtistInfo { get; set; }
    public bool PreferComposer { get; set; }
    public long LastSongId { get; set; }
    public bool Shuffle { get; set; }
    public bool IncludeAllAudio { get; set; }
    public int RepeatMode { get; set; }
}

/// <summary>
/// Servidor HTTP de mentira: responde segun la direccion pedida y apunta lo que se pidio. Ninguna
/// prueba sale a la red.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
            Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("") };
}

public static class Reflect
{
    /// <summary>
    /// Sustituye el HttpClient privado de un servicio por uno que habla con el servidor de mentira.
    /// La app crea su propio cliente; aqui se cambia solo para que la prueba no salga a la red.
    /// </summary>
    public static void ReplaceHttp(object service, HttpMessageHandler handler)
    {
        var field = service.GetType().GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(service)!).Dispose();
        field.SetValue(service, new HttpClient(handler));
    }

    public static T GetStatic<T>(Type type, string name) =>
        (T)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}

/// <summary>Flujo que no sabe su tamano ni admite buscar, como una descarga por partes.</summary>
public sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();
}
