using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Orden de reproduccion de la cola: el barajado y a que posicion se salta con siguiente y
/// anterior. Es la parte del motor que no depende de Android; el servicio de reproduccion la usa y
/// se encarga del reproductor, el foco de audio y las notificaciones.
/// </summary>
public static class QueueOrder
{
    /// <summary>
    /// Orden en que se recorre una cola de <paramref name="count"/> canciones y la posicion de la
    /// que se parte. En orden normal es la propia cola y se empieza en <paramref name="startAt"/>;
    /// en aleatorio se baraja (Fisher-Yates) dejando la cancion elegida la primera, para que la
    /// reproduccion aleatoria empiece justo por lo que el usuario ha pulsado.
    /// </summary>
    public static (List<int> Order, int Index) Build(int count, int startAt, bool shuffle, Random random)
    {
        if (count <= 0)
            return ([], -1);

        var indices = Enumerable.Range(0, count).ToList();

        if (!shuffle)
            return (indices, startAt);

        for (var i = indices.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var position = indices.IndexOf(startAt);
        if (position > 0)
            (indices[0], indices[position]) = (indices[position], indices[0]);

        return (indices, 0);
    }

    /// <summary>
    /// Posicion siguiente dentro del orden, o <c>null</c> si hay que parar: fin de la cola sin
    /// repeticion y sin que el usuario lo haya pedido. Si lo pide el usuario, o con repeticion,
    /// del final se vuelve al principio.
    /// </summary>
    public static int? Next(int orderIndex, int count, RepeatMode repeat, bool userRequested)
    {
        var isLast = orderIndex >= count - 1;

        if (isLast && repeat == RepeatMode.Off && !userRequested)
            return null;

        return isLast ? 0 : orderIndex + 1;
    }

    /// <summary>Posicion anterior; desde la primera se da la vuelta a la ultima.</summary>
    public static int Previous(int orderIndex, int count) =>
        orderIndex <= 0 ? count - 1 : orderIndex - 1;
}
