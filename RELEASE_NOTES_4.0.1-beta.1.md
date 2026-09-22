# Boxroom-TV 4.0.1 Beta 1

This beta merges Boxroom-TV with BOXROOM's new native Video system while keeping the richer library and playback features.

- Uses BOXROOM's native type-2 Video cases, Video Container, shelves, inspector, art editing, and save identity.
- Recursively discovers local video files and reads companion NFO metadata and artwork.
- Retains URL/remote playback through VLC, with selectable audio and subtitle tracks.
- Migrates legacy type-1200 references, loose cases, shelf entries, and the old Movies Box to native Video equivalents.
- Prevents BOXROOM's native `Interactable_TV` screenshot-cycle fallback from painting game artwork over an active VLC video.
- Reacquires the renderer's live screen material if `GameImagePainter` replaces it, then reapplies the VLC texture.
- Keeps native screenshot cycling available whenever no VLC source is loaded.

The merged native-video path has passed build and package validation. VLC video decoding and FMOD audio were confirmed in-game; the screen-material ownership fix is shipping in Beta for final in-game visual confirmation before Stable.
