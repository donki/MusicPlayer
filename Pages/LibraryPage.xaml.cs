using System.Collections.ObjectModel;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Pantalla principal: grupos, canciones y listas. El code-behind solo orquesta la interfaz y
/// delega en los servicios (constitucion 7).
/// </summary>
public partial class LibraryPage : ContentPage
{
    private enum Section
    {
        Artists,
        Songs,
        Playlists,
    }

    private readonly IMusicLibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly IPlaybackService _playback;
    private readonly IMediaAccessService _access;
    private readonly ILocalizationService _localization;
    private readonly IArtistInfoService _artistInfo;
    private readonly IToastService _toast;
    private readonly UpdateService _updates;

    private readonly ObservableCollection<ArtistRow> _artistRows = [];
    private readonly ObservableCollection<SongRow> _songRows = [];
    private readonly ObservableCollection<PlaylistRow> _playlistRows = [];

    private readonly SongSelection _selection;

    private Section _section = Section.Artists;
    private string _search = string.Empty;
    private bool _isBusy;

    /// <summary>
    /// Constructor sin parametros para la plantilla de Shell. Pide lo mismo al mismo contenedor
    /// que el constructor de inyeccion, asi que no hay dos formas de construir la pagina.
    /// </summary>
    public LibraryPage()
        : this(
            ServiceHelper.GetRequiredService<IMusicLibraryService>(),
            ServiceHelper.GetRequiredService<IPlaylistService>(),
            ServiceHelper.GetRequiredService<IPlaybackService>(),
            ServiceHelper.GetRequiredService<IMediaAccessService>(),
            ServiceHelper.GetRequiredService<ILocalizationService>(),
            ServiceHelper.GetRequiredService<IArtistInfoService>(),
            ServiceHelper.GetRequiredService<IToastService>(),
            ServiceHelper.GetRequiredService<UpdateService>())
    {
    }

    public LibraryPage(
        IMusicLibraryService library,
        IPlaylistService playlists,
        IPlaybackService playback,
        IMediaAccessService access,
        ILocalizationService localization,
        IArtistInfoService artistInfo,
        IToastService toast,
        UpdateService updates)
    {
        InitializeComponent();

        _library = library;
        _playlists = playlists;
        _playback = playback;
        _access = access;
        _localization = localization;
        _artistInfo = artistInfo;
        _toast = toast;
        _updates = updates;

        ArtistsView.ItemsSource = _artistRows;
        SongsView.ItemsSource = _songRows;
        PlaylistsView.ItemsSource = _playlistRows;

        _selection = new SongSelection(this, Selection, _songRows);
    }

    /// <summary>
    /// La flecha atras sale del modo de seleccion antes que de la pagina: es lo que espera
    /// cualquiera que haya usado la seleccion multiple de Android.
    /// </summary>
    protected override bool OnBackButtonPressed() =>
        _selection.Exit() || base.OnBackButtonPressed();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ApplyTexts();
        ApplySection();
        MiniPlayer.Start();

        _library.LibraryChanged += OnLibraryChanged;
        _playlists.PlaylistsChanged += OnPlaylistsChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        await EnsureLibraryAsync();
        await _updates.CheckAndPromptAsync(this);
    }

    protected override void OnDisappearing()
    {
        _library.LibraryChanged -= OnLibraryChanged;
        _playlists.PlaylistsChanged -= OnPlaylistsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;

        MiniPlayer.Stop();
        base.OnDisappearing();
    }

    // ==================================================================================
    //  Textos y estado de la pantalla
    // ==================================================================================

    private void ApplyTexts()
    {
        Title = _localization["LibraryTitle"];
        SearchEntry.Placeholder = _localization["SearchPlaceholder"];
        NewPlaylistButton.Text = _localization["NewPlaylist"];
        NoPlaylistsTitle.Text = _localization["NoPlaylistsTitle"];
        NoPlaylistsMessage.Text = _localization["NoPlaylistsMessage"];
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyTexts();
            RefreshRows();
        });

    private void OnLibraryChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshRows);

    private void OnPlaylistsChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshRows);

    /// <summary>
    /// Pide el permiso si hace falta y carga la biblioteca. Sin permiso no se insiste: se explica
    /// que pasa y se ofrece el camino a los ajustes del sistema.
    /// </summary>
    private async Task EnsureLibraryAsync(bool forceRescan = false)
    {
        if (_isBusy)
            return;

        if (!forceRescan && _library.HasScanned)
        {
            RefreshRows();
            return;
        }

        if (!await _access.IsGrantedAsync())
        {
            var accepted = await SocShared.ModernDialog.AlertAsync(this,
                _localization["PermissionTitle"], _localization["PermissionMessage"],
                _localization["GrantAccess"], _localization["Cancel"]);

            if (!accepted || !await _access.RequestAsync())
            {
                ShowStatus(_localization["PermissionDeniedTitle"], _localization["PermissionDeniedMessage"],
                    _localization["GrantAccess"], StatusAction.OpenSettings);
                return;
            }
        }

        _isBusy = true;
        ShowStatus(_localization["ScanningLibrary"], string.Empty, null, StatusAction.None, spinner: true);

        try
        {
            await _library.ScanAsync();
        }
        finally
        {
            _isBusy = false;
        }

        RefreshRows();
        _ = PrefetchArtistImagesAsync();
    }

    /// <summary>
    /// Rellena en segundo plano las fotos que falten, de una en una y respetando el limite de
    /// peticiones de la fuente. Solo se ejecuta si el usuario activo la busqueda en linea, y no
    /// bloquea nada: la rejilla ya esta pintada con los marcadores.
    /// </summary>
    private async Task PrefetchArtistImagesAsync()
    {
        if (!_artistInfo.IsEnabled)
            return;

        var pending = _library.Artists.Where(artist => artist.ImagePath is null).ToList();

        foreach (var artist in pending)
        {
            var info = await _artistInfo.GetAsync(artist.Name);
            if (info.ImagePath is null && info.Description is null)
                continue;

            artist.ImagePath = info.ImagePath;
            artist.Description = info.Description;

            if (info.ImagePath is not null)
                MainThread.BeginInvokeOnMainThread(() => UpdateArtistImage(artist.Name, info.ImagePath));
        }
    }

    private void UpdateArtistImage(string artistName, string imagePath)
    {
        var index = _artistRows.ToList().FindIndex(row => row.Name == artistName);
        if (index < 0)
            return;

        _artistRows[index] = new ArtistRow
        {
            Name = _artistRows[index].Name,
            Subtitle = _artistRows[index].Subtitle,
            Image = ImageSource.FromFile(imagePath),
        };
    }

    private void RefreshRows()
    {
        // Las filas se reconstruyen enteras, asi que lo que hubiera marcado ya no existe.
        _selection.Exit();

        var term = _search.Trim();

        _artistRows.Clear();
        foreach (var artist in _library.Artists)
        {
            if (term.Length > 0 && !artist.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                continue;

            _artistRows.Add(new ArtistRow
            {
                Name = artist.Name,
                Subtitle = SongCountText(artist.SongCount),
                Image = _library.GetArtistArt(artist),
            });
        }

        _songRows.Clear();
        foreach (var song in _library.Songs)
        {
            if (term.Length > 0 && !MatchesSearch(song, term))
                continue;

            _songRows.Add(BuildSongRow(song));
        }

        _playlistRows.Clear();
        foreach (var playlist in _playlists.Playlists)
        {
            if (term.Length > 0 && !playlist.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                continue;

            _playlistRows.Add(new PlaylistRow
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Subtitle = SongCountText(playlist.SongIds.Count),
            });
        }

        ApplySection();
    }

    private SongRow BuildSongRow(Song song)
    {
        var artist = song.ResolveGroupName(preferComposer: false);
        var subtitle = song.Album.Length > 0 && artist.Length > 0
            ? $"{artist} · {song.Album}"
            : artist.Length > 0 ? artist
            : song.Album.Length > 0 ? song.Album
            : _localization["UnknownArtist"];

        return new SongRow
        {
            Song = song,
            Title = song.Title.Length > 0 ? song.Title : _localization["UnknownTitle"],
            Subtitle = subtitle,
            Duration = TimeFormatter.Format(song.Duration),
            Artwork = _library.GetAlbumArt(song),
        };
    }

    private static bool MatchesSearch(Song song, string term) =>
        song.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Artist.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.AlbumArtist.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Composer.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
        song.Album.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    private string SongCountText(int count) =>
        count == 1 ? _localization["SongCountOne"] : _localization.Format("SongCountMany", count);

    // ==================================================================================
    //  Secciones
    // ==================================================================================

    private void OnArtistsTabClicked(object? sender, EventArgs e) => SelectSection(Section.Artists);

    private void OnSongsTabClicked(object? sender, EventArgs e) => SelectSection(Section.Songs);

    private void OnPlaylistsTabClicked(object? sender, EventArgs e) => SelectSection(Section.Playlists);

    private void SelectSection(Section section)
    {
        _selection.Exit();
        _section = section;
        ApplySection();
    }

    private void ApplySection()
    {
        var normal = (Style)Application.Current!.Resources["SegmentButton"];
        var selected = (Style)Application.Current!.Resources["SegmentButtonSelected"];

        ArtistsTabButton.Style = _section == Section.Artists ? selected : normal;
        SongsTabButton.Style = _section == Section.Songs ? selected : normal;
        PlaylistsTabButton.Style = _section == Section.Playlists ? selected : normal;

        var hasContent = _section switch
        {
            Section.Artists => _artistRows.Count > 0,
            Section.Songs => _songRows.Count > 0,
            _ => true,   // la seccion de listas siempre muestra el boton de crear una nueva
        };

        ArtistsView.IsVisible = _section == Section.Artists && hasContent;
        SongsView.IsVisible = _section == Section.Songs && hasContent;
        PlaylistsContainer.IsVisible = _section == Section.Playlists;

        // La seccion de listas trae su propio estado vacio: el boton de crear una lista tiene que
        // seguir a la vista aunque todavia no haya ninguna.
        PlaylistsView.IsVisible = _playlistRows.Count > 0;
        NoPlaylistsPanel.IsVisible = _playlistRows.Count == 0;

        if (hasContent)
        {
            HideStatus();
            return;
        }

        if (!_library.HasScanned)
            return;

        var searching = _search.Trim().Length > 0;
        ShowStatus(
            searching ? _localization["NoResultsTitle"] : _localization["EmptyLibraryTitle"],
            searching ? _localization["NoResultsMessage"] : _localization["EmptyLibraryMessage"],
            searching ? null : _localization["RescanLibrary"],
            searching ? StatusAction.None : StatusAction.Rescan);
    }

    // ==================================================================================
    //  Panel de estado (cargando, vacio, sin permiso)
    // ==================================================================================

    private enum StatusAction
    {
        None,
        Rescan,
        OpenSettings,
    }

    private StatusAction _statusAction = StatusAction.None;

    private void ShowStatus(string title, string message, string? actionText, StatusAction action, bool spinner = false)
    {
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        StatusMessage.IsVisible = message.Length > 0;

        StatusSpinner.IsVisible = spinner;
        StatusSpinner.IsRunning = spinner;

        _statusAction = action;
        StatusActionButton.Text = actionText ?? string.Empty;
        StatusActionButton.IsVisible = actionText is not null;

        StatusPanel.IsVisible = true;
        ArtistsView.IsVisible = false;
        SongsView.IsVisible = false;
        PlaylistsContainer.IsVisible = false;
    }

    private void HideStatus()
    {
        StatusPanel.IsVisible = false;
        StatusSpinner.IsRunning = false;
    }

    private async void OnStatusActionClicked(object? sender, EventArgs e)
    {
        switch (_statusAction)
        {
            case StatusAction.Rescan:
                await EnsureLibraryAsync(forceRescan: true);
                break;

            case StatusAction.OpenSettings:
                _access.OpenSystemSettings();
                break;
        }
    }

    private async void OnRescanClicked(object? sender, EventArgs e) =>
        await EnsureLibraryAsync(forceRescan: true);

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _search = e.NewTextValue ?? string.Empty;
        RefreshRows();
    }

    // ==================================================================================
    //  Acciones sobre los elementos
    // ==================================================================================

    private async void OnArtistTapped(object? sender, TappedEventArgs e)
    {
        if (Row<ArtistRow>(sender) is not { } row)
            return;

        await Shell.Current.GoToAsync(nameof(ArtistPage),
            new Dictionary<string, object> { [ArtistPage.NameParameter] = row.Name });
    }

    private void OnSongTapped(object? sender, EventArgs e)
    {
        if (Row<SongRow>(sender) is not { } row)
            return;

        if (_selection.HandleTap(row))
            return;

        PlayFromCurrentList(row);
    }

    private void OnSongLongPressed(object? sender, EventArgs e)
    {
        if (Row<SongRow>(sender) is { } row)
            _selection.Begin(row);
    }

    private void PlayFromCurrentList(SongRow row)
    {
        // La cola es lo que el usuario esta viendo, filtro incluido: es lo que espera al pulsar.
        var queue = _songRows.Select(item => item.Song).ToList();
        var index = queue.FindIndex(song => song.Id == row.Song.Id);
        if (index < 0)
        {
            queue = [row.Song];
            index = 0;
        }

        _playback.Play(queue, index);
    }

    private async void OnSongMenuClicked(object? sender, EventArgs e)
    {
        if (Row<SongRow>(sender) is not { } row)
            return;

        await SongMenu.ShowAsync(this, row.Song, () => PlayFromCurrentList(row));
    }

    private async void OnPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if (Row<PlaylistRow>(sender) is not { } row)
            return;

        await Shell.Current.GoToAsync(nameof(PlaylistPage),
            new Dictionary<string, object> { [PlaylistPage.IdParameter] = row.Id });
    }

    private async void OnPlaylistMenuClicked(object? sender, EventArgs e)
    {
        if (Row<PlaylistRow>(sender) is not { } row)
            return;

        var renameText = _localization["RenamePlaylistTitle"];
        var deleteText = _localization["Delete"];

        var choice = await SocShared.ModernDialog.ActionSheetAsync(this,
            _localization["PlaylistActionsTitle"], _localization["Cancel"], renameText, deleteText);

        if (choice == renameText)
        {
            var name = await SocShared.ModernDialog.PromptAsync(this,
                _localization["RenamePlaylistTitle"], null,
                _localization["Save"], _localization["Cancel"], row.Name);

            if (!string.IsNullOrWhiteSpace(name) && !_playlists.Rename(row.Id, name))
                _toast.Show(_localization["PlaylistExists"]);
        }
        else if (choice == deleteText)
        {
            var confirmed = await SocShared.ModernDialog.AlertAsync(this,
                _localization["DeletePlaylistTitle"],
                _localization.Format("DeletePlaylistMessage", row.Name),
                _localization["Delete"], _localization["Cancel"]);

            if (confirmed)
            {
                _playlists.Delete(row.Id);
                _toast.Show(_localization["PlaylistDeleted"]);
            }
        }
    }

    private async void OnNewPlaylistClicked(object? sender, EventArgs e)
    {
        var name = await SocShared.ModernDialog.PromptAsync(this,
            _localization["PlaylistNameTitle"], _localization["PlaylistNameMessage"],
            _localization["Create"], _localization["Cancel"],
            placeholder: _localization["PlaylistNamePlaceholder"]);

        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_playlists.Create(name) is null)
            _toast.Show(_localization["PlaylistExists"]);
    }

    /// <summary>Elemento asociado al control que ha disparado el evento dentro de una plantilla.</summary>
    private static T? Row<T>(object? sender) where T : class =>
        (sender as BindableObject)?.BindingContext as T;
}
