using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <inheritdoc cref="ISongTagsService"/>
/// <remarks>
/// Mismo planteamiento que las listas de reproduccion: un unico JSON en el almacenamiento privado,
/// indexado por el identificador del indice de medios. Se lee y se escribe de forma sincrona
/// porque el servicio de medios lo consulta desde el hilo que atiende a Android Auto.
/// </remarks>
public sealed class SongTagsService : ISongTagsService
{
    private const string FileName = "song_tags.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<SongTagsService> _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private Dictionary<long, SongTags> _tags = [];

    public SongTagsService(ILogger<SongTagsService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        Load();
    }

    public SongTags? Find(long songId)
    {
        lock (_gate)
            return _tags.GetValueOrDefault(songId);
    }

    public void Save(long songId, SongTags tags)
    {
        lock (_gate)
        {
            _tags[songId] = tags;
            Persist();
        }
    }

    public Song Apply(Song song) => Find(song.Id) is { } tags ? tags.ApplyTo(song) : song;

    public void Forget(IReadOnlyCollection<long> songIds)
    {
        if (songIds.Count == 0)
            return;

        lock (_gate)
        {
            var removed = false;
            foreach (var songId in songIds)
                removed |= _tags.Remove(songId);

            if (!removed)
                return;

            Persist();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var stored = JsonSerializer.Deserialize<Dictionary<long, SongTags>>(File.ReadAllText(_filePath));
            _tags = stored ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un fichero corrupto no puede tumbar el arranque (constitucion 9): se empieza de cero
            // y como mucho se pierden unas correcciones, no la biblioteca.
            _logger.LogError(ex, "The song tag corrections could not be read; starting without them.");
            _tags = [];
        }
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_tags, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "The song tag corrections could not be saved.");
        }
    }
}
