# Reverse-engineering three world map-event opcodes: Spinner (5), Trap (6), CreateTransport (0x13)

> Live document. Read-only RE against MAIN.EXE (radare2 project `albion_aaa`). Findings appended as confirmed.
> All addresses are file/linear addresses as shown by radare2 (`pd @ <addr>`). Companion: `_RE_ASK_SURRENDER.md` (dispatcher + table), `_RE_COMBAT.md` (RNG/PercentRoll/RandomVary).

## KNOWN FACTS (running list)

### Dispatch & record layout (CONFIRMED)
- Master map-event dispatcher = `fcn.000312c2`; handler-pointer table at `0x13da7c` indexed by event-type byte (from `_RE_ASK_SURRENDER.md`).
- **All three handlers share the same preamble** that locates the current event record:
  `record = 0x153160 + word[0x13d70a]*0x32 + 0x18`
  - `0x13d70a` = current map-event **index** (u16).
  - `0x153160` = base of the map-event array; stride **0x32 (50) bytes** per entry; the 10-byte on-disk record sits at **offset +0x18** within each entry.
  - So `record[+0]` = type byte, `record[+1..+9]` = the 9 data bytes (matches the 10-byte on-disk record).
- Handler addresses: Spinner = `0x3a8a1`, Trap = `0x3aa35`, CreateTransport = `0x3b6e0`.

### Shared helpers (CONFIRMED)
- `fcn.00094326` = **Watcom RNG** `seed=seed*0x41C64E6D+0x3039; return (seed>>16)&0x7FFF`.
- `fcn.00092c17` = `abs(int)`.
- `fcn.00035c22(base)` = **RandomVary** `(rand()%51 + 50) * base / 100` (uniform 50%..100%; matches combat).
- `fcn.00035b15(chance,max)` = **PercentRoll** → 0 if chance<=0; 1 if chance>=max; else `(rand()%max <= chance)`.
- `fcn.00035c77(sheet, attrIdx)` = effective attribute value: reads `sheet + attrIdx*8 + 0x2a` (8-byte attrs: Current/Max/Boost/Backup), then adds equipment modifiers. attrIdx<9.
- `fcn.00035b96(sheet, attrIdx)` = `PercentRoll(effectiveAttr(sheet,attrIdx), 100)`.
- `fcn.0008b739(handle)` = deref a bbmem.c handle → struct pointer (the CharacterSheet).
- `fcn.00036620(sheet)` = returns `sheet[+1]` = **Gender** (CharacterSheet +1, confirmed against `CharacterSheet.cs:190`).
- `fcn.000364b0(sheet)` = returns `sheet[+0x1e]` = **status-conditions bitmask** (CharacterSheet +0x1E, `CharacterSheet.cs:247`).
- `fcn.000363c2(sheet, idx)` = if `idx<12`: `sheet[+0x1e] |= (1<<idx)` (**inflict status condition** idx) + side effects.
- `fcn.000375eb(memberId, dmg)` = apply LP damage to a party member (also used by spells, `0x4dfa2`).
- `fcn.00039941(memberId)` = "is party slot occupied" (returns member-handle/nonzero); from `_RE_ASK_SURRENDER.md`.
- `fcn.00035783(memberId)` = resolve a member id → party slot via the party-id table `0x153cbe`.
- Party/world globals: `0x153b32` = current map id (u16 at +0x32 of a packed dword), `0x153b34` = party X, `0x153b36` = party Y, `0x153cbc` = active/leader member id, `0x14a48c` = **camera angle/position packed dword** (high word = facing), `0x14799c` = **3D-mode flag** (1 = first-person 3D map active), `0x153b38` = current party facing quadrant.
- CharacterSheet attribute order (confirmed `CharacterAttributes.cs`): 0 Str, 1 Int, 2 Dex, 3 Speed, 4 Stamina, **5 Luck**, 6 MagicRes, 7 MagicTalent.

---

## OPCODE 5 — SPINNER (handler `0x3a8a1`)

### On-disk layout (C# `SpinnerEvent`)
- `record[+1]` = **Unk1** = target facing direction.
- `record[+2..+9]` = asserted zero by C# serdes (unused).

### Algorithm (CONFIRMED from code)
```
handler @ 0x3a8a1:
  record = 0x153160 + idx*0x32 + 0x18            ; 0x3a8bb..0x3a8ce
  dir = record[+1] (Unk1)                          ; 0x3a8d4
  if (dir == 4) {                                  ; 0x3a8dc cmp eax,4
      dir = word[0x153b38]                          ; current facing  (0x3a8e1)
      do { dir = rand() % 4 } while (dir == word[0x153b38]) ; 0x3a8ea..0x3a90c
      ; -> pick a RANDOM quadrant DIFFERENT from current facing
  } else {
      dir = (byte)record[+1]                        ; 0x3a910 (xor ah,ah) use Unk1 as-is
  }
  if (word[0x14799c] == 0) return                   ; 0x3a91b: ONLY acts in 3D mode
  target = (-(dir << 0xc)) & 0x3fff                 ; 0x3a92f..0x3a939 : quadrant -> 14-bit angle
  cur    = (dword[0x14a48c] >> 0x10) & ...           ; current camera facing (high word)
  cw  = (cur - target) & 0x3fff                      ; clockwise distance
  ccw = (target - cur) & 0x3fff                      ; counter-clockwise distance
  step = (ccw <= cw) ? +1 : -1   (stored as 1 / -1) ; 0x3a979 jle : choose shorter turn
  ; snap to a multiple of 0x400 (1024) first:
  for (i=cur_angle ... ) align to 0x400 granularity, redraw each step ; 0x3a992..0x3a9cc
  ; then rotate toward target in steps, animating:
  while ((d = abs(camAngle - target)) != 0) {        ; 0x3a9dc loop
      if (d <= 0x400) camAngle += step*d  else camAngle += step*0x400  ; 0x3a9e9..0x3a9fd
      camAngle &= 0x3fff
      dword[0x14a48c] = camAngle << 0x10              ; 0x3aa0d store facing
      redraw_3d_frame()  ; fcn.000757d7 + fcn.00075e9f (0x3aa12/0x3aa17)
  }
  return
```

### Field meaning
| Field | On-disk | Meaning | Confidence | Evidence |
|---|---|---|---|---|
| Unk1 | u8 (+1) | **Target facing**: 0..3 = absolute quadrant (N/E/S/W); **4 = random** (a quadrant != current facing) | CONFIRMED | `0x3a8d4` read; `0x3a8dc cmp 4`; rand loop `0x3a8ea..0x3a90c`; `0x3a92f shl 0xc` |

### Plain English
A Spinner rotates the party's **facing only** (it never moves the party). Value space is **0..3 quadrants** (90° each); the engine works in a 14-bit angle space (0x4000 = full circle, one quadrant = 0x1000). `Unk1` is **absolute** (it sets, not adds, the target quadrant). `Unk1 == 4` is the special "spin to a random new direction" used by classic disorienting spinner tiles — it rolls `rand()%4` until it differs from the current facing. The rotation is then **animated** along the shorter arc (clockwise vs counter-clockwise chosen by comparing the two wrapped distances), redrawing each 3D frame. **It is a no-op outside 3D mode** (`word[0x14799c]==0` -> immediate return), confirming Spinner is a 3D-dungeon-only tile.

### COULD NOT DETERMINE
- The exact quadrant->compass mapping (which quadrant is "North"); the negate+`<<0xc` means the on-disk value is in the same units the renderer uses for facing — for the remake, treat Unk1 as the same direction enum used for party facing (0..3) and 4 as random.

### Implementation recommendation (C#)
- `SpinnerEvent.Unk1` is the **target facing**, not a "rotation amount". Rename/interpret: `0..3 => absolute Direction`, `4 => random direction (!= current)`.
- Add a handler in the **3D map** path (DungeonMap / Map3D party-movement code), NOT the 2D map. On entering a spinner tile, set the party facing to the target (animate the turn if you mirror the original; a snap is acceptable functionally). If `Unk1==4`, pick `Random(0..3)` excluding the current facing.
- Guard with "only when 3D" to match the original.

---

## OPCODE 6 — TRAP (handler `0x3aa35`)

### On-disk layout (C# `TrapEvent`)
- `record[+1]` = **Unk1** (condition index to inflict)
- `record[+2]` = **Unk2** (gender mask)
- `record[+3]` = **Unk3** (target scope selector)
- `record[+4]` = zeroed (skipped by serdes)
- `record[+5]` = **Unk5** (specific member id, used when Unk3==2)
- `record[+6..+7]` = **Unk6** u16 (base damage)
- `record[+8..+9]` = zeroed

### Top-level dispatch by `record[+3]` (Unk3) — CONFIRMED
```
handler @ 0x3aa35:
  record = 0x153160 + idx*0x32 + 0x18
  scope = record[+3] (Unk3)                          ; 0x3aa6d
  switch (scope) {
    case 0:  // ACTIVE / leader party member
        m = word[0x153cbc]                            ; 0x3aa9a active member id
        if (occupied(m)) applyTrap(m, record)         ; fcn.0003ab63  (0x3aab5)
        break;                                        ; 0x3aa8e..0x3aaba
    case 1:  // WHOLE PARTY (slots 0..5)
        for (i=0; i<6; i++)                            ; 0x3aac6..0x3aad9
            if (occupied(i+1)) {
                applyTrap(i+1, record)                 ; 0x3aafa
                if (word[0x147134] != 0) break;        ; 0x3aaff early-out flag
            }
        break;                                        ; 0x3aabf..0x3ab0b
    case 2:  // SPECIFIC member (id from record[+5])
        m = resolveMember(record[+5])                  ; fcn.00035783 (0x3ab1a)
        if (m != 0xffff) applyTrap(m, record)          ; 0x3ab38
        break;                                         ; 0x3ab0d..0x3ab3d
    default: break;
  }
  // post: if (someTime/flag fcn.000364b0(0x1597c0) & 0x731) call fcn.000354ac  ; 0x3ab3d..0x3ab55 (UI refresh / leader fixup)
```

### Per-member application `fcn.0003ab63(memberId, record)` — CONFIRMED
```
sheet = ptrTable[0x153a1c + memberId*4]               ; 0x3ab89 resolve sheet ptr
// (1) GENDER/IMMUNITY GATE:
g = fcn.00036620(sheet)            ; = sheet[+1] = Gender   (0x3ab95)
if ((record[+2] (Unk2) & (1 << g)) == 0) return        ; 0x3abb2: trap does NOT apply to this gender
if (fcn.000364b0(sheet) & 1) return                    ; 0x3abc8: already Unconscious (cond bit0) -> skip
// (2) LUCK SAVE:
if (fcn.00035b96(sheet, 5))        ; PercentRoll(Luck,100) SUCCEEDS  (0x3abde, edx=5)
{
    word[0x15e518] = sheet; eax = dword[0x15e364]
    fcn.0007dc07(...)              ; show "evaded/lucky" popup (string @0x13f777, coords 0x12c/0xa2)
    return;                        ; member AVOIDED the trap
}
// (3) ON FAILED SAVE — inflict condition + damage:
fcn.0003165c()                     ; (sfx / screen feedback) (0x3abd1, runs before save in code order)
cond = record[+1] (Unk1)
if (cond < 12) fcn.000363c2(sheet, cond)   ; sheet[+0x1e] |= (1<<cond)  inflict status (0x3ac1c)
dmg16 = record[+6] (Unk6)
if (dmg16 != 0) {
    d = fcn.00035c22(dmg16)        ; RandomVary(Unk6) = 50%..100% of Unk6  (0x3ac37)
    fcn.000375eb(memberId, d)      ; apply LP damage                       (0x3ac45)
}
return;
```

### Field meaning
| Field | On-disk | Meaning | Confidence | Evidence |
|---|---|---|---|---|
| Unk1 | u8 (+1) | **Status condition to inflict** (PlayerCondition bit index). `>=12` => none. Observed 1,6,7,11,255 (255=none) | CONFIRMED | `0x3abff` read +1; `0x3ac07 cmp 0xc`; `fcn.000363c2` sets `sheet[+0x1e] |= 1<<Unk1` |
| Unk2 | u8 (+2) | **Gender mask** of who the trap affects: `bit(Gender)` must be set. bit0=Male, bit1=Female. Observed 2 (Female), 3 (both) | CONFIRMED | `0x3abaa` read +2; gated by `1<<sheet[+1]` (`fcn.00036620`=Gender) |
| Unk3 | u8 (+3) | **Target scope**: 0=active/leader, 1=whole party, 2=specific member (id in +5) | CONFIRMED | `0x3aa6d` read +3; switch `0x3aa73..` |
| (pad) | u8 (+4) | unused / 0 | CONFIRMED | not read by handler |
| Unk5 | u8 (+5) | **Specific member id** (only used when Unk3==2) | CONFIRMED | `0x3ab10` read +5 -> `fcn.00035783` |
| Unk6 | u16 (+6) | **Base damage** (subject to RandomVary 50–100%); 0 = no damage | CONFIRMED | `0x3ac2e` read +6; `fcn.00035c22` RandomVary; `fcn.000375eb` apply |
| (pad) | u16 (+8) | unused / 0 | CONFIRMED | not read |

### Plain English
A Trap is applied per affected party member:
1. **Gender filter** — `Unk2` is a 2-bit mask; the trap only affects members whose Gender bit is set (e.g. `3` = everyone, `2` = women only). Members already Unconscious are skipped.
2. **Luck save** — each affected member rolls `PercentRoll(effectiveLuck, 100)`. **On success the member avoids the trap entirely** (a "lucky escape" popup is shown). This is the high-Luck escape gate.
   - Note: the prompt mentioned "Luck/Dexterity"; the original uses **Luck only** (attribute index 5). Dexterity (index 2) is NOT consulted here. CONFIRMED.
3. **On a failed save** — the member is given the status condition `Unk1` (a `1<<Unk1` bit in the conditions word, if `Unk1<12`) AND takes `RandomVary(Unk6)` LP damage (50–100% of `Unk6`).
4. **Scope** (`Unk3`) decides who is rolled: just the leader (0), the whole party slot-by-slot (1), or one named member (2, id in `Unk5`).

### CONFIDENCE summary (Trap)
CONFIRMED: scope switch, gender gate, Luck save, condition infliction (`1<<Unk1`), damage = RandomVary(Unk6), per-member loop. Field offsets verified against `CharacterSheet.cs` (Gender +1, Conditions +0x1E, attrs +0x2A).
COULD NOT DETERMINE:
- The exact PlayerCondition enum order vs `Unk1` bit indices — `fcn.000363c2` simply sets `1<<Unk1` in the conditions word, so `Unk1` is the bit position. Map `Unk1` to UAlbion `PlayerConditions` by bit value (e.g. bit0=Unconscious as used by the skip-check). The observed `Unk1` set {1,6,7,11} corresponds to specific conditions (e.g. Poisoned/Exhausted/etc.) — confirm against `PlayerConditions` flag values, not verified byte-for-byte here.
- Whether `fcn.0007dc07` ("lucky" popup) also plays per-member or once — it is called inside the per-member function, so once per saved member.

### Implementation recommendation (C#)
- Reinterpret `TrapEvent` fields: `Unk1 = ConditionToInflict (bit index, >=12 means none)`, `Unk2 = GenderMask (bit0 Male / bit1 Female)`, `Unk3 = TargetScope (0 leader / 1 party / 2 specific)`, `Unk5 = SpecificMemberId`, `Unk6 = BaseDamage`.
- Add a Trap handler (Querier or FlatMap/DungeonMap, mirroring ChangeIcon). Algorithm per affected member:
  ```csharp
  foreach (member in TargetSet(Unk3, Unk5)) {       // leader / whole party / specific
      if ((Unk2 & (1 << (int)member.Gender)) == 0) continue;   // gender filter
      if (member.HasCondition(PlayerConditions.Unconscious)) continue;
      if (PercentRoll(member.EffectiveLuck, 100)) { ShowLuckyEscape(member); continue; } // Luck save
      if (Unk1 < 12) member.AddCondition((PlayerConditions)(1 << Unk1));
      if (Unk6 != 0) member.Damage(RandomVary(Unk6));   // 50-100% of Unk6
  }
  ```
- Use the project's existing `PercentRoll` / `RandomVary` (already in `DamageCalculator`) and the Watcom RNG for byte-exactness.

---

## OPCODE 0x13 — CREATE TRANSPORT (handler `0x3b6e0`)

### On-disk layout (C# `CreateTransportEvent`) — matches handler
- `record[+1]` = **X** (byte)
- `record[+2]` = **Y** (byte)
- `record[+3]` = **Id** (byte, transport type)
- `record[+4..+5]` = zeroed (skipped)
- `record[+6..+7]` = **MapId** (u16; 0 = current map)
- `record[+8..+9]` = zeroed

### Algorithm (CONFIRMED)
```
handler @ 0x3b6e0:
  record = 0x153160 + idx*0x32 + 0x18
  x  = record[+1]                       ; 0x3b715
  y  = record[+2]                       ; 0x3b720
  id = record[+3]                       ; 0x3b72b  (transport type)
  map= word[record+6]                   ; 0x3b734
  if (x == 0 && y == 0) {               ; 0x3b73b..0x3b747
      x = word[0x153b34];  y = word[0x153b36]   ; default to CURRENT PARTY position
  }
  if (map == 0)  map = word[0x153b32]   ; 0x3b762: default to CURRENT MAP
  fcn.00012db7(eax=map, edx=x, ebx=y, ecx=id)   ; 0x3b781 insert into transport table
  return
```

### Transport-table inserter `fcn.00012db7(map, x, y, id)` — CONFIRMED
```
for (i=0; i<32; i++) {                   ; 0x12de5 cmp 0x20 (32 slots)
    slot = 0x153cda + i*6                 ; 6-byte records
    if (slot[+0] == 0) {                  ; first FREE slot (type 0 = empty)
        slot[+0] = (byte)id               ; transport TYPE
        slot[+1] = (byte)x
        slot[+2] = (byte)y
        slot[+4] = (word)map              ; +3 left as padding
        break;
    }
}
```

### Transport lookup `fcn.00012d15(map, x, y)` — CONFIRMED (the consumer)
```
for (i=0; i<32; i++) {
    slot = 0x153cda + i*6
    if (slot[+0] != 0 && slot[+1]==x && slot[+2]==y && slot[+4]==map)
        return slot[+0];                  ; returns the transport TYPE at that tile
}
return 0;                                 ; none here
```
- Called once, from the **world/travel menu builder** at `0x2200b`: it checks whether a transport sits on the party's current tile; if **not** present it enables a menu flag (`menu[+0x3c] |= 1`). I.e. presence of a transport at the tile toggles a board/disembark option in the world map UI.

### Field meaning
| Field | On-disk | Meaning | Confidence | Evidence |
|---|---|---|---|---|
| X | u8 (+1) | Transport tile **X**; 0,0 => spawn at party's current X/Y | CONFIRMED | `0x3b715`; default `0x3b74b` from `0x153b34` |
| Y | u8 (+2) | Transport tile **Y**; see above | CONFIRMED | `0x3b720`; default `0x153b36` |
| Id | u8 (+3) | **Transport type** (stored as slot type byte; nonzero => occupied). Selects which transport/sprite | CONFIRMED (storage); INFERRED (semantics = ship/raft/etc.) | `0x3b72b`; stored `slot[+0]` `0x12e15` |
| MapId | u16 (+6) | **Map** to place it on; 0 => current map | CONFIRMED | `0x3b734`; default `0x153b32` `0x3b764` |

### Plain English
CreateTransport **spawns a transport object onto the world/overland transport list** (a fixed 32-slot table at `0x153cda`, each slot `{type, x, y, pad, mapId(u16)}`). It does NOT teleport the party and is NOT itself a forced crossing — it merely **places a usable vehicle at a tile** (defaulting to the party's current position / current map when the coordinates / map are zero). The world-map travel code (`fcn.00012d15` at the menu builder `0x2200b`) later checks whether a transport sits on the party's tile to enable boarding/disembarking. The transport **Id** is its type byte (the value that distinguishes ship vs raft vs flying mount); the engine just stores and matches it. So in original gameplay a transport is **always optional** — it is offered as a vehicle the player may board, never an automatic teleport. (The actual board/move-with-transport logic lives in the world-map travel module that consumes the table, not in this opcode.)

### CONFIDENCE summary (CreateTransport)
CONFIRMED: byte mapping (X+1, Y+2, Id+3, MapId+6) — **exactly matches the current C# serdes**; default-to-current behaviour for (0,0) position and map==0; 32-slot 6-byte transport table at `0x153cda`; inserter + lookup.
INFERRED: that `Id` enumerates concrete transport kinds (ship/raft/flying). The handler treats it opaquely; the kind->sprite/movement mapping is resolved by the world-travel module that reads `slot[+0]`. Not byte-traced to a named enum here.
COULD NOT DETERMINE:
- The numeric Id->kind table (which Id is "ship" vs "raft" vs "eagle/flying"). The opcode stores the raw byte; decode the kind from the world-travel consumer or from game-data transport defs.
- Whether any specific map uses CreateTransport as a *story-mandatory* vehicle — structurally it is optional (a placed vehicle), but a given map's scripting could gate progress on using it.

### Implementation recommendation (C#)
- The current `CreateTransportEvent` field mapping (X, Y, Id, MapId) is **already correct** — no serdes change needed.
- Add a handler that inserts into a remake "transport list" keyed by (MapId, X, Y) with `Type = Id`, mirroring the original's 32-slot table semantics:
  - If `X==0 && Y==0`, use the party's current tile.
  - If `MapId == 0`, use the current map.
  - Store `{ Type=Id, X, Y, MapId }`.
- The world/overland map should, when the party stands on a transport tile, offer board/disembark (mirror `fcn.00012d15`). Treat the transport as an **optional vehicle**, not a teleport. Map `Id` to a transport-kind enum once the kind table is recovered from game data (PLACEHOLDER until then: keep the raw `Id` and pick a defensible default kind).

---

## GLOBAL CONFIDENCE / "could not determine" roll-up
- CONFIRMED across all three: the dispatcher record-locator, every data-byte offset each handler reads, and the core algorithm of each.
- Spinner: quadrant->compass orientation constant (UNKNOWN, cosmetic).
- Trap: exact `Unk1`->PlayerConditions bit names (the engine uses raw `1<<Unk1`); the save is **Luck only** (NOT Dexterity) — this corrects the prompt's "Luck/Dexterity" guess.
- CreateTransport: the `Id`->transport-kind table (UNKNOWN; opaque byte at the opcode level) and whether any map makes a crossing mandatory (structurally optional).
