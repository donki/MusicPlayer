using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Ficha de una cancion: datos de la pista, resena del grupo y **letra**, que va marcando la linea
/// que suena cuando el fichero trae la letra sincronizada.
/// </summary>
/// <remarks>
/// La letra sale de la etiqueta de la propia cancion o de un <c>.lrc</c> al lado (ver
/// <see cref="ILyricsService"/>): ni se descarga ni se genera. La resena del grupo si sale de
/// internet, y por eso solo aparece si el usuario ha activado esa busqueda (constitucion 3).
/// </remarks>
public partial class SongInfoPage : ContentPage
{
    private readonly IMusicLibraryService _library;
    private readonly IPlaybackService _playback;
    private readonly ILyricsService _lyricsService;
    private readonly IArtistInfoService _artistInfo;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;

    private readonly List<LyricRow> _rows = [];
    private readonly Song _song;

    private IDispatcherTimer? _timer;
    private Lyrics _lyrics = Lyrics.Empty;
    private int _currentLine = -1;

    public SongInfoPage(Song song)
    {
        InitializeComponent();

        _song = song;
        _library = ServiceHelper.GetRequiredService<IMusicLibraryService>();
        _playback = ServiceHelper.GetRequiredService<IPlaybackService>();
        _lyricsService = ServiceHelper.GetRequiredService<ILyricsService>();
        _artistInfo = ServiceHelper.GetRequiredService<IArtistInfoService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();

        BindableLayout.SetItemsSource(LyricsBox, _rows);
        ApplyTexts();
        ShowSong();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadLyricsAsync();
        _ = LoadArtistAsync();

        // El seguimiento solo tiene sentido con letra sincronizada; si no, el temporizador seria
        // gasto de bateria para no mover nada.
        if (_lyrics.IsSynced)
        {
            _timer ??= Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }

    private void ApplyTexts()
    {
        HeaderTitle.Text = _localization["SongDetailsTitle"];
        HeaderSubtitle.Text = _song.Title;
        ArtistSectionTitle.Text = _localization["ArtistSection"];
        LyricsSectionTitle.Text = _localization["LyricsSection"];
    }

    private void ShowSong()
    {
        var art = _library.GetAlbumArt(_song);
        Artwork.Source = art;
        Artwork.IsVisible = art is not null;

        TitleLabel.Text = _song.Title.Length > 0 ? _song.Title : _localization["UnknownTitle"];

        var artist = _song.ResolveGroupName(_settings.PreferComposer);
        ArtistLabel.Text = artist.Length > 0 ? artist : _localization["UnknownArtist"];

        AlbumLabel.Text = _song.Album.Length > 0 ? _song.Album : _localization["UnknownAlbum"];

        // Una sola linea con lo que de verdad distingue a un fichero de otro.
        var details = new List<string>();
        if (_song.Year > 0)
            details.Add(_song.Year.ToString());
        if (_song.Track > 0)
            details.Add($"#{_song.Track}");
        if (_song.Format.Length > 0)
            details.Add(_song.Format);
        details.Add($"{(int)_song.Duration.TotalMinutes}:{_song.Duration.Seconds:00}");

        DetailsLabel.Text = string.Join(" · ", details);
        FileLabel.Text = _song.FilePath;
    }

    // ==================================================================================
    //  Letra
    // ==================================================================================

    private async Task LoadLyricsAsync()
    {
        LyricsBusy.IsRunning = true;
        LyricsBusy.IsVisible = true;

        try
        {
            _lyrics = await _lyricsService.GetAsync(_song);
        }
        finally
        {
            LyricsBusy.IsRunning = false;
            LyricsBusy.IsVisible = false;
        }

        _rows.Clear();
        foreach (var line in _lyrics.Lines)
            _rows.Add(new LyricRow(line.Text));

        BindableLayout.SetItemsSource(LyricsBox, null);
        BindableLayout.SetItemsSource(LyricsBox, _rows);

        LyricsEmptyLabel.Text = _localization["LyricsNone"];
        LyricsEmptyLabel.IsVisible = !_lyrics.HasLyrics;

        LyricsSourceLabel.Text = _lyrics.IsSynced
            ? $"{_localization.Format("LyricsFrom", _lyrics.Source)} · {_localization["LyricsSynced"]}"
            : _localization.Format("LyricsFrom", _lyrics.Source);
        LyricsSourceLabel.IsVisible = _lyrics.HasLyrics;
    }

    /// <summary>
    /// Marca la linea que suena. Solo se repinta cuando cambia de linea: hacerlo cada 400 ms sobre
    /// una letra de cien lineas daria tirones sin ganar nada.
    /// </summary>
    private async void OnTick(object? sender, EventArgs e)
    {
        // Si el usuario se ha ido a otra cancion, la ficha deja de seguir a la musica.
        if (_playback.Current?.Id != _song.Id)
            return;

        var index = _lyrics.IndexAt(_playback.Position);
        if (index == _currentLine)
            return;

        if (_currentLine >= 0 && _currentLine < _rows.Count)
            _rows[_currentLine].IsCurrent = false;

        _currentLine = index;

        if (index < 0 || index >= _rows.Count)
            return;

        _rows[index].IsCurrent = true;

        if (index < LyricsBox.Children.Count && LyricsBox.Children[index] is View line)
        {
            try
            {
                await Scroller.ScrollToAsync(line, ScrollToPosition.Center, true);
            }
            catch (Exception)
            {
                // Un desplazamiento fallido (la vista todavia sin medir) no puede tirar la pagina.
            }
        }
    }

    // ==================================================================================
    //  Grupo
    // ==================================================================================

    private async Task LoadArtistAsync()
    {
        var artist = _song.ResolveGroupName(_settings.PreferComposer);
        if (artist.Length == 0)
        {
            ArtistDescription.Text = _localization["ArtistSectionEmpty"];
            return;
        }

        if (!_artistInfo.IsEnabled)
        {
            // Se explica por que no hay ficha, en vez de dejar el hueco vacio sin motivo.
            ArtistDescription.Text = _localization["ArtistSectionOffline"];
            return;
        }

        var info = await _artistInfo.GetAsync(artist);

        ArtistDescription.Text = info.Description is { Length: > 0 } text
            ? text
            : _localization["ArtistSectionEmpty"];

        if (info.ImagePath is { Length: > 0 } path)
        {
            ArtistImage.Source = ImageSource.FromFile(path);
            ArtistImageFrame.IsVisible = true;
        }
    }

    // ==================================================================================

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(new SongEditPage(_song));
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}

/// <summary>
/// Linea de la letra en pantalla. Necesita avisar de sus cambios porque la linea que suena se
/// resalta sola mientras la pagina esta abierta.
/// </summary>
public sealed class LyricRow : INotifyPropertyChanged
{
    private static readonly Color Highlight = Color.FromArgb("#3525CD");

    private bool _isCurrent;

    public LyricRow(string text) => Text = text;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
                return;

            _isCurrent = value;
            Notify(nameof(IsCurrent));
            Notify(nameof(TextColor));
            Notify(nameof(Opacity));
            Notify(nameof(FontAttributes));
            Notify(nameof(FontSize));
        }
    }

    /// <summary>La linea que suena va en el color de marca; el resto, en el color normal del tema.</summary>
    public Color TextColor => _isCurrent
        ? Highlight
        : (Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#E6E1E9")
            : Color.FromArgb("#191C1D"));

    public double Opacity => _isCurrent ? 1.0 : 0.65;

    public FontAttributes FontAttributes => _isCurrent ? FontAttributes.Bold : FontAttributes.None;

    public double FontSize => _isCurrent ? 17 : 15;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
