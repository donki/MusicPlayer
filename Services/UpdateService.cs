using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

/// <summary>
/// Comprobacion de version al arrancar (constitucion 15): consulta un manifiesto en el repositorio
/// del proyecto (fuente de confianza) y, si hay una version mas reciente que la instalada, avisa al
/// usuario y le propone actualizar. Es silenciosa y no bloqueante: sin red, o ya al dia, no molesta.
/// </summary>
/// <remarks>La red, la version instalada y el navegador llegan por delegados para poder probarla.</remarks>
public sealed class UpdateService
{
    public const string AppcastUrl = "https://raw.githubusercontent.com/donki/MusicPlayer/main/appcast.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly ILocalizationService _localization;
    private readonly ILogger<UpdateService> _logger;
    private readonly Func<Task<string>> _fetch;
    private readonly Func<string> _currentVersion;
    private readonly Func<string, Task> _open;
    private bool _checkedThisSession;

    public UpdateService(ILocalizationService localization, ILogger<UpdateService> logger)
        : this(localization, logger, () => Http.GetStringAsync(AppcastUrl), () => AppInfo.Current.VersionString,
            url => Browser.Default.OpenAsync(new Uri(url), BrowserLaunchMode.SystemPreferred))
    {
    }

    public UpdateService(ILocalizationService localization, ILogger<UpdateService> logger,
        Func<Task<string>> fetch, Func<string> currentVersion, Func<string, Task> open)
    {
        _localization = localization;
        _logger = logger;
        _fetch = fetch;
        _currentVersion = currentVersion;
        _open = open;
    }

    /// <param name="ask">Pregunta (titulo, mensaje, aceptar, cancelar); en la app, ModernDialog.</param>
    public async Task CheckAndPromptAsync(Func<string, string, string, string, Task<bool>> ask)
    {
        if (_checkedThisSession)
            return;
        _checkedThisSession = true;

        try
        {
            var manifest = JsonSerializer.Deserialize<Appcast>(await _fetch());
            if (manifest?.Version is null)
                return;

            var current = _currentVersion();
            if (CompareVersions(manifest.Version, current) <= 0)
                return;

            var wantsUpdate = await ask(
                _localization["UpdateAvailableTitle"],
                _localization.Format("UpdateAvailableMessage", manifest.Version, current),
                _localization["UpdateNow"], _localization["UpdateLater"]);

            if (wantsUpdate && !string.IsNullOrWhiteSpace(manifest.Url))
                await _open(manifest.Url);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or UriFormatException)
        {
            // Sin red o manifiesto no disponible: la comprobacion no debe molestar ni bloquear.
            _logger.LogInformation(ex, "The version check could not be completed.");
        }
    }

    /// <summary>Compara versiones numericas por partes («2026.08.27.0»). &gt;0 si a es mas nueva que b.</summary>
    public static int CompareVersions(string first, string second)
    {
        var left = Parts(first);
        var right = Parts(second);
        var length = Math.Max(left.Length, right.Length);

        for (var index = 0; index < length; index++)
        {
            var leftPart = index < left.Length ? left[index] : 0;
            var rightPart = index < right.Length ? right[index] : 0;
            if (leftPart != rightPart)
                return leftPart.CompareTo(rightPart);
        }

        return 0;
    }

    private static int[] Parts(string version) =>
        version.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();

    private sealed class Appcast
    {
        [JsonPropertyName("version")] public string? Version { get; set; }

        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
