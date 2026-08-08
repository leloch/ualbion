# docs/re/RE_CHEST_TRAP.md — The EXACT source of Albion's "chest is trapped" flag

Follow-up RE pass to settle the one item docs/re/RE_FIDELITY2.md left unconfirmed: **where does the
runtime lock-record `+0x1E` trap-armed flag come from in the data?**

## BOTTOM LINE UP FRONT

**The trap-armed flag is NOT a data byte or a packed bit in the chest/door record. It is
*derived at open-time* from the event node's link field: a chest (or door) is treated as trapped
iff its map-event node has a follow-on event link that is not `0xFFFF`.** Concretely:

```
record[+0x1E] (trap armed)  =  (word at event-node offset +8  !=  0xFFFF) ? 1 : 0
```

Event-node offset +8 is the **first link word after the 8-byte event body** — in the remake's
`EventNode`/`BranchNode` model this is the **`NextIfFalse` branch** of the chest/door event
(both `ChestEvent` and `DoorEvent` are `IBranchingEvent`). The trap *content* (the harm) is
whatever event chain that link points at; it is fired via `fcn.0003529d(1, ChestId)` /
`fcn.000350fc(1, ChestId)` keyed by the chest's own id. There is **no separate trap byte, no
high-bit packing, and no chest-properties table** — the prior doc's hypotheses (a)/(b)/(c) are
all disproved; the answer is (c-variant): "elsewhere = derived from the node link, not stored."

This is exactly why the remake's plumbing already fits: `ChestEvent`/`DoorEvent` implement
`IBranchingEvent`, and `InventoryScreenManager.InventoryClosed(triggeredTrap, …)` already does
`source.SetResult(!triggeredTrap)` so that a triggered trap routes the event chain down the
**false branch** (= the trap chain).

---

## KNOWN FACTS (verified this session, radare2 on `albion_aaa`)

### The chest map-event handler — `fcn.0003a396` (event type 0x03)
Event record base = `0x153160 + word[0x13d70a]*0x32 + 0x18` (`word[0x13d70a]` = current map/event
index). Byte layout of the 10-byte event body it reads (matches the remake's `ChestEvent.Serdes`):

| Off | Field | Handler read |
|---|---|---|
| +1 (u8)  | PickDifficulty   | `0x3a451 mov al,[eax+1]` → `fcn.000584e8` eax arg |
| +2 (u16) | Key              | `0x3a444/0x3a437 mov al,[eax+2]/[+3]` → edx/ebx |
| +4 (u8)  | UnlockedText     | `0x3a42a mov al,[eax+4]` → ecx |
| +5 (u8)  | OpenedText       | `0x3a41c mov al,[eax+5]` → stack arg_10h |
| +6 (u16) | **ChestId**      | `0x3a40f mov ax,[eax+6]` → stack arg_14h |
| +8 (u16) | **link / NextEvent** | see below — drives the trap-arm flag |

The handler computes the trap-arm flag immediately before the call:
```
0x3a3e2  ax = word[event+8]                 ; the first link word
0x3a3ee  cmp ax, 0xffff
0x3a3f5  if (ax != 0xffff) [ebp-0x10] = 1   ; (dword write → high bytes 0)
0x3a3fe  else             [ebp-0x10] = 0
0x3a405  eax = dword[ebp-0x12]; sar eax,0x10 ; extracts byte[ebp-0x10] = the 0/1 flag
0x3a40b  push eax                           ; arg_18h = trap-arm flag
0x3a45b  call fcn.000584e8(diff, key.lo, key.hi, unlk, open, chestId, armFlag)
```
So **`arg_18h` (the eventual `+0x1E` source) = `(word[event+8] != 0xFFFF) ? 1 : 0`.** CONFIRMED.

The door handler `fcn.0003a25f` is structurally identical (door.c path through `fcn.0005a0e6`):
it sets the same `arg_18h = (word[event+8] != 0xFFFF)` flag. So **doors and chests use the same
trap-arm derivation.** CONFIRMED.

### The chest open dispatcher — `fcn.000584e8`
Stores the args into globals and calls the chest UI control machinery. The arm flag lands in
`word[0x176f30]`:
```
arg_18h (arm flag) -> 0x176f30           ; 0x5855a
arg_14h (ChestId)  -> 0x176f26           ; used as eventNum for fcn.000350fc(1, ChestId)
eax     (diff)     -> 0x176f24
```
`0x176f30` is read at `0x588b4` (the chest control-source builder, below). CONFIRMED.

### The chest control-source builder — `fcn.00058883`
Builds a 4-word "source struct" on the stack (`[ebp-0x18]`) and calls
`fcn.0007410a(type=0x32, descriptor=0x13e718, &src, …)`:
```
src+0 = word[0x176f24]  = PickDifficulty
src+2 = word[0x176f32]  = Key
src+4 = word[0x176f30]  = ARM FLAG          ; 0x588b4  ★
src+6 = 0x176f36        = result-word ptr
```
The door builder `fcn.0005a48f` produces the identical struct (`src+4 = word[0x176f42]` arm flag,
fed from the door dispatcher). CONFIRMED.

### The shared lock-control INIT handler — `fcn.0005a6c0`
`fcn.0007410a(descriptor 0x13e718)` looks up the control vtable at `0x13e6dc`. Decoding that
vtable (array of `{u16 msgId, u32 fnptr}`, stride 6): the **msgId==1 (create/init) entry is
`fcn.0005a6c0`**. Both chest (type 0x32) and door (type 0x70) create through descriptor
`0x13e718`, so they share this init handler. It copies the source struct straight into the
lock record:
```
0x5a6f2  word[rec+0x1a] = word[src+0]   ; PickDifficulty
0x5a700  word[rec+0x1c] = word[src+2]   ; Key
0x5a70e  word[rec+0x1e] = word[src+4]   ; TRAP ARMED   ★  <-- THE SOURCE
0x5a71b  dword[rec+0x20] = dword[src+6] ; result ptr;  *result = 0
```
**This is the single write site of `+0x1E` from data anywhere in the binary.** It copies
`src+4`, which is the `(NextEvent != 0xFFFF)` flag derived in the event handler. CONFIRMED.

(Note: `fcn.0005608b` writes only `+0x1A`/`+0x1C` and *not* `+0x1E`; it is a **different**
control type's init, NOT the chest/door lock init. An earlier read mistook it for the chest
init. The chest/door lock control is `descriptor 0x13e718` → vtable `0x13e6dc` → init
`fcn.0005a6c0`, which DOES arm `+0x1E`. CONFIRMED via the vtable dump.)

### The pick + trap-fire code — `door.c` `0x5ab33` (re-confirmed, matches docs/re/RE_FIDELITY2.md)
`fcn.00074bda(id)` returns the lock record (`dword[id*4 + 0x179280]`). On the SKILL pick path:
```
0x5ab8d  if rec[+0x1a] >= 100 -> Msg 523 "cannot be picked", return
0x5abaa  roll = fcn.00035fd5(leader, 3)            ; lockpicking skill roll
0x5abc2  if roll >= rec[+0x1a] -> success (set *result=1)   ; auto-succeed
         else chance = (100 - diff)*roll/100; fcn.00035b15(chance,100)  ; percent roll
0x5ac0a  if success -> *result = 1 (opened)
0x5ac2b  else if rec[+0x1e] != 0 (TRAPPED):
0x5ac3c     evade = fcn.00035b96(leader, 2)        ; PercentRoll(EffDexterity,100)
0x5ac46     if evade -> Msg 524 "evades the trap"
0x5ac66     else fcn.00062a41(108,…) AV sting; Msg 525 "trap triggered"; *result = 3
0x5ac83     rec[+0x1e] = 0                          ; one-shot disarm (both evade & trigger)
0x5ac8b   else -> Msg 538 (failed pick, no trap, free retry)
```
CONFIRMED. (Stat 2 = Dexterity; result codes 1=opened, 3=trap.)

### The trap firing — `fcn.0003529d(1, ChestId)` / `fcn.000350fc(1, ChestId)`
On result==3 the chest handler runs `fcn.0003529d(1, word[event+6])` (`0x3a4aa`), i.e. it runs
the event chain whose entry id = the **ChestId** (`fcn.0003529d` is the type-dispatched
event-chain runner; arg eax=1 selects the chain kind, edx=entry id). The chest-internal trap
branch in `fcn.000584e8` similarly calls `fcn.000350fc(1, ChestId)`. So the **trap content is a
per-chest event chain keyed by the chest's own id**, not a hardcoded effect. CONFIRMED that it's
a script; the specific chains are per-map data (not enumerated here).

---

## ANSWERS TO THE THREE QUESTIONS

1. **Where is the trap encoded?** Not in any of the 9 `ChestEvent` data bytes, not a packed bit,
   not a ChestId-indexed properties table. It is **derived at open time from the event node's
   first link word (`+8`)**: trapped ⇔ that link ≠ `0xFFFF`. In the remake's node model that
   first link is the **`NextIfFalse` branch** of the (branching) chest/door event. CONFIRMED.

2. **What eventNum does the trap fire?** `fcn.0003529d`/`fcn.000350fc` are called with
   **`(1, ChestId)`** — the trap runs the event chain keyed by the chest's **own ChestId**, not a
   fixed script id and not literally the "next in the map chain." (The link at `+8` is only the
   *arming signal*; the chain that actually runs is the one associated with the chest id.)
   CONFIRMED for the call args; the chain lookup keyed by ChestId is how Albion stores per-chest
   "extra" event chains.

3. **Data dump needed?** No — the source was resolved statically. Note `CHESTDT*.XLD` are the
   chest **contents** (inventories), NOT lock/trap definitions; the lock/difficulty/key live in
   the map-event records (`MAP*.XLD`), and the trap flag is derived from the node link as above.

---

## CONFIDENCE

| Claim | Status |
|---|---|
| `+0x1E` written exactly once, by `fcn.0005a6c0` (`0x5a70e`), from `src+4` | **CONFIRMED** |
| `src+4` = `(word[event+8] != 0xFFFF) ? 1 : 0` (the node's first link word) | **CONFIRMED** |
| Chests and doors share descriptor `0x13e718` → init `fcn.0005a6c0` → both arm `+0x1E` | **CONFIRMED** (vtable `0x13e6dc` decoded) |
| Trap is NOT a packed bit / NOT a ChestId properties table | **CONFIRMED** (no other `+0x1E` writer exists; no such table referenced) |
| Trap fires `fcn.0003529d(1, ChestId)` (per-chest chain) on result 3 | **CONFIRMED** |
| Evade = `PercentRoll(EffDexterity,100)`; one-shot disarm | **CONFIRMED** (re-verified `0x5ab33`) |
| The first link word == the remake's `NextIfFalse` (vs `Next`) | **INFERRED (high confidence)** — `SerdesNode` reads `NextIfFalse` first for `IBranchingEvent`; the original reads `word[+8]` (first link) as the arm flag. Worth a one-time runtime check against a known trapped chest map. |
| The specific trap event chains per chest (their contents) | **NOT enumerated** (per-map data) |

---

## C# IMPLEMENTATION PLAN (concrete)

The good news: **no new data field is needed, and no serdes change is needed.** The trap flag is
not stored — it is "does this locked event have a false-branch / link?" The remake already models
chest/door events as `IBranchingEvent` and already threads `triggeredTrap` through. Wire it up:

### 1. Expose "is trapped" on the lock event (derived, no serdes change)
`ChestEvent`/`DoorEvent` are `IBranchingEvent`. Add a derived helper on `ILockedInventoryEvent`:
```csharp
// IBranchingEvent already carries the false branch; trapped == has one.
bool IsTrapped => /* the branching event's NextIfFalse (first link) is non-null */;
```
Implement it where the branch node is visible (the event node carries `NextIfFalse`). If the
event object itself doesn't see its node, pass the `BranchNode` (or a `bool hasFalseBranch`) into
`InventoryLockPane`/`InventoryScreenManager` when the lock screen is opened. Mark it
`// trap-armed iff the lock event has a false branch (orig: word[node+8] != 0xFFFF)`.

### 2. Add a runtime one-shot "armed" copy
Keep a session-scoped `bool _trapArmed` initialised from `IsTrapped`, so the trap can be disarmed
after one trigger/evade (orig zeroes `+0x1E` at `0x5ac83`). Persist per lock id if you want it to
survive re-opening the same chest in a session (orig state lives in the `0x179280[]` table; for
the remake, keying off the lock event id is sufficient).

### 3. Wire the trap into the SKILL-pick FAILURE path only
In `InventoryLockPane.PickLock()` — currently the `else` of `CanPick(leader)` just shows
`Lock_LeaderCannotPickThisLock`. Replace with (door.c `0x5ac28`+):
```csharp
else
{
    if (_trapArmed)
    {
        _trapArmed = false; // one-shot, regardless of outcome
        int dex = leader.Effective.Attributes.Dexterity.Current; // EffDexterity
        bool evaded = RaiseQuery(new QueryRandomChanceEvent((ushort)dex, QueryOperation.GreaterThan, 0)); // rand%100 <= dex
        if (evaded)
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_LeaderEvadesTheTrap)));   // 524-equiv
        else
        {
            // AV sting (orig fcn.00062a41(108,…)) + message 525-equiv, then close as "trap triggered"
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_TheTrapIsTriggered)));     // 525-equiv
            Raise(new TrapTriggeredEvent());  // -> InventoryScreenManager.InventoryClosed(triggeredTrap:true, unlocked:false)
        }
    }
    else
    {
        Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_LeaderCannotPickThisLock))); // 538-equiv, free retry
    }
}
```
Add a `TrapTriggeredEvent` (or reuse a close path) so `InventoryScreenManager` calls
`InventoryClosed(triggeredTrap: true, unlocked: false)`. It already does
`source.SetResult(!triggeredTrap)` → returns **false** → the querying event chain takes the
**false branch (`NextIfFalse`)**, which is the trap event chain. **This is the analogue of
`fcn.0003529d(1, ChestId)`** — the original's per-chest trap chain is the remake's false branch.
Stop hardcoding `triggeredTrap=false` (the existing `// TODO: Test with trapped chests/doors`).

### 4. Keep the item path (key / Lockpick) trap-free
`LockClicked()` (key & Lockpick) must NOT touch the trap — the original never reads `+0x1E` on the
item path, so **using a Lockpick item bypasses the trap entirely**. Leave `LockClicked()` as-is.
This is a legitimate strategy, not a bug.

### 5. Do NOT
- Do **not** add a trap byte/bit to `ChestEvent.Serdes` — there is none in the data.
- Do **not** add a Thief's-Amulet check — none exists.
- Do **not** add a per-attempt cost to skill picking — failed untrapped picks are free, unlimited.
- Do **not** apply a hardcoded damage amount — the harm is whatever the false-branch chain does
  (typically a `TrapEvent`/`DataChange` health-loss chain authored per chest).

### Files to touch
- `src/Formats/MapEvents/ILockedInventoryEvent.cs` — add derived `IsTrapped` (or pass the branch).
- `src/Game/Gui/Inventory/InventoryLockPane.cs` — DEX-evade roll + trap-trigger on pick failure.
- `src/Game/Gui/Inventory/InventoryScreenManager.cs` — stop hardcoding `triggeredTrap=false`;
  add a trap-close entry (`InventoryClosed(true, false)`); the false-branch routing already works.
- (Possibly) `src/Base/SystemText.*` — ensure 522–525, 538 equivalents are present
  (Lock_TheTrapIsTriggered / Lock_LeaderEvadesTheTrap).

---

## WHAT COULDN'T BE DETERMINED
- The exact per-chest trap event-chain **contents** (`fcn.0003529d(1, ChestId)` targets) — these
  are per-map data, not handler code. The mechanism is fully resolved; the specific chains are
  whatever each map author attached.
- A live runtime confirmation that the first link word (`event+8`) maps to the remake's
  `NextIfFalse` rather than `Next` — strongly inferred from `SerdesNode` ordering, but a single
  in-game check against a known trapped chest would nail it.
