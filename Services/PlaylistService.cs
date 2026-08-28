using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <inheritdoc cref="IPlaylistService"/>
/// <remarks>
/// Persistencia en un unico JSON dentro del almacenamiento privado de la aplicacion. El fichero es
/// pequeno (identificadores, no rutas), asi que se lee y se escribe de forma sincrona: el servicio
/// de medios lo consulta desde <c>OnLoadChildren</c>, que Android llama esperando una respuesta.
/// </remarks>
public sealed class PlaylistService : IPlaylistService
{
    private const string FileName = "playlists.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<PlaylistService> _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private List<Playlist> _playlists = new();

    public PlaylistService(ILogger<PlaylistService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        Load();
    }

    public event EventHandler? PlaylistsChanged;

    public IReadOnlyList<Playlist> Playlists
    {
        get
        {
            lock (_gate)
                return _playlists.ToList();
        }
    }

    public Playlist? Create(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;

        Playlist created;
        lock (_gate)
        {
            if (_playlists.Any(playlist => string.Equals(playlist.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                return null;

            created = new Playlist { Name = trimmed };
            _playlists.Add(created);
            Save();
        }

        RaiseChanged();
        return created;
    }

    public bool Rename(string playlistId, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return false;

        lock (_gate)
        {
            var playlist = _playlists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null)
                return false;

            if (_playlists.Any(item => item.Id != playlistId &&
                    string.Equals(item.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                return false;

            playlist.Name = trimmed;
            Save();
        }

        RaiseChanged();
        return true;
    }

    public void Delete(string playlistId)
    {
        lock (_gate)
        {
            if (_playlists.RemoveAll(playlist => playlist.Id == playlistId) == 0)
                return;
            Save();
        }

        RaiseChanged();
    }

    public Playlist? Find(string playlistId)
    {
        lock (_gate)
            return _playlists.FirstOrDefault(playlist => playlist.Id == playlistId);
    }

    public IReadOnlyList<string> PlaylistIdsContaining(long songId)
    {
        lock (_gate)
        {
            return _playlists
                .Where(playlist => playlist.SongIds.Contains(songId))
                .Select(playlist => playlist.Id)
                .ToList();
        }
    }

    public void SetMembership(long songId, IReadOnlyCollection<string> playlistIds)
    {
        lock (_gate)
        {
            foreach (var playlist in _playlists)
            {
                var shouldContain = playlistIds.Contains(playlist.Id);
                var doesContain = playlist.SongIds.Contains(songId);

                if (shouldContain && !doesContain)
                    playlist.SongIds.Add(songId);
                else if (!shouldContain && doesContain)
                    playlist.SongIds.Remove(songId);
            }

            Save();
        }

        RaiseChanged();
    }

    public void AddSongs(IReadOnlyCollection<long> songIds, IReadOnlyCollection<string> playlistIds)
    {
        if (songIds.Count == 0 || playlistIds.Count == 0)
            return;

        lock (_gate)
        {
            var added = false;
            foreach (var playlist in _playlists.Where(playlist => playlistIds.Contains(playlist.Id)))
            {
                foreach (var songId in songIds)
                {
                    // Una lista no repite canciones: anadir dos veces la misma no hace nada.
                    if (playlist.SongIds.Contains(songId))
                        continue;

                    playlist.SongIds.Add(songId);
                    added = true;
                }
            }

            if (!added)
                return;
            Save();
        }

        RaiseChanged();
    }

    public void RemoveSong(string playlistId, long songId) => RemoveSongs(playlistId, [songId]);

    public void RemoveSongs(string playlistId, IReadOnlyCollection<long> songIds)
    {
        if (songIds.Count == 0)
            return;

        lock (_gate)
        {
            var playlist = _playlists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null || playlist.SongIds.RemoveAll(songIds.Contains) == 0)
                return;
            Save();
        }

        RaiseChanged();
    }

    public void RemoveSongEverywhere(long songId) => RemoveSongsEverywhere([songId]);

    public void RemoveSongsEverywhere(IReadOnlyCollection<long> songIds)
    {
        if (songIds.Count == 0)
            return;

        lock (_gate)
        {
            var removed = false;
            foreach (var playlist in _playlists)
                removed |= playlist.SongIds.RemoveAll(songIds.Contains) > 0;

            if (!removed)
                return;
            Save();
        }

        RaiseChanged();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var json = File.ReadAllText(_filePath);
            var stored = JsonSerializer.Deserialize<List<Playlist>>(json);

            // Un fichero corrupto o de una version futura no puede tumbar el arranque
            // (constitucion 9): se descarta lo que no se entienda y se sigue con lo demas.
            _playlists = stored?.Where(playlist => playlist is { Name.Length: > 0 }).ToList() ?? new List<Playlist>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "The playlist file could not be read; starting with an empty list.");
            _playlists = new List<Playlist>();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_playlists, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "The playlists could not be saved.");
        }
    }

    private void RaiseChanged() => PlaylistsChanged?.Invoke(this, EventArgs.Empty);
}
