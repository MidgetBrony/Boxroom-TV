# Boxroom-TV 4.0.1

This Stable release promotes the 4.0.1 Beta 4 code line.

- Uses BOXROOM's native Video media type while retaining Boxroom-TV's recursive library scanning, NFO metadata, seasons, broader format support, VLC playback, and remote controls.
- Detects native BOXROOM video-capable displays through `Interactable_TV`, including the Arcade Machine and future compatible TVs, monitors, and cabinets.
- Keeps the previous ID-based display detection as a compatibility fallback.
- Includes shared display ownership for BR-Libretro so video and emulation do not compete for the same screen.
- Uses the Beta 4 initialization path without the explicit assembly-wide `PatchAll` call.
- Includes the packaged VLC runtime, LibVLCSharp, VLC Unity plugin, and yt-dlp tool required by Boxroom-TV.

Build, package contents, checksum, and installation compatibility are release-gated separately from live in-game playback acceptance.
