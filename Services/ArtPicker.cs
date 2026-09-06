using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// El «de dónde saco la imagen» que sale al querer ponerle una foto a un grupo o una carátula a una
/// canción.
/// </summary>
/// <remarks>
/// <para>Está aquí y no en cada pantalla porque las dos preguntan lo mismo y tienen que responder
/// igual: del aparato, de una dirección de internet, o buscándola en Google para copiar la
/// dirección de una que valga.</para>
///
/// <para><b>La búsqueda en Google abre el navegador</b> y no hace nada por su cuenta: raspar
/// resultados de Google no está permitido y ninguna imagen suya es libre de usar. Lo que hace la
/// aplicación es llevar al usuario al sitio con la consulta ya escrita —el título y el grupo, o el
/// nombre del fichero cuando no hay etiquetas, que es justo el caso en el que hace falta—; elegir
/// una imagen y traerla es cosa suya.</para>
/// </remarks>
public static class ArtPicker
{
    /// <summary>Lo que el usuario ha elegido hacer.</summary>
    public enum Origen
    {
        Nada,
        Dispositivo,
        Direccion,
        Buscar,
        Quitar,
    }

    /// <summary>Pregunta de dónde sacar la imagen.</summary>
    /// <param name="hayPuesta">Si ya hay una puesta a mano: solo entonces se ofrece quitarla.</param>
    public static async Task<Origen> PreguntarAsync(Page page, ILocalizationService textos, bool hayPuesta)
    {
        var opciones = new List<string>
        {
            textos["ArtFromDevice"],
            textos["ArtFromUrl"],
            textos["ArtSearchGoogle"],
        };

        if (hayPuesta)
        {
            opciones.Add(textos["ArtRemove"]);
        }

        var elegido = await SocShared.ModernDialog.ActionSheetAsync(
            page, textos["ArtTitle"], textos["Cancel"], [.. opciones]);

        if (elegido == textos["ArtFromDevice"])
            return Origen.Dispositivo;
        if (elegido == textos["ArtFromUrl"])
            return Origen.Direccion;
        if (elegido == textos["ArtSearchGoogle"])
            return Origen.Buscar;
        if (hayPuesta && elegido == textos["ArtRemove"])
            return Origen.Quitar;

        return Origen.Nada;
    }

    /// <summary>
    /// Abre la búsqueda de imágenes con la canción ya escrita: título y grupo, y si no hay ni eso,
    /// el nombre del fichero, que es lo único que queda cuando las etiquetas vienen vacías.
    /// </summary>
    public static Task BuscarAsync(Song song) => BuscarAsync(TextoDeBusqueda(song));

    /// <summary>La misma búsqueda para un grupo o compositor.</summary>
    public static Task BuscarAsync(string texto) =>
        Browser.Default.OpenAsync(
            new Uri($"https://www.google.com/search?tbm=isch&q={Uri.EscapeDataString(texto)}"),
            BrowserLaunchMode.SystemPreferred);

    /// <summary>Con qué se busca una canción: lo que se sepa de ella, y si no, su fichero.</summary>
    public static string TextoDeBusqueda(Song song)
    {
        var titulo = song.Title.Trim();
        var grupo = song.ResolveGroupName(preferComposer: false).Trim();

        if (titulo.Length > 0 || grupo.Length > 0)
        {
            return $"{titulo} {grupo}".Trim();
        }

        // Sin etiquetas queda el nombre del fichero, que suele traer «grupo - titulo» dentro. Se le
        // quitan la extension y los guiones bajos, que en una busqueda solo estorban.
        var nombre = Path.GetFileNameWithoutExtension(song.FilePath ?? string.Empty);
        return nombre.Replace('_', ' ').Trim();
    }
}
