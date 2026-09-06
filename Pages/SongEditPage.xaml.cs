using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Pages;

/// <summary>
/// Editor de las etiquetas de una cancion. Existe porque una biblioteca real llega con
/// «Unknown Artist», nombres mal escritos y albumes partidos por una tilde: corregirlos es lo que
/// hace que la busqueda y la agrupacion por grupo o compositor acierten.
/// </summary>
public partial class SongEditPage : ContentPage
{
    private readonly IMusicLibraryService _library;
    private readonly ISongTagsService _tags;
    private readonly ILocalizationService _localization;
    private readonly IToastService _toast;
    private readonly ISongLookupService _lookup;
    private readonly ICustomArtService _customArt;
    private readonly Song _song;

    public SongEditPage(Song song)
    {
        InitializeComponent();

        _song = song;
        _library = ServiceHelper.GetRequiredService<IMusicLibraryService>();
        _tags = ServiceHelper.GetRequiredService<ISongTagsService>();
        _localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _lookup = ServiceHelper.GetRequiredService<ISongLookupService>();
        _customArt = ServiceHelper.GetRequiredService<ICustomArtService>();

        ApplyTexts();
        Fill(SongTags.From(song));

        // Solo se puede volver atras si hay algo que deshacer.
        ResetButton.IsVisible = _tags.Find(song.Id) is not null;
    }

    /// <summary>
    /// Le pone caratula a esta cancion: del aparato, de una direccion, o buscandola en Google.
    /// </summary>
    /// <remarks>
    /// Es lo que arregla una biblioteca real: canciones sueltas sin caratula, y recopilatorios
    /// donde la del album no dice nada de la cancion. La elegida manda sobre la del album.
    /// </remarks>
    private async void OnArtClicked(object? sender, EventArgs e)
    {
        var puesta = _customArt.ForSong(_song.Id) is not null;

        switch (await ArtPicker.PreguntarAsync(this, _localization, puesta))
        {
            case ArtPicker.Origen.Dispositivo:
                await PonerImagenAsync(async () =>
                {
                    var foto = await MediaPicker.Default.PickPhotoAsync();
                    return foto is null ? null : await foto.OpenReadAsync();
                });
                break;

            case ArtPicker.Origen.Direccion:
                await PonerImagenAsync(async () =>
                {
                    var url = await SocShared.ModernDialog.PromptAsync(
                        this, _localization["ArtUrlTitle"], _localization["ArtUrlMessage"],
                        _localization["Ok"], _localization["Cancel"], placeholder: "https://");

                    return string.IsNullOrWhiteSpace(url)
                        ? null
                        : new MemoryStream(await _customArt.DownloadAsync(url));
                });
                break;

            case ArtPicker.Origen.Buscar:
                // Lo que se escriba ahora en el formulario manda sobre lo que traia la cancion: es
                // lo que el usuario esta arreglando, y si no hay nada, queda el nombre del fichero.
                await ArtPicker.BuscarAsync(ArtPicker.TextoDeBusqueda(Actual()));
                break;

            case ArtPicker.Origen.Quitar:
                _customArt.ClearSong(_song.Id);
                _toast.Show(_localization["ArtSaved"]);
                break;
        }
    }

    /// <summary>La cancion con lo que hay escrito ahora mismo en el formulario.</summary>
    private Song Actual() => new SongTags(
        TitleEntry.Text ?? string.Empty,
        ArtistEntry.Text ?? string.Empty,
        AlbumArtistEntry.Text ?? string.Empty,
        AlbumEntry.Text ?? string.Empty,
        ComposerEntry.Text ?? string.Empty,
        _song.Track,
        _song.Year).ApplyTo(_song);

    private async Task PonerImagenAsync(Func<Task<Stream?>> abrir)
    {
        try
        {
            await using var imagen = await abrir();
            if (imagen is null)
                return;

            await _customArt.SetSongAsync(_song.Id, imagen);
            _toast.Show(_localization["ArtSaved"]);
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(
                this, _localization["ArtFailed"], ex.Message, _localization["Ok"]);
        }
    }

    private void ApplyTexts()
    {
        HeaderTitle.Text = _localization["EditTagsTitle"];
        HeaderSubtitle.Text = _song.Title;

        TitleCaption.Text = _localization["TagTitle"];
        ArtistCaption.Text = _localization["TagArtist"];
        AlbumArtistCaption.Text = _localization["TagAlbumArtist"];
        AlbumCaption.Text = _localization["TagAlbum"];
        ComposerCaption.Text = _localization["TagComposer"];
        TrackCaption.Text = _localization["TagTrack"];
        YearCaption.Text = _localization["TagYear"];

        ScopeHint.Text = _localization["EditTagsScope"];
        ArtButton.Text = _localization["ArtTitle"];
        LookupButton.Text = _localization["LookupInfo"];
        ResetButton.Text = _localization["EditTagsReset"];
        SaveButton.Text = _localization["Save"];
    }

    private void Fill(SongTags tags)
    {
        TitleEntry.Text = tags.Title;
        ArtistEntry.Text = tags.Artist;
        AlbumArtistEntry.Text = tags.AlbumArtist;
        AlbumEntry.Text = tags.Album;
        ComposerEntry.Text = tags.Composer;
        TrackEntry.Text = tags.Track > 0 ? tags.Track.ToString() : string.Empty;
        YearEntry.Text = tags.Year > 0 ? tags.Year.ToString() : string.Empty;
    }

    /// <summary>
    /// Lo escrito, ya limpio. El indice de medios rellena los huecos con <c>&lt;unknown&gt;</c>;
    /// aqui un campo vacio se guarda vacio, que es lo que el usuario ve y lo que quiere decir.
    /// </summary>
    private SongTags Read() => new(
        Clean(TitleEntry.Text),
        Clean(ArtistEntry.Text),
        Clean(AlbumArtistEntry.Text),
        Clean(AlbumEntry.Text),
        Clean(ComposerEntry.Text),
        Number(TrackEntry.Text),
        Number(YearEntry.Text));

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static int Number(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), out var parsed) && parsed > 0 ? parsed : 0;

    /// <summary>
    /// Vuelve a lo que dice el fichero. No basta con repintar los campos: la cancion que se recibio
    /// ya trae la correccion aplicada, asi que hay que olvidarla y volver a leer el indice.
    /// </summary>
    private async void OnResetClicked(object? sender, EventArgs e)
    {
        await _library.ResetTagsAsync(_song);
        _toast.Show(_localization["EditTagsReverted"]);
        await Navigation.PopModalAsync();
    }

    /// <summary>
    /// Busca la ficha de la cancion en internet y ofrece rellenar los campos. No guarda nada por su
    /// cuenta: se ensena lo encontrado, el usuario decide, y aun despues puede corregir a mano
    /// antes de guardar.
    /// </summary>
    private async void OnLookupClicked(object? sender, EventArgs e)
    {
        if (!_lookup.IsEnabled)
        {
            // La busqueda en linea esta apagada por defecto: se dice donde se enciende en vez de
            // dejar un boton que no hace nada.
            await SocShared.ModernDialog.AlertAsync(this, _localization["LookupInfo"],
                _localization["LookupNeedsOnline"], "OK");
            return;
        }

        var original = LookupButton.Text;
        LookupButton.IsEnabled = false;
        LookupButton.Text = _localization["LookupSearching"];

        SongLookupResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            result = await _lookup.LookupAsync(Read(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = SongLookupResult.None;
        }
        finally
        {
            LookupButton.Text = original;
            LookupButton.IsEnabled = true;
        }

        if (!result.Found)
        {
            _toast.Show(_localization["LookupNoResults"]);
            return;
        }

        var accepted = await SocShared.ModernDialog.AlertAsync(this,
            _localization["LookupFoundTitle"],
            result.Describe(key => _localization[key]),
            _localization["LookupApply"],
            _localization["Cancel"]);

        if (!accepted)
            return;

        Apply(result);
        _toast.Show(_localization["LookupApplied"]);
    }

    /// <summary>
    /// Vuelca lo encontrado en el formulario. Lo que venga vacio no pisa lo que ya hubiera escrito:
    /// una busqueda a medias no puede borrar datos buenos.
    /// </summary>
    private void Apply(SongLookupResult result)
    {
        if (result.Title.Length > 0)
            TitleEntry.Text = result.Title;
        if (result.Artist.Length > 0)
            ArtistEntry.Text = result.Artist;
        if (result.Album.Length > 0)
            AlbumEntry.Text = result.Album;
        if (result.Year > 0)
            YearEntry.Text = result.Year.ToString();
        if (result.Track > 0)
            TrackEntry.Text = result.Track.ToString();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var tags = Read();

        if (tags.Title.Length == 0)
        {
            _toast.Show(_localization["EditTagsNeedTitle"]);
            return;
        }

        SaveButton.IsEnabled = false;
        var outcome = await _library.UpdateTagsAsync(_song, tags);
        SaveButton.IsEnabled = true;

        _toast.Show(outcome == TagsOutcome.Saved
            ? _localization["EditTagsSaved"]
            : _localization["EditTagsSavedInApp"]);

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
