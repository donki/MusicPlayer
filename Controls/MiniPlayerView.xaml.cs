using MusicPlayer.Helpers;
using MusicPlayer.Services;

namespace MusicPlayer.Controls;

/// <summary>
/// Barra de reproduccion reducida. No guarda estado propio: lo lee del servicio de reproduccion,
/// que es la unica fuente de verdad de lo que suena (constitucion 7).
/// </summary>
public partial class MiniPlayerView : ContentView
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPlaybackService _playback;
    private readonly IMusicLibraryService _library;
    private readonly ILocalizationService _localization;
    private IDispatcherTimer? _timer;

    public MiniPlayerView()
    {
        InitializeComponent();

        _playback = ServiceHelper.GetRequiredService<IPlaybackService>();
        _library = ServiceHelper.GetRequiredService<IMusicLibraryService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
    }

    /// <summary>La llama la pagina que la contiene al aparecer.</summary>
    public void Start()
    {
        _playback.StateChanged += OnPlaybackStateChanged;

        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = RefreshInterval;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();

        Refresh();
    }

    /// <summary>La llama la pagina que la contiene al desaparecer, para no dejar el temporizador vivo.</summary>
    public void Stop()
    {
        _playback.StateChanged -= OnPlaybackStateChanged;
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e) => RefreshProgress();

    private void OnPlaybackStateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var song = _playback.Current;
        IsVisible = song is not null;
        if (song is null)
            return;

        TitleLabel.Text = song.Title.Length > 0 ? song.Title : _localization["UnknownTitle"];

        var artist = song.ResolveGroupName(preferComposer: false);
        ArtistLabel.Text = artist.Length > 0 ? artist : _localization["UnknownArtist"];

        Artwork.Source = _library.GetAlbumArt(song);
        PlayPauseButton.Source = _playback.IsPlaying ? "ic_pause_w.png" : "ic_play_w.png";

        RefreshProgress();
    }

    private void RefreshProgress()
    {
        var duration = _playback.Duration;
        Progress.Progress = duration > TimeSpan.Zero
            ? Math.Clamp(_playback.Position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
            : 0;
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e) => _playback.TogglePlayPause();

    private void OnNextClicked(object? sender, EventArgs e) => _playback.Next();

    private void OnPreviousClicked(object? sender, EventArgs e) => _playback.Previous();

    private async void OnOpenNowPlaying(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("//NowPlayingPage");
}
