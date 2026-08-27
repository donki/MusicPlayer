using System.Globalization;

namespace MusicPlayer.Services;

/// <summary>
/// Unico origen de los textos visibles (constitucion 8). Ninguna pagina escribe texto de interfaz
/// en el markup ni en el codigo: todo se pide por clave a este servicio.
/// </summary>
public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    /// <summary>Idioma efectivo en uso ("es" o "en").</summary>
    string CurrentLanguage { get; }

    /// <summary>Idioma elegido por el usuario; cadena vacia si sigue al del sistema.</summary>
    string SelectedLanguage { get; }

    CultureInfo CurrentCulture { get; }

    /// <summary>Texto de la clave indicada. Nunca devuelve la clave al usuario.</summary>
    string this[string key] { get; }

    /// <summary>Texto de la clave con los marcadores <c>{0}</c>… sustituidos.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>Fija el idioma. Cadena vacia o <c>null</c> para volver a seguir al del sistema.</summary>
    void SetLanguage(string? languageCode);
}
