# Boxroom-TV 3.4.4

## Changes

- Adds a public synchronized temporary-playback API for other local mods.
- Uses one VLC decoder as the synchronized leader and shares its texture with
  follower displays.
- Routes VLC playback through positional FMOD audio and makes the nearest
  supported display audible.
- Preserves and restores each display's playlist, position, volume,
  brightness, loop, power, and playback state after temporary playback.
- Reinitializes the VLC decoder cleanly when playback is restored after room
  changes.

Dependencies: ModsPanel 2.6.2+, BR-MediaAPI 1.0.1+.
