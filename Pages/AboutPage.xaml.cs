using MusicPlayer.Helpers;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Pantalla «Acerca de» con la estructura comun a todas las apps sOCratic: cabecera, contacto,
/// idioma, privacidad, licencia y aviso legal (constitucion A.9).
/// </summary>
public partial class AboutPage : ContentPage
{
    /// <summary>Constante identica en todos los proyectos: si cambia, cambia en todos a la vez.</summary>
    private const string ContactEmail = "jsoladelarosa@gmail.com";

    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;

    public AboutPage()
        : this(
            ServiceHelper.GetRequiredService<ILocalizationService>(),
            ServiceHelper.GetRequiredService<IToastService>())
    {
    }

    public AboutPage(ILocalizationService localization, IToastService toast)
    {
        InitializeComponent();

        _localization = localization;
        _toast = toast;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = _localization["AboutTitle"];

        AppNameLabel.Text = _localization["AppName"];
        VersionLabel.Text = _localization.Format("VersionFormat", AppInfo.Current.VersionString);
        DescriptionLabel.Text = _localization["AppDescription"];

        ContactTitle.Text = _localization["ContactTitle"];
        ContactButton.Text = ContactEmail;
        ContactHint.Text = _localization["ContactHint"];

        LanguageTitle.Text = _localization["SectionLanguage"];
        LanguageHint.Text = _localization["LanguageHint"];
        SpanishButton.Text = _localization["SpanishButton"];
        EnglishButton.Text = _localization["EnglishButton"];

        PrivacyTitle.Text = _localization["PrivacyTitle"];
        PrivacyText.Text = _localization["PrivacyText"];

        LicenseTitle.Text = _localization["LicenseTitle"];
        LicenseText.Text = _localization["LicenseText"];

        LegalTitle.Text = _localization["LegalTitle"];
        LegalText1.Text = _localization["LegalText1"];
        LegalText2.Text = _localization["LegalText2"];
        WarningText.Text = _localization["WarningText"];

        ApplyLanguageButtons();
    }

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

    private async void OnContactClicked(object? sender, EventArgs e)
    {
        try
        {
            await Email.Default.ComposeAsync(new EmailMessage
            {
                Subject = _localization["AppName"],
                To = [ContactEmail],
            });
        }
        catch (Exception)
        {
            // Sin cliente de correo configurado se copia la direccion: el usuario puede escribir
            // desde donde quiera, en vez de quedarse sin manera de contactar.
            await Clipboard.Default.SetTextAsync(ContactEmail);
            _toast.Show(ContactEmail);
        }
    }

    private void OnSpanishClicked(object? sender, EventArgs e)
    {
        _localization.SetLanguage("es");
        ApplyTexts();
    }

    private void OnEnglishClicked(object? sender, EventArgs e)
    {
        _localization.SetLanguage("en");
        ApplyTexts();
    }
}
