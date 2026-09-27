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

    /// <summary>
    /// Atras (constitucion Mobile 7). Primero se cierra el menu lateral o el dialogo que haya abierto; despues
    /// decide la pagina visible (salir del modo seleccion, vaciar el buscador) y el Shell desapila
    /// las paginas de detalle (grupo, lista). Reproduciendo, Configuracion y Acerca de, abiertas desde
    /// el menu, vuelven a la Biblioteca. En la Biblioteca la aplicacion se oculta sin cerrarse: la
    /// musica sigue sonando porque la lleva el servicio de reproduccion, no la pantalla.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (FlyoutIsPresented)
        {
            FlyoutIsPresented = false;
            return true;
        }

        // Un dialogo abierto (menu de una cancion, confirmacion...) se cierra antes que nada.
        if (SocShared.ModernDialogBack.TryDismiss(CurrentPage))
            return true;

        if (base.OnBackButtonPressed())
            return true;

        if (CurrentPage is not LibraryPage)
        {
            Dispatcher.Dispatch(async () =>
            {
                try { await GoToAsync("//LibraryPage"); }
                catch (Exception ex) { SocShared.CrashGuard.Log(ex, "Atras a la biblioteca"); }
            });
            return true;
        }

#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
        return true;
#else
        return false;
#endif
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
