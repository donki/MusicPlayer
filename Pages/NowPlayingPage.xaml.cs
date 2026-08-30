using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Pantalla completa de reproduccion: caratula, barra de progreso con tiempo transcurrido y
/// duracion, transporte y acciones sobre la cancion que suena.
/// </summary>
public partial class NowPlayingPage : ContentPage
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPlaybackService _playback;
    private readonly IMusicLibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;

    private IDispatcherTimer? _timer;
    private bool _isSeeking;
    private long _shownSongId = -1;

    public NowPlayingPage()
        : this(
            ServiceHelper.GetRequiredService<IPlaybackService>(),
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IPlaylistService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>(),
            ServiceHelper.GetRequiredService<IToastService>())
    {
    }

    public NowPlayingPage(
        IPlaybackService playback,
        IMusicLibraryService library,
        IPlaylistService playlists,
        ILocalizationService localization,
        IToastService toast)
    {
        InitializeComponent();

        _playback = playback;
        _library = library;
        _playlists = playlists;
        _localization = localization;
        _toast = toast;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ApplyTexts();
        _playback.StateChanged += OnPlaybackStateChanged;

        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = RefreshInterval;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();

        Refresh();
    }

    protected override void OnDisappearing()
    {
        _playback.StateChanged -= OnPlaybackStateChanged;
        _timer?.Stop();
        base.OnDisappearing();
    }

    private void ApplyTexts()
    {
        Title = _localization["NowPlayingTitle"];
        EmptyTitle.Text = _localization["NothingPlayingTitle"];
        EmptyMessage.Text = _localization["NothingPlayingMessage"];
        GoToLibraryButton.Text = _localization["MenuLibrary"];
    }

    private void OnTick(object? sender, EventArgs e) => RefreshProgress();

    private void OnPlaybackStateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var song = _playback.Current;

        PlayerLayout.IsVisible = song is not null;
        EmptyPanel.IsVisible = song is null;

        if (song is null)
        {
            _shownSongId = -1;
            return;
        }

        // La caratula solo se vuelve a cargar al cambiar de pista, no en cada refresco.
        if (_shownSongId != song.Id)
        {
            _shownSongId = song.Id;

            // Sin caratula propia se pinta la del artista. Cuando no hay ninguna hay que ESCONDER
            // la imagen, no solo dejarla sin origen: al pasar de pista, la vista nativa conserva
            // el ultimo mapa de bits y se quedaba la portada anterior sobre la cancion nueva.
            var art = _library.GetArtworkOrArtistArt(song);
            Artwork.Source = art;
            Artwork.IsVisible = art is not null;
        }

        TitleLabel.Text = song.Title.Length > 0 ? song.Title : _localization["UnknownTitle"];

        var artist = song.ResolveGroupName(preferComposer: false);
        ArtistLabel.Text = artist.Length > 0 ? artist : _localization["UnknownArtist"];

        AlbumLabel.Text = song.Album.Length > 0
            ? (song.Format.Length > 0 ? $"{song.Album} · {song.Format}" : song.Album)
            : _localization["UnknownAlbum"];

        PlayPauseButton.Source = _playback.IsPlaying ? "ic_pause_w.png" : "ic_play_w.png";
        ShuffleButton.Opacity = _playback.Shuffle ? 1 : 0.4;

        RepeatButton.Source = _playback.Repeat == RepeatMode.One ? "ic_repeat_one.png" : "ic_repeat.png";
        RepeatButton.Opacity = _playback.Repeat == RepeatMode.Off ? 0.4 : 1;

        var index = _playback.QueueIndex;
        var total = _playback.Queue.Count;
        QueueLabel.Text = index >= 0 && total > 0
            ? _localization.Format("QueuePositionFormat", index + 1, total)
            : string.Empty;

        RefreshProgress();
    }

    private void RefreshProgress()
    {
        // Mientras el usuario arrastra, la barra es suya: el temporizador no la mueve.
        if (_isSeeking)
            return;

        var position = _playback.Position;
        var duration = _playback.Duration;

        ElapsedLabel.Text = TimeFormatter.Format(position);
        DurationLabel.Text = TimeFormatter.Format(duration);

        ProgressSlider.Value = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
            : 0;
    }

    private void OnSeekStarted(object? sender, EventArgs e) => _isSeeking = true;

    private void OnSeekCompleted(object? sender, EventArgs e)
    {
        _isSeeking = false;

        var duration = _playback.Duration;
        if (duration > TimeSpan.Zero)
            _playback.SeekTo(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * ProgressSlider.Value));

        RefreshProgress();
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e) => _playback.TogglePlayPause();

    private void OnNextClicked(object? sender, EventArgs e) => _playback.Next();

    private void OnPreviousClicked(object? sender, EventArgs e) => _playback.Previous();

    private void OnShuffleClicked(object? sender, EventArgs e)
    {
        _playback.Shuffle = !_playback.Shuffle;
        _toast.Show(_localization[_playback.Shuffle ? "ShuffleOn" : "ShuffleOff"]);
        Refresh();
    }

    private void OnRepeatClicked(object? sender, EventArgs e)
    {
        _playback.Repeat = _playback.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };

        _toast.Show(_localization[_playback.Repeat switch
        {
            RepeatMode.All => "RepeatAll",
            RepeatMode.One => "RepeatOne",
            _ => "RepeatOff",
        }]);

        Refresh();
    }

    /// <summary>
    /// Ficha de la cancion: datos, resena del grupo y letra. Es la misma pagina que se abre desde
    /// la biblioteca, para no tener dos sitios donde mirar lo mismo.
    /// </summary>
    private async void OnInfoClicked(object? sender, EventArgs e)
    {
        if (_playback.Current is not { } song)
            return;

        await Navigation.PushModalAsync(new SongInfoPage(song));
    }

    private async void OnAddToPlaylistClicked(object? sender, EventArgs e)
    {
        if (_playback.Current is not { } song)
            return;

        await Navigation.PushModalAsync(new PlaylistPickerPage(song));
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_playback.Current is not { } song)
            return;

        var confirmed = await SocShared.ModernDialog.AlertAsync(this,
            _localization["DeleteSongTitle"],
            _localization.Format("DeleteSongMessage", song.Title),
            _localization["Delete"], _localization["Cancel"]);

        if (!confirmed)
            return;

        // Se pasa a la siguiente antes de borrar: no tiene sentido seguir sonando un fichero que
        // esta a punto de desaparecer.
        _playback.Next();

        var outcome = await _library.DeleteAsync(song);
        switch (outcome)
        {
            case DeleteOutcome.Deleted:
                _playlists.RemoveSongEverywhere(song.Id);
                _toast.Show(_localization["SongDeleted"]);
                break;

            case DeleteOutcome.Cancelled:
                _toast.Show(_localization["DeleteCancelled"]);
                break;

            default:
                _toast.Show(_localization["DeleteFailed"]);
                break;
        }
    }

    private async void OnGoToLibraryClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//LibraryPage");
}
