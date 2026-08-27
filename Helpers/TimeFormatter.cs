namespace MusicPlayer.Helpers;

/// <summary>
/// Formato de los tiempos de reproduccion. Utilidad pura y sin estado (constitucion 5).
/// </summary>
public static class TimeFormatter
{
    /// <summary>
    /// Duracion como <c>m:ss</c>, o <c>h:mm:ss</c> a partir de una hora. Los tiempos negativos o
    /// desconocidos se muestran como <c>0:00</c> en vez de dejar la etiqueta vacia.
    /// </summary>
    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value == TimeSpan.MaxValue)
            value = TimeSpan.Zero;

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    /// <summary>Par «transcurrido / total» tal y como se muestra bajo la barra de progreso.</summary>
    public static string FormatProgress(TimeSpan position, TimeSpan duration) =>
        $"{Format(position)} / {Format(duration)}";
}
