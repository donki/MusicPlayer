using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Pages;
using MusicPlayer.Services;

namespace MusicPlayer.Helpers;

/// <summary>
/// Modo de seleccion multiple de una lista de canciones. Vive en un solo sitio porque las tres
/// listas (biblioteca, grupo y lista de reproduccion) tienen que comportarse exactamente igual:
/// se entra manteniendo pulsada una cancion, se marca y desmarca con toques y se sale con la
/// flecha atras o con la aspa de la barra.
/// </summary>
/// <remarks>
/// La pagina solo enlaza los gestos y el boton atras; todo lo demas (que se puede hacer con lo
/// marcado y que pasa despues) se decide aqui, siguiendo la constitucion 7.
/// </remarks>
internal sealed class SongSelection
{
    private readonly Page _page;
    private readonly SelectionBar _bar;
    private readonly IList<SongRow> _rows;

    private readonly IMusicLibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly IPlaybackService _playback;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;

    /// <param name="rows">
    /// La coleccion viva que pinta la lista, no una copia: las paginas la reconstruyen al cambiar
    /// la biblioteca o el filtro, y la seleccion tiene que leer siempre lo que hay en pantalla.
    /// </param>
    public SongSelection(Page page, SelectionBar bar, IList<SongRow> rows)
    {
        _page = page;
        _bar = bar;
        _rows = rows;

        _library = ServiceHelper.GetRequiredService<IMusicLibraryService>();
        _playlists = ServiceHelper.GetRequiredService<IPlaylistService>();
        _playback = ServiceHelper.GetRequiredService<IPlaybackService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();

        _bar.CloseClicked += (_, _) => Exit();
        _bar.SelectAllClicked += (_, _) => ToggleAll();
        _bar.PlayClicked += (_, _) => PlaySelection();
        _bar.AddToPlaylistClicked += async (_, _) => await AddToPlaylistsAsync();
        _bar.MoreClicked += async (_, _) => await ShowMoreAsync();
    }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Lista de reproduccion que se esta viendo, si es el caso. Solo entonces tiene sentido
    /// ofrecer quitar las canciones de ella, que no es lo mismo que borrarlas del dispositivo.
    /// La pagina la conoce despues de construirse, al resolver los parametros de navegacion.
    /// </summary>
    public string? PlaylistId { get; set; }

    /// <summary>Pulsacion larga sobre una fila: entra en el modo, o marca una mas si ya se estaba.</summary>
    public void Begin(SongRow row)
    {
        if (!IsActive)
        {
            IsActive = true;
            _bar.IsVisible = true;
            row.IsSelected = true;
        }
        else
        {
            row.IsSelected = !row.IsSelected;
        }

        Refresh();
    }

    /// <summary>
    /// Toque sobre una fila. Devuelve <c>true</c> si lo ha consumido, es decir, si estabamos en el
    /// modo de seleccion y el toque ha marcado o desmarcado en vez de reproducir.
    /// </summary>
    public bool HandleTap(SongRow row)
    {
        if (!IsActive)
            return false;

        row.IsSelected = !row.IsSelected;
        Refresh();
        return true;
    }

    /// <summary>Sale del modo. Devuelve <c>true</c> si habia algo que cerrar (boton atras).</summary>
    public bool Exit()
    {
        if (!IsActive)
            return false;

        IsActive = false;
        _bar.IsVisible = false;

        foreach (var row in _rows)
            row.IsSelected = false;

        return true;
    }

    /// <summary>
    /// Marca todo lo visible, o lo desmarca si ya estaba todo marcado. Un solo boton para las dos
    /// cosas: es lo que espera quien lo pulsa dos veces seguidas.
    /// </summary>
    private void ToggleAll()
    {
        var selectAll = _rows.Any(row => !row.IsSelected);

        foreach (var row in _rows)
            row.IsSelected = selectAll;

        Refresh();
    }

    private void Refresh()
    {
        var count = Selected().Count;
        var text = count == 1
            ? _localization["SelectedCountOne"]
            : _localization.Format("SelectedCountMany", count);

        _bar.Update(text, count > 0);
    }

    private List<SongRow> Selected() => _rows.Where(row => row.IsSelected).ToList();

    private List<Song> SelectedSongs() => Selected().Select(row => row.Song).ToList();

    // ==================================================================================
    //  Acciones sobre lo marcado
    // ==================================================================================

    /// <summary>
    /// Reproduce solo lo marcado, en el orden en que aparece en la lista. La cola pasa a ser la
    /// seleccion: es lo que se acaba de pedir, no la lista entera.
    /// </summary>
    private void PlaySelection()
    {
        var songs = SelectedSongs();
        if (songs.Count == 0)
            return;

        Exit();
        _playback.Shuffle = false;
        _playback.Play(songs, 0);
    }

    private async Task AddToPlaylistsAsync()
    {
        var songs = SelectedSongs();
        if (songs.Count == 0)
            return;

        Exit();
        await _page.Navigation.PushModalAsync(new PlaylistPickerPage(songs));
    }

    /// <summary>
    /// Lo que no cabe en la barra: quitar de la lista que se esta viendo y borrar del dispositivo.
    /// Son las dos acciones destructivas, y estan detras de un paso mas a proposito.
    /// </summary>
    private async Task ShowMoreAsync()
    {
        var songs = SelectedSongs();
        if (songs.Count == 0)
            return;

        var removeText = _localization["RemoveFromPlaylist"];
        var deleteText = _localization["ActionDelete"];

        string[] options = PlaylistId is null ? [deleteText] : [removeText, deleteText];

        var choice = await SocShared.ModernDialog.ActionSheetAsync(_page,
            _localization["SongActionsTitle"], _localization["Cancel"], options);

        if (choice == removeText && PlaylistId is not null)
        {
            _playlists.RemoveSongs(PlaylistId, songs.Select(song => song.Id).ToList());
            _toast.Show(_localization.Format("RemovedFromPlaylistMany", songs.Count));
            Exit();
        }
        else if (choice == deleteText)
        {
            await DeleteAsync(songs);
        }
    }

    /// <summary>
    /// Borra del dispositivo todo lo marcado. Se pregunta una vez aqui y otra el sistema, que
    /// ademas resuelve las varias canciones en una sola confirmacion.
    /// </summary>
    private async Task DeleteAsync(IReadOnlyList<Song> songs)
    {
        var confirmed = await SocShared.ModernDialog.AlertAsync(_page,
            _localization["DeleteSongsTitle"],
            _localization.Format("DeleteSongsMessage", songs.Count),
            _localization["Delete"], _localization["Cancel"]);

        if (!confirmed)
            return;

        var outcome = await _library.DeleteAsync(songs);
        switch (outcome)
        {
            case DeleteOutcome.Deleted:
                _playlists.RemoveSongsEverywhere(songs.Select(song => song.Id).ToList());
                _toast.Show(_localization.Format("SongsDeleted", songs.Count));
                break;

            case DeleteOutcome.Cancelled:
                _toast.Show(_localization["DeleteCancelled"]);
                break;

            default:
                _toast.Show(_localization["DeleteFailed"]);
                break;
        }

        Exit();
    }
}
