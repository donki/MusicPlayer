// Cuñas de lo que la app toma de Microsoft.Maui.Storage. En las pruebas no hay MAUI: la carpeta de
// datos es una temporal por prueba y las preferencias viven en memoria. Las dos van por flujo
// asincrono (AsyncLocal) para que las pruebas puedan correr en paralelo sin pisarse.
namespace Microsoft.Maui.Storage;

public static class FileSystem
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string AppDataDirectory
    {
        get => Current.Value ?? throw new InvalidOperationException("La prueba no ha preparado su carpeta de datos.");
        set => Current.Value = value;
    }
}

public static class Preferences
{
    private static readonly AsyncLocal<Dictionary<string, object>?> Current = new();

    private static Dictionary<string, object> Values => Current.Value ??= new Dictionary<string, object>();

    public static void Clear() => Current.Value = new Dictionary<string, object>();

    public static T Get<T>(string key, T defaultValue) =>
        Values.TryGetValue(key, out var value) ? (T)value : defaultValue;

    public static void Set<T>(string key, T value) => Values[key] = value!;
}
