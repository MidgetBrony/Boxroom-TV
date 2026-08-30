# Boxroom-TV 3

Boxroom-TV turns BOXROOM's flatscreen TVs, CRTs, monitors, and Modern Tech TV into video players. Movies and TV seasons are first-class BOXROOM media powered by BR-MediaAPI: they have their own cases, shelves, inspector entry, save identity, and a Movies Box in the furniture catalogue.

## Requirements

- BOXROOM with MelonLoader
- `BR_MediaAPI.dll` and its `brmediaapi_assets` bundle
- `ModsPanel.dll`
- `Boxroom_TV.dll`
- `LibVLCSharp.dll`, the VLC Unity native bridge, and the LibVLC 4 Windows runtime supplied by the Boxroom-TV manifest

LocalWorkshop is not required by the current release because Boxroom-TV 3 does not ship custom TV furniture. It is the intended catalogue SDK if custom TV placeables are added later.

## Movie library

Choose **Movie Library Location** in ModsPanel. Put each movie or TV season in its own folder:

```text
Movies/
  Rango/
    Rango.mp4
    cover.jpg
  Smiling Friends Season 1/
    Episode 01.mp4
    Episode 02.mp4
    cover.png
```

Supported file extensions are `.mp4`, `.m4v`, `.mov`, `.webm`, `.avi`, and `.mkv`. Playback is handled directly by LibVLC rather than Unity's operating-system decoder, providing broad container and codec support without conversion.

Files are naturally sorted, so `Episode 2` comes before `Episode 10`. Boxroom-TV does not use a private metadata format. Optional titles and stable library identities come from Kodi-compatible `.nfo` files; filenames and folders remain the fallback.

### Kodi/XBMC-compatible TV layout

Boxroom-TV recognizes the common Kodi structure and creates one case per season:

```text
TV Shows/
  Adventure Time (2010)/
    tvshow.nfo
    poster.jpg
    season01-poster.jpg
    Season 01/
      Adventure Time (2010) S01E01.mkv
      Adventure Time (2010) S01E02.mkv
    Season 02/
      Adventure Time (2010) S02E01.mkv
```

Those cases are titled `Adventure Time: S01` and `Adventure Time: S02`. The scanner understands `Season 1`, `Season 01`, `Series 1`, and `S01` folder names. It also groups files named with Kodi's recommended `S01E01` pattern when all episodes are stored directly in the show folder. Specials use `S00`.

The show title and default `<uniqueid>` are read from `tvshow.nfo` when present; otherwise the show folder name and relative path are used. Season artwork follows Kodi names such as `season01-poster.jpg`, then falls back to artwork in the season folder and finally the show's `poster.jpg`.

Movies support Kodi's recommended `<VideoFileName>.nfo` form and the alternative `movie.nfo`. Boxroom-TV reads `<title>` and the default `<uniqueid>` from these files. Playback ordering remains filename-based, so episode files should use `S01E01`, and multipart movies should use Kodi-style `part1`, `part2`, `cd1`, or `cd2` names.

Once refreshed, place a **Movies Box** and take cases from it. Hold a movie case and use it on a supported TV. Use an empty hand on the TV—or press `T` while looking at it—to open the controller-friendly remote.

Playback position, power, volume, brightness, loop state, and the current file are stored in `UserData/Boxroom-TV/TVState.json`.

The TV remote also accepts direct HTTP video links, YouTube pages, and Twitch channels, clips, or VODs. The packaged yt-dlp resolver converts supported webpage links into temporary streams; VLC receives video at up to 720p. YouTube's separate video and audio streams are attached together during playback.

## VLC playback

Boxroom-TV 3 uses the open-source VLC for Unity native texture bridge and LibVLCSharp. The original media file is opened immediately by LibVLC; it is not transcoded, copied, or changed. MKV, WebM, HEVC, AV1, VP9, Opus and other formats supported by the packaged LibVLC build use the same playback path. Hardware decoding is selected by LibVLC when available.

The native runtime is installed under `BOXROOM_Data/Plugins/x86_64` because Unity must discover the graphics bridge before MelonLoader initializes mods. The release manifest owns this runtime as a separately versioned dependency. Do not replace only one DLL: `VLCUnityPlugin.dll`, `LibVLCSharp.dll`, `libvlc.dll`, `libvlccore.dll`, and the `plugins` tree are a matched set.

## Credits and licences

Video playback is made possible by the contributors to [VLC and LibVLC](https://www.videolan.org/), [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp), and [VLC for Unity](https://github.com/videolan/vlc-unity). Boxroom-TV builds the VLC for Unity bridge from its published source with the trial/watermark option disabled; no commercial Videolabs binary is redistributed.

These components are distributed under the LGPL 2.1-or-later terms described in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Online page resolution uses [yt-dlp](https://github.com/yt-dlp/yt-dlp) under The Unlicense and its bundled third-party licences. Release archives retain all notices, licence texts, source URLs, exact source commits, and the ability to replace the dynamically loaded LGPL libraries.

## Building

Copy `Directory.Build.user.props.example` to the ignored `Directory.Build.user.props` and set `GamePath`, set `BOXROOM_GAME_PATH`, or pass `-p:GamePath=...`.

```text
dotnet restore Boxroom-TV.csproj
dotnet build Boxroom-TV.csproj -c Release --no-restore -p:DeployToGame=false
```

The Windows native bridge is built from the sibling `vlc-unity` repository with Meson and LLVM/MinGW. LibVLCSharp is built from its `master` branch for `netstandard2.0` with `Unity=true`. `Directory.Build.user.props` may override `VlcUnityRepo` and `LibVLCSharpRepo` when those repositories are elsewhere.

Deployment is opt-in with `-p:DeployToGame=true`. It deploys the managed mods plus the matched VLC runtime and native Unity bridge. A successful build validates compilation and file deployment only; native plugin loading, rendered video, audio, controls, and save restoration still require an in-game test.

## Migration from Boxroom-TV 1.x

The old mod registered movies as fake Steam games with negative app IDs and patched the Games Box. Version 3 removes that design entirely. Move folders from the old `Mods/Boxroom-TV/VideoLibrary` directory to the selected Movie Library Location. Old placed fake-game cases and `VideoAppIds.json` are not used by version 3; take replacement cases from the new Movies Box.
