using MusicPlayer.Models;
using MusicPlayer.Pages;
using MusicPlayer.Services;

namespace MusicPlayer.Helpers;

/// <summary>
/// Menu de acciones de una cancion. Vive en un solo sitio porque aparece en tres listas
/// (biblioteca, grupo y lista de reproduccion) y tienen que ofrecer exactamente lo mismo.
/// </summary>
internal static class SongMenu
{
    /// <param name="play">Que hacer al elegir «Reproducir»; cada lista arma su propia cola.</param>
    /// <param name="removeFromPlaylistId">
    /// Si se abre desde una lista de reproduccion, su identificador: entonces se ofrece tambien
    /// quitar la cancion de esa lista, que no es lo mismo que borrarla del dispositivo.
    /// </param>
    public static async Task ShowAsync(Page page, Song song, Action play, string? removeFromPlaylistId = null)
    {
        var localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        var playlists = ServiceHelper.GetRequiredService<IPlaylistService>();

        var playText = localization["ActionPlay"];
        var addText = localization["ActionAddToPlaylist"];
        var artistText = localization["ActionGoToArtist"];
        var infoText = localization["ActionSongInfo"];
        var editText = localization["ActionEditTags"];
        var removeText = localization["RemoveFromPlaylist"];
        var deleteText = localization["ActionDelete"];

        string[] options = removeFromPlaylistId is null
            ? [playText, addText, artistText, infoText, editText, deleteText]
            : [playText, addText, artistText, infoText, editText, removeText, deleteText];

        var choice = await SocShared.ModernDialog.ActionSheetAsync(page,
            localization["SongActionsTitle"], localization["Cancel"], options);

        if (choice is null)
            return;

        if (choice == playText)
        {
            play();
        }
        else if (choice == addText)
        {
            await page.Navigation.PushModalAsync(new PlaylistPickerPage(song));
        }
        else if (choice == artistText)
        {
            var name = song.ResolveGroupName(preferComposer: false);
            if (name.Length > 0)
                await Shell.Current.GoToAsync(nameof(ArtistPage),
                    new Dictionary<string, object> { [ArtistPage.NameParameter] = name });
        }
        else if (choice == infoText)
        {
            await page.Navigation.PushModalAsync(new SongInfoPage(song));
        }
        else if (choice == editText)
        {
            await page.Navigation.PushModalAsync(new SongEditPage(song));
        }
        else if (choice == removeText && removeFromPlaylistId is not null)
        {
            playlists.RemoveSong(removeFromPlaylistId, song.Id);
        }
        else if (choice == deleteText)
        {
            await DeleteAsync(page, song);
        }
    }

    /// <summary>
    /// Borra la cancion del dispositivo previa confirmacion. En Android 11+ el sistema vuelve a
    /// pedir confirmacion por su cuenta: se pregunta dos veces a proposito, porque no hay vuelta atras.
    /// </summary>
    public static async Task DeleteAsync(Page page, Song song)
    {
        var localization = ServiceHelper.GetRequiredService<ILocalizationService>();
        var library = ServiceHelper.GetRequiredService<IMusicLibraryService>();
        var playlists = ServiceHelper.GetRequiredService<IPlaylistService>();
        var toast = ServiceHelper.GetRequiredService<IToastService>();

        var confirmed = await SocShared.ModernDialog.AlertAsync(page,
            localization["DeleteSongTitle"],
            localization.Format("DeleteSongMessage", song.Title),
            localization["Delete"], localization["Cancel"]);

        if (!confirmed)
            return;

        var outcome = await library.DeleteAsync(song);
        switch (outcome)
        {
            case DeleteOutcome.Deleted:
                playlists.RemoveSongEverywhere(song.Id);
                toast.Show(localization["SongDeleted"]);
                break;

            case DeleteOutcome.Cancelled:
                toast.Show(localization["DeleteCancelled"]);
                break;

            default:
                toast.Show(localization["DeleteFailed"]);
                break;
        }
    }
}
