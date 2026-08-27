using Android.Content;
using Android.Provider;
using Microsoft.Extensions.Logging;
using MusicPlayer.Services;
using AndroidUri = Android.Net.Uri;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="IMediaAccessService"/>
public sealed class MediaAccessService : IMediaAccessService
{
    private readonly ILogger<MediaAccessService> _logger;

    public MediaAccessService(ILogger<MediaAccessService> logger) => _logger = logger;

    public async Task<bool> IsGrantedAsync()
    {
        try
        {
            return await Permissions.CheckStatusAsync<AudioLibraryPermission>() == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The audio permission status could not be read.");
            return false;
        }
    }

    public async Task<bool> RequestAsync()
    {
        try
        {
            return await Permissions.RequestAsync<AudioLibraryPermission>() == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The audio permission could not be requested.");
            return false;
        }
    }

    public void OpenSystemSettings()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(Settings.ActionApplicationDetailsSettings,
                AndroidUri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The application settings screen could not be opened.");
        }
    }

    /// <summary>
    /// El permiso acotado de audio. Desde Android 13 es <c>READ_MEDIA_AUDIO</c>; antes no existia y
    /// hay que caer en el de almacenamiento, que es mas amplio de lo necesario pero es el unico que
    /// esas versiones ofrecen.
    /// </summary>
    private sealed class AudioLibraryPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [(global::Android.Manifest.Permission.ReadMediaAudio, true)]
                : [(global::Android.Manifest.Permission.ReadExternalStorage, true)];
    }
}
