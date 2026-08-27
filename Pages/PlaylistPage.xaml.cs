using System.Collections.ObjectModel;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>Canciones de una lista de reproduccion del usuario.</summary>
public partial class PlaylistPage : ContentPage, IQueryAttributable
{
    /// <summary>Clave con la que la biblioteca pasa la lista al navegar.</summary>
    public const string IdParameter = "id";

    private readonly IMusicLibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly IPlaybackService _playback;
    private readonly ILocalizationService _localization;

    private readonly ObservableCollection<SongRow> _rows = [];
    private string _playlistId = string.Empty;

    public PlaylistPage()
        : this(
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IPlaylistService>(),
            ServiceHelper.GetRequiredService<IPlaybackService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>())
    {
    }

    public PlaylistPage(
        IMusicLibraryService library,
        IPlaylistService playlists,
        IPlaybackService playback,
        ILocalizationService localization)
    {
        InitializeComponent();

        _library = library;
        _playlists = playlists;
        _playback = playback;
        _localization = localization;

        SongsView.ItemsSource = _rows;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(IdParameter, out var value) && value is string id)
        {
            _playlistId = id;
            Load();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ApplyTexts();
        MiniPlayer.Start();
        _playlists.PlaylistsChanged += OnPlaylistsChanged;
        Load();
    }

    protected override void OnDisappearing()
    {
        _playlists.PlaylistsChanged -= OnPlaylistsChanged;
        MiniPlayer.Stop();
        base.OnDisappearing();
    }

    private void OnPlaylistsChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Load);

    private void ApplyTexts()
    {
        PlayAllButton.Text = _localization["PlayAll"];
        ShuffleAllButton.Text = _localization["ShufflePlay"];
        EmptyMessage.Text = _localization["EmptyPlaylistMessage"];
    }

    private void Load()
    {
        if (_playlistId.Length == 0 || SongsView is null)
            return;

        var playlist = _playlists.Find(_playlistId);
        if (playlist is null)
        {
            // La lista se borro mientras se estaba viendo: se vuelve en vez de dejar una pantalla
            // que ya no representa nada.
            _rows.Clear();
            return;
        }

        Title = playlist.Name;
        PlaylistNameLabel.Text = playlist.Name;

        _rows.Clear();
        foreach (var song in _library.FindByIds(playlist.SongIds))
            _rows.Add(BuildRow(song));

        PlaylistCountLabel.Text = _rows.Count == 1
            ? _localization["SongCountOne"]
            : _localization.Format("SongCountMany", _rows.Count);

        var isEmpty = _rows.Count == 0;
        EmptyMessage.IsVisible = isEmpty;
        PlayAllButton.IsEnabled = !isEmpty;
        ShuffleAllButton.IsEnabled = !isEmpty;
    }

    private SongRow BuildRow(Song song)
    {
        var artist = song.ResolveGroupName(preferComposer: false);

        return new SongRow
        {
            Song = song,
            Title = song.Title.Length > 0 ? song.Title : _localization["UnknownTitle"],
            Subtitle = artist.Length > 0 ? artist : _localization["UnknownArtist"],
            Duration = TimeFormatter.Format(song.Duration),
            Artwork = _library.GetAlbumArt(song),
        };
    }

    private void OnPlayAllClicked(object? sender, EventArgs e)
    {
        if (_rows.Count == 0)
            return;

        _playback.Shuffle = false;
        _playback.Play(_rows.Select(row => row.Song).ToList(), 0);
    }

    private void OnShuffleAllClicked(object? sender, EventArgs e)
    {
        if (_rows.Count == 0)
            return;

        _playback.Shuffle = true;
        _playback.Play(_rows.Select(row => row.Song).ToList(), Random.Shared.Next(_rows.Count));
    }

    private void OnSongTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SongRow row)
            PlayRow(row);
    }

    private async void OnSongMenuClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SongRow row)
            return;

        await SongMenu.ShowAsync(this, row.Song, () => PlayRow(row), _playlistId);
    }

    private void PlayRow(SongRow row)
    {
        var queue = _rows.Select(item => item.Song).ToList();
        var index = queue.FindIndex(song => song.Id == row.Song.Id);
        _playback.Play(queue, index < 0 ? 0 : index);
    }
}
