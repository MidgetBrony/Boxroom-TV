# Boxroom-TV 4.0.4

- Restore screen brightness control on native interactive TV materials by binding video to base and emission textures and enabling emission where supported.
- Rename the remote control to Picture brightness and decouple it from the ambient back-reflection glow.
- Apply a picture-exposure curve so the upper end visibly overexposes the image instead of only increasing reflected light.
- Keep emission texture mapping aligned with the video, including externally supplied textures and vertical flipping.
- Preserve the legacy screen shader's emission strength control.

Validation: local Release build; in-game brightness and texture-orientation verification remains pending.
