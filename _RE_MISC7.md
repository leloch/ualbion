# _RE_MISC7.md — misc small unknowns batch (queries, actions, map opcodes, torch burn, camp scene)

Date: 2026-07-05. Tool: radare2 project `albion_aaa` (MAIN.EXE, DOS LE).
Task: _TODO_1TO1.md §9 items 9 (torch burn), 11/12 (CloneAutomap/Wipe/Pause), 16 (queries+actions), 18 (campfire scene).

## 0. Known facts (from prior RE docs — starting points)

- QUERY dispatcher (quermod.c): entry ~0x3cb0a; record = `0x153160 + u16[0x13d70a]*50 + 0x18`;
  fields +1 type, +2 op, +3 imm, +6 u16 arg. `if (type > 0x2B) bail; jmp [0x3ca53 + type*4]`
  — 44-entry jump table at **0x3ca53**. Results merge at 0x3d965; comparator fcn.0003d983(value, op, arg).
  (_RE_5C.md §3)
- Prior partial reads: 0x20/0x25 noted as "direct fallthrough (no-op)"; 0x26 =
  Compare(GetEffectiveSkill(leader, imm), op, arg) @ 0x3d796; 0x27 "same shape, another sheet
  accessor (leader attribute, INFERRED)". These need confirming/expanding here.
- Map-event dispatcher fcn.000312c2; handler table **0x13da7c** (entry N = opcode N handler).
  From _RE_ASK_SURRENDER.md: 0x10 CloneAutomap = 0x3b05b, 0x17 Wipe = 0x3b984, 0x1A Pause = 0x3bbb4.
- Rest executor 0x68b05 (fade-out fcn.0009251d, per-member recovery fcn.00037958, then
  AdvanceClock fcn.000439b3). Hour tick = fcn.00043acc. (_RE_5C.md §4)
- Party light-item total = fcn.00038f24 (the item-light accumulator, _RE_5C.md item 1.3).
- Comparator ops: 0 NonZero, 1 <=, 2 <, 3 ==, 4 >=, 5 >.
- Event record layout (50-byte map event): +0x18 type byte, +0x19.. args (i.e. dispatcher-relative
  +1 = file offset byte 1 of the 12-byte event).

## A. Query types 0x08, 0x0B, 0x0D, 0x13, 0x24, 0x25, 0x26, 0x27 — ALL DECODED

### Dispatcher context (CONFIRMED, prologue @ 0x3cb03)

- `[ebp-0x10]` result, **initialised to 1 (TRUE)** @ 0x3cb03 — so a jump-table entry that goes
  straight to the merge (0x3d965) returns **TRUE**, not false. (The remake's stub-false for
  0x25 is therefore wrong-polarity; see below.)
- `[ebp-0x14]` = ptr to the 12-byte event data (record+0x18). +0 event type (0xC), +1 query
  subtype, +2 op, +3 imm, +6 u16 arg, **+8 u16 = false-branch event id (0xFFFF = none)**.
- Runtime record base (record = 0x153160 + n*50): `word[record+0]` = status word (must be 0
  for the context queries 0x0D/0x24 to run; INFERRED: nonzero = no player-action context),
  `word[record+0x2C]` = **the action CONTEXT value** — the word said / item used that
  triggered this chain.

### Bitfield helper fcn.000350fc(block, bit) / setter fcn.0003529d(block, bit) — block table (CONFIRMED)

| Block | Base | Bits | Meaning |
|---|---|---|---|
| 0 | 0x153d9a | 0x400 | Switches |
| 1 | 0x1594ba | 999 | Chests |
| 2 | 0x159537 | 999 | Doors |
| 3 | 0x157c9a | 0xC000 | NPC active |
| 4 | 0x153e1a | 0x1F400 | Chain-active (250/map) |
| 5 | ptr [0x15e598] | 0x5DC (1500) | **KNOWN DICTIONARY WORDS** (same 1500 as word table 0x15cc6c) |
| 6 | ptr [0x15e5a0] | 1500 | word-related set #2 (not needed here) |
| 7 | 0x15949a | 0x100 | 256-bit set (not needed here) |
| 8 | ptr [0x15e59c] | 1500 | word-related set #3 (not needed here) |

### 0x08 @ 0x3cd6c = **IsWordKnown(arg)** (CONFIRMED)

```c
result = GetBit(block 5, argument);   // op & imm IGNORED, raw bit
```
Two instructions: `fcn.000350fc(eax=5, edx=arg)` → result. Query is true iff dictionary
word #arg (0..1499) is in the party's known-words set.

### 0x0B @ 0x3cdbb = **Compare(map light-environment word [0x14798c], op, arg)** (CONFIRMED)

`Compare(word[0x14798c], op, arg)` — the same word the 0x21 "light enough" query gates on
(mapFlags & 3: 1 = dungeon lighting). I.e. "what lighting model does this map use".

### 0x0D @ 0x3ce64 = **"the WORD used to trigger this chain == arg" (synonym-aware)** (CONFIRMED)

```c
if (word[record+0] != 0) return 0;               // no player context
result = 0;
ctx = word[record+0x2C];                          // the word id the player said
if (ctx != 0xFFFE) {
    count = fcn.00044398(ctx, buf);               // collect ALL i in 0..1499 with
                                                  // wordTable[0x15cc6c + i*2] == ctx (max 8)
                                                  // = the synonym group of ctx
    for (i in buf)
        if (buf[i] == argument) {                 // arg is one of the group
            for (j in buf) SetBit(block 5, buf[j]);  // mark WHOLE group known
            result = 1; break;
        }
}
if (!result && word[event+8] == 0xFFFF)           // failed & no false-branch:
    fcn.0002f85d(432);                            // print SystemText 432
return result;                                    // "This word doesn't work here"
```
- fcn.0002f85d(id) = print system text (looks up ptr table [0x15d824 + id*4]).
- UAlbion SystemText 432 = `MapPopup_ThisWordDoesntWorkHere` — exact match.

### 0x13 @ 0x3d0ca = **Compare(leader sheet byte[+5], op, arg)** — leader LEVEL (see below)

```c
sheet = Lock(leaderHandle [0x1597c0]);            // fcn.0008b739 via fcn.000365d5
value = byte sheet[+5];                           // fcn.000365d5 = read sheet byte +5
Unlock;
return Compare(value, op, arg);
```
Sheet byte offsets already RE'd: +1 gender, +2 race, +3 class, +8 languages. +5 = Level
(matches the remake's CharacterSheetLoader offset — verify note below).

### 0x24 @ 0x3d6d8 = **"the ITEM used to trigger this chain has TYPE byte == arg"** (CONFIRMED)

```c
if (word[record+0] != 0) return 0;                // no player context
ctx = word[record+0x2C];                          // the item id used
tmpl = LockItemTemplate(ctx);                     // fcn.0004a621 + fcn.0004a521
value = byte tmpl[+1];                            // item TYPE/class byte (+1 in ITEMLIST rec)
UnlockItemTemplate();                             // fcn.0004a584
result = (value == argument);                     // EQUALITY only, op/imm ignored
if (!result && word[event+8] == 0xFFFF)
    fcn.0002f85d(431);                            // SystemText 431 =
return result;                                    // MapPopup_ThisItemDoesntWorkHere
```
So 0x24 = "used item is of item-class arg" (e.g. any torch / any pick), with the
"This item doesn't work here" default popup on failure when the query has no false branch.

### 0x25 @ jump-table → 0x3d965 directly = **NO-OP returning TRUE** (CONFIRMED)

Entry 0x25 (and 0x20) point at the merge label; result keeps its init value **1**.
The remake's stub `false` is the WRONG polarity for 0x25 — should be `true`
(only matters if shipped data uses it; shakeout found none).

### 0x26 @ 0x3d796 = **Compare(LeaderSkill[imm], op, arg)** (CONFIRMED, was in _RE_5C)

fcn.00035fd5(leaderHandle, imm) — imm bounds-checked < 4 (skills), reads the skill record
word (idx*8 array in the sheet). Compare vs arg.

### 0x27 @ 0x3d7ce = **Compare(LeaderAttribute[imm], op, arg)** (CONFIRMED — upgraded from _RE_5C's INFERRED)

fcn.00035c77(leaderHandle, imm): `if (imm >= 9) return 0; value = word[sheet + imm*8 + 0x2A]`
— the attribute array (9 records of 8 bytes @ sheet+0x2A, current-value word first).
Compare(value, op, arg).

## B. Action types 0x02, 0x09, 0x0E, 0x17, 0x2D, 0x39

(in progress)

## C. Map opcodes 0x10 CloneAutomap, 0x17 Wipe, 0x1A Pause

### 0x10 CloneAutomap — handler 0x3b05b (CONFIRMED)

Event args: **word+6 = SOURCE map id, word+8 = DEST map id** (dispatcher-relative;
= bytes 6/8 of the 12-byte event).

```c
assert(src != dst);                                   // events.c:0x60b
hA = GetResourceHandle(type 0, src);  hB = GetResourceHandle(type 0, dst);  // map data
a = Lock(hA); b = Lock(hB);
assert(a[4]==b[4] && a[5]==b[5]);                     // events.c:0x630 — same WIDTH/HEIGHT
if (a[2] == 1) {                                      // 3D maps only (byte+2 = map type)
    // 1. the automap discovery bitmap:
    hAuto = GetResourceHandle(type 0x1B, src);        // 0x1B = AUTOMAP resource
    assert(hAuto);                                    // events.c:0x64c
    fcn.00023480(hAuto, 0x1B, dst);                   // RE-KEY the src automap resource to
    Release(hAuto);                                   // key (0x1B, dst)  → dst now has src's
                                                      // exploration bitmap (a MOVE/re-key)
    // 2. goto-point discovered flags:
    listA = fcn.00012e3d(a);  countA = word[listA]; listA += 2;   // AutomapInfo list
    listB = fcn.00012e3d(b);  countB = word[listB]; listB += 2;   // (19-byte records)
    for (i < countA) for (j < countB)
        if (A[i].byte0 == B[j].byte0 && A[i].byte1 == B[j].byte1) {  // match by X,Y
            bit = GetBit(block 7, A[i].byte3);        // byte+3 = goto-point global id
            SetBit(block 7, B[j].byte3, bit);         // copy discovered flag src→dst
        }
}
```
- **Block 7 (0x15949a, 256 bits) = "automap goto-point discovered" flags**, keyed by the
  AutomapInfo record's id byte (+3). (UAlbion AutomapInfo: X, Y, Unk2, Id, Name[15] = 19 bytes.)
- Remake semantics to implement: copy EXPLORATION BITS from src map's automap to dst map's
  automap (maps must be same size, both 3D), and for each goto point in dst whose (X,Y)
  matches a goto point in src, mark it discovered iff the src one was.
- Note the bitmap transfer is a resource RE-KEY (src slot moved to dst) — in a remake a copy
  is the safe equivalent (src map is normally the map being switched away from).

### 0x17 Wipe — handler 0x3b984 → fcn.0006c54f(byte+1) (CONFIRMED structure)

`WipeEvent.Value` (event byte+1) selects a screen-transition mode, switch 0..5 in
fcn.0006c54f (>5 = no-op):

| Value | Call | Meaning |
|---|---|---|
| 0 | fcn.00075e71 | (see below) |
| 1 | fcn.00077ab9(x=0, y=0, w=[0x147124], h=[0x14712a]) | full-viewport rect op |
| 2 | fcn.0008137a | transition variant |
| 3 | fcn.000813e0 | transition variant |
| 4 | fcn.0008144f | transition variant |
| 5 | fcn.000814b1 | transition variant |

(sub-function identification below)

### 0x1A Pause — handler 0x3bbb4 (CONFIRMED)

```c
len = event byte+1;                                   // "Length"
if (len == 0) {
    fcn.0007368e();       // MODAL WAIT-FOR-INPUT: push UI ctx 0x137cbc, loop
                          // { fcn.000757d7(); done = fcn.000736ea(); fcn.00075e9f(); }
                          // until input received, pop ctx — i.e. wait for keypress/click
} else {
    t0 = timerNow();                                  // fcn.0008ab40 = dword[0x1835f0]
    while (timerNow() < t0 + len) {                   // wait len TIMER TICKS
        fcn.000757d7();                               // engine pump (same pair as the
        fcn.00075e9f();                               // modal wait: input poll + render)
    }
}
```
- Unit = ticks of the global timer dword[0x1835f0] (incremented by the timer ISR —
  rate identified below).

## D. Torch burn

(in progress)

## E. Campfire rest scene

(in progress)
