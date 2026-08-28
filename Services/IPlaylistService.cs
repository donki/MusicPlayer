using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Listas de reproduccion del usuario. Se guardan en el almacenamiento propio de la aplicacion,
/// nunca fuera del dispositivo (constitucion 9).
/// </summary>
public interface IPlaylistService
{
    event EventHandler? PlaylistsChanged;

    IReadOnlyList<Playlist> Playlists { get; }

    /// <summary>Crea una lista. Devuelve <c>null</c> si ya existe una con ese nombre.</summary>
    Playlist? Create(string name);

    bool Rename(string playlistId, string name);

    void Delete(string playlistId);

    Playlist? Find(string playlistId);

    /// <summary>Listas a las que pertenece la cancion.</summary>
    IReadOnlyList<string> PlaylistIdsContaining(long songId);

    /// <summary>
    /// Deja la cancion exactamente en las listas indicadas: la anade a las que falten y la quita
    /// de las que ya no esten marcadas. Es lo que necesita un selector de varias listas.
    /// </summary>
    void SetMembership(long songId, IReadOnlyCollection<string> playlistIds);

    /// <summary>
    /// Anade las canciones a las listas indicadas <b>sin quitarlas de ninguna otra</b>. Es lo que
    /// necesita una seleccion multiple: con varias canciones no hay una pertenencia comun que
    /// editar, solo canciones que se quieren meter en unas listas.
    /// </summary>
    void AddSongs(IReadOnlyCollection<long> songIds, IReadOnlyCollection<string> playlistIds);

    void RemoveSong(string playlistId, long songId);

    void RemoveSongs(string playlistId, IReadOnlyCollection<long> songIds);

    /// <summary>Quita la cancion de todas las listas; se usa al borrarla del dispositivo.</summary>
    void RemoveSongEverywhere(long songId);

    void RemoveSongsEverywhere(IReadOnlyCollection<long> songIds);
}
