using Android.Content;
using AndroidX.Core.Content;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="IPlaybackService"/>
/// <remarks>
/// Puente entre la interfaz y <see cref="MusicService"/>. No reproduce nada por su cuenta: todo
/// pasa por el servicio, que es el mismo que atiende a Android Auto. Asi no hay dos reproductores
/// que puedan discrepar sobre que esta sonando.
/// </remarks>
public sealed class PlaybackService : IPlaybackService
{
    private readonly ISettingsService _settings;

    public PlaybackService(ISettingsService settings)
    {
        _settings = settings;

        // El evento es estatico porque el servicio va y viene: la interfaz sigue enterandose de los
        // cambios aunque el servicio se haya destruido y vuelto a crear entre medias.
        MusicService.StateChanged += OnServiceStateChanged;
    }

    public event EventHandler? StateChanged;

    public Song? Current => MusicService.Instance?.Current;

    public IReadOnlyList<Song> Queue => MusicService.Instance?.Queue ?? [];

    public int QueueIndex => MusicService.Instance?.QueueIndex ?? -1;

    public bool IsPlaying => MusicService.Instance?.IsPlaying ?? false;

    public TimeSpan Position => MusicService.Instance?.Position ?? TimeSpan.Zero;

    public TimeSpan Duration => MusicService.Instance?.Duration ?? TimeSpan.Zero;

    public bool Shuffle
    {
        get => MusicService.Instance?.Shuffle ?? _settings.Shuffle;
        set
        {
            if (MusicService.Instance is { } service)
                service.SetShuffle(value);
            else
                _settings.Shuffle = value;
        }
    }

    public RepeatMode Repeat
    {
        get => MusicService.Instance?.Repeat ?? (RepeatMode)_settings.RepeatMode;
        set
        {
            if (MusicService.Instance is { } service)
                service.SetRepeat(value);
            else
                _settings.RepeatMode = (int)value;
        }
    }

    public void Play(IReadOnlyList<Song> queue, int index)
    {
        if (queue.Count == 0)
            return;

        if (MusicService.Instance is { } service)
        {
            service.PlayQueue(queue, index);
            EnsureServiceStarted();
            return;
        }

        // El servicio aun no existe: se deja la orden preparada y el se encarga al arrancar.
        MusicService.PendingRequest = (queue, index, true);
        EnsureServiceStarted();
    }

    public void Prepare(IReadOnlyList<Song> queue, int index)
    {
        if (queue.Count == 0)
            return;

        // Si ya hay algo cargado no se toca: recuperar la ultima cancion no puede pisar lo que el
        // usuario este escuchando ahora mismo.
        if (MusicService.Instance is { Current: not null })
            return;

        if (MusicService.Instance is { } service)
        {
            service.PlayQueue(queue, index, autoPlay: false);
            EnsureServiceStarted();
            return;
        }

        MusicService.PendingRequest = (queue, index, false);
        EnsureServiceStarted();
    }

    public void TogglePlayPause() => MusicService.Instance?.TogglePlayPause();

    public void Next() => MusicService.Instance?.Next();

    public void Previous() => MusicService.Instance?.Previous();

    public void SeekTo(TimeSpan position) => MusicService.Instance?.SeekTo(position);

    public void Stop() => MusicService.Instance?.StopPlayback();

    private static void EnsureServiceStarted()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MusicService));
        ContextCompat.StartForegroundService(context, intent);
    }

    private void OnServiceStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(this, EventArgs.Empty));
}
