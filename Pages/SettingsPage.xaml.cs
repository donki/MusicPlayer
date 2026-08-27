using MusicPlayer.Helpers;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>Configuracion: biblioteca, informacion en linea e idioma.</summary>
public partial class SettingsPage : ContentPage
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IMusicLibraryService _library;
    private readonly IArtistInfoService _artistInfo;
    private readonly IToastService _toast;

    private bool _isApplying;

    public SettingsPage()
        : this(
            ServiceHelper.GetRequiredService<ISettingsService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>(),
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IArtistInfoService>(),
            ServiceHelper.GetRequiredService<IToastService>())
    {
    }

    public SettingsPage(
        ISettingsService settings,
        ILocalizationService localization,
        IMusicLibraryService library,
        IArtistInfoService artistInfo,
        IToastService toast)
    {
        InitializeComponent();

        _settings = settings;
        _localization = localization;
        _library = library;
        _artistInfo = artistInfo;
        _toast = toast;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTexts();
        ApplyState();
    }

    private void ApplyTexts()
    {
        Title = _localization["SettingsTitle"];

        LibraryTitle.Text = _localization["SectionLibrary"];
        RescanButton.Text = _localization["RescanLibrary"];
        RescanHint.Text = _localization["RescanHint"];
        ComposerLabel.Text = _localization["GroupByComposer"];
        ComposerHint.Text = _localization["GroupByComposerHint"];

        OnlineTitle.Text = _localization["SectionOnline"];
        OnlineLabel.Text = _localization["OnlineArtistInfo"];
        OnlineHint.Text = _localization["OnlineArtistInfoHint"];
        ClearCacheButton.Text = _localization["ClearImageCache"];

        AutoTitle.Text = _localization["SectionAndroidAuto"];
        AutoHint.Text = _localization["AndroidAutoHint"];

        LanguageTitle.Text = _localization["SectionLanguage"];
        LanguageHint.Text = _localization["LanguageHint"];
        SpanishButton.Text = _localization["SpanishButton"];
        EnglishButton.Text = _localization["EnglishButton"];

        ApplyLanguageButtons();
    }

    private void ApplyState()
    {
        // Se marca mientras se ajustan los controles: fijar IsToggled dispara Toggled y guardaria
        // la preferencia como si el usuario la hubiera tocado.
        _isApplying = true;
        ComposerSwitch.IsToggled = _settings.PreferComposer;
        OnlineSwitch.IsToggled = _settings.OnlineArtistInfo;
        _isApplying = false;
    }

    /// <summary>El idioma activo se ve relleno de marca; el otro, de contorno (constitucion A.9).</summary>
    private void ApplyLanguageButtons()
    {
        var isSpanish = _localization.CurrentLanguage == "es";
        var primary = (Color)Application.Current!.Resources["Primary"];
        var onPrimary = (Color)Application.Current!.Resources["OnPrimary"];

        SpanishButton.BackgroundColor = isSpanish ? primary : Colors.Transparent;
        SpanishButton.TextColor = isSpanish ? onPrimary : primary;
        SpanishButton.BorderWidth = isSpanish ? 0 : 1;

        EnglishButton.BackgroundColor = isSpanish ? Colors.Transparent : primary;
        EnglishButton.TextColor = isSpanish ? primary : onPrimary;
        EnglishButton.BorderWidth = isSpanish ? 1 : 0;
    }

    private async void OnRescanClicked(object? sender, EventArgs e)
    {
        RescanButton.IsEnabled = false;
        try
        {
            var scanned = await _library.ScanAsync();
            _toast.Show(scanned
                ? _localization.Format("ScanCompleteFormat", _library.Songs.Count, _library.Artists.Count)
                : _localization["PermissionDeniedTitle"]);
        }
        finally
        {
            RescanButton.IsEnabled = true;
        }
    }

    private async void OnComposerToggled(object? sender, ToggledEventArgs e)
    {
        if (_isApplying)
            return;

        _settings.PreferComposer = e.Value;

        // Cambiar el criterio de agrupacion rehace los grupos: hay que releer la biblioteca.
        await _library.ScanAsync();
    }

    private void OnOnlineToggled(object? sender, ToggledEventArgs e)
    {
        if (_isApplying)
            return;

        _settings.OnlineArtistInfo = e.Value;
    }

    private void OnClearCacheClicked(object? sender, EventArgs e)
    {
        _artistInfo.ClearCache();
        _toast.Show(_localization["ImageCacheCleared"]);
    }

    private void OnSpanishClicked(object? sender, EventArgs e) => SetLanguage("es");

    private void OnEnglishClicked(object? sender, EventArgs e) => SetLanguage("en");

    private void SetLanguage(string languageCode)
    {
        _localization.SetLanguage(languageCode);
        ApplyTexts();
    }
}
