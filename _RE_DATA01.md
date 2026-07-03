# RE: DataChange percentage-operation semantics (bug DATA-01)

> Read-only RE against MAIN.EXE (radare2 project `albion_aaa`), 2026-07-02. **COMPLETE.**
> Question: for NumericOperation 6/7 (Add/SubtractPercentage), what BASE is the
> percentage taken of, per ChangeProperty? Plus rounding/clamping/IsRandom order.

## TL;DR — the answer

The original has **two different numeric-op appliers**:

1. **fcn.0003e2f1** — used for **Attribute / Skill / Health / Mana** (stats with a real max).
   Ops 6/7 use `delta = amount * MAX / 100` — **percent of the stat's (effective) maximum**, then clamp to [0, max].
2. **fcn.0003e0d5** — used for **Experience / TrainingPoints / MaxHealth / MaxMana / Gold / Food** (no natural max).
   Ops 6/7 use `delta = amount * CURRENT / 100` — **percent of the current value**, then clamp result to [0, capLiteral]
   where capLiteral = 0x7FFFFFFF (Experience) or 0x7FFF (all the others).
3. **Gold and Food additionally run a validity pre-check (fcn.0003e1f8) first**: if the operation would
   leave [0, 0x7FFF] the event **does nothing at all** (no clamping — the change is rejected and the
   event is flagged failed via fcn.00031699).

The remake's `NumericOperation.Apply` (`immediate*(max-min)/100` with `max` defaulting to
ushort.MaxValue / int.MaxValue) is therefore **wrong for every property in group 2**: e.g.
`Gold AddPercentage 10` adds `10*65535/100 = 6553` gold instead of 10% of current gold. That is DATA-01.

## Event plumbing (context)

- **DataChange map-event handler = 0x3c0bd** (entry 8 in the handler-pointer table at 0x13da7c; see `_RE_ASK_SURRENDER.md`).
- Current event record = `0x153160 + [0x13d70a]*0x32 + 0x18` (records 0x32 bytes; the 12 event bytes start at +0x18).
- **On-disk byte layout confirmed** (matches remake serdes `DataChangeEvent.Serdes`):
  `+0` type (8), `+1` ChangeProperty, `+2` NumericOperation, `+3` TargetType (DataChangeTarget 0..7),
  `+4` IsRandom, `+5` TargetId, `+6..7` Extra (u16), `+8..9` Amount (u16).
- 0x3c0bd switches on **byte+3 TargetType** (8-entry jump table at 0x3c0d7: 0x3c141 Leader,
  0x3c16e Everyone-loop over party slots 0..5, 0x3c1ce, 0x3c26c, 0x3c2bf, 0x3c312, 0x3c376, 0x3c398),
  resolves each character-sheet pointer and calls the per-character worker (12 call sites).
- **Per-character worker = fcn.0003c3eb.** Watcom regcall: `eax` = char-sheet handle (var_20h),
  `edx` = party display index or 0xFFFF (var_14h), `ebx` = event-record ptr (var_1ch).
- **fcn.00031699** = "mark current event failed": `and byte [record+2], 0xFD` (clears bit 1 of the
  event bookkeeping byte). Also called by Door/Chest/Trap/Encounter handlers on their failure paths.

## fcn.0003c3eb — IsRandom first, then property switch

```
0x3c41f  mov al, [rec+2]           ; op   (var_ch)
0x3c428  mov ax, [rec+6]           ; Extra (var_8h)
0x3c432  mov ax, [rec+8]           ; Amount (var_4h)
0x3c43c  cmp byte [rec+4], 0       ; IsRandom?
0x3c440  je  0x3c4b7
0x3c442  call fcn.00094326         ; rand()
0x3c44b  mov bx, Amount
0x3c454  idiv ebx                  ; edx = rand() % Amount
0x3c456  inc edx
0x3c457  mov Amount, edx           ; Amount = 1 + rand() % Amount   (uniform 1..Amount)
```

- **IsRandom is applied BEFORE everything else**, including the percentage ops: the *percentage itself*
  is randomized (1..Amount %), then multiplied by the base. (Remake note: `Generate(Amount)` returns
  0..Amount-1; original is 1..Amount.)
- Then `al = [rec+1]` (ChangeProperty), `cmp 0x15; ja default` → 22-case jump table at 0x3c45f.

## Property cases (jump table 0x3c45f)

| Case | ChangeProperty | Addr | Applier | current arg | max arg |
|---|---|---|---|---|---|
| 0 | Attribute | 0x3c4d9 | fcn.0003e2f1 | fcn.00035da6(char, attr=Extra) = `word[sheet+Extra*8+0x2A]` | fcn.00035ec9 = `word[sheet+Extra*8+0x2C]` (attr max) |
| 1 | Skill | 0x3c522 | fcn.0003e2f1 | fcn.00036193(char, skill=Extra) = `word[sheet+Extra*8+0x7A]` | fcn.000362b6 = `word[sheet+Extra*8+0x7C]` (skill max) |
| 2 | Health | 0x3c56b | fcn.0003e2f1 | fcn.0003674c = `word[sheet+0xCA]` (cur LP) | fcn.00036839 = **effective max LP** (see below) |
| 3 | Mana | 0x3c5f5 | fcn.0003e2f1 | fcn.00036a2a = `word[sheet+0xD0]` (cur SP) | fcn.00036b17 = **effective max SP** |
| 5 | Status | 0x3c62a | bit ops only: op0 clear / op1 set / op2 toggle condition #Extra (fcn.0003645a / fcn.000363c2 / read fcn.000364b0) | — | — |
| 7 | Language | 0x3c6ae | inline: op0 `and byte[sheet+8],~(1<<Extra)`, op1 `or`, op2 `xor` | — | — |
| 8 | Experience | 0x3c706 | **fcn.0003e0d5** | fcn.00036d08 = `dword[sheet+0xEE]` | **0x7FFFFFFF literal** |
| 9 | TrainingPoints | 0x3c737 | **fcn.0003e0d5** | fcn.00036db2 = `word[sheet+0x16]` | **0x7FFF literal** |
| 12 | EventSetId | 0x3c76a | fcn.0003717d(char, 0, Amount) | — | — |
| 13 | WordSetId | 0x3c77f | fcn.0003717d(char, 1, Amount) | — | — |
| 16 | KnownSpells | 0x3c797 | inline bit ops on `dword[sheet+0xF2+Extra*4]` bit Amount (op0 clear/1 set/2 xor) | — | — |
| 17 | MaxHealth | 0x3c826 | **fcn.0003e0d5** | fcn.00036934 = `max(1, word[sheet+0xCC])` (raw max LP) | **0x7FFF literal** |
| 18 | MaxMana | 0x3c855 | **fcn.0003e0d5** | fcn.00036c12 = `max(1, word[sheet+0xD2])` (raw max SP) | **0x7FFF literal** |
| 19 | Item | 0x3c884 | fcn.0003e42a(char, item=Extra, count=Amount, op); on ret 0 → fcn.00031699 (fail) | — | — |
| 20 | Gold | 0x3c8b2 | **pre-check fcn.0003e1f8** then **fcn.0003e0d5** | `word[sheet+0x18]` (gold) | **0x7FFF literal** |
| 21 | Food | 0x3c96e | **pre-check fcn.0003e1f8** then **fcn.0003e0d5** | `word[sheet+0x1A]` (rations) | **0x7FFF literal** |
| 4,6,10,11,14,15 & >0x15 | unused | 0x3ca25 | no-op (straight to epilogue) | — | — |

Sheet offsets match the known character-sheet layout (attributes 8-byte blocks cur/max at +0x2A/+0x2C,
skills at +0x7A/+0x7C, LP cur/max 0xCA/0xCC, SP cur/max 0xD0/0xD2, TP 0x16, Gold 0x18, Rations 0x1A, Exp u32 0xEE).

### Effective max LP/SP (fcn.00036839 / fcn.00036b17)

Both start from the stored max (`word[sheet+0xCC]` / `word[sheet+0xD2]`) then loop over the 9
active-spell-effect slots at `sheet+0x2E6` (6 bytes each): for each active effect, add (or subtract if
`byte[slot+3] & 4`) the spell definition's `byte[+6]` (LP-max delta) / `byte[+7]` (SP-max delta).
Result floor-clamped to 1. So Health/Mana percentages (and the max clamp) are computed against the
**buff/debuff-adjusted** maximum, not the raw stored one.

## fcn.0003e2f1 — applier WITH max (Attribute / Skill / Health / Mana)

Args (Watcom regcall): `eax` = current (var_14h), `edx` = max (var_10h), `ebx` = op, `ecx` = amount.
Returns new value in eax. Switch on op, table 0x3e317; op 2 and op>7 → return current unchanged.

| Op | Semantics (evidence addr) |
|---|---|
| 0 SetToMinimum | `value = 0` (0x3e358) |
| 1 SetToMaximum | `value = max` (0x3e364) |
| 2 Toggle | **no-op** (table entry = epilogue 0x3e41b) |
| 3 SetAmount | `value = min(amount, max)` (0x3e36f) |
| 4 AddAmount | `value += amount; if (value > max) value = max` (0x3e390) |
| 5 SubtractAmount | `if (value < amount) value = 0; else value -= amount` (0x3e3a9) |
| **6 AddPercentage** | `delta = amount * MAX / 100; value += delta; if (value > max) value = max` (0x3e3c2) |
| **7 SubtractPercentage** | `delta = amount * MAX / 100; if (value < delta) value = 0; else value -= delta` (0x3e3ee) |

Key instructions (op 6, 0x3e3c2): `mov edx,[amount]; imul edx,[MAX]; mov ebx,100; idiv ebx` —
the multiplicand is the **max argument** (var_10h), NOT the current value. Signed division by 100
truncating toward zero (`sar edx,0x1f; idiv`). Clamp is [0, max] via the op-specific branches above.

After the call:
- Attribute/Skill/Mana: result written straight back (fcn.00035e23 / fcn.00036210 / fcn.00036a77).
- Health (0x3c596): if member index == 0xFFFF → direct LP write (fcn.00036799); else the delta is
  routed through the party damage path fcn.000375eb (old>new) or heal path fcn.000377c5 (new>old),
  so death/KO/UI processing happens as with combat damage.

## fcn.0003e0d5 — applier WITHOUT natural max (Exp / TP / MaxHP / MaxSP / Gold / Food)

Same reg args: `eax` = current, `edx` = cap literal, `ebx` = op, `ecx` = amount. Table 0x3e0fb;
op 2 and op>7 → unchanged.

| Op | Semantics (evidence addr) |
|---|---|
| 0 SetToMinimum | `value = 0` (0x3e13c) |
| 1 SetToMaximum | `value = cap` (0x3e148) — i.e. 32767 (or 2^31-1 for Exp)! |
| 2 Toggle | no-op |
| 3 SetAmount | `value = amount` (0x3e153) — **no clamp** |
| 4 AddAmount | `value += amount; if (value > cap) value = cap` (0x3e15e) |
| 5 SubtractAmount | `if (value < amount) value = 0; else value -= amount` (0x3e177) |
| **6 AddPercentage** | `delta = amount * CURRENT / 100; value += delta; if (value > cap) value = cap` (0x3e190) |
| **7 SubtractPercentage** | `delta = amount * CURRENT / 100; if (value < delta) value = 0; else value -= delta` (0x3e1bc) |

Key instructions (op 6, 0x3e190): `mov edx,[amount]; imul edx,[var_14h]` — var_14h is the
**current-value argument**. So for these properties the percentage base is the **current value**.
Same truncating integer division by 100.

## fcn.0003e1f8 — Gold/Food validity pre-check (also used by the Modify handler @0x3dd52/0x3dda8 for party pools)

Args: `eax` = current, `edx` = cap (0x7FFF), `ebx` = op, `ecx` = amount. Returns 1 = OK, 0 = reject.
Ops 0/1/2 (and >7): always OK. Switch on op-3 (table 0x3e21b):

| Op | Rejects when |
|---|---|
| 3 SetAmount | `amount > cap` (0x3e25a) |
| 4 AddAmount | `current + amount > cap` (0x3e26e) |
| 5 SubtractAmount | `current < amount` (0x3e282) |
| 6 AddPercentage | `current + (amount*current/100) > cap` (0x3e293) |
| 7 SubtractPercentage | `current < amount*current/100` (0x3e2bd) |

On reject (0x3c8d7/0x3c993 → 0x3c964/0x3ca20): **no value is computed or written** and
fcn.00031699 clears the event's "success" bit — the change is refused outright, *not clamped*.
E.g. `Gold SubtractAmount 10` with 5 gold held leaves gold at 5 (and flags failure), it does NOT zero it.
On success, the new value is stored (`word[sheet+0x18]`/`+0x1A]`) and a gain/loss jingle+message is
played (fcn.0004a817 gold / fcn.0004a8af rations, text ids 0x16/0x148) unless suppressed by the
flags at 0x15e5be/0x13e142.

Experience / TrainingPoints / MaxHealth / MaxMana have **no pre-check** — they really do clamp at the
cap (0x7FFF / 0x7FFFFFFF) inside fcn.0003e0d5.

## ANSWER TABLE — ops 6/7 per property

| ChangeProperty | Base for `amount%` | Formula (op 6 / op 7) | Clamp |
|---|---|---|---|
| Attribute | attribute **max** (`+0x2C`) | `cur ± amount*max/100` | [0, max] |
| Skill | skill **max** (`+0x7C`) | `cur ± amount*max/100` | [0, max] |
| Health | **effective max LP** (stored max ± active-spell LP deltas, ≥1) | `cur ± amount*effMax/100` | [0, effMax] |
| Mana | **effective max SP** (ditto, ≥1) | `cur ± amount*effMax/100` | [0, effMax] |
| MaxHealth | **current max LP** (raw stored, ≥1) | `max ± amount*max/100` | [0, 0x7FFF] |
| MaxMana | **current max SP** (raw stored, ≥1) | `max ± amount*max/100` | [0, 0x7FFF] |
| Experience | **current XP** | `xp ± amount*xp/100` | [0, 0x7FFFFFFF] |
| TrainingPoints | **current TP** | `tp ± amount*tp/100` | [0, 0x7FFF] |
| Gold | **current gold** | `gold ± amount*gold/100` — but **rejected** (no-op + fail flag) if result would leave [0, 0x7FFF] | none (reject instead) |
| Food | **current rations** | same as Gold | none (reject instead) |

Shared details:
- Rounding: truncating signed integer division by 100 (`imul` then `idiv 100`). A percentage of a
  0-valued base yields delta 0 (so op 6/7 on 0 XP/gold/etc. does nothing).
- IsRandom: `amount = 1 + rand() % amount` computed **first**; the randomized amount then feeds the
  percentage math (randomized *percentage*, not randomized delta).
- Op 2 (Toggle) is a no-op for every numeric property (only Status/Language/KnownSpells implement it, as bit-xor).
- Op 3 (SetAmount) clamps to max for the bounded stats but is unclamped in fcn.0003e0d5
  (Gold/Food guard it via the pre-check; Exp/TP/MaxHP/MaxSP take the raw amount, though amount is u16 anyway).

## Remake comparison & recommendation

Current remake (`src/Formats/MapEvents/NumericOperation.cs`):
`AddPercentage => existing + immediate*(max-min)/100`, clamped, with `max` defaulting to
`ushort.MaxValue` (`Apply16`) / `int.MaxValue` (`Apply`). Call sites in `src/Game/State/SheetApplier.cs`:

| Property | Remake today | Original | Verdict |
|---|---|---|---|
| Health/Mana (`CharacterAttribute.Apply` passes max) | % of stored Max | % of *effective* max (spell-buff adjusted) | Essentially correct; buff adjustment is a nuance (remake models boosts separately) |
| Attribute/Skill | % of stored Max | % of max | Correct |
| MaxHealth/MaxMana (`ApplyToMax`, default max) | % of **65535** | % of **current max** | **BUG (DATA-01)** |
| Experience (`Apply`, default max) | % of **int.MaxValue** | % of current XP | **BUG (DATA-01)** |
| TrainingPoints/Gold/Food (`Apply16`, default max) | % of **65535** | % of current value | **BUG (DATA-01)** |

Recommended fix:
1. In `NumericOperationExtensions.Apply`, make ops 6/7 use `existing` as the base whenever no explicit
   max is supplied — or better, add an explicit `percentBase` notion: bounded stats pass their max,
   everything else passes `existing`. Simplest faithful form:
   - bounded call sites (attribute/skill/HP/SP `CharacterAttribute.Apply`): keep `immediate * Max / 100` (min is always 0 in the original; drop `-min`).
   - unbounded call sites (Exp/TP/Gold/Food/`ApplyToMax`): `immediate * existing / 100`, clamped to [0, 32767] (Exp: int.MaxValue).
2. Gold/Food should implement the original's **reject-don't-clamp** rule for ops 3–7 (including
   plain SubtractAmount with insufficient funds) and signal event-chain failure, mirroring
   fcn.0003e1f8 + fcn.00031699. This affects gameplay (paying costs you can't afford must fail).
3. Caps: TrainingPoints/Gold/Food/MaxHealth/MaxMana clamp at 32767 (not 65535); SetToMaximum for the
   unbounded group means "set to 32767", which events in practice never use.
4. IsRandom: original is `1 + rand()%Amount` (1..Amount); remake `Generate(Amount)` is 0..Amount-1.

## Function index

| Addr | Role |
|---|---|
| 0x3c0bd | DataChange map-event handler (switch on TargetType byte +3) |
| 0x3c3eb | per-character apply worker (IsRandom, switch on ChangeProperty byte +1) |
| 0x3e2f1 | numeric-op applier, bounded stats — **% of max** |
| 0x3e0d5 | numeric-op applier, unbounded stats — **% of current**, cap literal |
| 0x3e1f8 | Gold/Food/Modify range pre-check (reject, don't clamp) |
| 0x31699 | mark current event failed (`record[+2] &= ~2`) |
| 0x35da6/0x35ec9/0x35e23 | attribute current-get / max-get / set (index in dx) |
| 0x36193/0x362b6/0x36210 | skill current-get / max-get / set |
| 0x3674c/0x36839/0x36799 | LP current-get / effective-max-get / direct set |
| 0x375eb/0x377c5 | party-member damage / heal paths (used for Health changes with UI) |
| 0x36a2a/0x36b17/0x36a77 | SP current-get / effective-max-get / set |
| 0x36934/0x3699a | raw max-LP get (≥1) / set |
| 0x36c12/0x36c78 | raw max-SP get (≥1) / set |
| 0x36d08/0x36d54 | experience get (u32 @0xEE) / set |
| 0x36db2/0x36dfc | training-points get (u16 @0x16) / set |
| 0x3e42a | item add/remove worker (Item case) |
| 0x94326 | rand() |
