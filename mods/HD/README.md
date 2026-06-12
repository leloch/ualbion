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

## Known issue (resolved)

Two separate bugs originally blocked the override:

1. A buffer-math bug in `Png32Loader.Read` threw for any image taller than one pixel
   (the png32 path had only ever been exercised for writing). Fixed — verified via
   `UAlbion.exe --dump --formats png --ids "CombatBackground.5" -mods "Albion HD"`.
   NB `--ids` needs full enum names (`CombatBackground.5`), not aliases.

2. The "dump works but in-game shows the original" follow-up was not an asset-pipeline
   bug at all: the game and dump use the identical load path. The in-game test launched
   the game via PowerShell 5.1 `Start-Process`, which does **not** quote `ArgumentList`
   entries containing spaces — the process received `-mods Albion HD` as two tokens, so
   only `Albion` was loaded and the stray `HD` token was silently discarded.
   `CommandLineOptions` now (a) treats non-flag tokens following `-mods` as additional
   mod names, so the override loads even when a launcher strips the quoting, and
   (b) warns about any unrecognised argument instead of ignoring it silently.
   Verified in-game: with the mod, `CombatBackground.BarrelDungeon` loads 720×384
   (the override); without it, 360×192 (the original). The UI sprite path renders
   `IReadOnlyTexture<uint>` textures fine.

## Current limitation

Fixed-grid assets (2D map tiles, 3D wall/floor texture arrays, item/UI icons, character
sprites) treat pixel size as logical size; higher-resolution replacements would break
layouts and texture atlases. Extending HD support to those requires a per-asset
logical-scale property in the asset pipeline (planned follow-up).
