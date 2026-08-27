using System.Collections.ObjectModel;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Selector de listas de una cancion. Es de seleccion multiple a proposito: desde una cancion se
/// puede marcar de golpe en cuantas listas se quiera, y desmarcarla de las que ya no toque.
/// </summary>
public partial class PlaylistPickerPage : ContentPage
{
    private readonly IPlaylistService _playlists;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;
    private readonly Song _song;
    private readonly ObservableCollection<PlaylistChoiceRow> _rows = [];

    public PlaylistPickerPage(Song song)
    {
        InitializeComponent();

        _song = song;
        _playlists = ServiceHelper.GetRequiredService<IPlaylistService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();

        PlaylistsView.ItemsSource = _rows;

        ApplyTexts();
        Reload();
    }

    private void ApplyTexts()
    {
        HeaderTitle.Text = _localization["SelectPlaylistsTitle"];
        HeaderSubtitle.Text = _song.Title;
        EmptyTitle.Text = _localization["NoPlaylistsTitle"];
        EmptyMessage.Text = _localization["SelectPlaylistsHint"];
        NewPlaylistButton.Text = _localization["NewPlaylist"];
        SaveButton.Text = _localization["Save"];
    }

    private void Reload()
    {
        var current = _playlists.PlaylistIdsContaining(_song.Id);

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
        _playlists.SetMembership(_song.Id, selected);

        _toast.Show(selected.Count switch
        {
            0 => _localization["RemovedFromPlaylists"],
            1 => _localization["AddedToPlaylistsOne"],
            _ => _localization.Format("AddedToPlaylistsMany", selected.Count),
        });

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
