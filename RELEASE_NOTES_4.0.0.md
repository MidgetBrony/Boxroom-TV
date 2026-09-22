# Boxroom-TV 4.0.0

Boxroom-TV now extends BOXROOM's native Video system introduced in the September 2026 build.

- Uses native media type 2, `VideoData`, Video cases, shelves, inspector actions, art tools, and Video Container.
- Replaces the native one-level/four-format scan with recursive discovery, Kodi NFO titles and stable legacy mapping, TV-season grouping, natural episode order, and broader VLC-supported extensions.
- Routes native case, inspector, and TV playback actions through LibVLC.
- Keeps the controller-friendly remote with timeline, volume, audio-track, subtitle-track, brightness, speed, looping, power, and online URL controls.
- Keeps direct HTTP, YouTube, Twitch, yt-dlp, resume state, spatial audio, ambient glow, and synchronized-display playback.
- Migrates BR-MediaAPI Movie type 1200 references to native Video type 2, including shelf slots, loose cases, and the old Movies Box.
- Creates a one-time `RoomState.json.boxroom-tv-type1200.bak` and writes unmatched IDs to `UserData/Boxroom-TV/MigrationReport.json`.
- Imports the old Boxroom-TV movie-library setting when the native Video Library Location is empty.
- Removes BR-MediaAPI as a Boxroom-TV dependency. Other installed mods may still require it.

Build validation does not replace an in-game test of native case spawning, migrated rooms, video rendering, audio, subtitle switching, or save/reload behavior.
