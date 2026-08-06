# UAlbion 1:1 — Handover / Onboarding

> **Start here.** This is the entry point for anyone (human or AI) picking up the
> UAlbion 1:1 reimplementation effort. It maps the docs, states where things are, and
> shows the working loop that has been productive. Read this, then `_ENGINE_GOTCHAS.md`,
> then dip into the RE index as needed.

## What this project is

A faithful C#/.NET 9 remake of the 1996 MS-DOS RPG *Albion* (Blue Byte). Renderer is
Veldrid (D3D11/Vulkan/OpenGL). The goal is **1:1 behaviour with the original MAIN.EXE** —
not "close enough", byte-exact where the original's logic has been reverse-engineered.
Needs the original GOG game data layered through `mods/`.

## The doc map (read in this order)

| Doc | What it is |
|---|---|
| **`_HANDOVER.md`** (this) | Onboarding + doc map + workflow loop |
| **`_TODO_1TO1.md`** | **THE work ledger / source of truth for remaining work.** Check + update first; mark items done with commit hashes. Section 8 = deliberate deviations that must NOT be "fixed". |
| **`_ENGINE_GOTCHAS.md`** | The recurring bug-class landmines (read before touching combat/state/rendering). |
| **`_RE_INDEX.md`** | One-stop index: every RE'd mechanic → which doc/section/MAIN.EXE function has it. |
| `CLAUDE.md` | Operational quick-reference (build/test commands, harness, conventions). |
| `_RE_COMBAT.md` | Combat RE: damage/crit/XP/spells/rest/conditions + the punch-list + "Placeholder formulas". |
| `_RE_NOTES.md` | Non-combat RE: automap.c, sound id space, combat SFX, per-format field notes. |
| `_RE_5A.md`..`_RE_5D.md`, `_RE_6.md` | The batch-5/6 RE sweeps (executors, spell data, world tick, NPC constants, polish). |
| `_PROJECT_LOG.md` | Narrative engagement record (deep context, root-cause stories). |
| `_SESSION_STATUS.md` | Rolling per-session status. |

## Current state (as of 2026-06-13)

- **Build clean, ~492 tests green (7 assemblies), smoke 13/13.** Verified by audit.
- All four RE clusters (5A–5D) decoded **and applied**; the base-game playable critical
  path (combat, magic, services, rest, automap, conversations, save round-trip) is
  effectively complete and matches the RE'd formulas.
- Remaining work is small and enumerated in `_TODO_1TO1.md`: a few genuine 1:1 gaps
  (banish-spell area targeting is the only base-game player-facing combat one), RE-blocked
  polish (soul-rise anim, ambient sound, animated meshes), and test-hardening of the
  RE-applied systems. An RE batch 6 + a test batch are the active fronts.

## The working loop (what has been productive)

1. **Check `_TODO_1TO1.md`** for the next item; verify it's genuinely open by reading the
   cited code (the ledger has drifted before — trust code over comments/marks).
2. **For unknown original behaviour → reverse-engineer first** with radare2 (see the RE
   workflow in `_ENGINE_GOTCHAS.md` / `reference_radare2` memory). Mark guesses
   `// PLACEHOLDER:` naming what needs confirming; don't block on uncertainty.
3. **Implement**, matching the surrounding code's idiom.
4. **Gate every change** (non-negotiable):
   - `dotnet build src/UAlbion/UAlbion.csproj -c Release` (kill `UAlbion.exe` first — it
     locks its DLLs).
   - `dotnet test src/ualbion.ci.sln` — all green.
   - `_smoke_all_saves.ps1` — 13/13.
   - For anything player-visible: drive it live via the HTTP harness and screenshot.
5. **Commit each batch** with a message citing the RE function addresses / commit basis.
   Update `_TODO_1TO1.md` with the hash.

## Driving the game empirically (the HTTP harness)

Launch with `--harness-http 7878` (see CLAUDE.md for the exact `Start-Process` line — note
the PS-quoting gotcha below). Then POST events:
- `POST /event/raw` body `load_game 3` — fire any UAlbion event in text form.
- `GET /state` — party HP/SP/conditions, map, time, leader pos.
- `GET /ui` → `.elements` — visible UI (kind/label/x/y/id); `POST /click {id}`.
- `GET /healthz` — `lastError` (null = clean), fps, frame.
- `POST /screenshot` — PNG (then Read the file to inspect visually).
- `GET /npcs`, `/labyrinth`, `/wallpixels`, etc. — diagnostics.

Combat is fully drivable: `load_game 2` → `begin_combat_round` (one round per call).
Spells/services: `cast_spell`, `magic_menu`, `place_action ...`, `svc_heal ...`.

### Save-file map (which save is what — for harness testing)
> **Live registry: `_SESSIONS.md`** (this list went stale once already — save 2 was
> overwritten 2026-06-15).
- **Save 1** = Drinno3 (3D dungeon), 5-member lvl-13 party — **the combat testbed**.
- **Save 2** = TorontoBegin, solo Tom lvl 3 (no longer the Drinno fight!).
- **Save 3** = Jirinaar (3D city), no auto-combat — **use this for non-combat tests** (NPCs,
  services, automap, teleporter). Its goto-markers are already discovered.
- 2D maps among the saves: Nakiridaani (200), Winion (132), JirinaarTownHall (113),
  SnirdArmoury (118). Jirinaar (110) and HunterClanCellar (123) are **3D**.

## The one rule that matters most

**Verify empirically; never trust a comment or a ledger "done" mark.** This codebase has
repeatedly had stale comments and ledger drift (placeholders for shipped features, "done"
marks for open work, and vice-versa). Read the actual code / run the actual test.
