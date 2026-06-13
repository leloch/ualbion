# Reverse-engineering four story ActionType handlers (0x09, 0x0E, 0x17, 0x2D)

> Live document. Findings appended as confirmed. Read-only RE against MAIN.EXE
> (radare2 project `albion_aaa`). Sister doc to `_RE_ASK_SURRENDER.md`.
>
> Target: the conversation/event-chain `ActionType` values that real story NPCs use
> but whose decoded effect/trigger is unknown:
> - **0x09** — NPC 234 (Riko) / 242 (Gerwad)
> - **0x0E** — 981 (Tom, endgame)
> - **0x17** and **0x2D** — Sira (spell/seed scenes)

## KNOWN FACTS (running list)

- In UAlbion an `ActionEvent` is a **MapEvent of `MapEventType.Action` (= 0xE)**. Its
  on-disk record (10 bytes) is: type byte 0x0E, then `Unk2`(u8), `Block`(u8), `Unk4`(u8),
  `Unk5`(u8), `Argument`(u16), `Unk8`(u16). The `ActionType` enum (0x00..0x3D) is **NOT**
  the MapEventType byte — it is the **first data byte (`+1`) of an Action record**. Source:
  `src/Formats/MapEvents/ActionEvent.cs`, `ActionType.cs`.
- **The map-event dispatcher `fcn.000312c2` does NOTHING with Action (type 0xE).** The
  handler-pointer table `0x13da7c[0xE] = NULL`; the dispatcher's NULL-guard
  (`0x3138f cmp dword [eax*4+0x13da7c],0 ; je 0x313a7`) skips a NULL handler and advances to
  the next chained event. So an Action map event is a NO-OP in the walk-around map interpreter.
  CONFIRMED from code (see `_RE_ASK_SURRENDER.md` table).
- Therefore `ActionEvent` records are **not executable instructions** in the map interpreter.
  They function as **chain headers / selectors**: the conversation code searches the NPC's
  event set for a chain whose head is an Action matching `(ActionType, Block, Argument)`, and
  then runs the *rest* of that chain. This is exactly what the C# remake does:
  `Conversation.FindActionChain` + `TriggerChainEvent` (`src/Game/Gui/Dialogs/Conversation.cs`).
- Consequence: the **"effect" of a given ActionType is whatever the chained events after it
  do** (text, add/remove party member, data_change, etc.) — it is DATA, not engine code. The
  ActionType byte's only job is to be **matched against a trigger condition** so the engine
  knows *when* to fire that chain. So the real RE question is: **what game state/event causes
  the engine to look up each of these ActionType chains?** (the TRIGGER), not "what opcode runs".
- `events.c` (string `g:\albion\src\events.c`) holds the map-event handlers, spanning approx
  `0x39e7d`..`0x3da9a`. The conversation/action chain-matcher lives in a different module.

## THE ACTION-CHAIN MATCHER — `fcn.00030fc0` (CONFIRMED)

This is the original of C# `Conversation.FindActionChain`. It is the ONE function that
gives an `ActionType` its meaning: it selects which event chain to run.

**Signature** (Watcom register args): `eax` = event-set id (an NPC's `EventSetId` or
`WordSetId`); `edx` = a **trigger-type bit index** (a `TriggerType` value 0..15, or
`0xFFFF` = "do not filter by trigger"). **Returns** the matched chain index (u16) in `eax`,
or `0xFFFF` if none.

**Event-set in-memory layout** (CONFIRMED from `0x31015`..`0x31034`):
```
u16  count;                 // [ptr+0]  number of chains
u16  chainOffsets[count];   // [ptr+4]  head-event index of each chain
Event events[];             // [ptr+4+count*2]  each event = 0xC (12) bytes
```

**In-memory Event record = 12 bytes. For an Action event the matcher reads:**

| Off | Field (C# name)        | Used by matcher as |
|----|-------------------------|--------------------|
| +0 | event type             | must == **0xE** (Action) — `0x3107c cmp eax,0xe` |
| +1 | **ActionType subtype** (`ActionEvent.ActionType`) | compared to requested subtype — `0x310ab mov dl,[eax+1]; cmp` |
| +2 | **`ActionEvent.Unk2`**  | **trigger-class bitmask** — `0x31093 mov dl,[eax+2]; test dl,(1<<edx)` (only when edx≠0xFFFF) |
| +3 | **`ActionEvent.Block`** | compared to requested block; **0xFF = wildcard** — `0x310c3 cmp dx,[sel+0xa]` / `0x310de cmp 0xFF` |
| +6 | **`ActionEvent.Argument`** (u16) | compared to requested arg; **0x7D00 (=32000) = wildcard** — `0x310ee` / `0x31119 cmp 0x7d00` |

**Requested selector** (what the caller wants to match) lives in a 0x18-byte per-active-NPC
record at `0x153930 + word[0x13d708]*0x18` (= `var_28h`):
- `[sel+6]` u32: **low word = requested ActionType subtype**, **high word = requested Argument**.
- `[sel+0xa]` u16: **requested Block**.

**Match algorithm** (CONFIRMED): for each chain head event, require `type==0xE`; if the trigger
filter is active (`edx≠0xFFFF`) require `Unk2 & (1<<edx)`; require `subtype==requested`; then
prefer an exact `(Block,Argument)` match, else fall back to a wildcard match (`Block==0xFF`
**and** `Argument==0x7D00`). Returns the chain index of the best match.

### KEY DECODE: `ActionEvent.Unk2` (record +2) is a TriggerTypes bitmask (CONFIRMED)
The C# serdes comment says *"Unk2 always 1, unless ActionType==14 (0xE) in which case it is 2"*.
The matcher proves what that byte IS: it is the **low 8 bits of a `TriggerTypes` bitfield**
(`src/Formats/Assets/Maps/TriggerTypes.cs`), tested with `1 << triggerIndex`:
- **Unk2 = 1 = `TriggerTypes.Normal`** — the action fires under the Normal interaction/dialogue path.
- **Unk2 = 2 = `TriggerTypes.Examine`** — the action fires under the Examine path (this is why
  ActionType **0x0E** uniquely carries Unk2=2: it is gated to the *Examine* trigger, not Normal).
The filter is bypassed entirely when the caller passes `edx=0xFFFF`. The pure in-conversation
lookup (StartDialogue / DialogueLine etc.) effectively does NOT filter on Unk2; the Unk2 gate
only matters when the chain is reached via a map interaction (examine/touch/talk/use) where the
caller passes a specific `TriggerType`. CONFIDENCE: CONFIRMED for the bit semantics; INFERRED
that the conversation runner passes 0xFFFF (matches C# `FindActionChain` ignoring Unk2).

### Callers / how the matched chain is RUN (CONFIRMED)
`fcn.00030fc0` is invoked from the per-NPC trigger driver **`fcn.00030d7b(charIndex, triggerBit)`**
(`0x30dff`) and from `0x2b5c9 / 0x2feeb / 0x3076a / 0x307ff / 0x308dd` (the various interaction
entry points). `fcn.00030d7b` walks the NPC's two event-set slots
(`[charIndex*8 + slot*4 + 0x159708]` — slot 0 = EventSet, slot 1 = WordSet, exactly the C#
EventSetId/WordSetId fallback), calls the matcher, and on a hit (`≠0xFFFF`) builds a fresh
map-event context via `fcn.0003150d`, points it at the chain head, and runs it through the
**map-event dispatcher `fcn.000312c2`** (`0x30ebd`) → chain executor `fcn.0007545c`.

Because the chain HEAD is the Action event (type 0xE, NULL handler), the dispatcher skips the
header itself and executes the **following** events in the chain — i.e. the Action event is a
pure selector, and the EFFECT is the chain body. CONFIRMED.

## WHAT THE FOUR TARGET ActionTypes ARE (synthesis)

The four target values are NOT engine opcodes — there is no per-ActionType `switch` and no
hard-coded effect for 0x09/0x0E/0x17/0x2D anywhere in MAIN.EXE. Each is a **chain selector**:
the engine matches it via `fcn.00030fc0`, then runs whatever map-events are chained after it
(text, `add/remove_party_member`, `data_change`, `start_dialogue`, `signal`, etc.). The chain
bodies live in the game's `EVNTSET`/map data (XLD), NOT in the EXE. So:
- **EFFECT** of each ActionType = its chain body (data, per-NPC). UNKNOWN from the EXE alone;
  must be read from the converted `EventSet` assets (see "Reading the chain bodies" below).
- **TRIGGER** of each ActionType = (requested-subtype set by the interaction context)
  + (the chain's `Unk2` bitmask permitting that `TriggerType`). This IS in the EXE and is decoded.

### Trigger context per value (INFERRED from the matcher + the C# `Conversation` model + the in-data usage notes)

| ActionType | Used by | Trigger (how the engine requests this subtype) | Unk2 gate | Confidence |
|---|---|---|---|---|
| **0x09** | NPC 234 Riko / 242 Gerwad | A subtype-9 interaction request. Sits in the conversation/word block range (0x6 StartDialogue .. 0xD). Most likely fired from a dialogue/word block context (the requested subtype written into `sel+6` by the conversation runner), NOT a global tick. | =1 (Normal) | INFERRED |
| **0x0E** | NPC 981 Tom (endgame) | Uniquely carries **Unk2 = 2 = Examine**. So this chain is selected when the **Examine** interaction (`TriggerType.Examine`, `edx=1`) requests subtype 0x0E — i.e. on *examining* Tom / an examine-context endgame beat, not on plain talk. | =2 (Examine) | INFERRED (Unk2=2→Examine is CONFIRMED; that this is "examine Tom" is inferred from the single Unk2=2 case + Tom-endgame context) |
| **0x17** | Sira (spell/seed scene) | Subtype-0x17 request. Sira's scripted scenes are driven by the rest/sleep path (`PartySleeps` 0x3D) and dialogue; 0x17/0x2D are the in-scene script beats requested by those chains. | =1 (Normal) | INFERRED |
| **0x2D** | Sira (spell/seed scene) | Subtype-0x2D request (sits just below UseItem 0x2E in the item-action range). Likely an item/spell-bearing scripted beat in Sira's scene. | =1 (Normal) | INFERRED |

CONFIRMED for all four: there is no dedicated handler; effect = chain body; selection = `fcn.00030fc0`.

### How state transitions feed the matcher (CONFIRMED — corrects a common misconception)
There is **no `cmp …,0x3D` (PartySleeps) and no per-ActionType subtype scan** for time/rest in
the EXE. State-driven chains are fired by the **global trigger dispatcher `fcn.0003236a(code)`**,
which builds `mask = 1<<code` and runs every map zone/NPC trigger record whose bitfield (at
record +2) has that bit set, via `fcn.000312c2`. Firing sites (CONFIRMED):
- **MapInit (5)** — `fcn.0001183b` @ `0x11c9f`, after a map finishes loading.
- **EveryStep (6)** — `fcn.00043901` @ `0x43994`, per movement step.
- **EveryHour (7)** — `fcn.00043acc` @ `0x43b78`, each game-hour tick.
- **EveryDay (8)** — `fcn.00043b86` @ `0x43bca`, day rollover.
- **Rest/Sleep** — the rest executor `fcn.00068b05` calls advance-time `fcn.000439b3` @ `0x68ce1`,
  which loops `fcn.00043acc`; sleeping therefore fires its scripts through **EveryHour/EveryDay**,
  NOT a "PartySleeps" subtype. The "PartySleeps triggers Sira+Mellthas" behaviour is an
  EveryHour/EveryDay trigger record on that map, not a 0x3D ActionType scan.

(Note: `fcn.000666e9` / table `0x13eaf0` is the **PlaceAction service dispatcher**
— Heal/Cure/Merchant, a different enum — NOT the ActionType dispatcher. Confirmed by inspection.)

## Reading the chain bodies (to finish decoding the EFFECT)
The chain bodies are NOT in MAIN.EXE; they are in the game's `EventSet` data. The local install
has only save-state XLDs (`XLDLIBS/INITIAL`,`CURRENT`), not the main `EVNTSET`/map XLDs (those
are packed in `game.gog`). To decode the exact effect of each value, dump the converted
`EventSet` for the NPCs and read the events chained after each Action head:
- 234 (Riko) / 242 (Gerwad) → ActionType 0x09 chain
- 981 (Tom) → ActionType 0x0E chain (Examine-gated)
- Sira → ActionType 0x17 and 0x2D chains
Use UAlbion's `Decompiler` (`src/Scripting/Decompiler.cs`) on those event sets once full game
data is converted into `mods/Unpacked`. The C# `Conversation.FindActionChain` already locates
them correctly; only the *interpretation* of the chained bodies is outstanding.

## CONFIDENCE SUMMARY

CONFIRMED from code:
- ActionEvent (MapEventType 0xE) has a NULL map handler (`0x13da7c[0xE]=0`); it is a no-op in
  `fcn.000312c2`. Action events are chain SELECTORS, not opcodes. There is no per-ActionType
  effect switch in the EXE.
- The selector/matcher is `fcn.00030fc0` (= C# `FindActionChain`): event-set layout, the 12-byte
  event record, the (type==0xE, subtype@+1, Unk2@+2 trigger-bitmask, Block@+3 0xFF-wildcard,
  Argument@+6 0x7D00-wildcard) comparison, EventSet→WordSet fallback, and chain execution via
  `fcn.000312c2`/`fcn.0007545c`.
- `ActionEvent.Unk2` (record +2) = low 8 bits of a `TriggerTypes` bitfield; Unk2=1=Normal,
  Unk2=2=Examine (the latter only for ActionType 0x0E).
- State-driven chains fire via `fcn.0003236a(triggerCode)` (`1<<code` against record +2);
  MapInit/EveryStep/EveryHour/EveryDay sites confirmed; rest fires via time advance (EveryHour/Day).
- `fcn.000666e9`/`0x13eaf0` is the PlaceAction service dispatcher, not the ActionType path.

INFERRED:
- The specific interaction context that requests each subtype (0x09 Normal/dialogue; 0x0E Examine;
  0x17/0x2D Sira scene beats). The Unk2→TriggerType mapping is confirmed; the precise human-facing
  trigger ("examine Tom", "Sira's seed scene") is inferred from usage + the single Unk2=2 case.

COULD NOT DETERMINE:
- The exact EFFECT of each value's chain body — these are event data in `EVNTSET`/map XLDs not
  present locally (only save-state XLDs are installed; main data is in `game.gog`). They are NOT
  in the EXE. Decode requires dumping the NPC event sets (see "Reading the chain bodies").

## IMPLEMENTATION RECOMMENDATION (C# remake)

The architecture is already correct: do **not** add per-ActionType handlers. Keep treating an
`ActionEvent` (0xE) as a **chain header matched by `(ActionType, Block, Argument)`** and run the
rest of the chain (`Conversation.FindActionChain` + `TriggerChainEvent`). For the four values:

1. **No new dispatch code is needed for the EFFECT** — once the NPC event sets are present, the
   existing chain runner executes their bodies. The remaining work is purely making sure the
   right TRIGGER fires the lookup.
2. **`Unk2` is `TriggerTypes` low-byte, not a magic constant.** When matching from a map
   interaction, gate the chain by `(action.Unk2 & (1 << (int)triggerType)) != 0`, mirroring
   `fcn.00030fc0`. In a pure conversation lookup, pass "no filter" (ignore Unk2), as the current
   `FindActionChain` already does. Specifically: surface ActionType **0x0E** under the **Examine**
   trigger (Unk2=2), not plain TalkTo — so an *examine* on Tom in the endgame can fire it.
3. **Wildcards:** honour `Block == 0xFF` and `Argument == 0x7D00 (32000)` as wildcards in
   `FindActionChain` (the original prefers an exact match and falls back to the wildcard). The
   current C# matches exact only — add the wildcard fallback for 1:1 fidelity.
4. **Triggering:** ensure `TriggerType.Examine/Manipulate/TalkTo/UseItem` interactions, and the
   global `MapInit/EveryStep/EveryHour/EveryDay` ticks (already modelled by `TriggerType`), each
   request the appropriate ActionType lookup against the active NPC's event set — exactly the
   `fcn.0003236a` / `fcn.00030d7b` behaviour. Rest/sleep needs no special "PartySleeps" handler:
   advancing the clock fires EveryHour/EveryDay, which run the map's time-triggered chains.

Net: the four ActionTypes need NO bespoke engine logic — they are data-driven chains. The only
1:1 gaps to close in the remake are (a) the `Unk2` → `TriggerTypes` gate (esp. routing 0x0E to
Examine), and (b) the Block/Argument wildcard fallback in `FindActionChain`.

## KEY ADDRESSES
- `fcn.00030fc0` — ActionType chain MATCHER (= `FindActionChain`); the (0xE, subtype, Unk2-bitmask,
  block, arg) compare at `0x3107c`..`0x31119`.
- `fcn.00030d7b` — per-NPC trigger driver; walks EventSet/WordSet (`+0x159708`), runs match via
  `fcn.000312c2`.
- `fcn.000312c2` — map-event dispatcher; type byte → table `0x13da7c`; slot 0xE = NULL.
- `fcn.0007545c` / `fcn.00075687` — chain executor (depth limit 7 @ `0x13eeee`, frame stride 0x1e
  @ `0x179164`).
- `fcn.0003236a` — global trigger dispatcher (`1<<code` vs record +2 bitfield).
- MapInit `fcn.0001183b@0x11c9f`(5); EveryStep `fcn.00043901@0x43994`(6); EveryHour
  `fcn.00043acc@0x43b78`(7); EveryDay `fcn.00043b86@0x43bca`(8).
- Rest `fcn.00068b05` → advance-time `fcn.000439b3` @ `0x68ce1`.
- Active-NPC selector array `0x153930` (stride 0x18); NPC array `0x153160` (stride 0x32);
  event-set slot table `0x159708`.
- `fcn.000666e9` / table `0x13eaf0` — PlaceAction SERVICE dispatcher (NOT ActionType).
