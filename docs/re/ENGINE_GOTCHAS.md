# UAlbion — Engine Gotchas & Recurring Bug Classes

> The non-obvious landmines that have bitten this effort repeatedly. Read before touching
> the eventing system, combat, game state, or rendering. Each entry is a real bug that
> cost real time — they are not hypothetical.

## Eventing

### `Raise()` SKIPS the sender's own handlers — THE #1 recurring bug
`EventExchange.Raise(e)` runs handlers on every component **except the one that called
it**. So a component that raises an event it also subscribes to will NOT see it.
This silently broke, separately: natural combat victories (Battle raised `EndCombatEvent`
but never ran its own cleanup → battle + scene leaked forever), CombatDialog visibility,
and GameState rest. **Fix pattern:** call your own handler method directly instead of
raising, and guard against double-handling (e.g. a `_combatEnded` flag) when an external
raise can also reach it. Search `Battle.cs` for `// Raise() skips own handlers`.
Use `Enqueue()` (deferred, thread-safe) from background threads — `Raise()` is not thread-safe.

### A `switch` on a numeric-operation enum with a pass-through `default` is a trap
`SheetApplier.ApplyStatus` only handled `SetToMaximum/SetToMinimum/Toggle`;
`AddAmount`/`SubtractAmount` fell through to a silent no-op. Callers throughout the code
used the amount ops — so **every condition cure in the game did nothing** (heal-status
spells, the NPC Cure service, sleep decay) until 2026-06-12. When you add a switch on
`NumericOperation`/`ChangeProperty`, audit every caller's choice of operation, and make
amount-ops on a single flag collapse to set/clear. (`SheetApplier.cs` ChangeItem subtract
is the one branch still unhandled — unreachable in base data.)

## Game state / sheets

### `TargetId` casts are type-restricted — the naive cast throws at runtime
`TargetId` accepts only `None / PartyMember / NpcSheet / Target` — **not** `PartySheet`
or `MonsterSheet`. To route a combatant through `SheetApplier`, map `PartySheet.N →
PartyMember.N` (same numeric id; see `Battle.TryToTarget`) and skip monsters. The naive
`(TargetId)(AssetId)x.SheetId` compiles but throws. Leader-death once crashed on exactly
this.

### Effective vs underlying sheets; monsters are transient clones
Party members have an `Effective` snapshot recomputed per frame from their persistent
sheet in `GameState.Sheets` — so `DataChangeEvent` reaches them. **Monsters in combat are
transient clones NOT in `GameState.Sheets`**, so events can't reach them. That's why
`Battle` keeps `_liveHp` / `_liveSp` shadows keyed by `SheetId` for monster HP/SP, and
why area/instant-kill effects route through `ApplyDirectDamage`, not events.

### `MonsterData.CopyFrom` must DEEP-copy
It originally only copied `CombatGfx`, so the `Effective` monster clone lost its
`Animations` dictionary (+ scaling/hover fields) and every monster was frozen on idle
frame 0 in the battle view. Deep-copy the lists. Any new MonsterData field needs adding to
`CopyFrom` AND `EffectiveSheetCalculator` AND `InterpolatedCharacterSheet` (the combat
sheet reads go through `Effective`).

### `ApiUtil.Assert` is a NO-OP in Release builds
Never rely on it to guard a dereference — add an explicit null check too.

## Rendering

### NEVER re-enable SPIR-V optimization
The optimizing GLSL→SPIR-V compile in Release stripped unused resource declarations,
shifting every later D3D11 register off the slots Veldrid binds (the 3D dungeon rendered
the *palette texture* as walls) and broke structured-buffer reads (2D maps went black).
`ShaderCache` must always compile with debug options. If shaders ever regress to rainbow
stripes / black maps: compare `%LOCALAPPDATA%\ualbion\ShaderCache\*.hlsl` registers vs the
C# resource-set layouts and clear that cache (its hash covers GLSL content only, not
compile options).

### Combat presentation draw-order is hand-tuned, not depth-tested
The combat sprites are `NoDepthTest`, so **DrawLayer IS the draw order**. The presentation
owns the unused **0x2F0–0x2FE band**: backdrop 0x2F0, shadows 0x2F1, monster rows
0x2F2–0x2F5 (painter's order, back-to-front), effects 0x2FE — all BELOW the UI (0x301+).
The backdrop MUST render through the UI sprite path (`UiFixedPositionElement`), `ZeroOpaque`,
at the original's **360×192** rect (horizon at y=96 — do NOT stretch to 360×240, do NOT use
`DrawLayer.Interface` which covers the monsters). The combat **palette comes from the combat
background** (the original asserts without one) — `CombatManager` falls back to
`CombatBackground.Dungeon`.

### UI integer scale = `floor(min(w/360, h/240))`
A 720×**479** window renders the UI at **1×**. For 2× use `e:window_size 720 480`. Combat
needs height ≥ 480.

### "Giant pixelated blobs" on 3D maps are usually point-blank monster NPCs, not corruption
ChaseParty monster billboards standing on/next to the party tile fill the screen. This was
mistaken for texture corruption — it's contact-combat range.

## Time / world tick

### Bulk time advance must fire per-hour events
Rest / Wait / inn stays advance many hours at once. `GameClock` only detects hour
crossings from incremental frame time, so a bulk jump SKIPS all per-hour processing
(poison drain, EveryHour map chains, fatigue counter, dungeon-light decay, active-spell
decay) unless you fire `HourElapsedEvent` once per skipped hour yourself. See
`GameState.AdvanceTimeInHours`. (No double-fire: GameClock re-reads `state.Time` each
frame so it never sees the jump as a crossing.)

### Active-spell / shield state is HOUR-DURATION world state, not round-timed
MagicShield/PersonalProtection and Light write `SavedGame.ActiveSpells` (the original's
0x153b3e/0x153b3a tables): duration in **game hours**, decremented by the hour tick, NOT
refreshed while active. They persist across battles AND saves. Don't model them as
round-timed combat buffs.

## Tooling / build / harness

### Kill `UAlbion.exe` before building
A running game locks its DLLs → `MSB3021 ... file is locked`. `Get-Process UAlbion* |
Stop-Process -Force` first.

### PowerShell 5.1 `Start-Process -ArgumentList` strips quotes from spaced entries
`-mods "Albion HD"` arrives as two tokens, silently dropping the mod. Either invoke the
exe directly (`& $exe -d3d -c $cmd ...`, as `_smoke_all_saves.ps1` does) or absorb trailing
non-flag tokens (CommandLineOptions does this now). Don't trust `Start-Process` arg
quoting.

### Ghidra cannot read this DOS LE binary — use radare2
Ghidra 12.1 fails on the LE format. radare2 at `the local radare2 install`,
project `albion_aaa`. Invoke from PowerShell to dodge bash quoting:
`& '...\radare2.exe' -p albion_aaa -q -c '<cmd>'`.

### `AlbionTaskBuilder.SetException` used to destroy stack traces
Fixed with `ExceptionDispatchInfo.Capture(ex).Throw()`. If async combat/dialog exceptions
ever show no stack, check this.

## Reverse-engineering workflow (what makes RE agents reliable)
- One radare2 command per call (single `pdf`/`axt`); write findings to the output file
  **incrementally**, not at the end (two agents died and one lost ALL work writing only at
  the end).
- Each parallel agent owns its OWN output file (docs/re/RE_5A.md-style), disjoint binary
  regions, no shared-file appends.
- Give each agent a "KNOWN — DO NOT RE-DERIVE" list of already-decoded functions/addresses.
- Watch for the original's MISLABELS: e.g. "Summon" (fcn.0004f6a2) is actually CastSpell;
  "tries school 6" is actually `FindEquippedItemOfType(LongRangeWeapon)`. Verify, don't
  assume the disassembler's auto-names or earlier notes.
