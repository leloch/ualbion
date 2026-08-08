# docs/re/RE_FIDELITY2.md — Trapped chests + the "SLP" pool

Two focused RE passes against MAIN.EXE (radare2 project `albion_aaa`, DOS LE) to settle
two remake-fidelity questions:

1. **Trapped chests** — the trap mechanic the remake lacks (`triggeredTrap` hardcoded false).
2. **The "SLP" (spell-learning-points) pool** — claimed separate pool at sheet ~0xEA.

Bottom line up front:
- **Task 1:** the trap mechanic is real and fully RE'd (it already was, in docs/re/RE_5C.md §2`).
  This file re-verifies it byte-for-byte and gives the C# plan. There is **no Thief's-Amulet
  item check** — trap evasion is a **Dexterity percent-roll**. The trap **damage is not a
  constant**; result=3 makes the chest handler run a per-chest **map-event script** (the
  content defines the harm). Retries are unlimited; the trap disarms after one trigger.
- **Task 2: the premise is WRONG.** There is **no separate SLP pool** in MAIN.EXE. There is
  **one** points pool at **sheet +0x16** (which `CharacterSheet.cs` already serialises as
  `Combat.TrainingPoints`). It is granted `w[sheet+0xEA]` per level and **spent to raise
  SKILLS** (the "Learn close combat / lockpicking / …" NPC services), **not** to learn spells.
  **LearnSpells (the spell trainer) costs GOLD, not any pool.** The `+0xE8`
  (`SpellLearningPointsPerLevel`) field is **dead data — nothing in the binary reads it.**
  => `SheetApplier.cs:413` is mislabelled but **already behaves 1:1**; the recommended change
  is to fix the comment, not add a pool.

---

## KNOWN FACTS (verified this session, radare2 on `albion_aaa`)

### Sheet field offsets (runtime PRTCHAR == the serialised 742-byte common block)
The runtime sheet pointer is obtained by `fcn.0008b739(handle)` (bbmem.c handle-lock; 508
xrefs). All "+N" below are offsets into that dereferenced base and match `CharacterSheet.cs`'s
serialise order exactly:

| Offset | Field | Accessor / proof |
|---|---|---|
| +0x05 (u8)  | Level | read in ApplyLevelUp `mov al,[edx+5]` |
| +0x11 (u8)  | **Action points this turn** (NOT training points) | ApplyLevelUp stores `byte[edx+0x11]=clamp(level/w[+0xE2],1,4)`; also docs/re/RE_COMBAT.md:858` |
| +0x16 (u16) | **Points pool** (CharacterSheet.cs `Combat.TrainingPoints`) | `Get fcn.00036db2` reads `word[base+0x16]`; `Set fcn.00036dfc` writes it (clamp 0..0x7FFF) |
| +0x7A + idx·8 | Skills[idx] current (idx 0..3 = CloseCombat/Ranged/CritChance/LockPicking) | `GetSkillCur fcn.00036193` reads `word[base+idx·8+0x7A]` |
| +0x7C + idx·8 | Skills[idx] max | `GetSkillMax fcn.000362b6` reads `word[base+idx·8+0x7C]` |
| +0xE2 (u16) | LevelsPerActionPoint (AP divisor) | `cmp word[eax+0xE2],0` in ApplyLevelUp |
| +0xE4 (u16) | LifePointsPerLevel | `imul ax,word[edx+0xE4]` |
| +0xE6 (u16) | SpellPointsPerLevel | (per docs/re/RE_COMBAT.md, unchanged) |
| +0xE8 (u16) | `SpellLearningPointsPerLevel` in C# — **DEAD: no code reads `[base+0xE8]`** | exhaustive disp32 scan for `[reg+0xE8]` → no real instruction |
| +0xEA (u16) | **per-level grant feeding the +0x16 pool** (C# `TrainingPointsPerLevel`) | ApplyLevelUp `imul ax,word[edx+0xEA]` then Get(+0x16)+grant then Set(+0x16) |
| +0xEE (i32) | ExperiencePoints | `GetXP fcn.00036d08` |
| +0xF2 + school·4 | known-spell bitmask | `LearnSpell fcn.00060301` `or [eax+0xF2], 1<<num` |

### Lock record (door.c, built per chest/door event; ptr via `fcn.00074bda`)
| Field | Meaning |
|---|---|
| +0x1A (u16) | PickDifficulty 0..100 (>=100 unpickable) |
| +0x1C (u16) | Key item id |
| +0x1E (u16) | **trap-armed flag** |
| +0x20 (ptr) | → result word: **1 opened, 2 key, 3 trap fired** |

### Functions
| Addr | Meaning |
|---|---|
| `fcn.00036db2` | **GetPool**(sheet) → `word[+0x16]` (the "training points") |
| `fcn.00036dfc` | **SetPool**(sheet, v) → `word[+0x16]`, clamp 0..0x7FFF |
| `fcn.00036193` / `fcn.000362b6` | GetSkillCurrent / GetSkillMax (idx·8 + 0x7A/0x7C) |
| `fcn.00037c22` | ApplyLevelUp (grants pool via +0xEA into +0x16) |
| `fcn.000667ea` | **PlaceAction 0 = LearnCloseCombat / skill-trainer** (table `0x13eaf0[0]`) — SPENDS the +0x16 pool |
| `fcn.0006768c` | PlaceAction 0xB = **LearnSpells** — spends GOLD, no pool |
| `fcn.00060301` | LearnSpell core — sets known-bit + mastery, **no pool/TP touched** |
| `0x5ab33` | door.c PickLockButton (the skill pick path + trap) |
| `0x3a396` | map-event Chest handler (type 0x03); runs `fcn.000584e8` then a 5-case result switch |
| `fcn.000584e8` | chest.c open-and-run routine; returns 0..4 (3 = trap, 4 = key/extra) |
| `fcn.0003529d` | event/trap-chain runner invoked on chest result=3 |
| `fcn.00062a41` | generic AV cue helper (74 xrefs; id 108 = lock-trap sting) — **not damage** |
| `fcn.00035b96` | RollStat = PercentRoll(EffStat, 100); stat 2 = Dexterity (trap evade) |

---

## TASK 1 — TRAPPED CHESTS

### (a) Where the trap is encoded — CONFIRMED (handler) / INFERRED (data byte)
The lock sub-object of a chest carries a **trap-armed flag at lock-record +0x1E** (u16,
boolean). The 9 bytes the remake's `ChestEvent.Serdes` reads (PickDifficulty, Key u16,
UnlockedText, OpenedText, ChestId u16) are the **event-record** fields; the trap flag is a
property of the **chest/lock definition** that the chest setup copies into the live lock
record's +0x1E. The exact source byte in the chest data table (a dedicated flag vs a high bit
of an existing byte) was **not pinned down** — `fcn.00074bda` only returns the record pointer;
the populating site reads the map's chest table. **This is the one unconfirmed item.**

### (b) What the trap does — CONFIRMED (mechanism), CONTENT-DEFINED (amount)
The door.c trap branch itself applies **no damage**. On a *failed skill pick* of a *trapped*
lock that the leader *fails to evade*, it only:
```
fcn.00062a41(108,100,100,0,0)   // audiovisual sting (id 108) — NOT damage
Msg(525)                        // "the trap is triggered" (SystemText 0x20D = 525)
*rec->result = 3                // tell the caller a trap fired
rec[+0x1E] = 0                  // disarm (one-shot)
```
The **chest map-event handler** (`0x3a396`) then takes result 3 through its 5-case jump table
(`jmp cs:[val·4 + 0x3a3b1]`) and calls **`fcn.0003529d(1, eventNum)`** — i.e. it **fires a
map-event script** associated with the chest. So the *actual* harm (damage / condition /
amount) is whatever **TrapEvent / DamageEvent chain** the map author attached to that chest —
**not a hardcoded constant**. (Consistent with docs/re/RE_NOTES.md TrapEvent type 5.)

### (c) How it's disarmed / avoided — CONFIRMED
- **No Thief's-Amulet item check exists** anywhere in the lock path. The task's amulet
  hypothesis is **disproved**.
- Avoidance is a **single Dexterity percent-roll**: `fcn.00035b96(leader, 2)` =
  `PercentRoll(EffectiveDexterity, 100)` (`rand()%100 <= EffDex`). Success → SystemText 524
  ("…evades the trap"); failure → the trap fires (above).
- The trap can only fire on the **SKILL pick** path and only on **failure**. The **item path**
  (key / lockpick, handler `0x5a8f0`) never touches +0x1E, so **using a Lockpick item bypasses
  the trap entirely** (and a Lockpick opens any difficulty<100 unconditionally, always
  consumed). The picker is always the **party leader** (`0x1597c0`).
- The trap is **one-shot**: +0x1E is zeroed on both evade and trigger, so a lock traps at most
  once.

### (d) Lockpick retry semantics — CONFIRMED
- **Skill pick: unlimited free retries.** A failed pick with no (remaining) trap changes no
  state (Msg 538) — click again immediately, no time/item cost. The remake's current
  "unlimited free retries" is therefore **correct for the untrapped case**.
- The only consumable is the **Lockpick item** (item path), destroyed on every use including
  against an unpickable (diff 100) lock.
- Success formula (already in `CombatFormulas`): auto-succeed if `EffLockpicking >= difficulty`,
  else `PercentRoll(skill·(100−difficulty)/100, 100)`.

### TASK 1 CONFIDENCE
- Trap trigger conditions, evasion (DEX roll), one-shot disarm, result codes, retry semantics,
  Lockpick-bypasses-trap: **CONFIRMED** (door.c `0x5ab33`, re-verified; matches docs/re/RE_5C.md §2`).
- Trap *effect/amount* = content-defined map-event chain via `fcn.0003529d`: **CONFIRMED it's a
  script, not a constant**; the specific chain contents per chest were not enumerated.
- The source data byte that arms +0x1E in the chest table: **INFERRED / not located.**

### TASK 1 — C# implementation recommendation
The remake already has the formulae (`CombatFormulas.Lockpick*`) and the messages. Needed:

1. **Add a trap field to the chest data + `ChestEvent`.** Since the on-map trap source byte is
   unconfirmed, add `bool Trapped` (or `byte TrapEvent`) to `ChestEvent` populated from the
   converted chest data; mark `// PLACEHOLDER: confirm trap-arm byte in chest table`.
   Keep a runtime "armed" copy so the one-shot disarm persists for the session.
2. **Wire the trap into the skill-pick failure path** in `InventoryLockPane.PickLock()` /
   `CanPick()`. On a *failed* skill pick of an *armed trapped* lock:
   - roll Dexterity: `RaiseQuery(new QueryRandomChanceEvent((ushort)EffDex, GreaterThan, 0))`
     style, i.e. `rand%100 <= EffDex`;
   - on evade → `SystemText` "evades the trap" (524-equivalent), disarm;
   - on fail → play the trap AV cue, show 525-equivalent, **fire the chest's trap map-event
     chain** (the analogue of `fcn.0003529d`), set the close result to `triggeredTrap = true`,
     disarm.
   The plumbing already exists: `InventoryScreenManager.InventoryClosed(bool triggeredTrap,…)`
   and the `// TODO: Test with trapped chests/doors` at line 118 already thread a `triggeredTrap`
   bool through to the task result — just stop hardcoding it false.
3. **Do NOT** add a Thief's-Amulet code path or per-attempt cost — neither exists in the original.
4. Leave the **item path (key/lockpick) trap-free** — matches the original (Lockpick bypasses
   the trap). This is a legitimate player strategy, not a bug.

---

## TASK 2 — THE "SLP" POOL (premise disproved)

### (a) Sheet offset + per-level grant — CONFIRMED, but it is the TRAINING-POINTS pool
ApplyLevelUp (`fcn.00037c22`) grants, per level:
```
grant   = nLevels * w[sheet+0xEA]      // imul ax, word[edx+0xEA]
newPool = GetPool(sheet) + grant       // GetPool = fcn.00036db2 -> word[base+0x16]
SetPool(sheet, newPool)                // fcn.00036dfc -> word[base+0x16], clamp 0..0x7FFF
```
So the **pool lives at sheet +0x16** and the **per-level rate is at +0xEA**. `+0x16` is exactly
what `CharacterSheet.cs:229` serialises as `Combat.TrainingPoints`, and `+0xEA` is what
`CharacterSheet.cs:286` serialises as `TrainingPointsPerLevel`. There is **no second pool**.

### (b) What SPENDS the pool — CONFIRMED: skill trainers, NOT spell learning
`fcn.000667ea` = **PlaceAction service 0** (handler table `0x13eaf0[0]`, documented in
docs/re/RE_COMBAT.md:2245` as "LearnCloseCombat"). It is the **skill-trainer** NPC service:
```
pool = GetPool(member)                                   // fcn.00036db2 (+0x16)
if (pool == 0) -> "nothing to train" dialog
headroom = GetSkillMax(member, Unk5) - GetSkillCur(member, Unk5)   // skill #Unk5, 0..3
n = min(headroom, pool)
n = min(n, partyGold / Unk6)                             // Unk6 = gold-tenths per point
n = NumberInput(1..n)
if (TrySpendPartyGold(n * Unk6)) {                       // fcn.00067b9c, pooled gold
    SetSkillCur(member, Unk5, cur + n)                   // fcn.00036210 raise the skill
    SetPool(member, pool - n)                            // fcn.00036dfc  SPEND n points
}
```
So the +0x16 pool is **spent (1:1 with the points gained) to raise a SKILL** (close combat /
ranged / crit / lockpicking, idx 0..3), **and the player also pays gold per point**. Services
1 and 2 in the table (Heal/Cure-shaped neighbours) follow the same accessor family.

**Spell learning does NOT use the pool.** `LearnSpells` (PlaceAction 0xB, `fcn.0006768c`):
- gates on school bit `byte sheet+4 & (1<<Unk5)`, spell already-known, and level requirement;
- **price = Unk6 × SpellData.LevelRequirement (gold-tenths)** via `TrySpendPartyGold`;
- learns via `fcn.00060301`, which sets the known-bit at `+0xF2+school·4` and the initial
  mastery = 4 × effective MagicTalent — and **touches no pool and no TP** (verified: no
  `fcn.00036db2/dfc` call in `fcn.00060301`).

### Where the pool IS read for display — CONFIRMED
The character-info screen builder (`0x28032`) calls `GetPool` and stores it into its display
struct (`+0x28`). And a generic stat-editor (`fcn.0003c3eb`, case at `0x3c74b/0x3c760`) can
get/set the pool — that is the debug/script "set training points" path, not spell learning.

### (c) Does the C# already serialise this? — CONFIRMED yes (and already applied 1:1)
`CharacterSheet.cs` already has **both** rate fields and the pool:
- pool: `Combat.TrainingPoints` @ disk 0x16 (line 229) ✓
- rate: `TrainingPointsPerLevel` @ disk 0xEA (line 286) ✓ — the field the original actually uses
- `SpellLearningPointsPerLevel` @ disk 0xE8 (line 285) — present but **dead data** (only set for
  a few Iskai monsters in source data; **never read by MAIN.EXE**).

And `SheetApplier.cs:416-418` already does `TrainingPoints += TrainingPointsPerLevel` on
level-up — which is **exactly** the original's `pool(+0x16) += nLevels·w[+0xEA]`. So the
level-up grant is **already 1:1**, despite the "documented approximation" comment.

### TASK 2 CONFIDENCE
- No separate SLP pool exists; the single pool is +0x16 = TrainingPoints, fed by +0xEA:
  **CONFIRMED.**
- Pool is spent to raise skills (+ gold), not spells; spell learning is gold-only:
  **CONFIRMED.**
- `+0xE8` (`SpellLearningPointsPerLevel`) is never read by the engine: **CONFIRMED** (exhaustive
  `[reg+0xE8]` disp32 scan — no real instruction).
- Whether the skill-trainer services (PlaceAction 0/1/2) are actually wired in the remake's
  `PlaceActionManager` was **not checked** in this pass (see recommendation).

### TASK 2 — C# implementation recommendation
**Do NOT add a separate SLP pool — it would be a fiction.** Instead:

1. **Fix the misleading comment** at `SheetApplier.cs:413-418`. The code is already correct
   (1:1 with `fcn.00037c22`): pool +0x16 (`Combat.TrainingPoints`) grows by
   `TrainingPointsPerLevel` (+0xEA) per level. Replace the "documented approximation / SLP folded
   into TP" comment with the confirmed RE: "this IS the original's points pool; granted
   w[+0xEA]/level, spent by the skill-trainer NPC services."
2. **The real gap is the skill-trainer NPC service** (PlaceAction 0/1/2), if not present:
   it must **spend `Combat.TrainingPoints`** (1:1 with points gained) **and** pooled gold
   (Unk6 tenths per point) to raise `Skills[CloseCombat/Ranged/CritChance/LockPicking].Current`
   up to its `.Max`, via a number-input dialog. That is the only consumer of the pool.
3. **Spell learning** (LearnSpells service) should remain **gold-only**
   (price = Unk6 × LevelRequirement, in tenths), set the known-bit, seed mastery = 4 ×
   EffMagicTalent. **No pool spend.**
4. Optionally rename the C# field `SpellLearningPointsPerLevel` (0xE8) to `UnusedE8` /
   `DeadIskaiField` with a comment "set for a few Iskai monsters in source data; never read by
   the engine," to stop future readers re-deriving the wrong SLP theory.

---

## What couldn't be determined
- **Task 1:** the exact byte in the on-map chest data table that arms lock-record +0x1E (a
  dedicated flag vs a high bit of difficulty/text), and the concrete trap event chains per
  chest (`fcn.0003529d` target scripts) — these are data-format / per-map questions, not in the
  handler code.
- **Task 2:** whether the remake's `PlaceActionManager` already implements the skill-trainer
  services that spend the pool (out of scope for this RE pass; flagged for the C# side).
