# Boxroom-TV 3.4.6

- Ignore TV interaction shortcuts while BOXROOM is in Build Mode or another non-interaction tool is active.
- Prevent a placement click from attaching Boxroom-TV components to a television preview while BOXROOM is constructing it.
- Retain the 3.4.5 change that delays VLC/FMOD initialization until playback is requested.

This fixes the reported crash when placing a television and keeps normal placed-TV interaction unchanged.
