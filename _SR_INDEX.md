# SR (Static Recompilation) Repository — Index

> Maintained at `F:\Dev\albion\SR\`. M-HT's project that statically recompiles the
> original 1996 Albion DOS EXE into native binaries for modern OSes.

## Critical caveat — game logic source is NOT in this repo

SR's repo contains the **lifting infrastructure** (`SR.exe` configs, build scripts,
hand-written C wrappers for DOS APIs / sound / graphics / file I/O). The actual
*lifted assembly* — i.e. translated combat / magic / AI / map-event code from
`MAIN.EXE` — is **produced as a build artifact**, not committed.

To get the lifted source we need one of:

1. Run M-HT's `SR.exe` lifter on `F:\Dev\albion\Albion\MAIN.EXE` to produce
   `Albion-main.asm`. SR.exe itself doesn't appear in this checkout — would need
   to download / build it.
2. **Use a standalone disassembler on `MAIN.EXE`**:
   - `MAIN.EXE` is "MS-DOS LE" format (1.1 MB, 32-bit protected mode, DOS4GW).
   - Ghidra (free, Java) handles LE EXEs.
   - IDA Free, radare2 / Cutter also work.
   - **Currently no disassembler is installed on this machine** — `winget v1.28.240`
     is available but `ghidra`, `ida`, `radare2`, `objdump` are not in PATH.
3. Use the **freealbion wiki** (https://github.com/freealbion/freealbion/wiki) —
   it documents file formats well, less so behavior. UAlbion already absorbs
   most of this.

## Plan implication

Phases 2 / 3 / 4 (combat math / magic effects / AI) of `_SESSION_STATUS.md`
**cannot rely on grepping SR source for the answer**. They need:
- A disassembler set up against `MAIN.EXE`, OR
- Behaviour observation against the running original game (e.g. SR-Main.exe
  with frame-stepping + screenshot diff), OR
- An existing community-decoded reference (need to check freealbion + AlbLib).

Phase 1 (triage `RemainingUnknowns.txt`) can still proceed using format-only
analysis + freealbion wiki + observation.

## What IS in SR-Main (hand-written glue, 81 files)

These are M-HT's hand-written wrappers around the lifted DOS code — they implement
the system interfaces that the lifted code expects (DOS APIs, BBOPM graphics,
AIL audio, BBDOS file I/O, music drivers etc). The actual game-logic functions
called *into* this code are inside the lifted assembly.

### Biggest C files (LOC)

| File | LOC | Purpose |
|---|---|---|
| `Albion-3dengine.c` | 4910 | 3D rendering glue: BBOPM-to-3D bridge, polygon raster paths, wall/floor/object scan converters. **Hand-written** wrapper around lifted-asm 3D primitives. |
| `Albion-screenshot.c` | 2288 | Screenshot capture pipeline (LBM/BMP/TGA/PNG, padded variants, original Albion-format). |
| `main.c` | 1769 | Process entry: argument parsing, video-mode init, SDL window creation, event loop wrapper. |
| `smack.c` | 1752 | Smacker video codec (intro / credits FMV decoder). |
| `Game_memory.c` | 1523 | Virtual memory / DOS-segment emulation for the lifted code. |
| `Albion-music-midiplugin.c` | 1266 | MIDI plugin v1 dispatcher. |
| `Albion-music-midiplugin2.c` | 1157 | MIDI plugin v2 (newer NativeWindows / ADLMIDI / BASSMIDI path). |
| `Albion-smk.c` | 1132 | Smacker (FMV) higher-level wrapper. |
| `printf_x86.c` | 1020 | Watcom printf reimplementation (the original uses 32-bit Watcom calling convention). |
| `Albion-music.c` |  971 | Top-level music dispatcher / SBLASTER / SBPRO / GUS detection. |
| `Albion-BBDOS.c` |  841 | Blue Byte DOS file-I/O library reimpl (path translation, file handles). |
| `Albion-proc-vfs.c` |  815 | Virtual file system for game data access from lifted code. |
| `Albion-music-xmiplayer.c` |  809 | XMI (Extended MIDI) format player. |
| `Albion-sound.c` |  789 | Digital sound playback wrapper. |
| `virtualfs.c` |  745 | Generic VFS layer. |
| `Albion-proc-events.c` |  728 | **Process events** — input dispatch, keyboard/mouse to lifted-game events. **Not gameplay event handling.** |
| `Albion-BBOPM.c` |  619 | Blue Byte Object/Pixel Map graphics primitives (line, blit, fill). |
| `Albion-int2.c` |  530 | DOS interrupt handler set 2 (graphics interrupts). |
| `Game_config.c` |  522 | Configuration file (Albion.cfg) reader. |

### Architecture-specific (`x64/`, `x86/`, `arm/`, `llasm/`)

Glue assembly for the lifted code's interface to each host architecture.
`x86/Albion-3dengineprocs.asm` (111 LOC) and `x86/Game-asm.asm` (415 LOC) are
the largest — they expose hand-written 3D-engine primitives as labels the lifted
code calls into. These are *interfaces*, not game logic.

## What IS in SR-games/Albion/SR (lifter config)

Configuration that drives `SR.exe MAIN.EXE Albion-main.asm`:
- `bssborder.csv` — memory layout
- `*.sci` files — instruction-class hints (which bytes are code vs data)
- `build-{x64,x86,arm,llasm}.sh` — per-arch build pipelines
- `compact_source*.py`, `repair_short_jumps.py` — post-processing of the lifted .asm

No lifted source files are committed.

## What IS in SR/SR (the lifter source)

`SR/SR/SR_full.c` + `SR_basic.c` + `SR_full_arm*.c` + `SR_full_llasm.c` —
M-HT's lifter implementation itself. We could potentially BUILD this to get
`SR.exe`, then run `./SR.exe MAIN.EXE Albion-main.asm` to produce the lifted
source. Non-trivial build setup (originally for Linux); haven't attempted.

## Useful files when we DO have lifted code

After lifting, `Albion-main.asm` will contain everything but with names like
`seg000_xxxx` and `loc_yyyyy` rather than `combat_round`. Cross-referencing
against UAlbion's already-decoded data structures (e.g. `MapNpc` is 10 bytes
on disk → find `mov` instructions reading 10-byte chunks → that's likely the
NPC update loop) is the practical entry point.

## Where to look for each subsystem (once we have lifted source)

| Subsystem | Likely entry point hint |
|---|---|
| Combat round | Function called when "Start Round" button clicked; calls into per-mob action resolver |
| Spell cast | Function dispatched via `(school, spell_number)` indirect call |
| NPC schedule | Slow-clock tick handler that walks `MapNpc.Waypoints[]` indexed by `(GameMinutes/5) % 0x480` |
| 3D collision | Function called from movement that reads tile contents byte (`< 100` object, `>= 100` wall) |
| Save/load | `BBDOS` open/read/write for `SAVE.NNN` files — UAlbion's serdes is the spec |

## Status

- This file = Phase 0.1 deliverable.
- See `_RE_NOTES.md` (Phase 0.2) for the field-by-field decoding tracker.
- See `_SESSION_STATUS.md` for overall project progress.
