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

    /// <summary>Lineas de resena que se ven sin desplegar: suficiente para saber de quien va.</summary>
    private const int CollapsedBioLines = 3;

    private readonly IMusicLibraryService _library;
    private readonly IPlaybackService _playback;
    private readonly IArtistInfoService _artistInfo;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;

    private readonly ObservableCollection<SongRow> _rows = [];
    private readonly SongSelection _selection;
    private ArtistGroup? _artist;
    private bool _bioExpanded;
    private string _artistName = string.Empty;

    public ArtistPage()
        : this(
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IPlaybackService>(),
            ServiceHelper.GetRequiredService<IArtistInfoService>(),
            ServiceHelper.GetRequiredService<ISettingsService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>(),
            ServiceHelper.GetRequiredService<IToastService>())
    {
    }

    public ArtistPage(
        IMusicLibraryService library,
        IPlaybackService playback,
        IArtistInfoService artistInfo,
        ISettingsService settings,
        ILocalizationService localization,
        IToastService toast)
    {
        InitializeComponent();

        _library = library;
        _playback = playback;
        _artistInfo = artistInfo;
        _settings = settings;
        _localization = localization;
        _toast = toast;

        SongsView.ItemsSource = _rows;
        _selection = new SongSelection(this, Selection, _rows);
    }

    /// <summary>
    /// La flecha atras sale del modo de seleccion antes que de la pagina: es lo que espera
    /// cualquiera que haya usado la seleccion multiple de Android.
    /// </summary>
    protected override bool OnBackButtonPressed() =>
        _selection.Exit() || base.OnBackButtonPressed();

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

        _selection.Exit();

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

        ArtistImage.Source = _library.GetArtistArt(_artist);

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

        ArtistImage.Source = _library.GetArtistArt(_artist);

        ShowDescription(info.Description);
    }

    private void ShowDescription(string? description)
    {
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        ArtistBioLabel.Text = description ?? string.Empty;
        ArtistBioLabel.IsVisible = hasDescription;
        ArtistSourceLabel.IsVisible = hasDescription || _artist?.ImagePath is not null;

        // Cada grupo empieza recogido: al cambiar de ficha no se hereda lo desplegado del anterior.
        _bioExpanded = false;
        BioToggleButton.IsVisible = hasDescription;
        ApplyBioState();
    }

    /// <summary>Despliega o recoge la resena.</summary>
    private void OnToggleBioClicked(object? sender, EventArgs e)
    {
        _bioExpanded = !_bioExpanded;
        ApplyBioState();
    }

    private void ApplyBioState()
    {
        ArtistBioLabel.MaxLines = _bioExpanded ? -1 : CollapsedBioLines;
        BioToggleButton.Source = _bioExpanded ? "ic_close.png" : "ic_more.png";
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

    private void OnSongTapped(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SongRow row)
            return;

        if (_selection.HandleTap(row))
            return;

        PlayRow(row);
    }

    private void OnSongLongPressed(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SongRow row)
            _selection.Begin(row);
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

    /// <summary>
    /// Vuelve a buscar la ficha del grupo y **actualiza la foto en pantalla**. Fuerza la consulta:
    /// si la primera vez no se encontro nada, el resultado vacio se guarda 30 dias y sin forzar el
    /// boton no haria nada (nota de autor del 2026-08-29).
    /// </summary>
    private async void OnRefreshInfoClicked(object? sender, EventArgs e)
    {
        if (_artist is null)
            return;

        if (!_artistInfo.IsEnabled)
        {
            LookupHintCard.IsVisible = true;
            _toast.Show(_localization["ArtistLookupDisabled"]);
            return;
        }

        RefreshInfoButton.IsEnabled = false;
        InfoBusy.IsRunning = true;
        InfoBusy.IsVisible = true;

        try
        {
            var info = await _artistInfo.GetAsync(_artistName, forceRefresh: true);

            _artist.ImagePath = info.ImagePath;
            _artist.Description = info.Description;

            // La imagen se reasigna siempre, tambien cuando no hay: asi no se queda la anterior.
            ArtistImage.Source = _library.GetArtistArt(_artist);
            ShowDescription(info.Description);

            _toast.Show(info.ImagePath is null && info.Description is null
                ? _localization["InfoNotFound"]
                : _localization["InfoUpdated"]);
        }
        finally
        {
            InfoBusy.IsRunning = false;
            InfoBusy.IsVisible = false;
            RefreshInfoButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Renombra el grupo en todas sus canciones. Se corrige el campo que de verdad esta dando el
    /// nombre en cada una (artista del album, artista o compositor): tocar otro dejaria la cancion
    /// agrupada donde estaba.
    /// </summary>
    private async void OnRenameGroupClicked(object? sender, EventArgs e)
    {
        if (_artist is null)
            return;

        var newName = await SocShared.ModernDialog.PromptAsync(this,
            _localization["RenameGroupTitle"], null,
            _localization["Save"], _localization["Cancel"],
            initialValue: _artistName);

        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == _artistName)
            return;

        RenameButton.IsEnabled = false;
        InfoBusy.IsRunning = true;
        InfoBusy.IsVisible = true;

        var changed = 0;
        try
        {
            var preferComposer = _settings.PreferComposer;

            foreach (var song in _artist.Songs.ToList())
            {
                var tags = SongTags.From(song);

                // Que campo estaba mandando en la agrupacion de ESTA cancion.
                SongTags updated;
                if (preferComposer && song.Composer.Length > 0)
                    updated = tags with { Composer = newName };
                else if (song.AlbumArtist.Length > 0)
                    updated = tags with { AlbumArtist = newName };
                else if (song.Artist.Length > 0)
                    updated = tags with { Artist = newName };
                else if (song.Composer.Length > 0)
                    updated = tags with { Composer = newName };
                else
                    updated = tags with { Artist = newName };

                await _library.UpdateTagsAsync(song, updated);
                changed++;
            }
        }
        finally
        {
            InfoBusy.IsRunning = false;
            InfoBusy.IsVisible = false;
            RenameButton.IsEnabled = true;
        }

        _toast.Show(_localization.Format("GroupRenamed", changed));

        // La ficha del grupo anterior ya no existe: se vuelve a la biblioteca.
        await Shell.Current.GoToAsync("//LibraryPage");
    }

    private async void OnEnableLookupClicked(object? sender, EventArgs e)
    {
        _settings.OnlineArtistInfo = true;
        LookupHintCard.IsVisible = false;
        await LoadArtistInfoAsync();
    }
}
