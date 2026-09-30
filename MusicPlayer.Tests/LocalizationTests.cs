using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

[Collection(AppStateCollection.Name)]
public sealed partial class LocalizationTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
    }

    private static Dictionary<string, string> Table(string name) =>
        Reflect.GetStatic<Dictionary<string, string>>(typeof(LocalizationService), name);

    [Fact]
    public void Ingles_y_castellano_tienen_las_mismas_claves()
    {
        var english = Table("English");
        var spanish = Table("Spanish");

        Assert.Empty(english.Keys.Except(spanish.Keys));
        Assert.Empty(spanish.Keys.Except(english.Keys));
    }

    [Fact]
    public void Ningun_texto_vacio_y_mismos_huecos_de_formato()
    {
        var english = Table("English");
        var spanish = Table("Spanish");

        foreach (var (key, value) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"en:{key} vacio");
            Assert.False(string.IsNullOrWhiteSpace(spanish[key]), $"es:{key} vacio");
            Assert.True(Placeholders(value).SetEquals(Placeholders(spanish[key])), $"{key}: huecos distintos");
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        Hole().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Hole();

    [Fact]
    public void Idioma_elegido_y_respaldo_en_ingles()
    {
        var settings = new FakeSettings();
        var service = new LocalizationService(settings, NullLogger<LocalizationService>.Instance);
        var changed = 0;
        service.LanguageChanged += (_, _) => changed++;

        service.SetLanguage("es");
        Assert.Equal("es", service.CurrentLanguage);
        Assert.Equal("es", service.SelectedLanguage);
        Assert.Equal("es", settings.Language);
        Assert.Equal("es", service.CurrentCulture.Name);
        Assert.Equal(Table("Spanish")["SettingsTitle"], service["SettingsTitle"]);

        service.SetLanguage("fr");   // no soportado
        Assert.Equal("en", service.CurrentLanguage);
        Assert.Equal(Table("English")["SettingsTitle"], service["SettingsTitle"]);
        Assert.Equal(2, changed);
    }

    [Theory]
    [InlineData("es-ES", "es")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    public void Sin_eleccion_sigue_al_sistema_si_puede(string system, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(system);
        var service = new LocalizationService(new FakeSettings(), NullLogger<LocalizationService>.Instance);
        service.SetLanguage(null);
        Assert.Equal(expected, service.CurrentLanguage);
        Assert.Equal(string.Empty, service.SelectedLanguage);
    }

    [Fact]
    public void Clave_desconocida_no_se_ensena()
    {
        var service = new LocalizationService(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance);
        Assert.Equal(string.Empty, service["NoExiste"]);
        Assert.Equal(string.Empty, service.Format("NoExiste", 1));
    }

    [Fact]
    public void Format_rellena_con_la_cultura_del_idioma()
    {
        var service = new LocalizationService(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance);
        Assert.Equal(string.Format(Table("Spanish")["VersionFormat"], "1.2"), service.Format("VersionFormat", "1.2"));
    }

    [Fact]
    public void Format_con_argumentos_de_menos_devuelve_la_plantilla()
    {
        var service = new LocalizationService(new FakeSettings { Language = "en" }, NullLogger<LocalizationService>.Instance);
        Assert.Equal(Table("English")["UpdateAvailableMessage"], service.Format("UpdateAvailableMessage", "solo uno"));
    }

    [Fact]
    public void Falta_de_traduccion_cae_al_ingles()
    {
        var spanish = Table("Spanish");
        const string key = "SettingsTitle";
        var saved = spanish[key];
        spanish.Remove(key);
        try
        {
            var service = new LocalizationService(new FakeSettings { Language = "es" }, NullLogger<LocalizationService>.Instance);
            Assert.Equal(Table("English")[key], service[key]);
        }
        finally
        {
            spanish[key] = saved;
        }
    }
}
