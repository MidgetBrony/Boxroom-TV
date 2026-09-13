# Boxroom-TV 3.4.5

- Opening an empty TV remote no longer initializes the native VLC and FMOD playback stack.
- VLC now initializes only when local or online video playback is actually requested.
- Preserve automatic resume for televisions that already have saved playback.

This reduces the native crash surface on systems where VLC initialization fails while still allowing the remote and online-video controls to open.
