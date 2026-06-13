# Reverse-engineering `ask_surrender` (MapEventType 0x1C)

> Live document. Findings appended as confirmed. Read-only RE against MAIN.EXE (radare2 project `albion_aaa`).

## KNOWN FACTS (running list)

- MapEventType **0x1C = AskSurrender**. Confirmed from remake `src/Formats/MapEvents/MapEventType.cs:32` and serdes `AskSurrenderEvent.cs`.
- On-disk record = 10 bytes: 1 type byte (0x1C) + 9 data bytes.
- Data byte layout (from remake serdes `AskSurrenderEvent.Serdes`):
  - Unk1: u8 (byte +1)
  - Unk2: u8 (byte +2)
  - Unk3: u8 (byte +3)
  - Unk4: u8 (byte +4)
  - Unk5: u8 (byte +5)
  - Unk6: u16 LE (bytes +6,+7)
  - Unk8: u16 LE (bytes +8,+9)
- Event-type byte space runs 1..0x1D (+0xFF). Dispatch is a switch on the type byte.
- **Master map-event dispatcher: `fcn.000312c2`** (reads type at record `[+0x18]`, `cmp 0x1d`, `call [type*4 + 0x13da7c]`).
- **Map-event handler table: `0x13da7c`** — entry for 0x1C = **NULL** (no map handler).
- **Surrender check function: `fcn.0x51a2e`** (sets combat outcome `0x15f112 = 4`).
- **Monster behaviour table: `0x13e1f0`** (0x10-byte rows; row 8 col2 = the surrender check). Reader = `fcn.0004faeb` (key = combatant `[+0x12]-1`).
- **Trigger sites:** melee `fcn.0004eac1` @ `0x4eda7`, ranged `fcn.0004f057` @ `0x4f34e` (post-strike, attacker kind==2, damage>0).
- **Outcome consumers:** `fcn.0004b256` (`cmp 4` short-circuit) and `fcn.0004a97d` (switch case 3).
- **The 9 Unk data bytes of opcode 0x1C are NEVER read by the engine** — the opcode is inert; surrender is hard-wired monster AI.
- **Master map-event dispatcher = `fcn.000312c2`** (map.c / map_pum.c range). Reads event-type byte from `record[+0x18]`, bounds `1..0x1D`, then `call dword [type*4 + 0x13da7c]` — a **function-pointer table at `0x13da7c`** indexed by event type. Disasm evidence: `0x3136c mov al,[edx+0x18]`; `0x3137f cmp eax,0x1d`; `0x3139e..0x313a1 call dword [eax*4 + 0x13da7c]`.
- **Handler-pointer table at `0x13da7c`** (event-type → function), fully read:

| Idx | Type | Handler addr |
|---|---|---|
| 1 | MapExit | 0x39e7d |
| 2 | Door | 0x3a25f |
| 3 | Chest | 0x3a396 |
| 4 | Text | 0x3a4d7 |
| 5 | Spinner | 0x3a8a1 |
| 6 | Trap | 0x3aa35 |
| 7 | ChangeUsedItem | 0x3bed3 |
| 8 | DataChange | 0x3c0bd |
| 9 | ChangeIcon | 0x3ac52 |
| A | Encounter | 0x3ae03 |
| B | PlaceAction | 0x3af09 |
| C | Query | 0x3ca34 |
| D | Modify | 0x3da9a |
| **E** | **Action** | **0x00000000 (NULL)** |
| F | Signal | 0x3af2d |
| 10 | CloneAutomap | 0x3b05b |
| 11 | Sound | 0x3b489 |
| 12 | StartDialogue | 0x3b65a |
| 13 | CreateTransport | 0x3b6e0 |
| 14 | Execute | 0x3b78f |
| 15 | RemovePartyMember | 0x3b811 |
| 16 | EndDialogue | 0x3b95c |
| 17 | Wipe | 0x3b984 |
| 18 | PlayAnimation | 0x3b9cf |
| 19 | Offset | 0x3bad9 |
| 1A | Pause | 0x3bbb4 |
| 1B | SimpleChest | 0x3bc28 |
| **1C** | **AskSurrender** | **0x00000000 (NULL)** |
| 1D | Script | 0x3be6f |

- **KEY FINDING: AskSurrender (0x1C) has a NULL handler in the map-event table.** Just like `Action` (0xE), it is NOT dispatched through the normal map-step interpreter. This means `ask_surrender` is consumed/handled in a *different context* — the combat pipeline, not the walk-around map interpreter. CONFIDENCE: CONFIRMED from code.

- **The surrender mechanism is NOT an opcode handler at all — it is a monster-AI behaviour.** It lives in the combat module, driven by the monster's MONCHAR behaviour-strategy field, not by the 0x1C map-event record.

### The surrender call chain (all CONFIRMED from code)

1. **`fcn.0004faeb` = `GetBehaviourRow(combatant)`** (`0x4faeb`): reads `combatant->sheet[+0x12]` (u16), `idx = sheet[+0x12] - 1`; clamps `idx` to 0..8 (`cmp 9; jl; else 0`); `row = 0x13e1f0 + idx*0x10`. Returns row pointer. Evidence: `0x4fb09 mov ax,[eax+0x12]`; `0x4fb3a cmp eax,9`; `0x4fb4a shl eax,4`; `0x4fb4d mov edx,0x13e1f0`.

2. **Monster behaviour-strategy table at `0x13e1f0`** (rows 0x10 bytes = 4 dwords, indexed by `sheet[+0x12]-1`, 0..8). Column meanings: col0 (`+0`) = morale/flee check, col1 (`+4`) = secondary turn behaviour, col2 (`+8`) = post-strike special check. Full table:

| Row | Addr | col0 (+0) | col1 (+4) | col2 (+8) |
|---|---|---|---|---|
| 0 | 0x13e1f0 | 0x51506 morale | - | - |
| 1 | 0x13e200 | 0x515db | - | - |
| 2 | 0x13e210 | 0x51506 morale | 0x51717 | - |
| 3 | 0x13e220 | 0x51506 morale | - | 0x518c7 |
| 4 | 0x13e230 | 0x51506 morale | - | 0x51910 |
| 5 | 0x13e240 | 0x51506 morale | - | 0x51991 |
| 6 | 0x13e250 | 0x51620 | - | - |
| 7 | 0x13e260 | 0x516b2 | - | - |
| **8** | **0x13e270** | **0x51506 morale** | **0x517e5** | **0x51a2e = SURRENDER CHECK** |

   So a monster whose MONCHAR `sheet[+0x12] == 9` (idx 8) gets behaviour-row 8, the ONLY row with the surrender check in col2. This is the final boss's behaviour id. CONFIDENCE: CONFIRMED that row 8 col2 = surrender; INFERRED that idx 9/row 8 is the final boss (matches the unique-unkillable-boss design; not yet cross-checked against the boss MONCHAR data file).

3. **Trigger site — post-strike, in BOTH strike resolvers** (melee `fcn.0004eac1` @ `0x4eda7`, ranged `fcn.0004f057` @ `0x4f34e`), identical block:
```
if (someState == 2) {                  // cmp eax,2; jne skip  (eax = strike-result/animation state)
    row = GetBehaviourRow(attacker);   // attacker = [ebp-0x30]
    if (row[+8] != 0)                  // only behaviour row 8 qualifies
        row[+8]([ebp-0x2c]=target, [ebp-0x30]=attacker);  // call SURRENDER CHECK
}
```
   Evidence (melee): `0x4eda2 cmp eax,2`; `0x4eda7 call fcn.0004faeb`; `0x4edb2 cmp [eax+8],0`; `0x4edc1 call dword [ebx+8]`. Same at `0x4f346/0x4f34e/0x4f359/0x4f368` for ranged.

4. **`fcn.0x51a2e` = the surrender check** (function prologue at `0x51a2e`):
```
partySize = CountPartyMembers();              // fcn.00038e3b
threshold = (partySize - 2 < 1) ? 1 : (partySize - 2);   // i.e. max(1, partySize-2)
conscious = CountConsciousPartyMembers();     // fcn.00038ea3
if (conscious <= threshold)                   // cmp edx(conscious),eax(threshold); jg skip
    word[0x15f112] = 4;                        // COMBAT OUTCOME = 4 (SURRENDER)
```
   Evidence: `0x51a4b call fcn.00038e3b`; `0x51a51 sub eax,2`; `0x51a54 cmp eax,1; jge`; `0x51a74 call fcn.00038ea3`; `0x51a82 cmp edx,eax; jg 0x51a8f`; `0x51a86 mov word [0x15f112],4`.
   - `fcn.00038e3b` = loops 6 party slots, counts those where `fcn.00039941(slot+1)` (slot occupied) → party member count.
   - `fcn.00038ea3` = loops 6 party slots, counts those where slot occupied AND `fcn.00035875(sheet)` (alive = not null and NOT (GetStatusConditions & 1 = Unconscious)) → conscious member count.

5. **Outcome 4 consumed by the combat-end / post-combat handlers:**
   - `fcn.0004b256` (combat-end status) explicitly has a `cmp eax,4` branch (`0x4b2cf`) so outcome 4 short-circuits the normal victory/fled/defeat evaluation (it does NOT overwrite an already-set 4 with 1/2/3). Evidence: `0x4b2c9 mov ax,[0x15f112]; 0x4b2cf cmp eax,4; jne`.
   - `fcn.0004a97d` (post-combat dispatcher, called from encounter-end `fcn.00042237`) does `outcome-1` → 4-case switch (table `0x4aa5f`): case 3 = outcome 4 = surrender path at `0x4ab17`: clears combat conditions (`fcn.000650ad`), party-leader fix-up (`fcn.00039941`/`fcn.000354ac`), `fcn.00016e7f`, `fcn.0007574d`, then `fcn.00066575`. The post-combat map/next-event to load is passed in via the function's eax/edx args (→ `0x15f110`/`0x15f10c`).

### KEY CONCLUSION
The 9 data bytes of the `ask_surrender` (0x1C) map event are **NOT read by the surrender mechanism**. The surrender win-condition is implemented entirely as monster-AI behaviour-row 8, col2 (`fcn.0x51a2e`), keyed off MONCHAR `sheet[+0x12]`, with a fixed party-conscious threshold. The 0x1C map-event opcode has a NULL handler in the map dispatch table. See "Where do the Unk fields go?" below.

### The full surrender algorithm (plain English, CONFIRMED)

Trigger and member-fall, end to end:

1. During a combat round, a monster takes its strike action (melee `fcn.0004eac1` or ranged `fcn.0004f057`).
2. The strike rolls damage (`fcn.0004ee3b`). If damage > 0 it is applied to the chosen target via `fcn.0004dec9(target, damage)` — this is the LP-damage applier that can knock the struck party member **Unconscious** (status condition bit 0). The "party member who falls" is simply **the monster's strike target**, picked by the boss's normal target selection — there is NO fixed index and NO Unk field choosing it.
3. Immediately after applying damage, the resolver checks: is the **attacker a monster** (`combatant[+0] == 2`)? (`0x4ed97 mov ax,[attacker]; cmp eax,2`). If yes:
4. It looks up the attacker's behaviour row (`GetBehaviourRow`, keyed by combatant `[+0x12]`-1) and, **only if that row has a col2 callback** (`row[+8] != 0` — true only for behaviour row 8), calls it: `row[+8](target, attacker)` = `fcn.0x51a2e`, the surrender check.
5. The surrender check counts the party: `conscious = CountConscious()`, `threshold = max(1, partySize - 2)`. If `conscious <= threshold`, it sets **combat outcome `0x15f112 = 4`** = SURRENDER.
6. The combat loop's end-check (`fcn.0004b256`) sees outcome 4 and ends the battle. The post-combat dispatcher `fcn.0004a97d` handles outcome 4 (switch case 3): clears combat status conditions, restores leader, and proceeds to the next scripted event/map (the args passed to `fcn.0004a97d` → `0x15f110`/`0x15f10c` carry the follow-on map/event = the ending sequence).

So the win condition is purely emergent: the unkillable boss (behaviour strategy 9) keeps striking; each KO of a party member lowers the conscious count; once only `max(1, partySize-2)` conscious members remain, the boss "asks for surrender" → outcome 4 → the scripted ending fires.

### Where do the Unk1..Unk8 fields go? — they are UNUSED by the engine

- The map-event handler table at `0x13da7c` has a **NULL** entry for type 0x1C (CONFIRMED).
- A binary-wide search found **no** `cmp`/`mov`/`push` of the immediate `0x1c` that reads it as an event opcode (the few `mov eax,0x1c` hits are allocation sizes / automap string args, CONFIRMED).
- No converted game-data map in `mods/` contains an `ask_surrender` event (CONFIRMED by grep).
- Therefore the 9 data bytes of a 0x1C record are read by the disk serializer only; the engine never consumes them. The opcode appears to be a **vestigial / never-emitted map event**; the actual surrender logic is hard-wired in monster AI.

## Field-by-field table (Unk1..Unk8)

| Field | On-disk | Meaning | Confidence | Evidence |
|---|---|---|---|---|
| Unk1 | u8 (+1) | UNUSED / padding. No engine reader. | INFERRED-UNUSED | map table 0x13da7c[0x1C]=NULL; no 0x1C opcode compare anywhere |
| Unk2 | u8 (+2) | UNUSED / padding | INFERRED-UNUSED | same |
| Unk3 | u8 (+3) | UNUSED / padding | INFERRED-UNUSED | same |
| Unk4 | u8 (+4) | UNUSED / padding | INFERRED-UNUSED | same |
| Unk5 | u8 (+5) | UNUSED / padding | INFERRED-UNUSED | same |
| Unk6 | u16 (+6) | UNUSED / padding | INFERRED-UNUSED | same |
| Unk8 | u16 (+8) | UNUSED / padding | INFERRED-UNUSED | same |

(The combat-event 10-byte layout in this engine is generic; for active opcodes the trailing u16s are typically a target/next-event id, but for 0x1C none of them is ever read.)

## CONFIDENCE summary

CONFIRMED from code:
- Map-event dispatcher `fcn.000312c2`, handler table `0x13da7c`, 0x1C slot = NULL.
- Surrender is monster-AI behaviour-row 8 col2 = `fcn.0x51a2e`; behaviour table `0x13e1f0` (0x10 stride), reader `fcn.0004faeb` keyed on combatant `[+0x12]-1`.
- Trigger: post-strike in `fcn.0004eac1`/`fcn.0004f057` when attacker kind==2 and the strike dealt damage.
- Threshold: `conscious <= max(1, partySize-2)` → `0x15f112 = 4`. Helpers `fcn.00038e3b` (party count), `fcn.00038ea3` (conscious count via `fcn.00035875` = alive & not Unconscious).
- Outcome 4 consumed by `fcn.0004b256` and `fcn.0004a97d` (switch case 3).
- The 9 Unk bytes are never read by the engine.

INFERRED (not byte-verified against the boss data file):
- Behaviour strategy id 9 (runtime combatant `[+0x12]==9`, on-disk MONCHAR field, indexed 8) is the final boss. The mapping disk-MONCHAR `+0x0C` → runtime combatant `+0x12` is asserted in `_RE_INDEX.md`; I confirmed the runtime read at `+0x12` but did not trace the setup copy.
- Each Unk field's "padding" status (they could be designer metadata in an unshipped editor, but the runtime ignores them).

COULD NOT DETERMINE:
- Whether the ORIGINAL game's binary MAP/event data (raw XLD, not the converted mods) ever contains a 0x1C record — only checked the converted `mods/` assets and the code paths. Given the NULL handler this would be inert anyway.
- The exact "ending" map/event id that the surrender outcome chains to — it is supplied at runtime via the `fcn.0004a97d` eax/edx args (encounter follow-on), not a constant in the surrender code, so it depends on the boss encounter's event-chain data, not the engine.

## Implementation recommendation (for the C# remake)

`AskSurrenderEvent` (0x1C) should be treated as an **inert / no-op map event** for serialization round-trip fidelity (keep the 7 Unk fields exactly as-is — they preserve unknown bytes; do NOT invent semantics). Do not add a map-event handler for it.

The actual surrender WIN CONDITION belongs in combat (`src/Game/Combat/Battle.cs`), implemented as a monster-AI behaviour, not as a map-event handler:

1. Give the final-boss monster a behaviour-strategy flag (mirror MONCHAR behaviour id 9 → "CanRequestSurrender"). In UAlbion terms this is the monster sheet's AI/behaviour field.
2. In the post-strike resolution (`Battle.ApplyMeleeAttack` / ranged equivalent), after damage is applied to the target, if `attacker.IsMonster && attacker.Behaviour == RequestSurrender && damageDealt > 0`, run the surrender check.
3. Surrender check:
   ```csharp
   int partySize = Party.Members.Count(m => m.IsPresent);
   int threshold = Math.Max(1, partySize - 2);
   int conscious = Party.Members.Count(m => m.IsPresent && m.IsAlive && !m.HasCondition(PlayerConditions.Unconscious));
   if (conscious <= threshold)
       battleOutcome = BattleOutcome.Surrender;   // a 4th outcome alongside Victory/PartyFled/Defeat
   ```
4. Add a 4th `BattleOutcome.Surrender`. On that outcome, end the battle (do not award normal victory), clear combat conditions, then fire the boss encounter's follow-on event chain (the scripted Tom/Seed ending) — i.e. continue the encounter's event chain exactly as victory would, since the original passes the next-event via the encounter-end args. The ending content (DeploySeed → finale) is driven by the boss map's event chain, not by the engine.

Net: `AskSurrenderEvent` stays a data-only no-op; the gameplay lives in combat AI. This matches the original 1:1.

