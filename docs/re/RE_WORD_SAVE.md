# RE: Discovered Conversation Words — savegame storage

> Goal: find where Albion's savegame stores the player's *known/discovered conversation
> words* (the cross-NPC keyword bitfield) so UAlbion can persist `_discoveredWords`
> (a `HashSet<WordId>` in `GameState.cs`) across save/reload.

## KNOWN FACTS (confirmed from remake source)

- **F-1** Remake currently keeps known words ONLY at runtime: `GameState._discoveredWords`
  (`HashSet<WordId>`), populated by `WordKnownEvent`, `DiscoverTopics`, and conversation
  text scanning. NOT serialized → lost on save/reload. (`src/Game/State/GameState.cs:74-79,121-135`)
- **F-2** The save header is asserted to be **exactly 0x5b8c bytes** long
  (`SavedGame.cs:323`). Everything for word storage must live inside that header.
- **F-3** Header layout already decoded (offsets relative to `headerOffset`, i.e. after
  the 0x10-byte name+magic+version preamble). From `SavedGame.Serdes`:
  - `0x000` Unk9..PartyDirection (small fixed fields)
  - `0x011` ActiveSpells (0x50 ushorts = 0xA0 bytes)
  - `0x0B1` UnkB1 (0xE5 bytes)  ← **prime suspect region; see below**
  - `0x196` ActiveMembers (6 × 2)
  - `0x1A2` Unk1A2 / ActiveItems / HoursSinceResting / CombatPositions
  - `0x1B2` Misc (MiscState)
  - `0x272` Switches (FlagSet, SwitchCount=1024 bits = 0x80 bytes)
  - `0x2f2` DisabledChains (512 maps × 250 bits = 0x3e80 bytes)
  - `0x4172` RemovedNpcs (512 maps × 96 bits = 0x1800 bytes)
  - `0x5972` AutomapMarkers (256 bits = 0x20 bytes)
  - `0x5992` UnlockedChests (999 bits = 0x7d bytes)
  - `0x5A0F` UnlockedDoors (999 bits = 0x7d bytes)
  - `0x5A8C` Tickers (TickerSet) → runs to 0x5b8c (end of header)
- **F-4** The remake author already left a TODO at `SavedGame.cs:311`:
  *"KnownWord flag dictionaries. Known 3D automap info markers? Battle positions?"*
  and commented-out `_unk5Flags/_unk6Flags/_unk8Flags` with sizes
  `Unk5Count/Unk6Count/Unk8Count = 1500` ("Words?") = 0xBC bytes each (`SavedGame.cs:32-34`).
  **1500 bits = 0xBC bytes is the candidate word-bitfield size** (1 bit per word, ~1500 words).
- **F-5** Word identity in the remake is a **global** `WordId` = `(AssetType.Word<<24) | index`,
  serialized to disk as a U16 (`WordId.SerdesU16`, `WordKnownEvent.cs:21`). So word ids are a
  single global index space, not per-NPC dictionary offsets, at least as the remake models it.

## HEADLINE RESULT

**The original Albion DOS engine does NOT persist discovered conversation words to the
savegame.** The known-words bitfields are runtime-only heap arrays; no code path copies
them into either saved block. (CONFIRMED — see §S below, two independent radare2 passes,
cross-references exhausted.)

Consequence for the remake: a *1:1-faithful* implementation should also NOT save them
(they reset each process and are repopulated by play). If the maintainer wants a
*deviation* that improves QoL by persisting them, that requires an out-of-band /
appended field — it must NOT go inside the 0x5b8c (orig 0x5bb8) header, which is
byte-exact against the original and has no free space. See §R (Recommendation).

---

## INVESTIGATION LOG (radare2 evidence on MAIN.EXE)

### W. The word storage (in-memory) — CONFIRMED
The word system is the same generic "flag-array" facility as switches/chests/doors.
Two shared accessors take an **array selector in `eax`** and a **bit index in `edx`**:
- **`GetFlag` = fcn.000350fc** — returns `byte[base + (idx>>3)] & (1 << (idx&7))` (bit read @0x3523c).
- **`SetFlag` = fcn.0003529d** — op in `ebx` (0=Clear `and not`, 1=Set `or`, 2=Toggle `xor`);
  Set path @0x35404: `shr eax,3 ; add eax,[base] ; cl=idx&7 ; dl=1<<cl ; or [eax],dl`.

Selector table (identical in both accessors):

| sel | base | size | meaning |
|----|----|----|----|
| 0 | 0x153d9a | 0x80 (1024 bits) | Switches |
| 1 | 0x1594ba | 0x7d (999) | UnlockedChests |
| 2 | 0x159537 | 0x7d (999) | UnlockedDoors |
| 3 | 0x157c9a | 0x1800 (512×96) | RemovedNpcs |
| 4 | 0x153e1a | 0x3e80 (512×250) | DisabledChains |
| **5** | **[0x15e598]** (heap ptr) | **0x5dc bits = 0xBC bytes** | **WORDS — KNOWN** |
| **6** | **[0x15e5a0]** (heap ptr) | **1500 bits = 0xBC bytes** | **WORDS — seen/highlight ("new")** |
| 7 | 0x15949a | 0x100 (256) | AutomapMarkers |
| **8** | **[0x15e59c]** (heap ptr) | **1500 bits = 0xBC bytes** | **WORDS — 3rd state (set, test site not found)** |

The three word arrays are heap-allocated contiguously by **init fcn.00045e80**: malloc
handle → 0x15e58c; base → 0x15e598; 0x15e5a0 = base+0xBC; 0x15e59c = base+0x178; then
zeroed. Teardown nulls them (~0x460df). **They live ABOVE 0x1596e0**, i.e. outside the
saved header struct (which ends at 0x153b28+0x5bb8 = 0x1596e0).

### M. The "set word known" map-opcode (WordKnown) — CONFIRMED
The Modify opcode (0xD) handler **fcn.0003da9a**, subtype byte == 8, case @0x3dced:
```
bx = word[node+2]   ; operation (Clear/Set/Toggle)
dx = word[node+6]   ; WORD ID  (global u16)  -> matches remake WordKnownEvent layout
eax = 5             ; selector = KNOWN-words array
call fcn.0003529d   ; SetFlag(5, wordId, op)
; if word[0x15e5be] != 0:  eax = 6 ; call fcn.0003529d   ; also stamps array 6
```
This is the binary counterpart of `WordKnownEvent` (`src/Formats/MapEvents/WordKnownEvent.cs`)
and `GameState`'s `On<WordKnownEvent>` handler.

### G. The "test word known" gate — CONFIRMED
**GetFlag fcn.000350fc** has ~30 callers. Confirmed word gate: **fcn.00044b99 @0x44c48**
(conversation topic display) does `eax=5; call fcn.000350fc` to test KNOWN, then `eax=6`
to set the highlight marker. This is the dialogue topic-offer gate (remake equivalent:
`Conversation.cs` seeding `_topics` from `DiscoveredWords` and the topic window).

### I. Word-id → bit mapping — CONFIRMED
Word ids are the **GLOBAL** word index (not per-NPC dictionary offsets) by the time they
reach Get/SetFlag — the per-NPC→global resolution happens earlier (word-list load). Formula:
- **byte = base + (wordId >> 3)**
- **bit  = 1 << (wordId & 7)**
- Range guarded against 1500 (0x5dc). This matches the remake's global `WordId` (serialized U16).

### S. Save/Load — words are NOT persisted — CONFIRMED (decisive)
Note: a first pass swapped the SAVE/LOAD labels; corrected via the DOS int-21h primitives:
- **DOS_Write = fcn.00085d05** (strings "DOS_Write: ..."; low-level fcn.000aac03).
- **DOS_Read  = fcn.00085bca** (strings "DOS_Read: ..."; low-level fcn.000aa7bc → `mov ah,0x3f; int 0x21`).
- **SAVE = fcn.00024842** (calls DOS_Write; refs "SAVED_GAME_NR"/"ALBION").
- **LOAD = fcn.00024037** (calls DOS_Read).

The savegame = two fixed blocks + length-prefixed variable chunks:
- Header block: **edx=0x153b28, ebx=0x5bb8** (write @0x24b2b / read @0x2420b).
- Map/automap block: **edx=0x159c58, ebx=0x3000**.
- Plus scalar records (counts, checksum 0x25051971, magic 'K') and variable chunks whose
  source ptr comes from chunk-getters (fcn.0008ba38/0008b739/0008b808/00062f0a/…).

**Cross-reference proof:** `axt` over all four word globals (handle 0x15e58c, ptrs
0x15e598 / 0x15e59c / 0x15e5a0) returns ONLY:
- the init/alloc fn (0x45e8e..0x460e9), and
- GetFlag / SetFlag + inline accessors (0x3504c..0x3539e).
**None** appears inside SAVE (fcn.00024842), LOAD (fcn.00024037), or any of their
chunk-getter / IO helpers. No memcpy stages the word arrays into the header before write.
The header struct (0x153b28..0x1596e0) does not contain the word arrays, and the trailing
0x2c bytes (0x5b8c..0x5bb8) are far too small for even one 0xBC word block — so the word
data is not hidden there either.

### H. Header-size discrepancy: orig 0x5bb8 vs remake 0x5b8c — NOTED (not word-related)
The original header struct is **0x5bb8** bytes (asserted in saveload.c); the remake
asserts **0x5b8c** then reads a separate `Unknown5B8C = 0x2C` blob immediately after
(`SavedGame.cs:323-324`). 0x5b8c + 0x2c = 0x5bb8 — so the remake accounts for the same
bytes, just splitting the assert one field early. This 0x2c tail is NOT the word bitfield.

---

## §R RECOMMENDATION (for UAlbion `SavedGame` serdes)

**Confidence: HIGH** that the original does not persist words; **HIGH** on the in-memory
layout (3× 0xBC-byte bitfields, sel 5/6/8, global-word-id bit indexing).

Two valid options — pick per the maintainer's 1:1-vs-playability axis:

**Option A — stay 1:1 (do nothing to the save format).**
The current behaviour (runtime-only `_discoveredWords`, lost on reload) is *faithful*.
Just document it: add a one-line note next to `_discoveredWords` and remove/annotate the
`SavedGame.cs:311` TODO so it's not mistaken for a missing feature. Selector 6/8 (the
"seen/highlight" + 3rd state) are UI cosmetics; the remake collapses them into one set,
which is acceptable.

**Option B — QoL deviation: persist words (recommended if "revive/playable" wins).**
Do NOT touch the 0x5b8c/0x5bb8 header (byte-exact). Instead append AFTER the existing
serdes (after the final `s.Pad("Padding", 4)` in `SavedGame.Serdes`) a small, versioned,
optional trailer that the original loader would never see and the remake reads only if
present (guard on stream-remaining / a sentinel), so original-format saves still load:

```
// UAlbion extension trailer (NOT in the original format) — discovered words.
// Original game does not persist words (RE: docs/re/RE_WORD_SAVE.md). This is a deliberate
// QoL deviation; gated so vanilla 0x5bb8 saves still load.
if (s.IsWriting() || s.BytesRemaining > 0) {
    var marker = s.UInt32("WordExtMarker", 0x57524453); // 'WRDS'
    if (marker == 0x57524453) {
        // 0xBC bytes = 1500 bits, 1 bit per global WordId (byte = id>>3, bit = id&7)
        // Only the KNOWN array (orig selector 5) is gameplay-relevant.
        var packed = PackWords(save.DiscoveredWords); // byte[0xBC]
        s.Bytes("KnownWords", packed, 0xBC);
        save.SetDiscoveredWordsFromPacked(packed);
    }
}
```
Mapping: store as a `byte[0xBC]` (1500-bit) field on `SavedGame` (mirroring the original's
sel-5 array). `WordId.Id` (the global index) → `bit i`: `packed[i>>3] |= 1<<(i&7)`.
On load, enumerate set bits → `WordId.FromDisk(i, mapping)` → add to the set; on save,
`DiscoveredWords.Select(w => w.ToDisk(mapping))` → set bits. Reuse `FlagSet(1500)` exactly
as the other bitfields do (it already does byte=i>>3, bit=i&7 — see `FlagSet.GetPacked`).
Have `GameState.SaveGame`/`LoadGame` round-trip `_discoveredWords` ↔ this field.

Note `WordKnownEvent` already serializes the word as a global U16 disk id, so the global
index space is the correct key — no per-NPC dictionary translation needed.

**Pitfall:** keep the extension OPTIONAL and behind a marker so the 13 existing test saves
(which are vanilla 0x5bb8 layout) still pass `_smoke_all_saves.ps1`; appending an
unconditional 0xBC field would change file length and could trip the round-trip byte-equal
expectations / the loader's chunk walk. The append-after-everything + remaining-bytes guard
avoids that.
