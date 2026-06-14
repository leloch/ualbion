# RE: CreateTransport map event (MapEventType 0x13)

## KNOWN FACTS (given, CONFIRMED upstream)
- Master map-event dispatcher = fcn.000312c2; handler table at 0x13da7c indexed by event type.
- Entry for type 0x13 (CreateTransport) = handler at **0x3b6e0**.
- Remake on-disk layout (src/Formats/MapEvents/CreateTransportEvent.cs):
  byte X; byte Y; byte Id; byte pad; byte pad; u16 MapId; u16 pad.

---

## 1. WHAT THE HANDLER DOES  (CONFIRMED, radare2)

### handler fcn.0x3b6e0 (the CreateTransport dispatch entry)
```
evt   = 0x153160 + 50*[0x13d70a] + 0x18      // pointer to the current event record (10 bytes)
X     = evt[1]   (u8)
Y     = evt[2]   (u8)
Id    = evt[3]   (u8)                         // the "Unk"/Id field
MapId = evt[6]   (u16)

if (X == 0 && Y == 0) {                       // 0,0 -> use party's current tile
    X = [0x153b34]; Y = [0x153b36];           // party world X / Y
}
if (MapId == 0) MapId = [0x153b32];           // 0 -> current map id

fcn.00012db7(eax=MapId, edx=X, ebx=Y, ecx=Id) // append to the spawn table
return
```
Event record byte 0 is the type byte (0x13). So the on-disk 10-byte record is
`[type][X][Y][Id][?][?][MapId.lo][MapId.hi][?][?]` — this MATCHES the C# Serdes EXACTLY
(X, Y, Id, 2 skipped, MapId u16, 2 skipped).

**=> The C# field meanings, now CONFIRMED:**
- `X` = spawn tile X (0 with Y=0 means "the party's current tile")
- `Y` = spawn tile Y
- `Id` = the spawned-object kind id (the transport/object identifier; index into a gfx/NPC table — see below)
- `MapId` = which map it belongs to (0 = current map). The remake's "0 = stay on current map" comment is correct.

### writer fcn.00012db7 = "register a spawned map object"
```
table = 0x153cda      // 32 entries x 6 bytes  (0xC0 = 192 bytes total)
for (i=0;i<32;i++){
    slot = table + 6*i;
    if (slot[0]==0){                 // first free slot (Id 0 == empty marker)
        slot[0]=Id; slot[1]=X; slot[2]=Y; slot[4..5]=MapId(u16);  // slot[3] spare
        break;
    }
}
```
Per-entry 6-byte layout: `[Id][X][Y][spare][MapId.lo][MapId.hi]`.

**So CreateTransport does NOT spawn a ship/raft/disc object directly. It simply appends one
record `(Id, X, Y, MapId)` to a 32-slot "dynamic placed-object" registry.** The Id is what
selects the visual/behaviour; the engine has no hard-coded "is it a ship vs a horse" branch
in this handler — that is entirely data-driven by the Id value.

### reader fcn.00012d15 = "what object id is placed at (X,Y) on this map?"  (the ONLY reader)
```
fcn.00012d15(eax=MapId, edx=X, ebx=Y):
    for each non-empty slot:
        if slot[1]==X && slot[2]==Y && slot[4]==MapId: return slot[0]   // the Id
    return 0
```
Only ONE caller: address 0x2200b, inside a HUD/option-state builder (sets string ids
0x2df/0x1e9/0x1ea/0x259 and flag bits into a UI struct at [ebp-0x10]). At 0x21fe7:
```
if ([0x153ccc] == 0) {                        // only when NOT currently on a transport
    if (lookup(curMap, partyX, partyY) == 0)  // no placed object on our tile
        uiStruct[0x3c] |= 1;                  // enable/flag an action option
}
```
i.e. the placed-object registry is consulted to decide whether the party is standing on a
boardable/placed object at its current tile (gating a HUD action — almost certainly the
"board / use transport" option).

---

## 2. BOARDING / RIDING STATE  (PARTIALLY CONFIRMED)

The "am I on a transport / what icon is the party blob" lives in the word at **0x153ccc**,
which sits 14 bytes BEFORE the spawn table (0x153cd2/0x153cce/0x153ccc are a small header
in front of 0x153cda).

CONFIRMED facts about 0x153ccc:
- It is grouped with the party position triple and copied as a unit:
  fcn.000149a8 packs `0x153b34 (X), 0x153b36 (Y), 0x153b38 (dir), 0x153ccc, 0x1482b4`
  into a snapshot struct at 0x134d08 and restores them together (save/restore party pose).
- fcn.0001e52d packs `0x153ccc` into the party-marker draw struct at 0x13504e together with
  party X/Y before calling the blob-render routine (fcn.0001e650). So 0x153ccc is part of
  the party's on-map VISUAL/movement pose.
- The HUD gate above only queries the placed-object table when `0x153ccc == 0`
  (i.e. when on foot). When non-zero the party is treated as already on a transport.

INFERRED (consistent with all evidence, not byte-proven):
- `0x153ccc` = the current transport / party-icon id (0 = on foot). Boarding sets it to the
  placed object's Id; this changes which sprite the party blob draws and (because it is
  carried with the position pose) which movement/collision ruleset applies.
- The movement passability check that makes water walkable when riding a ship is the
  collision routine reached via the party-step path (fcn.000129c6 is the tile-passability
  query used right next to the lookup at 0x22034); I did not fully trace the exact branch
  that says "if on transport, water is passable". COULD-NOT-CONFIRM the precise faster/flying
  modifiers per transport id.

---

## 3. SAVEGAME PERSISTENCE  (CONFIRMED via offset arithmetic)

Memory→save mapping anchor: the active-spells table at mem 0x153b3a == save header
offset 0x11 (the C# `ActiveSpells`, 0x50 ushorts). From there:
- mem 0x153cda (spawn table) == save offset 0x11 + (0x153cda-0x153b3a)=0x1A0 → **0x1B1 ≈ 0x1B2**.
- The spawn table is exactly **0xC0 (192) bytes** (32×6).
- In SavedGame.cs the field at offset **0x1B2** is `Misc` (`MiscState`, documented "Len: 0xC0",
  192 bytes, serialized as 24 opaque `long`s in src/Formats/Assets/Save/MiscState.cs).

**=> CONFIRMED: the CreateTransport placed-object registry IS persisted in the savegame — it
is the `MiscState` 0xC0 block.** It is currently round-tripped as opaque bytes (24 longs),
so saves already preserve transports correctly even though nothing reads them.

The small header (0x153ccc..0x153cd9, the on-transport/icon state) falls just before Misc,
inside the already-serialized span Unk1A2 / ActiveItems / HoursSinceResting / CombatPositions
(save 0x1A2..0x1B2). So the on-transport state is also already persisted (as opaque bytes),
not yet surfaced as a typed field.

---

## 4. IS IT ON THE CRITICAL PATH?  (CONFIRMED)

- `grep create_transport` and `grep CreateTransport` over **F:\Dev\albion\ualbion\mods\**
  → **ZERO hits.** No converted Albion map uses the CreateTransport (0x13) event.
- In UAlbion source, CreateTransportEvent is parsed (MapEvent.cs:70, MapEventType 0x13) but
  has **no handler component anywhere** — it is decoded and dropped.
- The Gratogel↔Maini and other sea crossings in shipped Albion are driven by Teleport /
  MapExit events (player talks to a ferryman → scripted teleport), NOT by spawning a
  player-drivable transport. The 0x13 opcode appears to be a vestigial / engine-supported-
  but-unused feature in the retail data (like ask_surrender 0x1C).

CONFIDENCE on necessity: HIGH that it is **NOT** required for a 1:1 retail playthrough.
(Caveat: I grepped the converted mods only; if any unconverted XLD map block contains a 0x13
byte it would be missed, but the absence across all converted maps plus the teleport-based
crossings strongly indicates it is unused on the critical path.)

---

## 5. C# IMPLEMENTATION RECOMMENDATION

Because no shipped map uses it and saves already round-trip the data as opaque bytes, this is
**LOW priority for 1:1**. The minimal faithful design, if implemented:

A. **Transport registry (mirrors the 32×6 table):**
   - Add a typed view over the existing `MiscState` 0xC0 block instead of inventing new save
     state. Define `struct PlacedTransport { byte Id; byte X; byte Y; byte _spare; ushort MapId; }`
     and (re)serialize MiscState as `PlacedTransport[32]` (keeping total = 0xC0 so existing
     saves stay byte-identical). This is the faithful, zero-drift representation.
   - Keep the "first slot with Id==0 is free; Id==0 means empty" semantics.

B. **Event handler:** add a `CreateTransportChain`/handler (a `Component` that
   `On<CreateTransportEvent>`), replicating fcn.0x3b6e0:
   ```
   x = e.X; y = e.Y;
   if (x==0 && y==0) { x = party.TileX; y = party.TileY; }
   var map = e.MapId.IsNone ? state.MapId : e.MapId;
   miscState.AddTransport(e.Id, x, y, map);   // append to first free slot
   ```
   Plug into the same place other map events are dispatched (the MapEvent → game-event
   bridge; see how MapExit/Teleport handlers are wired under src/Game/).

C. **Boarding + movement:** add an `OnTransportId` (byte, 0 = on foot) to the live party
   state, persisted into the 0x153ccc slot (currently opaque in the Unk1A2..CombatPositions
   span — carve it out as a typed `ushort TransportIcon`/`OnTransport` field there).
   - When the party stands on a tile whose registry lookup (match X/Y/MapId) returns a
     non-zero Id, expose a "board" HUD action (the gate at 0x21fe7).
   - Boarding sets `OnTransport = Id`; this should (a) swap the party map sprite and
     (b) relax the 2D collision rule so water/relevant terrain becomes passable.
     Hook into the existing 2D movement/collision in src/Game/Entities/Map2D/ (the
     passability check that today blocks the party from non-walkable tiles).
   - I could NOT byte-confirm the per-Id movement modifiers (water vs flying vs faster), so
     mark those `// PLACEHOLDER:` and derive transport behaviour from the Id→gfx mapping
     when a concrete in-game transport is identified.

D. **Plugs into:** MapEvent dispatch (handler), `SavedGame.MiscState` (registry storage,
   already persisted), party live state + 2D map entity sprite/collision (board/ride effect),
   and the HUD/context-menu builder (board action). No new save-format growth needed.

---

## CONFIDENCE SUMMARY
- Handler reads X/Y/Id/MapId and appends to a 32×6 registry: **CONFIRMED**.
- C# field meanings (X, Y, Id, MapId; 0,0=party tile; MapId 0=current): **CONFIRMED**.
- Registry == savegame `MiscState` 0xC0 block (already persisted opaquely): **CONFIRMED** (offset arithmetic).
- 0x153ccc = on-transport / party-icon state, persisted with party pose: **CONFIRMED it's part of party pose; INFERRED it's the transport id**.
- Not used by any shipped/converted map; crossings are teleport-driven: **CONFIRMED (mods grep)**.

## COULD NOT DETERMINE
- The exact Id→graphic / Id→behaviour mapping (which Id = ship vs raft vs disc vs horse), and
  the precise movement modifiers (water-passable / faster / flying) per transport. The handler
  itself is behaviour-agnostic; this lives in the collision/movement + gfx-selection code keyed
  on 0x153ccc / the looked-up Id, which I traced to the passability query (fcn.000129c6) and the
  party-blob draw (fcn.0001e650) but did not byte-decode branch-by-branch.
- Whether any UNconverted XLD map block contains a raw 0x13 event (only converted mods grepped).
