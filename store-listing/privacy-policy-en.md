# Privacy Policy — Music Player

**Last updated: 27 August 2026**

Music Player is an application by Socratic. This policy explains what data the app uses and what it
does with it. In short: **your music and your playlists never leave your device**.

## What data the app uses

| Data | What for | Where it stays |
|---|---|---|
| Audio files on the device | Playing them and grouping them by artist or composer. | On your device. Never copied or uploaded. |
| Song tags (title, artist, album, composer) | Sorting and grouping the library. | On your device. |
| Your playlists | Remembering which songs you put in each playlist. | In the app's private storage, on your device. Deleted when you uninstall the app. |
| Your preferences (language, shuffle, grouping) | Remembering how you want to use the app. | In the app's private storage. |

## What the app does not do

- **No user accounts.** No email, phone number or any personal data is requested.
- **No ads, no trackers.** No advertising or analytics SDK is included.
- **No usage data is collected.** Nobody knows what you listen to, when, or how much.
- **Your music is never uploaded anywhere.**

## Internet connections

The app connects to the internet in two cases only, and neither one sends personal data:

1. **Version check at startup.** A small file is downloaded from the project repository to find out
   whether a newer version exists. Nothing about you or your device is sent.

2. **Artist photos and biographies (optional, off by default).** Only if you turn it on in
   *Settings › Online information*. In that case **only the artist name** is sent to these public
   services, none of which requires an account or an API key:
   - **MusicBrainz** (`musicbrainz.org`), to identify the artist.
   - **Wikidata** (`wikidata.org`) and **Wikimedia Commons** (`commons.wikimedia.org`), to get the
     photo.
   - **Wikipedia** (`wikipedia.org`), to get a short summary.

   No song title, file name, device identifier or personal data is ever sent. Downloaded images are
   stored on your device and you can delete them at any time from
   *Settings › Delete downloaded images*.

   These services receive your IP address, as with any internet request. Their own privacy policies
   apply to that.

## Permissions

- **Access to audio files** (`READ_MEDIA_AUDIO`, or the storage permission on Android 12 and
  earlier): without it the app has nothing to play. Only audio files are read.
- **Storage write access** (Android 9 and earlier only): used solely to delete a song when you ask
  for it. From Android 10 onwards the deletion is confirmed by the system itself.
- **Media playback foreground service and notifications**: so the music keeps playing with the
  screen off and so the controls are shown.
- **Keep the CPU awake** (`WAKE_LOCK`): prevents playback from being cut off with the screen off.
- **Internet**: for the two uses described above.

## Android Auto

When you connect the phone to your car, Android Auto can query your library to show it on the
vehicle screen. That query happens between applications on your own device; nothing goes to the
internet. The app only allows that query from the system media controllers, not from any installed
application.

## Children

The app is not specifically directed at children and collects no data from anyone.

## Changes to this policy

If this policy changes, the new version will be published alongside the app and the date in the
header will be updated.

## Contact

**jsoladelarosa@gmail.com**
