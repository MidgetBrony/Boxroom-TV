# Boxroom-TV 4.0.1 Beta 2

- Updates BOXROOM's native **Display Your Videos** catalogue page while Boxroom-TV is installed.
- Documents recursive subfolder scanning, optional `cover.jpg` and NFO metadata, the native Video Container, and the correct TV/remote workflow.
- Replaces the vanilla unsupported-format warning with accurate VLC-powered format guidance.
- Shows native Video case and total video-file counts on the catalogue page.
- Prevents stale FMOD PCM from accumulating while a TV is distant, inaudible, or disabled.
- Resynchronizes VLC audio and video to the same playback timestamp when returning to audible range.
- Retains the larger PCM jitter buffer needed for smooth, clip-free playback.

The distance-return synchronization fix was confirmed in-game before this Beta was published.
