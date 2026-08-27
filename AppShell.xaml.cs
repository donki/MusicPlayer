using MusicPlayer.Helpers;
using MusicPlayer.Pages;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class AppShell : Shell
{
    private readonly ILocalizationService _localization;

    public AppShell()
    {
        InitializeComponent();

        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _localization.LanguageChanged += OnLanguageChanged;

        // Paginas de detalle: no estan en el menu, se llega a ellas desde la biblioteca.
        Routing.RegisterRoute(nameof(ArtistPage), typeof(ArtistPage));
        Routing.RegisterRoute(nameof(PlaylistPage), typeof(PlaylistPage));

        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
        ApplyTexts();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(ApplyTexts);

    private void ApplyTexts()
    {
        MenuAppName.Text = _localization["AppName"];
        MenuLibraryLabel.Text = _localization["MenuLibrary"];
        MenuNowPlayingLabel.Text = _localization["MenuNowPlaying"];
        MenuSettingsLabel.Text = _localization["MenuSettings"];
        MenuAboutLabel.Text = _localization["MenuAbout"];
    }

    private async void OnLibraryTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//LibraryPage");

    private async void OnNowPlayingTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//NowPlayingPage");

    private async void OnSettingsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//SettingsPage");

    private async void OnAboutTapped(object? sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    /// <summary>
    /// Se navega ANTES de cerrar el menu: al reves, la animacion de cierre se come la navegacion y
    /// el menu se cierra sin ir a ninguna parte.
    /// </summary>
    private async Task NavigateAsync(string route)
    {
        await GoToAsync(route);
        FlyoutIsPresented = false;
    }
}
