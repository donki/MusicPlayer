using Microsoft.Extensions.Logging;
using MusicPlayer.Helpers;
using MusicPlayer.Pages;
using MusicPlayer.Services;

namespace MusicPlayer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Servicios (constitucion 5 y 7: la logica vive aqui, las paginas solo la orquestan).
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<IArtistInfoService, ArtistInfoService>();
        builder.Services.AddSingleton<ISongLookupService, SongLookupService>();
        builder.Services.AddSingleton<IPlaylistService, PlaylistService>();
        builder.Services.AddSingleton<ISongTagsService, SongTagsService>();
        builder.Services.AddSingleton<UpdateService>();

#if ANDROID
        builder.Services.AddSingleton<IToastService, Platforms.Android.ToastService>();
        builder.Services.AddSingleton<IMediaAccessService, Platforms.Android.MediaAccessService>();
        builder.Services.AddSingleton<IMusicLibraryService, Platforms.Android.MusicLibraryService>();
        builder.Services.AddSingleton<IPlaybackService, Platforms.Android.PlaybackService>();
        builder.Services.AddSingleton<ILyricsService, Platforms.Android.LyricsService>();
#endif

        // Paginas: se resuelven por el contenedor cuando la navegacion lo permite y, si no, por su
        // constructor sin parametros, que pide lo mismo al mismo contenedor (ver ServiceHelper).
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<NowPlayingPage>();
        builder.Services.AddTransient<ArtistPage>();
        builder.Services.AddTransient<PlaylistPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AboutPage>();

#if DEBUG
        builder.Services.AddLogging(logging => logging.AddDebug());
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }
}
