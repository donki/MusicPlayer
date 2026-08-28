using System.Collections.ObjectModel;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Selector de listas. Es de seleccion multiple a proposito: se puede marcar de golpe en cuantas
/// listas se quiera.
/// </summary>
/// <remarks>
/// Se comporta de dos maneras segun de donde se abra, y la diferencia no es un capricho:
/// <list type="bullet">
///   <item><description>Con <b>una</b> cancion edita su pertenencia: las listas donde ya esta
///   aparecen marcadas y desmarcar una la saca de ella.</description></item>
///   <item><description>Con <b>varias</b> solo anade. No hay una pertenencia comun que mostrar
///   (cada cancion esta en unas listas distintas), y desmarcar significaria sacar canciones de
///   listas que el usuario no ha mirado.</description></item>
/// </list>
/// </remarks>
public partial class PlaylistPickerPage : ContentPage
{
    private readonly IPlaylistService _playlists;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;
    private readonly IReadOnlyList<Song> _songs;
    private readonly ObservableCollection<PlaylistChoiceRow> _rows = [];

    public PlaylistPickerPage(Song song)
        : this([song])
    {
    }

    public PlaylistPickerPage(IReadOnlyList<Song> songs)
    {
        InitializeComponent();

        _songs = songs;
        _playlists = ServiceHelper.GetRequiredService<IPlaylistService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();

        PlaylistsView.ItemsSource = _rows;

        ApplyTexts();
        Reload();
    }

    /// <summary>Con una sola cancion se edita su pertenencia; con varias solo se anade.</summary>
    private bool IsMembershipMode => _songs.Count == 1;

    private void ApplyTexts()
    {
        HeaderTitle.Text = _localization["SelectPlaylistsTitle"];
        HeaderSubtitle.Text = IsMembershipMode
            ? _songs[0].Title
            : _localization.Format("SongCountMany", _songs.Count);
        EmptyTitle.Text = _localization["NoPlaylistsTitle"];
        EmptyMessage.Text = IsMembershipMode
            ? _localization["SelectPlaylistsHint"]
            : _localization["SelectPlaylistsHintMany"];
        NewPlaylistButton.Text = _localization["NewPlaylist"];
        SaveButton.Text = _localization["Save"];
    }

    private void Reload()
    {
        // Con varias canciones se parte de nada marcado: lo que se marque se anade, y punto.
        var current = IsMembershipMode
            ? _playlists.PlaylistIdsContaining(_songs[0].Id)
            : [];

        _rows.Clear();
        foreach (var playlist in _playlists.Playlists)
        {
            _rows.Add(new PlaylistChoiceRow
            {
                Id = playlist.Id,
                Name = playlist.Name,
                IsSelected = current.Contains(playlist.Id),
            });
        }

        var hasPlaylists = _rows.Count > 0;
        PlaylistsView.IsVisible = hasPlaylists;
        EmptyPanel.IsVisible = !hasPlaylists;
        SaveButton.IsEnabled = hasPlaylists;
    }

    private async void OnNewPlaylistClicked(object? sender, EventArgs e)
    {
        var name = await SocShared.ModernDialog.PromptAsync(this,
            _localization["PlaylistNameTitle"], _localization["PlaylistNameMessage"],
            _localization["Create"], _localization["Cancel"],
            placeholder: _localization["PlaylistNamePlaceholder"]);

        if (string.IsNullOrWhiteSpace(name))
            return;

        var created = _playlists.Create(name);
        if (created is null)
        {
            _toast.Show(_localization["PlaylistExists"]);
            return;
        }

        // Se guarda lo ya marcado antes de recargar, o al crear una lista se perderia la seleccion.
        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Id).ToHashSet();
        selected.Add(created.Id);

        Reload();
        foreach (var row in _rows)
            row.IsSelected = selected.Contains(row.Id);

        // La casilla no se entera de un cambio en el modelo: se vuelve a enlazar la lista entera.
        PlaylistsView.ItemsSource = null;
        PlaylistsView.ItemsSource = _rows;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Id).ToList();

        if (IsMembershipMode)
            _playlists.SetMembership(_songs[0].Id, selected);
        else
            _playlists.AddSongs(_songs.Select(song => song.Id).ToList(), selected);

        _toast.Show(selected.Count switch
        {
            // Sin nada marcado y editando pertenencia, guardar significa sacarla de todas.
            0 => IsMembershipMode ? _localization["RemovedFromPlaylists"] : _localization["NothingAdded"],
            1 => _localization["AddedToPlaylistsOne"],
            _ => _localization.Format("AddedToPlaylistsMany", selected.Count),
        });

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
