// Cuñas de lo que UpdateService toma de Microsoft.Maui.ApplicationModel para su constructor de la
// app (version instalada y navegador). Las pruebas usan el otro constructor; esto solo compila.
namespace Microsoft.Maui.ApplicationModel;

public enum BrowserLaunchMode { SystemPreferred, External }

public sealed class AppInfo
{
    public static AppInfo Current { get; } = new();
    public string VersionString => "0.0.0.0";
}

public sealed class Browser
{
    public static Browser Default { get; } = new();
    public Task<bool> OpenAsync(Uri uri, BrowserLaunchMode mode) => Task.FromResult(false);
}
