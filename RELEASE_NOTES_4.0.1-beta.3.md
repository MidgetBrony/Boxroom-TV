# Boxroom-TV 4.0.1 Beta 3

- Detects native BOXROOM video-capable displays through `Interactable_TV` instead of relying on a fixed list of placeable IDs.
- Adds Boxroom-TV playback support for the new Arcade Machine.
- Automatically supports future TVs, monitors, arcade cabinets, and similar displays when BOXROOM marks them as video-capable.
- Keeps the previous ID-based detection as a compatibility fallback for older supported displays.
- Includes the shared Boxroom-TV display lease used by BR-Libretro so video and emulation do not compete for the same screen.

The generic display resolver was verified against the newly extracted BOXROOM source. The Release build and deployed DLL were verified; final playback on the Arcade Machine still requires an in-game test.
