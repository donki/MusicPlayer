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
    /// <summary>Favoritas en la raiz del coche: es una lista mas, pero merece estar a un toque.</summary>
    public const string FavoritesId = "playlist|" + Playlist.FavoritesId;
    private const string ArtistPrefix = "artist|";
    private const string PlaylistPrefix = "playlist|";
    private const string SongPrefix = "song|";

    private const string ChannelId = "playback";
    private const int NotificationId = 1;

    /// <summary>Android Auto no pagina: una lista enorme tarda y se corta sola. Se acota aqui.</summary>
    private const int MaxBrowsableChildren = 500;

    /// <summary>
    /// Quienes han navegado la biblioteca (Android Auto, el Asistente, el Bluetooth del coche, la
    /// interfaz del sistema…). Se apuntan al abrirles la puerta en <see cref="OnGetRoot"/> porque
    /// hacen falta despues: las imagenes se sirven desde ficheros privados de la aplicacion y hay
    /// que darle permiso de lectura sobre cada una a <b>todos</b>. Antes se guardaba solo el
    /// ultimo, y como el Bluetooth y la interfaz del sistema se conectan despues que Auto, el
    /// permiso se le acababa dando a quien no lo necesitaba y al coche no le llegaba ninguno.
    /// </summary>
    private readonly HashSet<string> _navegantes = [];

    /// <summary>Copias de las imagenes que se le pueden enseñar a otra aplicacion.</summary>
    private const string AutoArtFolder = "auto-art";

    /// <summary>Lado maximo de la caratula que se copia para el coche. La pantalla no pide mas.</summary>
    private const int AutoArtSize = 512;

    /// <summary>Albumes de los que ya se sabe que no tienen caratula, para no volver a abrirlos.</summary>
    private readonly HashSet<long> _albumesSinCaratula = [];

    /// <summary>Las acciones propias que Android Auto pinta como botones.</summary>
    private const string ShuffleAction = "com.socratic.musicplayer.SHUFFLE";
    private const string RepeatAction = "com.socratic.musicplayer.REPEAT";
    private const string FavoriteAction = "com.socratic.musicplayer.FAVORITE";

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

        // El corazon del coche y sus listas tienen que reflejar lo que se marque en el movil.
        if (ServiceHelper.GetService<IPlaylistService>() is { } playlists)
            playlists.PlaylistsChanged += OnPlaylistsChanged;

        // Los controladores conocidos que esten instalados reciben permiso sobre las imagenes
        // desde el principio, sin esperar a que naveguen la biblioteca: la interfaz del sistema
        // pinta la caratula de la notificacion leyendo la direccion de la sesion, y nunca llama
        // a OnGetRoot (se veia en el registro: "Permission Denial ... uid=1000").
        lock (_navegantes)
        {
            foreach (var paquete in MediaBrowserCallers)
            {
                if (IsPackageInstalled(paquete))
                    _navegantes.Add(paquete);
            }
        }

        PublishPlaybackState();

        if (PendingRequest is { } pending)
        {
            PendingRequest = null;
            PlayQueue(pending.Queue, pending.Index, pending.AutoPlay);
        }
    }

    private void OnPlaylistsChanged(object? sender, EventArgs e)
    {
        PublishPlaybackState();
        NotifyChildrenChanged(PlaylistsId);
        NotifyChildrenChanged(FavoritesId);
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
        if (ServiceHelper.GetService<IPlaylistService>() is { } playlists)
            playlists.PlaylistsChanged -= OnPlaylistsChanged;

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

        lock (_navegantes)
            _navegantes.Add(clientPackageName);

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

        // Se contesta desde otro hilo: preparar las caratulas de una lista larga lleva su tiempo
        // (hay que copiarlas la primera vez) y Android llama a esto desde el hilo principal.
        result.Detach();

        Task.Run(() =>
        {
            var items = new List<MediaBrowserCompat.MediaItem>();

            try
            {
                var library = ServiceHelper.GetService<IMusicLibraryService>();
                var playlists = ServiceHelper.GetService<IPlaylistService>();
                var localization = ServiceHelper.GetService<ILocalizationService>();

                if (library is not null && !library.HasScanned)
                {
                    // Android Auto puede arrancar el proceso sin que la interfaz se haya abierto
                    // nunca: en ese caso la biblioteca todavia esta vacia y hay que leerla aqui.
                    library.ScanAsync().GetAwaiter().GetResult();
                }

                items = BuildChildren(parentId, library, playlists, localization);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "The browse node {ParentId} could not be built.", parentId);
            }

            result.SendResult(new JavaList<MediaBrowserCompat.MediaItem>(items));
        });
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
            items.Add(Browsable(FavoritesId, localization?["Favorites"] ?? "Favorites",
                iconUri: IconoDeRecurso(Resource.Drawable.ic_auto_favorite_on)));
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
                    ImagenParaElCoche(artist.ImagePath)
                        ?? PrimeraCaratula(artist.Songs, library)
                        ?? IconoDeRecurso(Resource.Drawable.ic_auto_artist)));
            }

            return items;
        }

        if (parentId == PlaylistsId)
        {
            foreach (var playlist in playlists?.Playlists ?? [])
            {
                // Sin imagen el coche pinta un triangulo de aviso, como si fuera un error. Se le
                // da la caratula de la primera cancion que tenga, y si ninguna tiene, un icono.
                items.Add(Browsable(PlaylistPrefix + playlist.Id, playlist.Name,
                    SongCountText(localization, playlist.SongIds.Count),
                    playlist.IsFavorites
                        ? IconoDeRecurso(Resource.Drawable.ic_auto_favorite_on)
                        : PrimeraCaratula(library.FindByIds(playlist.SongIds), library)
                            ?? IconoDeRecurso(Resource.Drawable.ic_auto_playlist)));
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

    /// <summary>
    /// La imagen de un grupo, en una direccion que <b>otra aplicacion</b> pueda abrir.
    /// </summary>
    /// <remarks>
    /// <para>Antes se pasaba un <c>file://</c> a la foto guardada. En el movil se veia —es la misma
    /// aplicacion— y en el coche no: Android Auto corre en otro proceso y no puede leer un fichero
    /// privado nuestro. Por eso las caratulas de las canciones si salian, porque esas son
    /// <c>content://media/…</c> del sistema, que puede leer cualquiera.</para>
    ///
    /// <para>Ahora se sirve por el <c>FileProvider</c> de la aplicacion y se le da permiso de
    /// lectura a quien esta navegando. El fichero se copia antes a la carpeta de <b>cache</b>
    /// porque es la unica que el proveedor tiene declarada; la carpeta de datos, donde vive la
    /// imagen original, no esta.</para>
    /// </remarks>
    private AndroidUri? ImagenParaElCoche(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
            return null;

        try
        {
            var copia = System.IO.Path.Combine(CarpetaParaElCoche(), System.IO.Path.GetFileName(imagePath));
            if (!System.IO.File.Exists(copia) ||
                System.IO.File.GetLastWriteTimeUtc(copia) < System.IO.File.GetLastWriteTimeUtc(imagePath))
            {
                System.IO.File.Copy(imagePath, copia, overwrite: true);
            }

            return UriCompartida(copia);
        }
        catch (Exception ex)
        {
            // Sin imagen se ve el marcador generico, que es mucho mejor que quedarse sin lista.
            _logger?.LogWarning(ex, "No se pudo preparar la imagen de grupo para el coche.");
            return null;
        }
    }

    /// <summary>
    /// La caratula de una cancion, en una direccion que el coche pueda abrir.
    /// </summary>
    /// <remarks>
    /// <para>Se daba por hecho que la caratula del indice de medios
    /// (<c>content://media/external/audio/albumart/…</c>) la podia leer cualquiera, y no: desde
    /// Android 13 el proveedor de medios exige al que la abre el permiso de leer audio, y Android
    /// Auto no lo tiene. Nosotros si, asi que la abrimos aqui, la reducimos y la guardamos en la
    /// cache; de ahi se sirve como las imagenes de grupo, por el proveedor propio y con permiso
    /// dado a quien navega.</para>
    ///
    /// <para>La imagen puesta a mano manda sobre la del album, igual que en el movil.</para>
    /// </remarks>
    private AndroidUri? CaratulaParaElCoche(Song song, IMusicLibraryService library)
    {
        if (library.GetCustomArtPath(song) is { } propia)
            return ImagenParaElCoche(propia);

        if (song.AlbumId <= 0)
            return null;

        lock (_albumesSinCaratula)
        {
            if (_albumesSinCaratula.Contains(song.AlbumId))
                return null;
        }

        try
        {
            var copia = System.IO.Path.Combine(CarpetaParaElCoche(), $"album-{song.AlbumId}.jpg");
            if (!System.IO.File.Exists(copia))
            {
                using var bitmap = LoadAlbumArt(song);
                if (bitmap is null)
                {
                    lock (_albumesSinCaratula)
                        _albumesSinCaratula.Add(song.AlbumId);
                    return null;
                }

                var scale = Math.Min(1f, (float)AutoArtSize / Math.Max(bitmap.Width, bitmap.Height));
                using var scaled = scale < 1f
                    ? Bitmap.CreateScaledBitmap(bitmap, (int)(bitmap.Width * scale), (int)(bitmap.Height * scale), true)
                    : null;

                using var stream = System.IO.File.Create(copia);
                (scaled ?? bitmap).Compress(Bitmap.CompressFormat.Jpeg!, 85, stream);
            }

            return UriCompartida(copia);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo preparar la caratula del album {AlbumId} para el coche.", song.AlbumId);
            return null;
        }
    }

    /// <summary>La caratula de la primera cancion que tenga una, o <c>null</c>.</summary>
    private AndroidUri? PrimeraCaratula(IEnumerable<Song> songs, IMusicLibraryService library)
    {
        foreach (var song in songs)
        {
            if (CaratulaParaElCoche(song, library) is { } art)
                return art;
        }

        return null;
    }

    /// <summary>La foto del grupo de la cancion, si el grupo tiene una puesta o descargada.</summary>
    private AndroidUri? ImagenDelGrupo(Song song, IMusicLibraryService library)
    {
        var name = song.ResolveGroupName(preferComposer: false);
        return name.Length > 0 ? ImagenParaElCoche(library.FindArtist(name)?.ImagePath) : null;
    }

    private static string CarpetaParaElCoche()
    {
        var carpeta = System.IO.Path.Combine(FileSystem.CacheDirectory, AutoArtFolder);
        System.IO.Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    /// <summary>Direccion del proveedor propio para un fichero de la cache, con permiso de lectura
    /// para todos los que han navegado la biblioteca.</summary>
    private AndroidUri? UriCompartida(string path)
    {
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            this, $"{PackageName}.fileProvider", new Java.IO.File(path));
        if (uri is null)
            return null;

        string[] navegantes;
        lock (_navegantes)
            navegantes = [.. _navegantes];

        foreach (var paquete in navegantes)
        {
            try
            {
                GrantUriPermission(paquete, uri, ActivityFlags.GrantReadUriPermission);
            }
            catch (Java.Lang.SecurityException)
            {
                // Un paquete que ya no esta o al que no se le puede dar permiso: se sigue con el resto.
            }
        }

        return uri;
    }

    /// <summary>Un icono de la propia aplicacion como direccion que el coche entiende.</summary>
    private AndroidUri? IconoDeRecurso(int drawable) =>
        AndroidUri.Parse($"android.resource://{PackageName}/{drawable}");

    private List<MediaBrowserCompat.MediaItem> Playables(
        IEnumerable<Song> songs, string contextId, IMusicLibraryService library)
    {
        var items = new List<MediaBrowserCompat.MediaItem>();

        foreach (var song in songs.Take(MaxBrowsableChildren))
        {
            // Como en el movil: su caratula, si no la foto del grupo, y si no un icono neutro
            // (sin nada, el coche pinta un triangulo de aviso).
            var art = CaratulaParaElCoche(song, library)
                ?? ImagenDelGrupo(song, library)
                ?? IconoDeRecurso(Resource.Drawable.ic_auto_song);

            var builder = new MediaDescriptionCompat.Builder()
                .SetMediaId($"{SongPrefix}{song.Id}|{contextId}")!
                .SetTitle(song.Title)!
                .SetSubtitle(song.ResolveGroupName(preferComposer: false))!
                .SetIconUri(art)!;

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
    /// Controladores de medios que pueden navegar la biblioteca, ademas de la propia aplicacion.
    /// </summary>
    /// <remarks>
    /// Android Auto proyectado desde el movil, el simulador de escritorio, el asistente y el
    /// puente de Bluetooth, que es el que usa el equipo del coche cuando no va por cable.
    /// </remarks>
    private static readonly string[] MediaBrowserCallers =
    [
        "com.google.android.projection.gearhead",
        "com.google.android.autosimulator",
        "com.google.android.carassistant",
        "com.google.android.googlequicksearchbox",
        "com.google.android.wearable.app",
        "com.android.bluetooth",
        "com.android.systemui",
    ];

    private bool IsPackageInstalled(string packageName)
    {
        try
        {
            PackageManager?.GetApplicationInfo(packageName, 0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decide si un llamante puede navegar la biblioteca.
    /// </summary>
    /// <remarks>
    /// <para><b>Por que no basta con el permiso.</b> Antes solo se aceptaba a quien tuviera
    /// concedido <c>MEDIA_CONTENT_CONTROL</c>. Suena razonable, pero <b>Android Auto no lo
    /// tiene</b>: es un permiso reservado a aplicaciones privilegiadas y Auto se instala desde
    /// Play como una mas. Comprobado en el movil —Auto instalado y sin ese permiso—, y por eso el
    /// coche no mostraba ni el icono: <c>OnGetRoot</c> devolvia null y para Auto la aplicacion
    /// sencillamente no existia.</para>
    ///
    /// <para><b>Como se valida ahora.</b> Por nombre de paquete conocido, pero exigiendo ademas
    /// que sea del sistema o que lo haya instalado Play. Un nombre de paquete a secas se lo puede
    /// poner cualquier APK de fuera; pasando por Play, no. Se mantiene el permiso como via
    /// alternativa para los controladores privilegiados de verdad.</para>
    ///
    /// <para><b>Por que no se fijan los certificados.</b> Es lo que hace el ejemplo oficial de
    /// Google, y es mas estricto, pero obliga a llevar escritas las huellas de cada controlador.
    /// Si una huella cambia o se copia mal, esto vuelve a fallar <i>en silencio</i> —exactamente el
    /// fallo que se esta arreglando— y solo se nota subiendose al coche. Se prefiere una
    /// comprobacion que no pueda quedarse obsoleta sin que nadie se entere.</para>
    /// </remarks>
    private bool IsCallerAllowed(string clientPackageName)
    {
        if (string.Equals(clientPackageName, PackageName, StringComparison.Ordinal))
            return true;

        try
        {
            if (PackageManager?.CheckPermission(
                    global::Android.Manifest.Permission.MediaContentControl,
                    clientPackageName) == Permission.Granted)
            {
                return true;
            }

            if (!MediaBrowserCallers.Contains(clientPackageName, StringComparer.Ordinal))
                return false;

            return IsSystemOrFromPlay(clientPackageName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The caller {Package} could not be verified.", clientPackageName);
            return false;
        }
    }

    /// <summary>Del sistema, o instalado desde Play. Lo que un APK de fuera no puede fingir.</summary>
    private bool IsSystemOrFromPlay(string clientPackageName)
    {
        var manager = PackageManager;
        if (manager is null)
            return false;

        var info = manager.GetApplicationInfo(clientPackageName, 0);
        if (info.Flags.HasFlag(ApplicationInfoFlags.System) ||
            info.Flags.HasFlag(ApplicationInfoFlags.UpdatedSystemApp))
        {
            return true;
        }

        // GetInstallSourceInfo es de Android 11; por debajo queda el metodo antiguo, en desuso pero
        // el unico que hay. Sin instalador conocido (carga lateral) no se acepta.
        var installer = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? manager.GetInstallSourceInfo(clientPackageName)?.InstallingPackageName
#pragma warning disable CS0618
            : manager.GetInstallerPackageName(clientPackageName);
#pragma warning restore CS0618

        return installer is "com.android.vending" or "com.google.android.feedback";
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

        if (ServiceHelper.GetService<IMusicLibraryService>() is { } library)
        {
            // La direccion, para quien sepa abrirla, y el mapa de bits, para quien no: el coche
            // lee el que le venga mejor. La sesion reduce el mapa de bits sola antes de enviarlo.
            if (CaratulaParaElCoche(song, library) is { } art)
            {
                builder.PutString(MediaMetadataCompat.MetadataKeyAlbumArtUri, art.ToString());
                builder.PutString(MediaMetadataCompat.MetadataKeyDisplayIconUri, art.ToString());
            }

            if (LoadAlbumArt(song) is { } bitmap)
            {
                builder.PutBitmap(MediaMetadataCompat.MetadataKeyAlbumArt, bitmap);
                builder.PutBitmap(MediaMetadataCompat.MetadataKeyDisplayIcon, bitmap);
            }
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
                .SetState(stateCode, (long)Position.TotalMilliseconds, IsPlaying ? 1.0f : 0f)!;

            // Aleatorio y repetir, como BOTONES.
            //
            // No basta con declarar ActionSetShuffleMode / ActionSetRepeatMode y llamar a
            // SetShuffleMode / SetRepeatMode —que ya se hacia—: eso le dice al coche en que modo
            // estamos, pero Android Auto no dibuja ningun control por ello. Los unicos botones que
            // pinta ademas de los de siempre son las ACCIONES PROPIAS de la sesion, y por eso alli
            // no habia forma de poner una lista en aleatorio ni de repetir una cancion.
            //
            // El estado se cuenta en el icono y en el rotulo, porque un boton que no dice como esta
            // obliga a probarlo para averiguarlo. Android Auto tiñe todos estos iconos de blanco
            // —comprobado en el Desktop Head Unit—, asi que el color no vale para nada: el icono de
            // "activado" lleva el glifo recortado sobre un disco lleno, que se distingue por forma.
            //
            // Orden: favorita, aleatorio, repetir. El coche los pinta de izquierda a derecha tal
            // como se añaden.
            var textos = ServiceHelper.GetService<ILocalizationService>();

            // Favorita: corazon lleno si la cancion esta en la lista, vacio si no.
            var esFavorita = Current is { } sonando &&
                (ServiceHelper.GetService<IPlaylistService>()?.IsFavorite(sonando.Id) ?? false);

            state.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
                    FavoriteAction,
                    textos?[esFavorita ? "AutoFavoriteOn" : "AutoFavoriteOff"] ?? (esFavorita ? "Favorite" : "Add to favorites"),
                    esFavorita ? Resource.Drawable.ic_auto_favorite_on : Resource.Drawable.ic_auto_favorite)
                .Build());

            state.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
                    ShuffleAction,
                    textos?[Shuffle ? "AutoShuffleOn" : "AutoShuffleOff"] ?? (Shuffle ? "Shuffle: on" : "Shuffle: off"),
                    Shuffle ? Resource.Drawable.ic_auto_shuffle_on : Resource.Drawable.ic_auto_shuffle)
                .Build());

            var (repeatIcon, repeatKey) = Repeat switch
            {
                RepeatMode.One => (Resource.Drawable.ic_auto_repeat_one_on, "AutoRepeatOne"),
                RepeatMode.All => (Resource.Drawable.ic_auto_repeat_on, "AutoRepeatAll"),
                _ => (Resource.Drawable.ic_auto_repeat, "AutoRepeatOff"),
            };

            state.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
                    RepeatAction, textos?[repeatKey] ?? repeatKey, repeatIcon)
                .Build());

            var built = state.Build();

            _session.SetPlaybackState(built);
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
        var library = ServiceHelper.GetService<IMusicLibraryService>();
        if (library is null)
            return null;

        // La imagen puesta a mano manda, como en el movil.
        if (library.GetCustomArtPath(song) is { } propia && System.IO.File.Exists(propia))
            return BitmapFactory.DecodeFile(propia);

        var art = library.GetAlbumArtUri(song);
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

        /// <summary>
        /// Los botones propios del coche. El de repetir <b>gira</b>: no repetir, la lista, esta
        /// cancion. Un boton no da para tres estados de otra manera, y es como funciona en
        /// cualquier reproductor.
        /// </summary>
        public override void OnCustomAction(string? action, Bundle? extras)
        {
            switch (action)
            {
                case ShuffleAction:
                    _service.SetShuffle(!_service.Shuffle);
                    break;

                case RepeatAction:
                    _service.SetRepeat(_service.Repeat switch
                    {
                        RepeatMode.Off => RepeatMode.All,
                        RepeatMode.All => RepeatMode.One,
                        _ => RepeatMode.Off,
                    });
                    break;

                case FavoriteAction:
                    // El cambio de lista dispara PlaylistsChanged y con el se repinta el corazon.
                    if (_service.Current is { } song)
                        ServiceHelper.GetService<IPlaylistService>()?.ToggleFavorite(song.Id);
                    break;
            }
        }

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
