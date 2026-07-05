# RE: Combat Audio (MAIN.EXE) — settling the 443/762 "sound vs text" contradiction

**Task:** _TODO_1TO1.md §9 T2.b / doc-drift item 10. Determine which combat "sound" ids are
SAMPLES (real audio) vs SYSTEXTS (message-window text), decode combat-music selection, and
produce a fix list for `CombatAudio.cs` / `Battle.cs`.

Status: **COMPLETE (2026-07-05)** — contradiction settled in _RE_NOTES.md's favour with one
correction (Sample 268 = per-hit damage thud, not death scream). Each fact carries the
radare2 evidence address. See "Definitive cue table" + "Fix list" at the bottom.

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

## Open questions — ALL RESOLVED

1. Strike pipeline 446/449/451/452/455 → ShowCombatMessage → ShowSystemMessage (F1/F2/F5).
2. 442/453/454/698 → all SYSTEXTS (F4).
3. Combat music: fixed Song 26 on entry, SetMapMusic([0x147998]) restore on exit (M3/M4).
4. No victory fanfare; party-wipe game-over screen plays Song 43 (M4/M5).

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

### F3. fcn.0004dec9 (ApplyDamage): Sample 268 plays on EVERY damaging hit, not just death — CORRECTION to _RE_NOTES

Flow (obj = target combatant struct, edx = damage; only called with damage > 0):
1. Clears the 24-byte script block @ 0x15f126 (death script context).
2. Damage wakes the target: ClearCondition(9 = Asleep) @ 0x4df1e.
3. Writes damage to obj+0x4a, display timer 20 to obj+0x4c, starts anim sub_idx 8
   (hit flash / floating damage number) via fcn.00051ab5 @ 0x4df48.
4. Branch on **word[obj+0] == 1** (team; 1 = party — team-1 combatants live in the 6-slot
   array @ 0x15e6ac, other side @ 0x15e944, both 0x6e-byte stride, melee cb 0x4eafd/0x4eb19):
   - party victim:   0x4df78 `PlaySample(268, prio 100, vol 100, var 50, rate 0→11000)`
   - monster victim: 0x4e03c `PlaySample(268, prio 100, vol 60,  var 50, rate 15000)`
   Both then ShowCombatMessage(**450** = "{VICT}{NAME} receives {DAMG} damage!", ebx=damage)
   @ 0x4df8d / 0x4e051.
5. THEN the LP math (fcn.0003674c read @ 0x4e05c, SetLifePoints fcn.00036799 @ 0x4e09c) and,
   if LP hits 0, the death script fcn.0002fce1(0x15f126) @ 0x4e0cc + anim + fcn.0004e124.

So sample 268 is the generic **"damage lands" thud** (pitch/volume keyed on the VICTIM's
side), and it is the ONLY sample the strike pipeline ever plays. There is no separate
death scream and no extra sound at LP==0. _RE_NOTES.md "Combatant dies → 268" is a
mislabel — the play sites sit before the LP check, on every ApplyDamage call.

- fcn.0004de64 = ShowCombatMessageWithName(combatant, textId): sprintf(buf, 300,
  systexts[id], nameFromSheet) → fcn.0002f8b4 (direct text post). Callers 0x4b876/0x4bb2f
  read id tables @ 0x4a96d {737,740,629,738} / 0x4a975 {419,739,627,422} — the
  haste/freeze/blind/berserk apply+expiry announcements ("%s has been magically
  accelerated!" etc.). Also text, not sound.

### F4. The remaining "sound" ids Battle.cs raises — all SYSTEXTS too

| id | site | evidence | SYSTEXTS string (XLDLIBS\ENGLISH\SYSTEXTS) |
|---|---|---|---|
| 442 | 0x56cb2 (player Move picker, after fcn.0005717b claim check) | `mov eax,0x1ba; call fcn.0002f85d` — direct ShowSystemMessage | `{INK 006}Nowhere to move!` |
| 454 | 0x4f59d→0x4f5a9 inside fcn.0004f4da (ammo reserve empty; also clears self+0x4e and sets [0x15f140]=1) | ShowCombatMessage | `{COMB}{NAME} has used up {HIS } ammunition!` |
| 698 | 0x5fcfa→0x5fd08 in fcn.0005fb21 (spell resolution: zero continuations ⇒ fizzle; caster from [0x1775ae]; clears caster+0x4e) | ShowCombatMessage | `{COMB}{NAME}'s spell does not hit anybody!` |
| 453 | 0x4edf9 / 0x4f3a0 (strike loop stops: target slot empty/dead) | ShowCombatMessage | `{COMB}{NAME} misses!` |
| 414 | 0x4b693→0x4b6ce (combat trap trigger, grid slot from 0x176d74) | ShowCombatMessage | `{COMB}{NAME} triggered a trap!` |

(The other `mov eax,0x1ba` hit @ 0x94486 is arithmetic in unrelated AIL driver code, not a
text/sample id.) Text 448 "Attack repelled!" has no code reference (searched b8/b9/66b8/66b9
encodings) — unused, like the dead evasion branch's 447 (`{VICT}{NAME} cannot be hurt!`,
compiled-out at 0x4ecab/0x4f252 behind `xor eax,eax; test eax,eax`).

### F5. Ranged callback fcn.0004f057 mirrors melee exactly (all text)

446 @ 0x4f202, 455 @ 0x4f216, 447 @ 0x4f252 (dead code), 449 @ 0x4f2f6, 451 @ 0x4f36d,
452 @ 0x4f381, 453 @ 0x4f3a0 — all via fcn.0004ddfb. Ammo is consumed via fcn.0004f3e2
(call @ 0x4f1de, empty → fcn.0004f4da → text 454). **No projectile-launch sample exists**
(no fcn.00062a41 call site anywhere in 0x4f000–0x53000; the 14-byte strike table @ 0x13e370
holds gfx + projectile speed only). Ranged shots are silent apart from the impact thud (268
via ApplyDamage) — same as melee.

### F6. Retreat / flee — text only

fcn.0004f5d3 (Flee, action kind 4): ShowSystemMessage(**445**) directly (`mov eax,0x1bd;
call fcn.0002f85d` @ 0x4f60f/0x4f614 and 0x4f63d/0x4f642 — the mob-row and party-row
branches), then SetCondition(5 = Fleeing). No sample.

### F7. Condition announcements 762..773 — text only (re-confirmed)

fcn.000363c2 (SetCondition): for party members newly gaining condition c, calls
ShowSystemMessage(762 + c) (add eax,0x2fa @ 0x3643b → fcn.0002f85d). No sample.

## Music (the T2.b headline)

### M1. Three XMIDI slots

| fn | slot globals (busy / current-song) | role |
|---|---|---|
| fcn.00062629 = PlaySongSlotA(songId) | 0x177fd6 / 0x177fd9 | **map music** track |
| fcn.00062765 = PlaySongSlotB(songId) | 0x177fac / 0x177faf | **ambient** track (also the script "song" opcode @ 0x6af37) |
| fcn.00062925 = PlaySongSlotC(songId) | 0x177fc1 / 0x177fc4 | one-off event songs (map sound-event case 0 @ 0x3b57d) |

All three: songId 0 ⇒ stop slot; same-song ⇒ early-out (no restart); load file 0x1d
(SONGS0.XLD) sub-entry songId → AIL sequence via fcn.0008f2cb.

### M2. fcn.00012764 = SetMapMusic(musicIdx) — the dual song+ambient table

Validates 1 <= idx < 0x3c (60), then:
- PlaySongSlotA( word[0x11749 + idx*2] )  — music
- PlaySongSlotB( word[0x117c1 + idx*2] )  — ambient

The two 60-entry word tables match the "L Song Ambient" comment block in UAlbion
`src/Base/Song.cs` **exactly** (e.g. idx 17→0x11/0x07, 19→0x13/0x1F, 20→0x14/0x05,
25→0x19/0x08, 35→0x23/0x15, 43→0x03/0x09; idx 0 holds 0x32/0x00 but is unreachable —
idx 0 returns early). Note idx 19/20 map to songs 19/20 ("CombatMusic"/"TechCombatMusic"
in the UAlbion enum) — those two songs are *map-music table entries*, NOT what combat
plays. The current map's music index lives at **word [0x147998]** (set on map load
@ 0x11a1c/0x11a3d/0x11bf2 and by the map sound-event music case @ 0x3b528).

Callers: 0x11bf6 (map enter), 0x3b520 (map "sound" event music case; arg 0xFFFF ⇒ replay
current [0x147998]), **0x4ae1a (combat teardown — restore)**, 0x6aef0 (script opcode).

### M3. Combat entry music — FIXED Song 26, no selection

Combat scene init fcn.0004ac00 (combat.c, loads COMBACK file 0x27 sub [0x15f10c]):
```
0x4ac16  PlaySongSlotA(0)      ; stop map music
0x4ac1d  PlaySongSlotB(0)      ; stop ambient
...grid/task setup...
0x4ad0c  mov eax, 0x1a         ; 26
0x4ad11  call fcn.00062629     ; PlaySongSlotA(26)  ← THE combat music
```
The id is an immediate constant — **no terrain / combat-background / region selection**.
Every fight plays SONGS entry **26** (= UAlbion `Base.Song.CombatMusic2`). The combat
background id [0x15f10c] only selects COMBACK gfx + palette (table 0x13e150); it never
touches the music. The UAlbion names `CombatMusic`(19)/`TechCombatMusic`(20) describe map
music-table entries 19/20, not combat-entry behaviour.

### M4. Combat exit — restore map music, no victory fanfare

Combat teardown (0x4ade6..0x4ae27, runs on every combat exit path):
```
0x4ae12  xor eax,eax
0x4ae14  mov ax, word [0x147998]   ; current map's music index
0x4ae1a  call fcn.00012764         ; SetMapMusic → restores music + ambient
```
No PlaySample / jingle anywhere in the combat module besides ApplyDamage's 268 (checked
the full fcn.00062a41 xref list: nothing in 0x4a000–0x5a000 except 0x4df78/0x4e03c).
**Victory plays no fanfare** — outcome case 0/1 of the post-combat dispatcher
(fcn.0004a97d switch on [0x15f112]-1 @ 0x4aa8d) goes straight to loot/XP handling
(fcn.000648a7) and the teardown's map-music restore.

### M5. Defeat — game-over screen switches to Song 43

Outcome case 2 (party wiped) @ 0x4aad2 → **fcn.00035541 = ShowGameOverScreen**:
```
0x35559  fcn.0006f0d9            ; fade/teardown
0x3555e  PlaySongSlotA(0x2b=43)  ; game-over dirge (UAlbion Base.Song.HalfBrokenMidi)
0x35568  PlaySongSlotB(0)        ; stop ambient
0x3556f  fcn.00061d71            ; stop all one-shot samples
0x3558f  fcn.0006bdc5(11, ..., 3); show picture 11 (game-over art)
0x3559e  word[0x147134] = 1      ; game-over flag
```
Also reached via fcn.000354ac (party-wipe check used by rest/poison paths — callers
0x377b7, 0x383c5, 0x3ab55, 0x4aaf3/0x4ab2e (post-combat leader-dead check), 0x4b489/0x4b490,
0x64905, 0x64c29). So Song 43 is the "party dead" music wherever the wipe happens.

## Definitive cue table (event → id space → id)

| combat event | original output | id | notes |
|---|---|---|---|
| Move announced | TEXT | 444 | `{COMB}{NAME} is moving.` |
| Move blocked (resolution) | TEXT | 443 | `{INK 006}Move was blocked!` |
| Move refused (picker, no valid tile / tile claimed) | TEXT | 442 | `{INK 006}Nowhere to move!` |
| Flee announced | TEXT | 445 | `{COMB}{NAME} is fleeing!` |
| Attack with weapon | TEXT | 446 | `{COMB}{NAME} is attacking {VICT}{NAME} with {WEAP}!` |
| Attack bare-handed | TEXT | 455 | `{COMB}{NAME} attacks {VICT}{NAME}!` |
| To-hit roll failed | TEXT | 452 | `{COMB}{NAME} misses {HIS } victim!` |
| Strike-loop target gone/dead | TEXT | 453 | `{COMB}{NAME} misses!` |
| Critical hit | TEXT | 449 | `{COMB}{NAME} makes critical hit!` |
| Damage fully absorbed | TEXT | 451 | `No damage done!` |
| Damage lands (any amount > 0) | **SAMPLE + TEXT** | Sample **268** + text 450 | party victim: vol 100 rate 11000; monster victim: vol 60 rate 15000; both variation 50 (±25 vol, ±2500 Hz). Text `{VICT}{NAME} receives {DAMG} damage!` |
| Death (LP→0) | (nothing extra) | — | no scream/jingle beyond the 268 thud of the killing blow |
| Ammo exhausted | TEXT | 454 | `{COMB}{NAME} has used up {HIS } ammunition!` |
| Spell fizzle (nobody in area) | TEXT | 698 | `{COMB}{NAME}'s spell does not hit anybody!` |
| Combat trap triggered | TEXT | 414 | `{COMB}{NAME} triggered a trap!` |
| Condition gained (party) | TEXT | 762+cond | 12 messages, unchanged |
| Haste/Freeze/Blind/Berserk apply | TEXT | 419/739/627/422 | via fcn.0004de64 (%s = name) |
| Haste/Freeze/Blind/Berserk expire | TEXT | 737/740/629/738 | via fcn.0004de64 |
| Spell cast | SAMPLES | per-spell table | unchanged (_RE_NOTES.md table stands) |
| Combat entry | MUSIC | Song **26** on slot A | fixed; map music+ambient stopped first |
| Combat exit (any outcome incl. flee/victory) | MUSIC | SetMapMusic([0x147998]) | restores music + ambient pair |
| Victory | — | — | no fanfare |
| Party wiped | MUSIC | Song **43** | game-over screen (fcn.00035541), ambient stopped, samples stopped |

## Fix list for the remake

1. **`CombatManager.cs` (~line 62-69)**: replace the Toronto→TechCombatMusic /
   else→CombatMusic PLACEHOLDER with unconditional `Base.Song.CombatMusic2` (26).
   The restore-on-Complete (map song) already matches M4. Optionally also restore the
   map's *ambient* track if/when the remake models the dual music+ambient pair.
2. **`Battle.cs:128`** (move refused): replace `SoundEffectEvent(new SampleId(442)...)`
   with the combat message SYSTEXTS 442 ("Nowhere to move!") — same `ShowCombatMessage`
   helper the file already uses for 443/444/445.
3. **`Battle.cs:740`** (ammo exhausted): replace `SampleId(454)` with SYSTEXTS 454 text.
4. **`Battle.cs:1269` and `Battle.cs:1446`** (spell/item fizzle): replace `SampleId(698)`
   with SYSTEXTS 698 text.
5. **`CombatAudio.cs`**: the damage thud is currently gated on `e.Killed` — play Sample 268
   whenever `!e.Heal && e.Amount > 0` (killed or not), keep the side split (party victim
   vol 100/rate default-11000, monster vol 60/rate 15000) and add variation 50 semantics
   (vol ±25 clamp 0..127, rate ±2500 Hz clamp 1000..44100) if SoundEffectEvent supports it.
   Death adds nothing on top. Update the class comment ("death scream" → "damage thud").
6. **Strike/miss/crit/absorb text**: verify Battle.cs shows 446/455 on attack, 452 on miss,
   453 on strike-loop stop, 449 on crit, 451 on absorb, 450 with the damage number on hit —
   these are the original's only per-strike feedback (no audio).
7. **Game over**: on party wipe, play Song 43 (`Base.Song.HalfBrokenMidi` — worth renaming
   to `GameOverMusic`) with ambient/samples stopped, alongside the existing GameOver video.
8. **Doc drift**: _RE_COMBAT.md's "plays sound NNN" wording (lines ~314/320/705/728/732/1239,
   §"strike pipeline") should be re-worded to ShowCombatMessage/SYSTEXTS; _RE_NOTES.md's
   "combatant dies → 268" corrected to "damage lands → 268" (this file supersedes both).

All disassembly quotes from radare2 project `albion_aaa` (MAIN.EXE, DOS LE), 2026-07-05.
