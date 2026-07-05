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
Sheet byte offsets already RE'd: +1 gender, +2 race, +3 class, +8 languages. **+5 = Level** —
CONFIRMED against the remake's own loader (`CharacterSheet.cs:194` — `Level ... // 5`).
So **0x13 = Compare(party leader's LEVEL, op, arg)**.

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

## B. Action types 0x02, 0x09, 0x0E, 0x17, 0x2D, 0x39 — ALL DECODED

### The trigger machinery (CONFIRMED)

- Action chains are consumed by a **trigger-task system**: a 24-byte param block is passed to
  **fcn.0002fca0 / fcn.0002fce1** ("RunTrigger"). Block layout:
  `+0` flags, `+2` target kind, `+4` target id, **`+6` = ACTION TYPE**, `+8` = argument (u16),
  `+0xA` = byte argument, `+0xC` = optional callback fn ptr (called at end of stream), `+0x10` = extra ptr.
- fcn.0002fce1 pushes a task (24-byte stack at **0x153930**, top idx [0x13d708], alloc
  fcn.000311c0), memcpys the block in (fcn.00092bcd), picks one of three static **(cmd,param)
  word streams** — 0x13d70c (normal), 0x13d73c (if [0x15e5be] = in-conversation), 0x13d768
  (if [0x13e142] = combat) — and runs the stream interpreter fcn.0002fd69: each (cmd,param)
  pair calls `[cmd*4 + 0x13d810]` (handler table; "events.c").
- **Stream cmd 1 = fcn.0002ff5b = "find & run map ACTION chain"**: walks every NPC on the
  current tile with flag 0x400 + every map chain; matches chain HEAD events of
  **type 0xE (Action)** by: `head.byte1 == task+6 (action type)` AND
  (`head.byte3 == task+0xA` OR (`head.byte3 == 0xFF` AND `head.word6 == task+8`));
  a head with `word6 == 0x7D00 (32000)` is remembered as the WILDCARD fallback chain.
  Other cmds run the event-set (conversation partner) and per-party-member variants.
- The matched chain then runs through the standard chain runner (records at 0x153160), with
  the record's context word +0x2C available to queries 0x0D/0x24.

### Trigger sites — every fcn.0002fca0/fce1 call in MAIN.EXE (param block +6 = type)

| Site | Block | Type | Context |
|---|---|---|---|
| 0x26888 | 0x13d074 | 0x42 | (beyond remake enum; +2=1, +4=dyn id, cb 0x268a2) |
| 0x2bc1d / 0x2bd3a | 0x13d33a | 0x2E UseItem | item use, cb 0x2c491 |
| 0x356ff | 0x13da64 | 0x43 | (beyond remake enum; cb 0x35719) |
| 0x3b04d | 0x13daf4 | **0x39 SignalTarget** | MAP SIGNAL handler (see below) |
| 0x46009 | stack | 0x06 StartDialogue | conversation open |
| 0x46dcb | 0x13e0dc | 0x08 DialogueLine | +0xA = chosen prompt number |
| 0x46f98 | stack | **0x09** | word-input: UNKNOWN word typed (see below) |
| 0x4703f | stack | 0x00 Word | per-synonym word match |
| 0x470f0 | stack | **0x02** | word-input: known word, no Word chain matched |
| 0x47269/0x47373/0x4746b/0x47546 | stack | dyn | conversation misc (flags|=2; 0x47546 cb 0x475a2) |
| 0x490dd / 0x4936b | 0x13e0f4/0x13e10c | 0x00 Word | word said via other UI paths (cb 0x49101/0x49387) |
| 0x4ad2b | 0x13e178 | 0x0A | |
| 0x4dfe4 / 0x4e0cc | 0x15f126 | **0x0E** | scripted damage: target KILLED (see below) |
| 0x4e018 / 0x4e117 | 0x15f126 | **0x0D** | scripted damage: target hurt but survived |
| 0x4ea9c | 0x13e1be | 0x13 | (cb 0x4eac1 — melee strike fn! combat round hook) |
| 0x4f032 | 0x13e1d6 | 0x15 | (cb 0x4f057 — ranged strike fn! combat round hook) |
| 0x59c26 | 0x13e666 | 0x31/0x32 dyn | (0x59xxx module; type written before call) |
| 0x59db5 | 0x13e67e | 0x34 PlacedItemInChest | (cb 0x59dd2) |
| 0x5acf1 | 0x13e7c2 | 0x3B | (cb 0x5acff) |
| 0x5eccd | 0x13e9c2 | **0x2D** | OUT-OF-COMBAT SPELL CAST (see below) |
| 0x5ef16 | 0x13e9da | **0x17** | COMBAT SPELL CAST (see below) |
| 0x68ab5 | 0x13ec02 | 0x3D PartySleeps | rest executor (word[0x178020] = hours) |

### The six requested types

**0x02 — "said a known word that no Word-chain handles" (conversation fallback).**
fcn.00046f28 (the word-input handler): gets typed/selected word (fcn.0004460c);
if the word is a real dictionary word, expands its synonym group (fcn.00044398) and fires
ActionType 0 (Word) per synonym (on a hit it sets word-bit blocks 5, 6 AND 8 for the whole
group = word known + 2 sibling sets). **If no synonym matched any Word chain → fire
ActionType 2 (arg 0)**; if a 0x02 chain exists it runs (then fcn.00048973(2,0) bookkeeping),
else the stock "nothing to say" text prints. So 0x02 = per-NPC catch-all "whatever you ask
about" head (ES156 Garris: he answers everything with his fare demand — the remake's "Pay?"
guess is wrong; it is a generic unknown-topic responder).

**0x09 — "typed word NOT in the dictionary"** (fcn.0004460c returned 0xFFFE): fire
ActionType 9 (arg 0); if no 0x09 chain → default text. This is the PASSWORD-PROMPT
mechanism (Riko 234 / Gerwad 242: wrong-password reply chains); the right password is a
dictionary word handled by a Word(0) chain.

**0x0D / 0x0E — scripted-damage outcome triggers.** The event-damage routine (0x4df3d..0x4e11c;
used by trap/DataChange-style damage with sample 0x10c and text 0x1C2) fires after applying
damage: target-kind word +2 (1 = NPC-sheet path via dead-flag fcn.000364b0&1; 2 = party
member via LP get/set fcn.0003674c/fcn.00036799), +4 = target id;
**type 0x0E if the target DIED, 0x0D if it survived**. (Remake note "981_Tom endgame" fits:
Action 0x0E = on-kill hook.)

**0x17 — spell cast IN COMBAT.** Combat spell executor stores a 0x1C-byte spell context at
combatant+0x52 (fcn.0005ee?? via shared ExecuteSpell fcn.0005ef24), then fcn.0005eea8
(called from the combat pipeline at 0x4f81b / 0x4f912) fires: +2/+4 = caster kind/id
(combatant words +0/+2), **+8 = spell school, +0xA = spell number**, +0x10 = combatant ptr,
cb fcn.0005ecda. So Action(0x17, school, spell#) = "this spell was just cast in combat"
(the scripted Sira/Kenget beats).

**0x2D — spell cast OUT of combat.** The map-context cast routine (0x5ec00..0x5ecd2; spell
from the sheet's active-spell list +0x31C, school byte+0x15 / number byte+0x16 → globals
0x1775a0/0x1775a2, caster slot & handle → 0x1775a8/0x1775aa) calls ExecuteSpell
fcn.0005ef24; **if it returns nonzero** fires Action(0x2D): +4 = caster slot,
+8 = school, +0xA = spell number, cb fcn.0005ecda.

**0x39 SignalTarget — fired by the MAP SIGNAL event (opcode 0xF, handler 0x3af2d).**
Handler tail (0x3afb7..0x3b04d): resolves the running chain's owner id (record+0x2A):
if it equals the active conversation NPC ([0x15e5c0], only meaningful when in-dialogue
[0x15e5be]/[0x15e5c2]) → target (kind 3, id 0); else if it is a party member
(fcn.00035783) → (kind 1, id = member slot); else (3,0).
Then fires Action(0x39) with **+8 (argument) = the Signal event's byte+1**. I.e. the Signal
opcode re-dispatches into an Action(SignalTarget) chain keyed by the signal number, aimed at
the entity that owns the current chain.

Bonus: the original also has action types 0x42, 0x43, 0x47 (beyond the remake's 0x3D max) —
engine-internal triggers never present in map data (map data caps at 0x3D).

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

| Value | Call | Behaviour (decoded) |
|---|---|---|
| 0 | fcn.00075e71 | poll input (fcn.000757d7) + rebuild frame (fcn.0007382a) + present (fcn.00075e9f) — an IMMEDIATE full redraw ("wipe now", no transition) |
| 1 | fcn.00077ab9(0, 0, w=[0x147124], h=[0x14712a]) | fill the whole viewport (mode-8 rect op) — CUT TO BLACK/blank |
| 2 | fcn.0008137a | stepped PALETTE FADE: loop pct = 10,20..100, fcn.00080fdb(0,0x100,0,0,0,pct) + present twice per step (~10 frames) |
| 3 | fcn.000813e0 | sibling stepped palette fade (opposite direction / variant) |
| 4 | fcn.0008144f | sibling stepped palette fade variant |
| 5 | fcn.000814b1 | sibling stepped palette fade variant |

So Wipe = screen-transition opcode: 0 = redraw, 1 = blank, 2..5 = ~10-step palette fades
over the full 256-colour palette. (2/3 and 4/5 pair as out/in — INFERRED from shape; the
exact in/out polarity of 3/4/5 not traced instruction-by-instruction.)

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
- **Unit = 1/60 second.** dword[0x1835f0] is the system timer tick (incremented in
  fcn.00089ba8 when the ISR flag byte [0x13fde8]&0x10 is set; PIT reprogram fn at
  0xac460). The M-HT static recompile emulates exactly this counter (`Game_TimerTick`,
  Albion-timer.c) at 16/17/17 ms intervals = **60.0 Hz**. So `Pause N` = wait N/60 s
  while keeping the engine pumping; `Pause 0` = wait for a keypress/click.

## D. Torch burn — YES, the original burns LightSource charges hourly (CONFIRMED)

**fcn.0003911d** is the FIRST call of the hour tick (fcn.00043acc, before spell decay
fcn.000605ed and the hour-counter increment). For each present party member (fcn.00039941),
it locks the sheet and scans BOTH:

- the 9 EQUIPMENT slots (6-byte ItemSlots @ sheet+0x2E6), and
- the 24 BACKPACK slots (@ sheet+0x31C):

```c
tmpl = LockItemTemplate(slot);                    // fcn.0004a521 (skip empty, fcn.0004a49c)
if (tmpl.byte1 == 0x16 /* ItemType.LightSource */) {
    if (slot.charges != 0xFF) {                   // slot byte+1; 0xFF = INFINITE (magic lights)
        slot.charges--;                           // ONE CHARGE PER GAME HOUR
        if (slot.charges == 0)
            RemoveItemsFromSlot(member, slotIdx, 1);   // fcn.00049584 — slotIdx 1..9 equip,
    }                                                  // 10.. backpack; amount--, slot cleared
}                                                      // at amount 0 (fcn.0004a4ee)
```

- Hour-tick call chain: fcn.00043acc → fcn.0003911d (torch burn) → fcn.000605ed (spell
  decay) → `word[0x153b2e]++` (hour) → fcn.000395ec → hunger every 2h (fcn.00039362) →
  EveryHour map trigger fcn.0003236a(7) → day rollover fcn.00043b86 (EveryDay trigger 8).
- ItemData.Charges IS the torch lifetime in HOURS; burning applies everywhere the item is
  carried (equipped hand or backpack), all six members.
- **Stack quirk (faithful-to-original):** RemoveItemsFromSlot decrements only the AMOUNT
  and does NOT reset the slot's charges byte. For a stacked slot (amount ≥ 2) the byte is 0
  after the removal, so next hour `0 - 1 = 0xFF` → the remaining stack reads as INFINITE
  and never burns again. Recommended remake behaviour: implement the per-hour decrement +
  destroy; either replicate the wrap or (defensible fix) re-init charges from the template
  when a stacked item is consumed — mark whichever is chosen.

## E. Campfire rest scene — NO picture; the original rest is fade-to-black + text (CONFIRMED)

Rest executor **fcn.00068b05**, full sequence (complete call list — no picture/FLIC/overlay
anywhere in it):

1. `fcn.00077bba(0x1F)` — UI/cursor mode.
2. `fcn.0009251d(0x1470f4, 0x14709c, 0,0, 360, [0x147138], 0, 0)` — palette **fade-out**.
3. `fcn.00077ce4(0x14709c, 0, 0, 360, [0x147138])` — **fill the whole viewport** (blank
   screen), `fcn.00075e9f()` present. The screen is BLACK for the duration.
4. Per-member rest recovery loop (fcn.00037958) — heals BEFORE the clock advance (as in _RE_5C §4).
5. Duration + message: if `[0x147990]==1` (rest-anywhere map flag) or hour in 4..18 →
   text ptr [0x15e198] ("you rest 8 hours"), hours = 8. Otherwise (night, outdoor camp):
   text ptr [0x15e194], hours = `hour < 4 ? 7-hour : 24-hour+7` — **sleep until 7am**.
   Text goes through fcn.0007dc07 = the standard MESSAGE WINDOW (text renderer, no image).
6. Rations: per present member — if the PartySleeps action set [0x178020] use its healing
   value [0x17800e] (fcn.00068d2f(member, pct): LP += maxLP*pct/100); else if rations >= 2:
   rations -= 2, fcn.00068d2f(member, 50) (heal 50% of max LP); else print "too hungry"
   text [0x15e19c] for that member. Write rations back (fcn.00038bbb).
7. `AdvanceClock(hours)` (fcn.000439b3).
8. Palette **fade-in** (fcn.0009251d again), present, fcn.00077c9e, fcn.00075589.

**Verdict for _TODO_1TO1 item 18: there is NO campfire scene/picture in the original.**
The remake's text-only rest is already faithful; the only missing visuals are the palette
fade-out/in and blanked viewport around the clock advance. (CAMP pictures in the assets are
used by the intro/cutscene sequences, not the rest command.)

## F. Corrections / bonus findings for other docs

- `_RE_5C.md §3` said 0x20/0x25 "fallthrough no-op" — they are no-ops returning **TRUE**
  (dispatcher default result = 1), not false. `Querier.cs` stubs them false — wrong polarity
  (harmless per shakeout, but should be fixed for 1:1).
- Out-of-combat casting: a "spell index" >= 10 in the cast routine (0x5ec00) means casting
  FROM AN ITEM in backpack slot idx-10; school/number come from the item template bytes
  +0x15/+0x16.
- Word bit-sets: matching a Word action in conversation sets the word's bit in blocks 5, 6
  AND 8 (three parallel 1500-bit sets; 5 = known words, 6/8 = un-RE'd siblings — likely
  "mentioned in this conversation" style tracking).
- Combat-round action hooks exist: type 0x13 (cb = melee strike fn 0x4eac1) and 0x15
  (cb = ranged strike fn 0x4f057) are triggered around combat rounds — relevant if UnkD
  (0x13)/(0x15) chains ever show in data.
