using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// La cola de reproduccion: canciones, orden (normal o barajado), posicion, aleatorio y repeticion.
/// Antes vivia repartida por MusicService; aqui se prueba sin reproductor. El servicio solo carga la
/// pista que diga <see cref="Current"/>.
/// </summary>
public sealed class PlaybackQueue
{
    private readonly Random _random;
    private List<Song> _songs = [];
    private List<int> _order = [];
    private int _orderIndex = -1;

    public PlaybackQueue(Random? random = null) => _random = random ?? Random.Shared;

    public bool Shuffle { get; private set; }

    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    public IReadOnlyList<Song> Songs => _songs;

    public bool IsEmpty => _order.Count == 0;

    public Song? Current => _orderIndex >= 0 && _orderIndex < _order.Count ? _songs[_order[_orderIndex]] : null;

    /// <summary>Posicion de la cancion actual en la cola tal cual (no en el orden barajado), o -1.</summary>
    public int QueueIndex => _orderIndex >= 0 && _orderIndex < _order.Count ? _order[_orderIndex] : -1;

    /// <summary>Carga una cola nueva empezando por <paramref name="index"/> (acotado a la cola).</summary>
    public void Load(IReadOnlyList<Song> songs, int index)
    {
        _songs = songs.ToList();
        BuildOrder(Math.Clamp(index, 0, Math.Max(_songs.Count - 1, 0)));
    }

    /// <summary>Cambia el modo aleatorio sin cortar lo que suena: la actual se queda donde esta.</summary>
    /// <returns>false si ya estaba asi.</returns>
    public bool SetShuffle(bool enabled)
    {
        if (Shuffle == enabled)
            return false;

        Shuffle = enabled;
        var current = QueueIndex;
        BuildOrder(current < 0 ? 0 : current);
        return true;
    }

    /// <summary>Retrocede una; false si la cola esta vacia.</summary>
    public bool MovePrevious()
    {
        if (_order.Count == 0)
            return false;
        _orderIndex = QueueOrder.Previous(_orderIndex, _order.Count);
        return true;
    }

    /// <summary>
    /// Avanza segun la repeticion. false si no hay siguiente (fin de la cola sin repetir): entonces
    /// se para, no se vuelve a empezar en silencio.
    /// </summary>
    public bool MoveNext(bool userRequested)
    {
        if (_order.Count == 0)
            return false;

        var next = QueueOrder.Next(_orderIndex, _order.Count, Repeat, userRequested);
        if (next is null)
            return false;

        _orderIndex = next.Value;
        return true;
    }

    /// <summary>Se deja de reproducir: no hay cancion actual, pero la cola se conserva.</summary>
    public void Stop() => _orderIndex = -1;

    private void BuildOrder(int startAt) =>
        (_order, _orderIndex) = QueueOrder.Build(_songs.Count, startAt, Shuffle, _random);
}
