# Third-party notices

Boxroom-TV uses the following dynamically loaded open-source components for video playback:

## VLC / LibVLC

- Project: VideoLAN VLC media player and LibVLC
- Source: https://code.videolan.org/videolan/vlc
- Runtime source revision: the revision recorded by the packaged VLC 4 nightly manifest
- Licence: GNU Lesser General Public License, version 2.1 or later, with individual modules retaining their accompanying notices

## LibVLCSharp

- Project: VideoLAN LibVLCSharp
- Source: https://code.videolan.org/videolan/LibVLCSharp
- Integrated source revision: `333a98a54095c94c966c4fca117cd11cffeee919`
- Local modification: the Unity Windows loader accepts the standard `BOXROOM_Data/Plugins/x86_64` runtime layout and sets `VLC_PLUGIN_PATH` to its `plugins` child directory.
- Licence: GNU Lesser General Public License, version 2.1 or later

## VLC for Unity

- Project: VideoLAN VLC for Unity
- Source: https://github.com/videolan/vlc-unity
- Integrated source revision: `f2bbedd5bc84f3e1e979a543f4341a9b9c370dff`
- Build configuration: Windows x64, release, `watermark=false`
- Licence: GNU Lesser General Public License, version 2.1 or later

The libraries remain separate, dynamically loaded files. Recipients may replace them with compatible builds. Boxroom-TV does not apply DRM, signing restrictions, or other technical measures that prevent replacement. The complete corresponding upstream source and the small loader modification are identified above.

## yt-dlp

- Project: yt-dlp
- Source: https://github.com/yt-dlp/yt-dlp
- Packaged release: `2026.08.19`, official Windows x64 standalone executable
- Licence: The Unlicense for yt-dlp, with the standalone executable's bundled dependencies retaining their respective embedded licence notices

yt-dlp is used only to resolve supported webpage URLs into temporary video and audio stream addresses. VLC performs playback.
