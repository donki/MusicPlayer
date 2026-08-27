namespace MusicPlayer.Helpers;

/// <summary>
/// Acceso al contenedor de dependencias desde las paginas y desde el servicio de reproduccion,
/// que MAUI no inyecta automaticamente (constitucion 5 y 7).
/// </summary>
public static class ServiceHelper
{
    private static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    /// <summary>
    /// Contenedor ya inicializado, o el de la aplicacion de plataforma si esta clase todavia no
    /// se ha inicializado. El segundo caso ocurre de verdad: Android Auto puede arrancar el
    /// proceso por el servicio de medios, sin pasar por la actividad.
    /// </summary>
    public static IServiceProvider? Services => _services ??= IPlatformApplication.Current?.Services;

    public static T GetRequiredService<T>() where T : notnull
    {
        var services = Services
            ?? throw new InvalidOperationException("ServiceHelper was used before MauiProgram initialized it.");

        return services.GetRequiredService<T>();
    }

    public static T? GetService<T>() where T : class => Services?.GetService<T>();
}
