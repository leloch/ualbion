# RE: Combat Audio (MAIN.EXE) — settling the 443/762 "sound vs text" contradiction

**Task:** _TODO_1TO1.md §9 T2.b / doc-drift item 10. Determine which combat "sound" ids are
SAMPLES (real audio) vs SYSTEXTS (message-window text), decode combat-music selection, and
produce a fix list for `CombatAudio.cs` / `Battle.cs`.

Status: IN PROGRESS (2026-07-05). Facts appended incrementally; each fact carries the
radare2 evidence address.

## Known facts going in (from _RE_NOTES.md, already CONFIRMED there)

- `fcn.0002f85d` = **ShowSystemMessage(textId)** — posts SYSTEXTS string to the combat/system
  message window (control @ [0x1530ea], msg 0x16). NOT a sample player.
- `fcn.00062a41` = **PlaySample(eax=id, edx=prio, ebx=vol, ecx=variation, push rate)** —
  SAMPLES0-2.XLD (file-table id 0x1e), real ids 0..299. 74 call sites.
- `fcn.00062f60` = PlayLoopedSample3D (ambients / positional map sounds).
- `fcn.00062925` = **PlaySong(songId)** — file 0x1d = SONGS0.XLD → AIL XMIDI via fcn.0008f2cb.
  Only ONE xref found so far: 0x3b57d (map "sound" event, case 0). (verified `axt` 2026-07-05)
- SYSTEXTS 443 = "Move was blocked!", 444 = "{NAME} is moving.", 445 = "{NAME} is fleeing!"
  (call sites 0x4e70f/0x4e788/0x4e78d/0x4e81b in the Move handler fcn.0004e6d1).
- SYSTEXTS 762+condId = per-condition announcements (fcn.000363c2 @ 0x3643b, add eax,0x2fa).
- Combatant death plays **Sample 268** (fcn.0004dec9 @ 0x4df78 vol100/rate11000 and
  @ 0x4e03c vol60/rate15000).
- Spell SFX: real samples 38, 201..268 via per-spell handlers (table in _RE_NOTES.md).

## Open questions

1. Strike pipeline fcn.0004eac1 / fcn.0004f057: are 446/449/451/452/455 ShowSystemMessage
   or PlaySample calls?
2. What are ids 442 (move refused), 453 (target died), 454 (ammo exhausted), 698 (fizzle)
   that Battle.cs currently raises as SampleId?
3. Combat music: which song plays on combat entry, how selected, restore on exit?
4. Victory/defeat fanfare?

## Findings

### F1. fcn.0004ddfb = ShowCombatMessage(attacker, target, value, textId) — CONFIRMED

105 bytes. All the strike-pipeline "PlaySound" calls in _RE_COMBAT.md are calls to THIS.

```
fcn.0004ddfb(eax=attackerPtr, edx=targetPtr, ebx=valueWord, ecx=textId):
    [0x15f100] = attackerPtr          ; 0x4de1f — {NAME} substitution source
    [0x15f108] = targetPtr            ; 0x4de27 — {TARG} substitution source
    [0x15f11c] = valueWord            ; 0x4de2f — {DAMG}-style numeric substitution
    [0x15f11e] = attacker ? attacker[+0x50] : 0   ; 0x4de42/0x4de4a
    ShowSystemMessage(textId)         ; 0x4de59 → call fcn.0002f85d  ← THE PROOF
```

It never touches PlaySample (fcn.00062a41). **Every id it is handed is a SYSTEXTS id.**
18 call sites: 0x4b6ce, 0x4df8d, 0x4e051, 0x4ec6b, 0x4ec7f, 0x4ecb8, 0x4ed5c, 0x4edd3,
0x4ede7, 0x4ee06, 0x4f20f, 0x4f223, 0x4f25f, 0x4f303, 0x4f37a, 0x4f38e, 0x4f3ad, 0x4f5a9,
0x5fd08.

### F2. Melee callback fcn.0004eac1 id loads (all → fcn.0004ddfb, i.e. TEXT)

| site | id | meaning (per pipeline position) |
|---|---|---|
| 0x4ec5e | 446 | armed attack announcement |
| 0x4ec72 | 455 | bare-handed attack announcement |
| 0x4ecab | 447 | (dead-code defender-evasion branch) |
| 0x4ed4f | 449 | critical hit |
| 0x4edc6 | 451 | damage fully absorbed (0 dmg) |
| 0x4edda | 452 | to-hit roll missed |
| 0x4edf9 | 453 | target died mid-strike-loop (loop stop) |

The ONLY audio call reachable from the strike resolution is Sample 268 inside
ApplyDamage (fcn.0004dec9) when the target dies. Melee swing/hit/miss/crit = silent,
text-only. _RE_NOTES.md was right; _RE_COMBAT.md's "PlaySound" naming was wrong.

### F3. fcn.0004dec9 (ApplyDamage) death cues

- Type-1 combatant (word[obj]==1): 0x4df78 PlaySample(268, prio100, vol100, var50, rate 0→11000)
  then 0x4df7d loads text **450** (with ebx = damage word) → ShowCombatMessage @ 0x4df8d.
- Else branch: 0x4e03c PlaySample(268, vol 60, rate 15000) + second ShowCombatMessage @ 0x4e051.
- fcn.0004de64 = ShowCombatMessageWithName(combatant, textId): sprintf(buf, 300,
  systexts[id], nameFromSheet) → fcn.0002f8b4 (direct text post). Callers 0x4b876,
  0x4bb2f (combat scene begin/end area), 0x6020f, 0x602b8, 0x602c7, 0x602d6 (spell-gate
  messages). Also text, not sound.
