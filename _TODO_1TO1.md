# UAlbion → 1:1 TODO — the single source of truth

> Everything still standing between the current build and a 1:1 match with the original
> MAIN.EXE implementation. Compiled 2026-06-12 from a ground-truth sweep of the source
> (every `PLACEHOLDER` / `TODO` marker), the RE docs, and the session logs. Update this
> file as items land; nothing in it is tracked anywhere else.
>
> Verification gates for every change: `dotnet test src/ualbion.ci.sln` (493 green),
> `_smoke_all_saves.ps1` (13/13), and a live harness check where behaviour is visible.

## 0. Current state in one line

All 11 original goal-tiers and the 19-item placeholder list are done except the
RE-blocked remainder below. Combat (strike pipeline, crits, XP, spells incl. margin
scaling, services, rest/fatigue, battle view, loot window, automap glyph rendering)
matches the RE'd formulas; the open items are mostly *small mechanics* and *constants*.

---

## 1. RE in flight (background agents running now)

### Cluster A — combat action executors → output lands in `_RE_5A.md`
| # | Question | Unlocks |
|---|---|---|
| A1 | Move path-finding `fcn.00051b51` (+`fcn.00053871`): search algorithm, walk pacing, blocked-path behaviour | `Battle.MoveCombatant` walks the route instead of teleporting |
| A2 | Ranged details `fcn.0004f057`: ammo matching/consumption/slot, row restrictions; **and whether MELEE has a reach restriction** (we currently let any tile hit any tile) | `Battle.cs:462` AI ranged commit, ammo in `ApplyMeleeAttack`, possible melee reach validation |
| A3 | Summon action `fcn.0004f6a2`: what's copied, into which slot, gates/caps | New Summon handler — **we don't implement Summon at all** |
| A4 | Insane/Panicking/Fleeing turn behaviour: insane target pick, panic move, when fleeing combatants LEAVE the battle | `MonsterAi.ResolveStatusBehavior` + `Battle` flee mechanic (comment at Battle.cs:476 admits "would flee if we had a flee mechanic") |
| A5 | Morale byte (sheet+0x0F) reads in combat AI — monster flight formula | Monsters never flee in the remake; Morale is parsed but unused |
| A6 | Hover bob: periodic bob on top of the MONCHAR hover offset (amplitude/period) | `BattleView.cs:26` placeholder |

### Cluster B — SPELLDAT & cast core → output lands in `_RE_5B.md`
| # | Question | Unlocks |
|---|---|---|
| B1 | SPELLDAT record layout + target-AREA enumeration: row spells, all-monster spells, the "Big" trap/mine tile shape | Area resolution in `Battle.CastQueuedSpell`; `TrapSpellEffect` area placement; `BanishDemons`/`DemonExodus` rows (`InstantKillSpellEffects.cs:101`); trap-vs-mine visibility (`OquloKamulosSpells.cs:25`) |
| B2 | GoddessWrath's random picker `fcn.0005f7ec` exact algorithm | Match our picker to the original |
| B3 | Per-item cast strength (the ITEMLIST byte the item-cast path reads) | `Battle.cs:793` — item casts currently run at M=100 |
| B4 | School 6 (AI primary) + school 4 spell lists, handlers, Ks | `SpellClass.Unk6`; monster AI casts the real specials |
| B5 | Insurance: fire/lightning lines margin-scaled? type-1 strength pool vestigial? | Confirms two generalisations made in code |

## 2. RE queued — NOT yet launched

### Cluster C — world tick & state (hour tick `fcn.00043acc`, 0x153b3e, query dispatcher)
| # | Question | Unlocks |
|---|---|---|
| C1 | ActiveSpells decay: slot layout in `SavedGame.ActiveSpells[0x50]` (Light = slots 0..1; Levitation = ?), decay trigger + rate, percentage→ambient mapping | Real Light/Levitation: replace `ActivePartySpells` booleans + the guessed ambient +50 / decay (`DjiKasSpells.cs:58`, `MapRenderable3D.cs:48`, `SupportSpellEffects.cs:246`, `Collider3D.cs:35`); persist in the save's existing field |
| C2 | Lockpicking: PickDifficulty vs LockPicking-skill formula + trap-trigger roll | `InventoryLockPane.cs:102` guessed probabilities |
| C3 | Query opcodes 0xC / 0x19 / 0x1E / 0x21 | `Querier.cs:87-90` hardwired `false` |
| C4 | Does an hourly hostile spawn interrupt rest? (small) | Possible rest-interruption mechanic |

### Cluster D — NPC & misc constants (lowest priority; could fold into C)
| # | Question | Unlocks |
|---|---|---|
| D1 | Chase give-up radius 2D/3D (we guess 16 Manhattan) | `Npc2D.cs:314`, `Npc3D.cs:114` |
| D2 | 3D NPC walk speed | `Npc3D.cs:21` |
| D3 | MonsterEye proximity levels (we guess 16 tiles) | `MonsterEye.cs:12` |
| D4 | 3D party collision margin (we use min(T/4, 50) world units) | `DungeonMap`/`Movement3D` |
| D5 | SetPartyLeader unk2/unk3 (we pass 3, 0) | `StatusBarPortrait.cs:89,198` |
| D6 | Sheet offset 0x1C (party-leave related) | `CharacterSheet.cs:241` |
| D7 | MapNpc / TickerSet / EventSet / BaseMapData leftover unknown fields | Format completeness |
| D8 | PlaceAction Unk2 (last unknown service field) | `PlaceActionManager` |

## 3. Implementation blocked only on the above RE

Each lands as soon as its cluster reports: A1→combat walk, A2→ammo+reach+AI ranged,
A3→Summon, A4/A5→flee/morale/insane fidelity, A6→hover bob, B1→area spells + trap
areas + trap/mine visibility, B2→wrath picker, B3→item cast strength, B4→school-6
registration, C1→Light/Levitation, C2→lockpicking, C3→queries, D→constant swaps.

## 4. Implementation possible NOW (no RE needed)

| Item | Where | Notes |
|---|---|---|
| 3D `change_npc_*` dispatch | `DungeonMap.cs` | 2D morphs work (live+persisted); 3D maps have no ChangeNpc dispatch and sprite changes need an ObjectGroup rebuild |
| AI ranged commit (interim) | `Battle.cs:462` | Could commit when the monster holds a LongRangeWeapon (gate exists since batch A); full ammo rules wait on A2 |
| Selection3D per-tile picking | `Selection3D.cs:22` | Ground-plane intersection only; wall-face ray refinement |
| Conversation default block | `Conversation.cs:187` | Unhandled BlockId falls through silently — enumerate which ids hit it (harness trace) and handle |
| Goddess' amulet activation | `InventoryManager.cs:223` | Special-item activation chain ("TODO: Goddess' amulet etc"); story-critical late-game item |
| VideoManager positioned pics | `VideoManager.cs:90` | x/y args ignored (intro uses 0,0); matters for any non-origin show_pic |
| TextFormatter Damage token | `TextFormatter.cs:40` | Guessed semantics; scan game texts for actual usage to confirm |
| Dead `TacticalSpriteId` member | `PartyMember.cs:63`, `ICombatParticipant` | Unused (grid reads `Effective.TacticalGfx`, which works for party+monsters) — remove |
| Animated 3D meshes | `MapObject.cs:102` | 3D map objects don't animate |
| Z-fighting hack | `MapObject.cs:178` | "still happens sometimes" |
| Weight limit on give | `InventoryManager.cs:514` | Party members can exceed carry weight |
| ChangeNpcMovement "other flags" | `NpcManager2D.cs` InitialiseState | Only SimpleMsg flag is mapped from MapNpcFlags |

## 5. Doc drift / comment cleanups (5 minutes each, do with next touch)

- `Battle.cs:1037` + `InventoryManager.cs:705` — say "PLACEHOLDER until the battle-loot
  window exists"; **the window exists** (batch A). Reword: broken gear staying equipped
  is a deliberate equivalent-outcome deviation.
- `PlaceActionManager.cs:34` — "Unk3/Unk4 ... aren't shown" is FALSE since batch A.
- `CombatBuffs.cs:13` — "magnitudes/durations PLACEHOLDER pending RE" is stale
  (durations RE'd; shield pct landed). Rewrite.
- `BattleView.cs:26` — placeholder list shrinks once A6 lands.
- `README.md` "Current Status" — still lists combat/magic/3D events/3D automap as
  UNIMPLEMENTED; all exist. Rewrite the section (good first impression for visitors).
- `_PROJECT_LOG.md`/`_SESSION_STATUS.md` — add an eighth-pass section when the RE
  sweep lands.

## 6. QoL / maintainer wishlist (not 1:1 fidelity, but wanted)

- 2D mouse-click path-finding movement (README wishlist).
- Key rebinding UI (`InputBinder` exists, no UI).
- Save-slot picker: scrollbar + 99 slots (`PickSaveSlotMenu.cs:23`); dedupe the save
  path logic (`GameState.cs` + `PickSaveSlotMenu.cs`).
- Keyboard support in conversation topic window (`ConversationTopicWindow.cs:20`).
- LoadMapPromptDialog → textbox (`LoadMapPromptDialog.cs:9`).
- i18n: price formatting (`TextFormatter.cs:164`).
- `DumpJson.cs:46` NRE when dumping event sets.
- New-game start coords → config (`MainMenu.cs:76`).
- NightPalettes → config/asset (`NightPalettes.cs:7`).

## 7. Verification debt (live checks to run after the next batches)

1. **Loot window**: kill a drop-carrying monster group via harness → window lists
   items/gold → Take All lands in party inventory.
2. **NPC morph persistence**: change_npc_sprite on a 2D map, leave, return → sprite
   still changed.
3. **Hourly events during rest**: poison drains and EveryHour chains fire across an
   inn stay (clock-advance path).
4. **Goto markers**: walk onto a Jirinaar marker tile → automap shows glyph 18 →
   Teleporter lists it; save/load → bit persists (offset 0x5972).
5. **Shield pct**: cast MagicShield in combat → trace shows defense multiplied, spell
   gate resist boosted.
6. **Full intro replay** after the RE sweep lands (regression vs `_PLAYTHROUGH.md`).

## 8. Known deliberate deviations (documented, NOT bugs — do not "fix")

- Broken equipment stays equipped+Broken instead of moving to the loot list
  (equivalent outcome; repairable via the RepairItem service).
- Ground shadows: 50%-black blend instead of the original's palette-remap LUT
  (0x17d25c) — the LUT is a dest-pixel remap the sprite pipeline can't express;
  visually identical minus palette quantisation.
- Berserk expressed additively (+half values) instead of in-place ×1.5 — identical at
  application, *more* accurate at expiry (no rounding loss).
- The remake draws a party marker on the automap (original uses a UI cursor).
- CombatBackground.Dungeon fallback for maps without one (original fatally asserts).
- Audio fail-soft: missing/busy audio device runs muted (original requires a device).
