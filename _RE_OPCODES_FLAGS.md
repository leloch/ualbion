# Reverse-engineering Signal / Execute / ChangeUsedItem map-event opcodes

> Live document. Findings appended as confirmed. Read-only RE against MAIN.EXE (radare2 project `albion_aaa`).
> Companion to `_RE_ASK_SURRENDER.md` (dispatcher table) and `_RE_INDEX.md`.

## KNOWN FACTS (running list)

### Dispatch / event-record model (CONFIRMED)
- Map-event dispatcher = **`fcn.000312c2`**. Handler table at **`0x13da7c`**, indexed by event-type byte (`call dword [type*4 + 0x13da7c]`, evidence `0x3139e..0x313a1`). Table re-dumped from binary — matches `_RE_ASK_SURRENDER.md`.
  - idx 7 ChangeUsedItem = **0x3bed3**, idx 0xF Signal = **0x3af2d**, idx 0x14 Execute = **0x3b78f**. (Re-verified by dumping `pxw 0x13da7c`.)
- **Per-zone "event-manager" control block**: `[0x13d70a]` (u16 = current zone/map slot index) × **0x32 (50 bytes)** + base **`0x153160`**. Every one of the three handlers recomputes this same pointer. Call it `ZMB` (zone-manager block). Evidence: identical prologue `mov ax,[0x13d70a]; imul eax,0x32; mov edx,0x153160; add` in dispatcher (`0x312dc`), Signal (`0x3af47`), Execute (`0x3b7a9`), ChangeUsedItem (`0x3bef9`).
- **ZMB field layout** (50 bytes), from dispatcher `fcn.000312c2`:
  - `ZMB[+0]` (u16) = interpreter state/mode (Signal requires ==1; ChangeUsedItem switches on it). 
  - `ZMB[+2]` (u16) = flags word (bit0 tested at `0x313bc`).
  - `ZMB[+4]` (dword) = event-list base pointer/handle (passed to `fcn.0008b739` to deref).
  - `ZMB[+0x10]` (u16) = **current event-node index** within the chain (0xFFFF = no active chain → dispatcher exits).
  - `ZMB[+0x14]` (dword) = byte offset added to the deref'd base.
  - `ZMB[+0x18]` = start of the **current 12-byte in-memory event node** (the dispatcher copies `node[+0]` = the on-disk 10-byte record into ZMB+0x18, plus 2 trailing bytes). Node layout below.
- **Event NODE layout** (the 10 on-disk bytes live at `ZMB+0x18`; i.e. node[+0]=type byte... but handlers index the node relative to ZMB, so node-field +N == `ZMB[+0x18+N]`). In the handlers the node base used is either `ZMB` directly with field offsets +0x28/+0x2a/+0x2c (= node +0x10/+0x12/+0x14 — these are the runtime-appended fields, NOT the on-disk 10 bytes) OR `ZMB+0x18` (= node[+0], the on-disk record). See per-opcode notes.
  - In-memory node is **12 bytes** in the chain array (`imul ...,0xc` at dispatcher `0x31323`); the 10 on-disk bytes + 2 runtime bytes.
- **Active-actor / NPC context**: `[0x13d708]` (u16 index) × **0x18 (24 bytes)** + base **`0x153930`** = current NPC/actor state block (`NPCB`). Used by Execute and ChangeUsedItem. Evidence Execute `0x3b7c1`, ChangeUsedItem `0x3c029`.
- **"Used-item" actor context** (for ChangeUsedItem): `[0x1597d0]` (dword = active party-member sheet pointer), `[0x1597d4]` (u16 = active member index, 1-based). Set at item-use / dialogue entry points (`0x2252f`, `0x2bc12`, `0x2bd2f`, `0x2be53`).

### Chain-advance output globals (CONFIRMED — how a handler tells the interpreter what to do next)
- `[0x13daf6]` (u16) = **next-action / disposition code** written by every handler. Values seen: 0 = fall through / continue to linked next node; 1 = jump to a specific node index (the index is in `0x13daf8`); 3 = stop / branch-not-taken.
- `[0x13daf8]` (u16) = **next node index** (used when `0x13daf6==1`).
- `[0x13dafc]` (u16) = the node's **link/next field** = `node[+0x19]` (the on-disk "next event" byte), copied near every handler's tail.
- `0x13daf4` = base of this little output struct; passed to **`fcn.0002fce1`** (the chain-advance/commit routine) at each handler tail. (Execute calls **`fcn.0002fd69`** instead — a sibling.)

---

## OPCODE 0xF — Signal  (handler 0x3af2d)

### Disassembly map (CONFIRMED addresses)
- `0x3af47` load ZMB (`[0x13d70a]*0x32 + 0x153160`).
- `0x3af5a/0x3af63` zero outputs `[0x13daf6]=0`, `[0x13daf8]=0`.
- `0x3af6f` `cmp word[ZMB+0] , 1` (interpreter mode must be 1) — `jne` to tail `0x3b03a` (does nothing but commit link).
- `0x3af83` read `ax = word[ZMB+0x28]` → local `kind` (`ebp-0xc`). This is node runtime field +0x10.
- `0x3af8a..` switch on `kind`: ==1 path, ==2 → tail, 0 → the signal-state check at `0x3afac`.
- `0x3afac` `cmp word[ZMB+0x28],0`; if !=0 → tail.
- `0x3afb7/0x3afc1` `cmp word[0x15e5be],0` / `cmp word[0x15e5c2],0` — two global gates.
- `0x3afcd` `dx = word[0x15e5c0]`; `cmp dx, word[ZMB+0x2a]` (`ZMB+0x2a` = node runtime field +0x12 = the SIGNAL ID).
- `0x3afdf` if matched-state → `[0x13daf6]=3` (stop).
- `0x3aff3` else `ax = word[ZMB+0x2a]` (signal id); `call fcn.00035783` (= **SignalIdToSlot**, searches table `0x153cbe`, 6 entries u16, returns 1-based slot or 0xFFFF).
- `0x3b00d` `cmp eax,0xffff` (`ebp-4` = slot) ; if found → `[0x13daf6]=1`, `[0x13daf8]=slot` (jump the interpreter to that node index).
- `0x3b03a` tail: `[0x13dafc] = byte[ZMB+0x19]` (node link), then `fcn.0002fce1(0x13daf4)` commit.

### Helper fcn.00035783 = SignalIdToSlot (CONFIRMED)
- Loops i=0..5: `word[0x153cbe + i*2] == inputSignalId` → return i+1; else 0xFFFF. So there are **6 signal "slots"**, a table of 6 signal-id words at `0x153cbe`, and the engine maps an incoming signal id to its slot index, then directs the event interpreter to jump to the node index = slot.

### Globals (Signal) — RESOLVED
- `0x153cbe` = a **6-entry (u16) per-zone "signal slot" table** (CONFIRMED). Set up / reset by the zone-init routine **`fcn.00034b66`** (`0x34bef mov word[0x153cbe],1`), which lives in **`g:\albion\src\prtlogic.c`** (source-path string leaked at `0x130f3c` — this is the "part-logic" map-event interpreter). The 6 slots are read into a local array and handed to **`fcn.000236d6`** (record type 0x11=17) → they are part of the **persisted zone state** (saved/restored with the savegame). So the 6 signals are persistent per-zone flags.
- `0x15e5be` / `0x15e5c0` / `0x15e5c2` = a 3-word "active/pending signal" register set, written by the NPC/automap subsystem (`fcn.00045d64` @ `0x45db2`, plus `0x45f3a`, `0x460d6`). `0x15e5be`!=0 means "a signal is currently active/pending"; `0x15e5c0` = its id (matched against the node's `SignalId` at `ZMB+0x2a` to decide stop-vs-continue).

### Plain-English (INFERRED, see confidence)
- Signal is NOT a simple global-flag setter. It is an **event-chain branch/dispatch primitive**. `SignalId` (= node field at ZMB+0x2a) is looked up in a 6-slot signal table (`0x153cbe`); if present, the interpreter is told (`0x13daf6=1`, `0x13daf8=slot`) to **jump to the event node whose index = the signal slot** — i.e. it routes execution to a registered handler chain. There is also a "current signal" register (`0x15e5be/c0/c2`) that, when its id matches, instead **stops** the chain (`0x13daf6=3`).
- CONFIDENCE: the table lookup + chain-jump = CONFIRMED from code; the *semantic naming* ("which game system registers the 6 signals / writes 0x15e5c0") = INFERRED, pending xref of who writes `0x153cbe` and `0x15e5c0`.

---

## OPCODE 0x14 — Execute  (handler 0x3b78f)

### Disassembly map (CONFIRMED)
- `0x3b7a9` ZMB; `0x3b7b9` `add 0x18` → `ebp-8 = ZMB+0x18` = the on-disk node record base (so `node[+N]`).
- `0x3b7c1` NPCB = `[0x13d708]*0x18 + 0x153930` → `ebp-0xc`.
- `0x3b7d7` `cmp byte[node+1], 0` (= **Unk1**, the first data byte after the type byte).
  - if Unk1 != 0 (`0x3b7dd`): `or byte[NPCB+0], 2` — **set bit1 of the active NPC's flag byte**.
  - else (Unk1 == 0, `0x3b7e5`): `and byte[NPCB+0], 0xfd` — **clear bit1**; then `call fcn.0002fd69` (chain-advance, returns next-index in ax → `ebp-4`); if that result == 0 (`0x3b7f8 jne skip`): `dx = word[node+8]` (= **Unk8**), `word[node+0xa] = dx` (copy Unk8 into node+0xa).
- tail `0x3b808` epilogue (no separate commit visible in first 40 — see note).

### Plain-English (CONFIRMED structure, INFERRED naming)
- **Execute toggles a flag (bit 0x02) on the currently-active NPC** based on `Unk1`:
  - `Unk1 != 0` → set NPC flag bit 0x02 (enable/activate).
  - `Unk1 == 0` → clear NPC flag bit 0x02 (disable/deactivate), AND (when the chain-advance helper reports state 0) copy `Unk8` into the node's runtime field +0xa.
- So `Unk1` is a **boolean enable/disable** for the NPC's "execute" behaviour; `Unk8` is a **value latched into the node** when disabling (a saved target/next-event id, written to node+0xa). Bit 0x02 on `NPCB[+0]` is almost certainly the NPC's "active/scripted" state bit.
- CONFIDENCE: control flow + which bit + which fields = CONFIRMED. The meaning of NPC flag bit 0x02 and node+0xa = INFERRED (pending xref of who reads `NPCB[+0] & 2` and `node[+0xa]`).

---

## OPCODE 0x7 — ChangeUsedItem  (handler 0x3bed3)

### Disassembly map (CONFIRMED)
- `0x3bef2` `call fcn.00031699` (UI/lock prologue).
- `0x3bef9` ZMB; `0x3bf0c` `ebp-0x20 = ZMB+0x18` (node record).
- `0x3bf15` `eax = [0x1597d0]` (active party-member sheet) → `fcn.0008b739` deref → `ebp-0x14` = sheet base.
- `0x3bf24` `ax = [0x1597d4]` (active member index, 1-based). Branch: if >9 use offset 0x31c (backpack region), else offset 0x2e6 region; compute `ebp-0x10` = pointer to a per-member byte (slot-count style). (This is selecting the active member's inventory area.)
- `0x3bf6a` `al = byte[ebp-0x10]`; `cmp 1; jle 0x3bfca` — if the member has <=1 of *something* (slot count? carried-item count?), skip the "can't" message.
  - else `fcn.00049c51(sheet+0x31c, 0x18)` (scan 24 backpack slots) → if `ax!=0` show a **message** (builds string `0x12c=300` via `fcn.00081f42`/`fcn.0002f8b4`, arg `[0x15e3e4]`) and set `ebp-4 = 0` (abort flag). i.e. "inventory full / can't" guard.
- `0x3bfca` `fcn.0008b808` (release deref); `cmp word[ebp-4],0; je 0x3c0b4` (if aborted, exit doing nothing).
- **Determine the item to CONSUME** → `ebp-8` (init 0xFFFF):
  - `0x3bfe6` `word[ebp-0x164] = word[ZMB+0]` (interpreter mode); switch:
    - mode 0 / >1 (`jbe 0x3c00a`): read `word[ZMB+0x2a]`; if `== 4` → `ebp-8 = word[ZMB+0x2c]` (the map-event "used item" register, runtime node field +0x14).
    - mode 1 (`je 0x3c027`): NPCB = `[0x13d708]*0x18+0x153930`; if `word[NPCB+6] == 0x2e (46)` → `ebp-8 = word[NPCB+8]` (the NPC's "applied item").
  - i.e. `ebp-8` = **the item the player just USED/applied** (sourced from either the map trigger register or the active NPC's use-item field).
- `0x3c057` `cmp word[ebp-8],0xffff; je 0x3c0b4` (no used item → exit).
- `0x3c064` **CONSUME**: `fcn.0003e42a(sheet=[0x1597d0], item=ebp-8, ebx=1, ecx=5)` → op 5 = SUBTRACT 1 (remove one of the used item). Return → `ebp-0xc`; `cmp 0; je 0x3c0b4` (only continue if inventory actually changed).
- `0x3c088` **GIVE**: `fcn.0003e42a(sheet, item=word[ZMB+0x18+6] = node ItemId field (+6), ebx=1, ecx=4)` → op 4 = ADD 1 (give one of the event's `ItemId`). Return → `ebp-0xc`; `cmp 0; je 0x3c0b4`.
- `0x3c0af` if both succeeded: `call fcn.0003165c` (commit / refresh — likely re-renders inventory / advances chain).
- tail `0x3c0b4` epilogue.

### Helper fcn.0003e42a = ModifyInventoryItemCount(sheet, itemId, count, op) (CONFIRMED)
- Args (Watcom regcall): eax=sheet ptr, edx=itemId, ebx=count, ecx=op.
- Computes current total = equipped (`fcn.00037f6e`) + backpack (`fcn.00037ff3`); checks item present (`fcn.0003e1f8`); applies arithmetic op via `fcn.0003e0d5`:
  - **op 4 = ADD** (clamped to max), **op 5 = SUBTRACT** (clamped to 0); other ops 0/1/3/6 = set-0/set/set/percent.
- If new total > old → adds item stacks into backpack slots (region `sheet+0x31c`, 24 slots × 6 bytes, item-id at slot+0x320), via `fcn.0004a621`/`fcn.0004977f`. If new < old → removes. 
- **Returns 1 if inventory was actually mutated, 0 if no change** (so the handler's `je exit` guards are "only proceed when the previous step really happened").

### Plain-English (CONFIRMED)
- ChangeUsedItem performs an **item TRANSFORMATION on the active party member's inventory**:
  1. It removes (consumes) ONE of the item the player just *used/applied* (the "used item" register, `ZMB+0x2c` or `NPCB+8`).
  2. It adds ONE of the item named in the event record (`node ItemId` at on-disk offset +6).
  - Both are real inventory mutations (`fcn.0003e42a`), each guarded so the give only happens if the consume succeeded (and a final commit `fcn.0003165c` only if both succeeded).
- There is also an up-front "inventory full / can't carry" guard that aborts (with a message) before any mutation.

### Comparison to UAlbion's current handler
- UAlbion `Querier.On<ChangeUsedItemEvent>` only sets `EventContext.UsedItemOverride` so a *later* `query used_item==X` matches. **That is NOT what the original does.** The original CONSUMES the used item and GIVES the event's `ItemId`. The query-override is a *different* mechanism (it makes the used-item register report `ItemId` for subsequent queries) and **misses the inventory mutation entirely**. CONFIDENCE: CONFIRMED.

---

## IMPLEMENTATION RECOMMENDATIONS (for the C# remake)

### Signal (0xF)
- Mirror the original as an **event-chain branch** keyed on a small (6-slot) set of **persistent per-zone signal flags**, not a generic global flag.
- Where state lives: add a per-map "signal slots" array (6 entries) to the saved map/zone state in `GameState` (alongside switches/tickers). Note UAlbion already has `GetSwitch`/`GetTicker`/`IsNpcDisabled` persistent stores in `IGameState` — the signal slots are the same shape (6 ids per zone).
- Handler behaviour: on a `signal` event, look up `SignalId` in the zone's 6-slot table; if registered, **redirect the running event chain to the node index = the slot** (i.e. the chain interpreter's "jump to event N"); if the currently-active signal register matches, **stop** the chain instead.
- CAVEAT: the exact producer that *registers* an id into a slot (vs. the consumer here) was not fully traced; in practice Albion "signals" gate cross-event branches within a zone. If UAlbion has no chain-jump-by-index primitive, the pragmatic faithful behaviour is: `signal X` sets persistent zone-signal X, and `query`/branch nodes test it — mark `// PLACEHOLDER:` for the exact jump-target semantics. CONFIDENCE on the *mechanism* = CONFIRMED; on the higher-level "what content uses it" = INFERRED.

### Execute (0x14)
- Mirror as: `Unk1 != 0` → **enable** the active NPC's scripted/"execute" state (set NPC flag bit 0x02); `Unk1 == 0` → **disable** it (clear bit 0x02) and, when the disable takes effect, latch `Unk8` into the NPC/node's saved field (node+0xa).
- In UAlbion terms this maps onto the existing **NPC-disabled** persistent state (`IGameState.IsNpcDisabled` / `IsNpcDisabled(MapId, npc)` — see `Querier.cs:43`). `execute unk1=1` = re-enable NPC, `execute unk1=0` = disable NPC; `Unk8` is the latched value (likely a target/next-event id) to restore.
- So `Unk1` should be renamed `Enable` (bool) and `Unk8` is the latched id. CONFIDENCE: control flow CONFIRMED; the precise consumer of bit 0x02 / node+0xa = INFERRED (treat node+0xa latch as bookkeeping unless a reader is found).

### ChangeUsedItem (0x7) — UAlbion is currently WRONG
- The original **mutates inventory**: it (1) **removes one** of the item the player just *used/applied* (the "used item" register), then (2) **adds one** of the event's `ItemId`. It is an item **transformation** (consume tool → receive product), with an "inventory full → abort with message" guard.
- UAlbion's `Querier.cs:40` only sets `EventContext.UsedItemOverride`. That makes a *subsequent* `query used_item==X` match, but it **never consumes the used item and never grants `ItemId`** — so the puzzle reward / tool-consumption is missing.
- Recommended fix: in the `change_used_item` handler, on the active party member:
  ```csharp
  var state = Resolve<IGameState>();
  var member = /* active member from EventContext / party */;
  var usedItem = ctx.UsedItemOverride ?? ctx.Source.AssetId; // the tool being applied
  if (member.Inventory.TakeItem(usedItem, 1))          // op5: subtract 1, must succeed
      member.Inventory.GiveItem(e.ItemId, 1);          // op4: add 1
  ```
  (Mirror the original's guards: abort if backpack is full; only give if the consume actually changed inventory.) Keep `UsedItemOverride` too **only** if some chains still query used_item afterwards — but the inventory mutation is the load-bearing part.
- CONFIDENCE: CONFIRMED that the original consumes used-item and gives `node.ItemId` via the real inventory routine `fcn.0003e42a`.

## CONFIDENCE SUMMARY

CONFIRMED:
- All three handler addresses re-verified against table `0x13da7c`.
- The ZMB (zone-manager block) addressing (`[0x13d70a]*0x32+0x153160`) and the chain-advance output globals (`0x13daf6/f8/fc`, commit `fcn.0002fce1`).
- Signal: signal-id → 6-slot table lookup (`fcn.00035783`, table `0x153cbe`) → interpreter jump (`0x13daf6=1; 0x13daf8=slot`); stop-on-match against `0x15e5c0`.
- Execute: `Unk1` sets/clears NPC flag bit 0x02 on the active NPC (`NPCB=[0x13d708]*0x18+0x153930`); `Unk8` latched into node+0xa when disabling.
- ChangeUsedItem: consume used-item (op5 −1) then give `node.ItemId` (op4 +1) via `fcn.0003e42a` = real inventory mutation; UAlbion's override-only handler is insufficient.

RESOLVED since first pass:
- `0x153cbe` is a 6-entry per-zone signal-slot table, initialised by `fcn.00034b66` (in `g:\albion\src\prtlogic.c`) and persisted via `fcn.000236d6` (record type 0x11). It is saved/restored state.
- `0x15e5be/c0/c2` = active-signal register (id at `0x15e5c0`), written by the NPC/automap subsystem `fcn.00045d64`.

STILL INFERRED (not byte-verified):
- The exact higher-level meaning of NPC flag bit 0x02 (Execute) and the consumer of the node+0xa latched value — the bit clearly is the NPC "active/scripted" enable; ties to the persistent NPC-disabled store.
- Whether Signal's slot→node-index jump is into the same zone event-chain array or a dedicated signal-handler list. The mechanism (lookup → set `0x13daf6=1`/`0x13daf8=slot`) is confirmed; the chain it indexes into is the zone's, but the registration of ids into the 6 slots (the producer) was not fully traced.

COULD NOT DETERMINE:
- The producer path that writes a specific signal id into a `0x153cbe` slot at runtime (only the init reset `=1` and the consumer/lookup were traced).

