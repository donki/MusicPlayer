using System.Collections.ObjectModel;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Canciones de un grupo o compositor, con su foto y una resena breve cuando el usuario ha
/// activado la busqueda en linea.
/// </summary>
public partial class ArtistPage : ContentPage, IQueryAttributable
{
    /// <summary>Clave con la que la biblioteca pasa el grupo al navegar.</summary>
    public const string NameParameter = "name";

    private readonly IMusicLibraryService _library;
    private readonly IPlaybackService _playback;
    private readonly IArtistInfoService _artistInfo;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;

    private readonly ObservableCollection<SongRow> _rows = [];
    private ArtistGroup? _artist;
    private string _artistName = string.Empty;

    public ArtistPage()
        : this(
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IPlaybackService>(),
            ServiceHelper.GetRequiredService<IArtistInfoService>(),
            ServiceHelper.GetRequiredService<ISettingsService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>())
    {
    }

    public ArtistPage(
        IMusicLibraryService library,
        IPlaybackService playback,
        IArtistInfoService artistInfo,
        ISettingsService settings,
        ILocalizationService localization)
    {
        InitializeComponent();

        _library = library;
        _playback = playback;
        _artistInfo = artistInfo;
        _settings = settings;
        _localization = localization;

        SongsView.ItemsSource = _rows;
    }

    /// <summary>
    /// El nombre del grupo llega como objeto, no dentro de la cadena de la ruta: asi no hay que
    /// codificarlo ni descodificarlo, y un grupo con «&amp;» o «%» en el nombre no rompe nada.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(NameParameter, out var value) && value is string name)
        {
            _artistName = name;
            Load();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTexts();
        MiniPlayer.Start();
        Load();
        _ = LoadArtistInfoAsync();
    }

    protected override void OnDisappearing()
    {
        MiniPlayer.Stop();
        base.OnDisappearing();
    }

    private void ApplyTexts()
    {
        PlayAllButton.Text = _localization["PlayAll"];
        ShuffleAllButton.Text = _localization["ShufflePlay"];
        EnableLookupButton.Text = _localization["EnableOnlineLookup"];
        LookupHintLabel.Text = _localization["ArtistLookupDisabled"];
        ArtistSourceLabel.Text = _localization["ArtistImageSource"];
    }

    private void Load()
    {
        if (_artistName.Length == 0 || SongsView is null)
            return;

        _artist = _library.FindArtist(_artistName);
        Title = _artistName;
        ArtistNameLabel.Text = _artistName;

        _rows.Clear();
        if (_artist is null)
        {
            ArtistCountLabel.Text = _localization.Format("SongCountMany", 0);
            return;
        }

        foreach (var song in _artist.Songs)
            _rows.Add(BuildRow(song));

        ArtistCountLabel.Text = _artist.SongCount == 1
            ? _localization["SongCountOne"]
            : _localization.Format("SongCountMany", _artist.SongCount);

        if (_artist.ImagePath is not null)
            ArtistImage.Source = ImageSource.FromFile(_artist.ImagePath);

        ShowDescription(_artist.Description);
    }

    /// <summary>
    /// Busca foto y resena si el usuario lo ha permitido. Si no, se explica por que no hay foto y
    /// se ofrece activarlo, en vez de dejar un hueco sin explicacion.
    /// </summary>
    private async Task LoadArtistInfoAsync()
    {
        if (_artist is null)
            return;

        LookupHintCard.IsVisible = !_artistInfo.IsEnabled && _artist.ImagePath is null;
        if (!_artistInfo.IsEnabled)
            return;

        var info = await _artistInfo.GetAsync(_artistName);
        if (info.ImagePath is null && info.Description is null)
            return;

        _artist.ImagePath = info.ImagePath;
        _artist.Description = info.Description;

        if (info.ImagePath is not null)
            ArtistImage.Source = ImageSource.FromFile(info.ImagePath);

        ShowDescription(info.Description);
    }

    private void ShowDescription(string? description)
    {
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        ArtistBioLabel.Text = description ?? string.Empty;
        ArtistBioLabel.IsVisible = hasDescription;
        ArtistSourceLabel.IsVisible = hasDescription || _artist?.ImagePath is not null;
    }

    private SongRow BuildRow(Song song) => new()
    {
        Song = song,
        Title = song.Title.Length > 0 ? song.Title : _localization["UnknownTitle"],
        Subtitle = song.Album.Length > 0 ? song.Album : _localization["UnknownAlbum"],
        Duration = TimeFormatter.Format(song.Duration),
        Artwork = _library.GetAlbumArt(song),
    };

    private void OnPlayAllClicked(object? sender, EventArgs e)
    {
        if (_artist is null || _artist.SongCount == 0)
            return;

        _playback.Shuffle = false;
        _playback.Play(_artist.Songs, 0);
    }

    private void OnShuffleAllClicked(object? sender, EventArgs e)
    {
        if (_artist is null || _artist.SongCount == 0)
            return;

        _playback.Shuffle = true;
        _playback.Play(_artist.Songs, Random.Shared.Next(_artist.SongCount));
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

        await SongMenu.ShowAsync(this, row.Song, () => PlayRow(row));
    }

    private void PlayRow(SongRow row)
    {
        var queue = _rows.Select(item => item.Song).ToList();
        var index = queue.FindIndex(song => song.Id == row.Song.Id);
        _playback.Play(queue, index < 0 ? 0 : index);
    }

    private async void OnEnableLookupClicked(object? sender, EventArgs e)
    {
        _settings.OnlineArtistInfo = true;
        LookupHintCard.IsVisible = false;
        await LoadArtistInfoAsync();
    }
}
