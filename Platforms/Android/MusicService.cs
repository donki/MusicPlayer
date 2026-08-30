using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using Android.Runtime;
using AndroidX.Media;
using AndroidX.Media.Session;
using Microsoft.Extensions.Logging;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;
using AndroidUri = Android.Net.Uri;
using CoreNotification = AndroidX.Core.App.NotificationCompat;
using MediaNotification = AndroidX.Media.App.NotificationCompat;

// androidx.media (MediaSessionCompat, MediaBrowserServiceCompat, MediaStyle) esta marcada como
// obsoleta en favor de Media3. Se usa a proposito: es la interfaz que Android Auto sigue exigiendo
// para exponer una biblioteca navegable, y funciona desde API 21. La migracion a Media3 queda
// registrada como mejora futura (constitucion 21), no como deuda oculta.
#pragma warning disable CS0618 // Type or member is obsolete

namespace MusicPlayer.Platforms.Android;

/// <summary>
/// Motor de reproduccion y, a la vez, el <c>MediaBrowserService</c> que ve Android Auto.
/// </summary>
/// <remarks>
/// Es deliberadamente una sola pieza. Android Auto, la notificacion, los botones del volante y la
/// interfaz de la aplicacion mandan sobre la MISMA sesion de medios, asi que hay un unico estado de
/// reproduccion: lo que se pausa en el coche queda pausado en el telefono, sin sincronizacion que
/// pueda desfasarse.
/// </remarks>
[Service(
    Exported = true,
    Enabled = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["android.media.browse.MediaBrowserService"])]
[IntentFilter([Intent.ActionMediaButton])]
public sealed class MusicService : MediaBrowserServiceCompat, AudioManager.IOnAudioFocusChangeListener
{
    // --- Identificadores del arbol que navega Android Auto ---
    public const string RootId = "root";
    public const string ArtistsId = "artists";
    public const string PlaylistsId = "playlists";
    public const string AllSongsId = "allsongs";
    private const string ArtistPrefix = "artist|";
    private const string PlaylistPrefix = "playlist|";
    private const string SongPrefix = "song|";

    private const string ChannelId = "playback";
    private const int NotificationId = 1;

    /// <summary>Android Auto no pagina: una lista enorme tarda y se corta sola. Se acota aqui.</summary>
    private const int MaxBrowsableChildren = 500;

    /// <summary>Volumen al que baja la musica cuando otra app pide foco temporal (un aviso del GPS).</summary>
    private const float DuckVolume = 0.2f;

    /// <summary>Antes de este punto, «anterior» va a la pista previa; despues, al principio de esta.</summary>
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Instancia viva del servicio. El puente <see cref="PlaybackService"/> la usa para mandar
    /// ordenes sin tener que enlazarse: es el mismo proceso.
    /// </summary>
    public static MusicService? Instance { get; private set; }

    /// <summary>Cambio de pista, de estado o de cola, venga de donde venga la orden.</summary>
    public static event EventHandler? StateChanged;

    /// <summary>
    /// Cola pedida antes de que el servicio existiera. Al pulsar una cancion con el servicio aun
    /// sin arrancar, la orden se deja aqui y el servicio la recoge en <c>OnCreate</c>.
    /// </summary>
    public static (IReadOnlyList<Song> Queue, int Index, bool AutoPlay)? PendingRequest { get; set; }

    private MediaSessionCompat? _session;
    private MediaPlayer? _player;
    private AudioManager? _audioManager;
    private AudioFocusRequestClass? _focusRequest;
    private ILogger? _logger;

    private List<Song> _queue = [];
    private List<int> _order = [];
    private int _orderIndex = -1;
    private bool _isForeground;
    private bool _wasPlayingBeforeFocusLoss;

    /// <summary>
    /// La pista se deja cargada y en pausa, sin sonar. Es lo que hace falta al abrir la aplicacion
    /// para recuperar la ultima cancion: aparece lista para darle al play, pero nadie quiere que un
    /// reproductor se ponga a sonar solo al abrirlo.
    /// </summary>
    private bool _startPaused;

    public bool Shuffle { get; private set; }

    public RepeatMode Repeat { get; private set; } = RepeatMode.Off;

    public IReadOnlyList<Song> Queue => _queue;

    public Song? Current =>
        _orderIndex >= 0 && _orderIndex < _order.Count ? _queue[_order[_orderIndex]] : null;

    public int QueueIndex => _orderIndex >= 0 && _orderIndex < _order.Count ? _order[_orderIndex] : -1;

    public bool IsPlaying
    {
        get
        {
            try
            {
                return _player?.IsPlaying == true;
            }
            catch (Java.Lang.IllegalStateException)
            {
                return false;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            try
            {
                return _player is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(_player.CurrentPosition);
            }
            catch (Java.Lang.IllegalStateException)
            {
                return TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Duration => Current?.Duration ?? TimeSpan.Zero;

    // ==================================================================================
    //  Ciclo de vida del servicio
    // ==================================================================================

    public override void OnCreate()
    {
        base.OnCreate();
        Instance = this;

        _logger = ServiceHelper.GetService<ILoggerFactory>()?.CreateLogger(nameof(MusicService));
        _audioManager = (AudioManager?)GetSystemService(AudioService);

        CreateNotificationChannel();

        _session = new MediaSessionCompat(this, nameof(MusicService));
        _session.SetCallback(new SessionCallback(this));
        _session.Active = true;
        SessionToken = _session.SessionToken;

        var settings = ServiceHelper.GetService<ISettingsService>();
        if (settings is not null)
        {
            Shuffle = settings.Shuffle;
            Repeat = (RepeatMode)settings.RepeatMode;
        }

        PublishPlaybackState();

        if (PendingRequest is { } pending)
        {
            PendingRequest = null;
            PlayQueue(pending.Queue, pending.Index, pending.AutoPlay);
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Los botones del volante y de los auriculares llegan como intents de boton de medios.
        if (_session is not null)
            MediaButtonReceiver.HandleIntent(_session, intent);

        // startForegroundService exige pasar a primer plano en 5 segundos, suene ya algo o no.
        EnterForeground();

        return StartCommandResult.Sticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Cerrar la app desde recientes con la musica parada no debe dejar el servicio colgado.
        if (!IsPlaying)
            StopSelf();

        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        AbandonAudioFocus();
        ReleasePlayer();

        _session?.SetCallback(null);
        _session?.Release();
        _session = null;

        if (ReferenceEquals(Instance, this))
            Instance = null;

        base.OnDestroy();
    }

    // ==================================================================================
    //  Arbol de navegacion de Android Auto
    // ==================================================================================

    public override BrowserRoot? OnGetRoot(string? clientPackageName, int clientUid, Bundle? rootHints)
    {
        if (clientPackageName is null || !IsCallerAllowed(clientPackageName))
        {
            _logger?.LogWarning("Browse request from {Package} was refused.", clientPackageName);
            return null;
        }

        // Pistas de presentacion: los grupos se ven mejor como rejilla de fotos y las canciones
        // como lista. Android Auto las respeta; quien no las entienda las ignora sin romperse.
        var extras = new Bundle();
        extras.PutBoolean("android.media.browse.CONTENT_STYLE_SUPPORTED", true);
        extras.PutInt("android.media.browse.CONTENT_STYLE_BROWSABLE_HINT", 2);
        extras.PutInt("android.media.browse.CONTENT_STYLE_PLAYABLE_HINT", 1);

        return new BrowserRoot(RootId, extras);
    }

    public override void OnLoadChildren(string? parentId, Result? result)
    {
        if (result is null)
            return;

        var items = new List<MediaBrowserCompat.MediaItem>();

        try
        {
            var library = ServiceHelper.GetService<IMusicLibraryService>();
            var playlists = ServiceHelper.GetService<IPlaylistService>();
            var localization = ServiceHelper.GetService<ILocalizationService>();

            if (library is not null && !library.HasScanned)
            {
                // Android Auto puede arrancar el proceso sin que la interfaz se haya abierto nunca:
                // en ese caso la biblioteca todavia esta vacia y hay que leerla aqui.
                library.ScanAsync().GetAwaiter().GetResult();
            }

            items = BuildChildren(parentId, library, playlists, localization);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "The browse node {ParentId} could not be built.", parentId);
        }

        result.SendResult(new JavaList<MediaBrowserCompat.MediaItem>(items));
    }

    private List<MediaBrowserCompat.MediaItem> BuildChildren(
        string? parentId,
        IMusicLibraryService? library,
        IPlaylistService? playlists,
        ILocalizationService? localization)
    {
        var items = new List<MediaBrowserCompat.MediaItem>();
        if (library is null || parentId is null)
            return items;

        if (parentId == RootId)
        {
            items.Add(Browsable(ArtistsId, localization?["TabArtists"] ?? "Artists"));
            items.Add(Browsable(PlaylistsId, localization?["TabPlaylists"] ?? "Playlists"));
            items.Add(Browsable(AllSongsId, localization?["TabSongs"] ?? "Songs"));
            return items;
        }

        if (parentId == ArtistsId)
        {
            foreach (var artist in library.Artists.Take(MaxBrowsableChildren))
            {
                var subtitle = SongCountText(localization, artist.SongCount);
                items.Add(Browsable(ArtistPrefix + artist.Name, artist.Name, subtitle,
                    artist.ImagePath is null ? null : AndroidUri.FromFile(new Java.IO.File(artist.ImagePath))));
            }

            return items;
        }

        if (parentId == PlaylistsId)
        {
            foreach (var playlist in playlists?.Playlists ?? [])
            {
                items.Add(Browsable(PlaylistPrefix + playlist.Id, playlist.Name,
                    SongCountText(localization, playlist.SongIds.Count)));
            }

            return items;
        }

        if (parentId == AllSongsId)
            return Playables(library.Songs.Take(MaxBrowsableChildren), AllSongsId, library);

        if (parentId.StartsWith(ArtistPrefix, StringComparison.Ordinal))
        {
            var artist = library.FindArtist(parentId[ArtistPrefix.Length..]);
            return artist is null ? items : Playables(artist.Songs, parentId, library);
        }

        if (parentId.StartsWith(PlaylistPrefix, StringComparison.Ordinal))
        {
            var playlist = playlists?.Find(parentId[PlaylistPrefix.Length..]);
            return playlist is null ? items : Playables(library.FindByIds(playlist.SongIds), parentId, library);
        }

        return items;
    }

    private List<MediaBrowserCompat.MediaItem> Playables(
        IEnumerable<Song> songs, string contextId, IMusicLibraryService library)
    {
        var items = new List<MediaBrowserCompat.MediaItem>();

        foreach (var song in songs.Take(MaxBrowsableChildren))
        {
            var art = library.GetAlbumArtUri(song);
            var builder = new MediaDescriptionCompat.Builder()
                .SetMediaId($"{SongPrefix}{song.Id}|{contextId}")!
                .SetTitle(song.Title)!
                .SetSubtitle(song.ResolveGroupName(preferComposer: false))!;

            if (art is not null)
                builder.SetIconUri(AndroidUri.Parse(art));

            items.Add(new MediaBrowserCompat.MediaItem(builder.Build()!, MediaBrowserCompat.MediaItem.FlagPlayable));
        }

        return items;
    }

    private static MediaBrowserCompat.MediaItem Browsable(
        string mediaId, string title, string? subtitle = null, AndroidUri? iconUri = null)
    {
        var builder = new MediaDescriptionCompat.Builder()
            .SetMediaId(mediaId)!
            .SetTitle(title)!;

        if (subtitle is not null)
            builder.SetSubtitle(subtitle);
        if (iconUri is not null)
            builder.SetIconUri(iconUri);

        return new MediaBrowserCompat.MediaItem(builder.Build()!, MediaBrowserCompat.MediaItem.FlagBrowsable);
    }

    private static string SongCountText(ILocalizationService? localization, int count)
    {
        if (localization is null)
            return count == 1 ? "1 song" : $"{count} songs";

        return count == 1 ? localization["SongCountOne"] : localization.Format("SongCountMany", count);
    }

    /// <summary>
    /// Solo se deja navegar la biblioteca a la propia aplicacion y a los controladores de medios
    /// del sistema (Android Auto, el asistente, la interfaz del sistema), que son los que tienen
    /// concedido <c>MEDIA_CONTENT_CONTROL</c>. Es mínimo privilegio: la lista de musica del usuario
    /// no tiene por que estar abierta a cualquier app instalada.
    /// </summary>
    private bool IsCallerAllowed(string clientPackageName)
    {
        if (string.Equals(clientPackageName, PackageName, StringComparison.Ordinal))
            return true;

        try
        {
            return PackageManager?.CheckPermission(
                global::Android.Manifest.Permission.MediaContentControl, clientPackageName) == Permission.Granted;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The caller {Package} could not be verified.", clientPackageName);
            return false;
        }
    }

    // ==================================================================================
    //  Ordenes de reproduccion
    // ==================================================================================

    public void PlayQueue(IReadOnlyList<Song> queue, int index, bool autoPlay = true)
    {
        _queue = queue.ToList();
        BuildOrder(startAt: Math.Clamp(index, 0, Math.Max(_queue.Count - 1, 0)));
        StartCurrent(autoPlay);
    }

    public void PlayFromMediaId(string? mediaId)
    {
        if (mediaId is null || !mediaId.StartsWith(SongPrefix, StringComparison.Ordinal))
            return;

        var rest = mediaId[SongPrefix.Length..];
        var separator = rest.IndexOf('|');
        if (separator <= 0 || !long.TryParse(rest[..separator], out var songId))
            return;

        var contextId = rest[(separator + 1)..];
        var library = ServiceHelper.GetService<IMusicLibraryService>();
        if (library is null)
            return;

        var queue = ResolveContextQueue(contextId, library);
        var index = queue.FindIndex(song => song.Id == songId);
        if (index < 0)
        {
            var single = library.FindById(songId);
            if (single is null)
                return;
            queue = [single];
            index = 0;
        }

        PlayQueue(queue, index);
    }

    /// <summary>Reproduce lo que mejor case con lo que el usuario ha pedido por voz.</summary>
    public void PlayFromSearch(string? query)
    {
        var library = ServiceHelper.GetService<IMusicLibraryService>();
        if (library is null || library.Songs.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(query))
        {
            PlayQueue(library.Songs, 0);
            return;
        }

        var term = query.Trim();

        var artist = library.Artists.FirstOrDefault(item =>
            item.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        if (artist is not null)
        {
            PlayQueue(artist.Songs, 0);
            return;
        }

        var matches = library.Songs
            .Where(song => song.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                        || song.Album.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        if (matches.Count > 0)
            PlayQueue(matches, 0);
    }

    private List<Song> ResolveContextQueue(string contextId, IMusicLibraryService library)
    {
        if (contextId.StartsWith(ArtistPrefix, StringComparison.Ordinal))
            return library.FindArtist(contextId[ArtistPrefix.Length..])?.Songs.ToList() ?? [];

        if (contextId.StartsWith(PlaylistPrefix, StringComparison.Ordinal))
        {
            var playlist = ServiceHelper.GetService<IPlaylistService>()?.Find(contextId[PlaylistPrefix.Length..]);
            return playlist is null ? [] : library.FindByIds(playlist.SongIds).ToList();
        }

        return library.Songs.ToList();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Resume();
    }

    public void Resume()
    {
        if (_player is null)
        {
            if (Current is not null)
                StartCurrent();
            return;
        }

        if (!RequestAudioFocus())
            return;

        try
        {
            _player.Start();
            EnterForeground();
            PublishPlaybackState();
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            _logger?.LogWarning(ex, "Playback could not be resumed.");
        }
    }

    public void Pause()
    {
        try
        {
            _player?.Pause();
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            _logger?.LogWarning(ex, "Playback could not be paused.");
        }

        LeaveForeground();
        PublishPlaybackState();
    }

    public void Next() => Advance(userRequested: true);

    public void Previous()
    {
        // Si la cancion acaba de empezar se va a la anterior; si no, se vuelve a su principio.
        if (Position > RestartThreshold)
        {
            SeekTo(TimeSpan.Zero);
            return;
        }

        if (_order.Count == 0)
            return;

        _orderIndex = _orderIndex <= 0 ? _order.Count - 1 : _orderIndex - 1;
        StartCurrent();
    }

    public void SeekTo(TimeSpan position)
    {
        try
        {
            _player?.SeekTo((int)position.TotalMilliseconds);
            PublishPlaybackState();
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            _logger?.LogWarning(ex, "The player could not seek.");
        }
    }

    public void StopPlayback()
    {
        AbandonAudioFocus();
        ReleasePlayer();
        _orderIndex = -1;
        LeaveForeground(removeNotification: true);
        PublishPlaybackState();
        StopSelf();
    }

    public void SetShuffle(bool enabled)
    {
        if (Shuffle == enabled)
            return;

        Shuffle = enabled;

        var settings = ServiceHelper.GetService<ISettingsService>();
        if (settings is not null)
            settings.Shuffle = enabled;

        // Se rehace el orden dejando la cancion actual donde esta: cambiar el modo no debe cortar
        // lo que esta sonando.
        var current = QueueIndex;
        BuildOrder(startAt: current < 0 ? 0 : current);
        PublishPlaybackState();
    }

    public void SetRepeat(RepeatMode mode)
    {
        Repeat = mode;

        var settings = ServiceHelper.GetService<ISettingsService>();
        if (settings is not null)
            settings.RepeatMode = (int)mode;

        PublishPlaybackState();
    }

    // ==================================================================================
    //  Motor
    // ==================================================================================

    private void BuildOrder(int startAt)
    {
        if (_queue.Count == 0)
        {
            _order = [];
            _orderIndex = -1;
            return;
        }

        var indices = Enumerable.Range(0, _queue.Count).ToList();

        if (Shuffle)
        {
            // Barajado de Fisher-Yates, con la pista elegida en primer lugar para que la
            // reproduccion aleatoria empiece justo por lo que el usuario ha pulsado.
            for (var i = indices.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var position = indices.IndexOf(startAt);
            if (position > 0)
                (indices[0], indices[position]) = (indices[position], indices[0]);

            _order = indices;
            _orderIndex = 0;
        }
        else
        {
            _order = indices;
            _orderIndex = startAt;
        }
    }

    private void StartCurrent(bool autoPlay = true)
    {
        var song = Current;
        if (song is null)
            return;

        // Cargando en pausa NO se pide el foco de audio: abrir Music Player no puede callar lo que
        // este sonando en otra aplicacion.
        if (autoPlay && !RequestAudioFocus())
            return;

        _startPaused = !autoPlay;

        ReleasePlayer();

        try
        {
            _player = new MediaPlayer();
            _player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetContentType(AudioContentType.Music)!
                .SetUsage(AudioUsageKind.Media)!
                .Build()!);
            _player.SetWakeMode(this, WakeLockFlags.Partial);

            _player.Prepared += OnPlayerPrepared;
            _player.Completion += OnPlayerCompletion;
            _player.Error += OnPlayerError;

            _player.SetDataSource(this, AndroidUri.Parse(song.ContentUri)!);
            _player.PrepareAsync();

            var settings = ServiceHelper.GetService<ISettingsService>();
            if (settings is not null)
                settings.LastSongId = song.Id;

            PublishMetadata(song);
            PublishPlaybackState();
        }
        catch (Exception ex) when (ex is Java.IO.IOException or Java.Lang.IllegalArgumentException or Java.Lang.SecurityException)
        {
            // Formato no soportado o fichero desaparecido: se avisa y se pasa a la siguiente en vez
            // de dejar el reproductor mudo sin explicacion (constitucion 10).
            _logger?.LogWarning(ex, "The file for song {SongId} could not be opened.", song.Id);
            ReportPlaybackFailure();
            Advance(userRequested: false);
        }
    }

    private void OnPlayerPrepared(object? sender, EventArgs e)
    {
        if (_startPaused)
        {
            // Cargada y lista, pero muda hasta que el usuario pulse play.
            _startPaused = false;
            PublishPlaybackState();
            return;
        }

        try
        {
            _player?.Start();
            EnterForeground();
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            _logger?.LogWarning(ex, "Playback could not start after preparing.");
        }

        PublishPlaybackState();
    }

    private void OnPlayerCompletion(object? sender, EventArgs e)
    {
        if (Repeat == RepeatMode.One)
        {
            SeekTo(TimeSpan.Zero);
            Resume();
            return;
        }

        Advance(userRequested: false);
    }

    private void OnPlayerError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        _logger?.LogWarning("The player reported error {What}/{Extra}.", e.What, e.Extra);
        e.Handled = true;
        ReportPlaybackFailure();
        Advance(userRequested: false);
    }

    /// <summary>Pasa a la siguiente pista respetando el modo de repeticion.</summary>
    private void Advance(bool userRequested)
    {
        if (_order.Count == 0)
            return;

        var isLast = _orderIndex >= _order.Count - 1;

        if (isLast && Repeat == RepeatMode.Off && !userRequested)
        {
            // Fin de la cola sin repeticion: se para, no se vuelve a empezar en silencio.
            Pause();
            SeekTo(TimeSpan.Zero);
            return;
        }

        _orderIndex = isLast ? 0 : _orderIndex + 1;
        StartCurrent();
    }

    private void ReleasePlayer()
    {
        if (_player is null)
            return;

        _player.Prepared -= OnPlayerPrepared;
        _player.Completion -= OnPlayerCompletion;
        _player.Error -= OnPlayerError;

        try
        {
            _player.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
            // Parar un reproductor que aun no habia empezado es normal; no hay nada que registrar.
        }

        _player.Release();
        _player.Dispose();
        _player = null;
    }

    private void ReportPlaybackFailure()
    {
        var message = ServiceHelper.GetService<ILocalizationService>()?["PlaybackFailed"];
        if (!string.IsNullOrEmpty(message))
            ServiceHelper.GetService<IToastService>()?.Show(message);
    }

    // ==================================================================================
    //  Foco de audio
    // ==================================================================================

    private bool RequestAudioFocus()
    {
        if (_audioManager is null)
            return true;

        AudioFocusRequest granted;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _focusRequest ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)!
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetContentType(AudioContentType.Music)!
                    .SetUsage(AudioUsageKind.Media)!
                    .Build()!)!
                .SetOnAudioFocusChangeListener(this)!
                .Build();

            granted = _audioManager.RequestAudioFocus(_focusRequest!);
        }
        else
        {
#pragma warning disable CA1422 // La sobrecarga moderna no existe antes de Android 8.
            granted = _audioManager.RequestAudioFocus(this, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        }

        return granted == AudioFocusRequest.Granted;
    }

    private void AbandonAudioFocus()
    {
        if (_audioManager is null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_focusRequest is not null)
                _audioManager.AbandonAudioFocusRequest(_focusRequest);
        }
        else
        {
#pragma warning disable CA1422
            _audioManager.AbandonAudioFocus(this);
#pragma warning restore CA1422
        }
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Loss:
                _wasPlayingBeforeFocusLoss = false;
                Pause();
                break;

            case AudioFocus.LossTransient:
                _wasPlayingBeforeFocusLoss = IsPlaying;
                Pause();
                break;

            case AudioFocus.LossTransientCanDuck:
                // Un aviso del navegador no tiene por que cortar la musica: basta con bajarla.
                _player?.SetVolume(DuckVolume, DuckVolume);
                break;

            case AudioFocus.Gain:
                _player?.SetVolume(1f, 1f);
                if (_wasPlayingBeforeFocusLoss)
                {
                    _wasPlayingBeforeFocusLoss = false;
                    Resume();
                }

                break;
        }
    }

    // ==================================================================================
    //  Sesion de medios y notificacion
    // ==================================================================================

    private void PublishMetadata(Song song)
    {
        if (_session is null)
            return;

        var builder = new MediaMetadataCompat.Builder()
            .PutString(MediaMetadataCompat.MetadataKeyMediaId, song.Id.ToString())!
            .PutString(MediaMetadataCompat.MetadataKeyTitle, song.Title)!
            .PutString(MediaMetadataCompat.MetadataKeyDisplayTitle, song.Title)!
            .PutString(MediaMetadataCompat.MetadataKeyArtist, song.ResolveGroupName(preferComposer: false))!
            .PutString(MediaMetadataCompat.MetadataKeyDisplaySubtitle, song.ResolveGroupName(preferComposer: false))!
            .PutString(MediaMetadataCompat.MetadataKeyAlbum, song.Album)!
            .PutLong(MediaMetadataCompat.MetadataKeyDuration, (long)song.Duration.TotalMilliseconds)!;

        var art = ServiceHelper.GetService<IMusicLibraryService>()?.GetAlbumArtUri(song);
        if (art is not null)
        {
            builder.PutString(MediaMetadataCompat.MetadataKeyAlbumArtUri, art);
            builder.PutString(MediaMetadataCompat.MetadataKeyDisplayIconUri, art);
        }

        _session.SetMetadata(builder.Build());
    }

    private void PublishPlaybackState()
    {
        if (_session is not null)
        {
            var stateCode = IsPlaying ? PlaybackStateCompat.StatePlaying
                : Current is null ? PlaybackStateCompat.StateStopped
                : PlaybackStateCompat.StatePaused;

            var state = new PlaybackStateCompat.Builder()
                .SetActions(
                    PlaybackStateCompat.ActionPlay |
                    PlaybackStateCompat.ActionPause |
                    PlaybackStateCompat.ActionPlayPause |
                    PlaybackStateCompat.ActionSkipToNext |
                    PlaybackStateCompat.ActionSkipToPrevious |
                    PlaybackStateCompat.ActionSeekTo |
                    PlaybackStateCompat.ActionStop |
                    PlaybackStateCompat.ActionPlayFromMediaId |
                    PlaybackStateCompat.ActionPlayFromSearch |
                    PlaybackStateCompat.ActionSetShuffleMode |
                    PlaybackStateCompat.ActionSetRepeatMode)!
                .SetState(stateCode, (long)Position.TotalMilliseconds, IsPlaying ? 1.0f : 0f)!
                .Build();

            _session.SetPlaybackState(state);
            _session.SetShuffleMode(Shuffle
                ? PlaybackStateCompat.ShuffleModeAll
                : PlaybackStateCompat.ShuffleModeNone);
            _session.SetRepeatMode(Repeat switch
            {
                RepeatMode.One => PlaybackStateCompat.RepeatModeOne,
                RepeatMode.All => PlaybackStateCompat.RepeatModeAll,
                _ => PlaybackStateCompat.RepeatModeNone,
            });
        }

        UpdateNotification();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
            return;

        var name = ServiceHelper.GetService<ILocalizationService>()?["NowPlayingTitle"] ?? "Now playing";
        var channel = new NotificationChannel(ChannelId, name, NotificationImportance.Low)
        {
            LockscreenVisibility = NotificationVisibility.Public,
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    private Notification? BuildNotification()
    {
        var song = Current;
        if (song is null || _session is null)
            return null;

        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var contentIntent = launchIntent is null
            ? null
            : PendingIntent.GetActivity(this, 0, launchIntent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new CoreNotification.Builder(this, ChannelId)
            .SetContentTitle(song.Title)!
            .SetContentText(song.ResolveGroupName(preferComposer: false))!
            .SetSubText(song.Album)!
            .SetSmallIcon(ResolveNotificationIcon())!
            .SetLargeIcon(LoadAlbumArt(song))!
            .SetContentIntent(contentIntent)!
            .SetVisibility(CoreNotification.VisibilityPublic)!
            .SetOnlyAlertOnce(true)!
            .SetShowWhen(false)!
            .SetDeleteIntent(MediaButtonReceiver.BuildMediaButtonPendingIntent(this, PlaybackStateCompat.ActionStop))!;

        builder.AddAction(global::Android.Resource.Drawable.IcMediaPrevious, "previous",
            MediaButtonReceiver.BuildMediaButtonPendingIntent(this, PlaybackStateCompat.ActionSkipToPrevious));

        builder.AddAction(
            IsPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay,
            "playpause",
            MediaButtonReceiver.BuildMediaButtonPendingIntent(this, PlaybackStateCompat.ActionPlayPause));

        builder.AddAction(global::Android.Resource.Drawable.IcMediaNext, "next",
            MediaButtonReceiver.BuildMediaButtonPendingIntent(this, PlaybackStateCompat.ActionSkipToNext));

        builder.SetStyle(new MediaNotification.MediaStyle()
            .SetMediaSession(_session.SessionToken)!
            .SetShowActionsInCompactView(0, 1, 2)!);

        return builder.Build();
    }

    /// <summary>
    /// Identificador del icono monocromo de la notificacion. Se resuelve por nombre para no
    /// depender de como se llame la clase de recursos generada; si faltara, se cae en el icono
    /// de reproduccion del sistema antes que dejar la notificacion sin icono (que no se muestra).
    /// </summary>
    private int ResolveNotificationIcon()
    {
        var id = Resources?.GetIdentifier("ic_notification", "drawable", PackageName) ?? 0;
        return id != 0 ? id : global::Android.Resource.Drawable.IcMediaPlay;
    }

    private Bitmap? LoadAlbumArt(Song song)
    {
        var art = ServiceHelper.GetService<IMusicLibraryService>()?.GetAlbumArtUri(song);
        if (art is null || ContentResolver is null)
            return null;

        try
        {
            using var stream = ContentResolver.OpenInputStream(AndroidUri.Parse(art)!);
            return stream is null ? null : BitmapFactory.DecodeStream(stream);
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.IO.IOException)
        {
            // Muchos albumes no tienen caratula: es lo normal, no un error.
            return null;
        }
    }

    private void UpdateNotification()
    {
        var notification = BuildNotification();
        if (notification is null)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(NotificationId, notification);
    }

    private void EnterForeground()
    {
        var notification = BuildNotification() ?? BuildPlaceholderNotification();

        if (_isForeground)
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Notify(NotificationId, notification);
            return;
        }

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, notification);

            _isForeground = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "The playback service could not enter the foreground.");
        }
    }

    private void LeaveForeground(bool removeNotification = false)
    {
        if (!_isForeground)
            return;

        // Detach deja la notificacion visible con los controles: pausar no debe hacerla desaparecer.
        StopForeground(removeNotification ? StopForegroundFlags.Remove : StopForegroundFlags.Detach);
        _isForeground = false;

        if (removeNotification)
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Cancel(NotificationId);
        }
    }

    /// <summary>
    /// Notificacion minima para cumplir el plazo de 5 segundos que da Android tras
    /// <c>startForegroundService</c> cuando todavia no hay pista preparada.
    /// </summary>
    private Notification BuildPlaceholderNotification() =>
        new CoreNotification.Builder(this, ChannelId)
            .SetContentTitle(ServiceHelper.GetService<ILocalizationService>()?["AppName"] ?? "Music Player")!
            .SetSmallIcon(ResolveNotificationIcon())!
            .SetVisibility(CoreNotification.VisibilityPublic)!
            .SetShowWhen(false)!
            .Build()!;

    // ==================================================================================
    //  Ordenes que llegan de la sesion (Android Auto, volante, auriculares, notificacion)
    // ==================================================================================

    private sealed class SessionCallback : MediaSessionCompat.Callback
    {
        private readonly MusicService _service;

        public SessionCallback(MusicService service) => _service = service;

        public override void OnPlay() => _service.Resume();

        public override void OnPause() => _service.Pause();

        public override void OnStop() => _service.StopPlayback();

        public override void OnSkipToNext() => _service.Next();

        public override void OnSkipToPrevious() => _service.Previous();

        public override void OnSeekTo(long pos) => _service.SeekTo(TimeSpan.FromMilliseconds(pos));

        public override void OnPlayFromMediaId(string? mediaId, Bundle? extras) =>
            _service.PlayFromMediaId(mediaId);

        public override void OnPlayFromSearch(string? query, Bundle? extras) =>
            _service.PlayFromSearch(query);

        public override void OnSetShuffleMode(int shuffleMode) =>
            _service.SetShuffle(shuffleMode != PlaybackStateCompat.ShuffleModeNone);

        public override void OnSetRepeatMode(int repeatMode) =>
            _service.SetRepeat(repeatMode switch
            {
                PlaybackStateCompat.RepeatModeOne => RepeatMode.One,
                PlaybackStateCompat.RepeatModeAll or PlaybackStateCompat.RepeatModeGroup => RepeatMode.All,
                _ => RepeatMode.Off,
            });
    }
}
