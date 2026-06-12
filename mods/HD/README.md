# UAlbion HD override mod

Drop upscaled true-colour PNGs into the folders below and launch with the mod enabled:

```
UAlbion.exe -mods "Albion HD"
```

(or add `HD` to the active-mods user setting). Any file that exists here replaces the
original asset; anything missing falls through to the original Albion data, so the mod
can be filled in incrementally.

## Folders / naming

| Folder | Asset | Original ids | File name |
|---|---|---|---|
| `CombatBackgrounds/` | combat backdrop (360px wide originals) | 1-19 | `<id>.png` (e.g. `10.png`) |
| `DungeonBackgrounds/` | 3D dungeon sky | 1-3 | `<id>.png` |
| `Pictures/` | fullscreen story images | 1-45 | `<id>.png` |

The numeric id matches the leading number of the files exported by
`UAlbion.exe --dump --formats png` (e.g. `data/exported/gfx/CombatBackgrounds/10_0_0_CombatBackground.png`
→ upscale it → save as `CombatBackgrounds/10.png`).

These categories are stretch-to-fit at render time, so any source resolution works —
4x/8x upscales are fine.

## Known issue (partially resolved)

The original blocker was a buffer-math bug in `Png32Loader.Read` that threw for any
image taller than one pixel (the png32 path had only ever been exercised for writing).
Fixed — overrides now resolve through the asset pipeline (verified via
`UAlbion.exe --dump --formats png --ids "CombatBackground.5" -mods "Albion HD"`:
a 1440×768 override wins over the 360×192 original, and falls back correctly without
the mod). NB `--ids` needs full enum names (`CombatBackground.5`), not aliases.

Still open: in-game display of the 32-bit override through the UI sprite path hasn't
been confirmed visually yet — the combat backdrop showed the original in a quick test.
Needs a check of whether the sprite batch renders `IReadOnlyTexture<uint>` textures and
whether the in-game load takes a different branch from the dump path.

## Current limitation

Fixed-grid assets (2D map tiles, 3D wall/floor texture arrays, item/UI icons, character
sprites) treat pixel size as logical size; higher-resolution replacements would break
layouts and texture atlases. Extending HD support to those requires a per-asset
logical-scale property in the asset pipeline (planned follow-up).
