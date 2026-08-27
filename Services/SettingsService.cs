namespace MusicPlayer.Services;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    private const string LanguageKey = "app_language";
    private const string OnlineArtistInfoKey = "online_artist_info";
    private const string PreferComposerKey = "prefer_composer";
    private const string LastSongKey = "last_song_id";
    private const string ShuffleKey = "shuffle";
    private const string RepeatKey = "repeat_mode";

    public string Language
    {
        get => Preferences.Get(LanguageKey, string.Empty);
        set => Preferences.Set(LanguageKey, value ?? string.Empty);
    }

    // Apagado por defecto: la consulta en linea solo se hace si el usuario la activa.
    public bool OnlineArtistInfo
    {
        get => Preferences.Get(OnlineArtistInfoKey, false);
        set => Preferences.Set(OnlineArtistInfoKey, value);
    }

    public bool PreferComposer
    {
        get => Preferences.Get(PreferComposerKey, false);
        set => Preferences.Set(PreferComposerKey, value);
    }

    public long LastSongId
    {
        get => Preferences.Get(LastSongKey, 0L);
        set => Preferences.Set(LastSongKey, value);
    }

    public bool Shuffle
    {
        get => Preferences.Get(ShuffleKey, false);
        set => Preferences.Set(ShuffleKey, value);
    }

    public int RepeatMode
    {
        get => Preferences.Get(RepeatKey, 0);
        set => Preferences.Set(RepeatKey, value);
    }
}
