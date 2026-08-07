# UAlbion - Reverse-Engineering Notes

> Tracks every `Unknown*` / `Unk*` field UAlbion has identified but not yet decoded.
> Phase 1.1 added a first-pass classification per file location.

## Status legend

| Status | Meaning |
|---|---|
| `unknown` | Not yet investigated. |
| `cosmetic` | Visual/UI only. |
| `save-state` | Save-file bookkeeping; round-trips byte-equal; no live gameplay impact known. |
| `combat` | Used by combat math (hit / damage / status). |
| `combat-suspect` | CharacterSheet field in 0x1C..0x90 block — combat-related per upstream notes. |
| `magic` | Used by spell-cast handlers. |
| `ai` | NPC / monster behaviour. |
| `world` | Map / lighting / fog / 3D-specific. |
| `script-arg` | Argument to a script Map / ScriptEvent — semantics depends on engine handler. |
| `flag-bit` | Bit within a known flag enum — needs per-bit observation. |
| `item-flag` | Bit within ItemFlags / ItemSlotFlags. |
| `status-flag` | Bit within PhysicalConditions / MentalConditions. |
| `sheet-other` | CharacterSheet field outside the combat block. |
| `suspected` | Hypothesis recorded but not verified. |
| `verified` | SR-traced or empirically confirmed. |
| `dead` | Padding / always-zero / no observable use. |

## Summary by status (Phase 1.1 first-pass)

| Status | Count |
|---|---|
| `unknown` | 232 |
| `flag-bit` | 65 |
| `combat-suspect` | 29 |
| `sheet-other` | 19 |
| `world` | 12 |
| `item-flag` | 6 |
| `status-flag` | 2 |

Total: 365

## Per-file index

| File | Unknowns |
|---|---|
| [NpcState.cs](#npcstate) | 58 |
| [CharacterSheet.cs](#charactersheet) | 49 |
| [MiscState.cs](#miscstate) | 26 |
| [ItemFlags.cs](#itemflags) | 10 |
| [SavedGame.cs](#savedgame) | 9 |
| [ActionType.cs](#actiontype) | 8 |
| [QueryType.cs](#querytype) | 8 |
| [FlatMapFlags.cs](#flatmapflags) | 8 |
| [Map3DFlags.cs](#map3dflags) | 8 |
| [PhysicalConditions.cs](#physicalconditions) | 8 |
| [MentalConditions.cs](#mentalconditions) | 8 |
| [DummyMapEvent.cs](#dummymapevent) | 7 |
| [LabyrinthObjectFlags.cs](#labyrinthobjectflags) | 7 |
| [AskSurrenderEvent.cs](#asksurrenderevent) | 7 |
| [CreateTransportEvent.cs](#createtransportevent) | 7 |
| [FloorAndCeiling.cs](#floorandceiling) | 7 |
| [PlaceActionEvent.cs](#placeactionevent) | 6 |
| [ModifyUnk2Event.cs](#modifyunk2event) | 6 |
| [NpcFlags.cs](#npcflags) | 6 |
| [LabyrinthData.cs](#labyrinthdata) | 6 |
| [DoScriptEvent.cs](#doscriptevent) | 6 |
| [ItemSlotFlags.cs](#itemslotflags) | 5 |
| [TrapEvent.cs](#trapevent) | 5 |
| [StartAnimEvent.cs](#startanimevent) | 5 |
| [ActionEvent.cs](#actionevent) | 4 |
| [ChangeUnkCEvent.cs](#changeunkcevent) | 3 |
| [ChangeUnkBEvent.cs](#changeunkbevent) | 3 |
| [SpellEnvironments.cs](#spellenvironments) | 3 |
| [Wall.cs](#wall) | 3 |
| [SpellClass.cs](#spellclass) | 3 |
| [ChangeProperty.cs](#changeproperty) | 3 |
| [TriggerTypes.cs](#triggertypes) | 3 |
| [TriggerType.cs](#triggertype) | 3 |
| [DisableEventChainEvent.cs](#disableeventchainevent) | 2 |
| [TeleportEvent.cs](#teleportevent) | 2 |
| [PlayAnimationEvent.cs](#playanimationevent) | 2 |
| [ChangeUnk0Event.cs](#changeunk0event) | 2 |
| [ExecuteEvent.cs](#executeevent) | 2 |
| [RemovePartyMemberEvent.cs](#removepartymemberevent) | 2 |
| [NpcActiveEvent.cs](#npcactiveevent) | 2 |
| [SetPartyLeaderEvent.cs](#setpartyleaderevent) | 2 |
| [EncounterEvent.cs](#encounterevent) | 2 |
| [SetMapLightingEvent.cs](#setmaplightingevent) | 2 |
| [TileFlags.cs](#tileflags) | 2 |
| [ChangeIconEvent.cs](#changeiconevent) | 2 |
| [MapNpc.cs](#mapnpc) | 2 |
| [AddPartyMemberEvent.cs](#addpartymemberevent) | 2 |
| [SpellTargets.cs](#spelltargets) | 2 |
| [AutomapInfo.cs](#automapinfo) | 2 |
| [PlayEvent.cs](#playevent) | 1 |
| [MapEventZone.cs](#mapeventzone) | 1 |
| [ChangeGoldEvent.cs](#changegoldevent) | 1 |
| [TileData.cs](#tiledata) | 1 |
| [NpcMovementTypes.cs](#npcmovementtypes) | 1 |
| [SpinnerEvent.cs](#spinnerevent) | 1 |
| [TickerEvent.cs](#tickerevent) | 1 |
| [SwitchEvent.cs](#switchevent) | 1 |
| [LabyrinthObject.cs](#labyrinthobject) | 1 |
| [SoundEvent.cs](#soundevent) | 1 |
| [ItemData.cs](#itemdata) | 1 |
| [ChangeLanguageEvent.cs](#changelanguageevent) | 1 |
| [ChangeItemEvent.cs](#changeitemevent) | 1 |
| [ChangeHealthEvent.cs](#changehealthevent) | 1 |
| [ChangeManaEvent.cs](#changemanaevent) | 1 |
| [ChangePartyRationsEvent.cs](#changepartyrationsevent) | 1 |
| [ChangePartyGoldEvent.cs](#changepartygoldevent) | 1 |
| [ChangeStatusEvent.cs](#changestatusevent) | 1 |
| [MapChange.cs](#mapchange) | 1 |
| [NumericOperation.cs](#numericoperation) | 1 |
| [OffsetEvent.cs](#offsetevent) | 1 |
| [VisitedEvent.cs](#visitedevent) | 1 |
| [ChangeFoodEvent.cs](#changefoodevent) | 1 |
| [ChangeExperienceEvent.cs](#changeexperienceevent) | 1 |
| [MapEventType.cs](#mapeventtype) | 1 |

## NpcState.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 108 | ushort | Unk4 | unknown |  | 4 |
| 109 | ushort | Unk6 | unknown |  | 6. Always 0? |
| 110 | byte | Unk8 | unknown |  | 8. Always 0? |
| 111 | long | Unk9 | unknown |  | 9. Always -1? |
| 112 | ushort | Unk11 | unknown |  | 11 |
| 113 | ushort | Unk13 | unknown |  | 13 Always 0xffff? |
| 114 | ushort | Unk15 | unknown |  |  |
| 115 | ushort | Unk17 | unknown |  |  |
| 116 | ushort | Unk19 | unknown |  | IsActive? |
| 117 | ushort | Unk1B | unknown |  |  |
| 118 | ushort | Unk1D | unknown |  |  |
| 119 | byte | Unk1F | unknown |  |  |
| 120 | byte | Unk20 | unknown |  |  |
| 121 | ushort | Unk21 | unknown |  |  |
| 122 | ushort | Unk23 | unknown |  |  |
| 123 | ushort | Unk25 | unknown |  |  |
| 124 | ushort | Unk27 | unknown |  |  |
| 125 | byte | Unk29 | unknown |  |  |
| 130 | byte | Unk32 | unknown |  |  |
| 131 | byte | Unk33 | unknown |  |  |
| 132 | ushort | Unk34 | unknown |  |  |
| 133 | ushort | Unk36 | unknown |  |  |
| 134 | ushort | Unk38 | unknown |  |  |
| 135 | ushort | Unk3A | unknown |  |  |
| 136 | ushort | Unk3C | unknown |  |  |
| 137 | ushort | Unk3E | unknown |  |  |
| 138 | ushort | Unk40 | unknown |  |  |
| 139 | ushort | Unk42 | unknown |  |  |
| 144 | ushort | Unk4C | unknown |  |  |
| 145 | ushort | Unk4E | unknown |  |  |
| 146 | byte | Unk50 | unknown |  |  |
| 147 | byte | Unk51 | unknown |  |  |
| 148 | byte | Unk52 | unknown |  |  |
| 149 | byte | Unk53 | unknown |  |  |
| 150 | ushort | Unk54 | unknown |  |  |
| 151 | ushort | Unk56 | unknown |  | Probably flags |
| 152 | ushort | Unk58 | unknown |  |  |
| 153 | ushort | Unk5A | unknown |  |  |
| 154 | ushort | Unk5C | unknown |  |  |
| 155 | ushort | Unk5E | unknown |  |  |
| 156 | byte | Unk60 | unknown |  |  |
| 157 | byte | Unk61 | unknown |  |  |
| 158 | ushort | Unk62 | unknown |  |  |
| 159 | byte | Unk64 | unknown |  |  |
| 160 | byte | Unk65 | unknown |  |  |
| 161 | ushort | Unk66 | unknown |  |  |
| 162 | ushort | Unk68 | unknown |  |  |
| 163 | ushort | Unk6A | unknown |  |  |
| 164 | ushort | Unk6C | unknown |  |  |
| 165 | ushort | Unk6E | unknown |  |  |
| 166 | ushort | Unk70 | unknown |  |  |
| 167 | ushort | Unk72 | unknown |  |  |
| 168 | ushort | Unk74 | unknown |  |  |
| 169 | ushort | Unk76 | unknown |  | Always 0xffff? |
| 170 | ushort | Unk78 | unknown |  | Always 0xffff? |
| 171 | ushort | Unk7A | unknown |  |  |
| 172 | ushort | Unk7C | unknown |  |  |
| 173 | ushort | Unk7E | unknown |  |  |

## CharacterSheet.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 81 | byte | Unknown6 | sheet-other |  |  |
| 82 | byte | Unknown7 | sheet-other |  |  |
| 83 | byte | Unknown11 | sheet-other |  |  |
| 84 | byte | Unknown12 | sheet-other |  |  |
| 85 | byte | Unknown13 | sheet-other |  |  |
| 86 | byte | Unknown14 | sheet-other |  |  |
| 87 | byte | Unknown15 | sheet-other |  |  |
| 88 | byte | Unknown16 | sheet-other |  |  |
| 89 | ushort | Unknown1C | combat-suspect |  |  |
| 90 | ushort | Unknown20 | combat-suspect |  |  |
| 91 | ushort | Unknown22 | combat-suspect |  |  |
| 93 | ushort | Unknown24 | combat-suspect |  |  |
| 94 | ushort | Unknown26 | combat-suspect |  |  |
| 95 | ushort | Unknown28 | combat-suspect |  |  |
| 96 | ushort | Unknown2E | combat-suspect |  |  |
| 97 | ushort | Unknown30 | combat-suspect |  |  |
| 98 | ushort | Unknown36 | combat-suspect |  |  |
| 99 | ushort | Unknown38 | combat-suspect |  |  |
| 100 | ushort | Unknown3E | combat-suspect |  |  |
| 101 | ushort | Unknown40 | combat-suspect |  |  |
| 102 | ushort | Unknown46 | combat-suspect |  |  |
| 103 | ushort | Unknown48 | combat-suspect |  |  |
| 104 | ushort | Unknown4E | combat-suspect |  |  |
| 105 | ushort | Unknown50 | combat-suspect |  |  |
| 106 | ushort | Unknown56 | combat-suspect |  |  |
| 107 | ushort | Unknown58 | combat-suspect |  |  |
| 108 | ushort | Unknown5E | combat-suspect |  |  |
| 109 | ushort | Unknown60 | combat-suspect |  |  |
| 110 | ushort | Unknown66 | combat-suspect |  |  |
| 111 | ushort | Unknown68 | combat-suspect |  |  |
| 112 | byte | Unknown6C | combat-suspect |  |  |
| 113 | ushort | Unknown7E | combat-suspect |  |  |
| 114 | ushort | Unknown80 | combat-suspect |  |  |
| 115 | ushort | Unknown86 | combat-suspect |  |  |
| 116 | ushort | Unknown88 | combat-suspect |  |  |
| 117 | ushort | Unknown8E | combat-suspect |  |  |
| 118 | ushort | Unknown90 | combat-suspect |  |  |
| 121 | ushort | UnknownCE | sheet-other |  |  |
| 122 | ushort | UnknownD6 | sheet-other |  |  |
| 123 | ushort | UnknownDA | sheet-other |  |  |
| 124 | ushort | UnknownDC | sheet-other |  |  |
| 125 | uint | UnknownDE | sheet-other |  |  |
| 126 | ushort | UnknownE2 | sheet-other |  |  |
| 127 | ushort | UnknownE4 | sheet-other |  |  |
| 128 | uint | UnknownE6 | sheet-other |  |  |
| 129 | uint | UnknownEA | sheet-other |  |  |
| 131 | ushort | UnknownFA | sheet-other |  |  |
| 132 | ushort | UnknownFC | sheet-other |  |  |
| 133 | byte[] | UnkMonster | unknown |  | 472 bytes more than an NPC |

## MiscState.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | int | Unk0 | unknown |  |  |
| 11 | long | Unk8 | unknown |  |  |
| 12 | long | Unk10 | unknown |  |  |
| 13 | long | Unk18 | unknown |  |  |
| 14 | long | Unk20 | unknown |  |  |
| 15 | long | Unk28 | unknown |  |  |
| 16 | long | Unk30 | unknown |  |  |
| 17 | long | Unk38 | unknown |  |  |
| 18 | long | Unk40 | unknown |  |  |
| 19 | long | Unk48 | unknown |  |  |
| 20 | long | Unk50 | unknown |  |  |
| 21 | long | Unk58 | unknown |  |  |
| 22 | long | Unk60 | unknown |  |  |
| 23 | long | Unk68 | unknown |  |  |
| 24 | long | Unk70 | unknown |  |  |
| 25 | long | Unk78 | unknown |  |  |
| 26 | long | Unk80 | unknown |  |  |
| 27 | long | Unk88 | unknown |  |  |
| 28 | long | Unk90 | unknown |  |  |
| 29 | long | Unk98 | unknown |  |  |
| 30 | long | UnkA0 | unknown |  |  |
| 31 | long | UnkA8 | unknown |  |  |
| 32 | long | UnkB0 | unknown |  |  |
| 33 | long | UnkB8 | unknown |  |  |
| 34 | long | UnkC0 | unknown |  |  |
| 35 | long | UnkC8 | unknown |  |  |

## ItemFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 8 | ? | ? | item-flag |  | Double battle-axe only |
| 11 | flag | Unk3 | flag-bit |  |  |
| 12 | flag | Unk4 | flag-bit |  | Spell scrolls, potions, HUD items, throwing daggers/stones and lockpicks |
| 13 | flag | Unk5 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |
| 15 | flag | Unk7 | flag-bit |  |  |
| 16 | flag | Unk8 | flag-bit |  | Dji-Cantos stone only |
| 17 | flag | Unk9 | flag-bit |  |  |
| 19 | flag | Unk11 | flag-bit |  |  |
| 20 | flag | Unk12 | flag-bit |  |  |

## SavedGame.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 13 | ? | ? | unknown |  |  |
| 41 | ushort | Unk0 | unknown |  |  |
| 42 | uint | Unk1 | unknown |  |  |
| 43 | byte[] | Unk9 | unknown |  |  |
| 44 | byte[] | Unknown15 | unknown |  |  |
| 46 | byte[] | Unknown2C1 | unknown |  |  |
| 47 | byte[] | Unknown5B9F | unknown |  |  |
| 49 | byte[] | Unknown5B71 | unknown |  |  |
| 50 | Unknown35Byte[] | Unk35Byte | unknown |  | Len = 16 |

## ActionType.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 7 | flag | Unk2 | flag-bit |  | Pay money? See ES156 (Garris, Gratogel sailor) |
| 8 | flag | Unk4 | unknown |  |  |
| 13 | flag | Unk9 | unknown |  |  |
| 14 | flag | UnkE | unknown |  |  |
| 15 | flag | Unk17 | unknown |  |  |
| 16 | flag | Unk2D | unknown |  |  |
| 21 | flag | Unk39 | unknown |  |  |
| 22 | flag | Unk3D | unknown |  |  |

## QueryType.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 6 | flag | Unk1 | unknown |  |  |
| 7 | flag | Unk4 | unknown |  |  |
| 13 | flag | UnkC | unknown |  |  |
| 23 | flag | Unk1E | unknown |  |  |
| 25 | flag | Unk19 | unknown |  |  |
| 27 | flag | Unk21 | unknown |  |  |
| 30 | flag | Unk29 | unknown |  |  |
| 31 | flag | Unk2A | unknown |  |  |

## FlatMapFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 8 | ? | ? | world |  |  |
| 9 | flag | Unk1 | flag-bit |  |  |
| 10 | flag | Unk2 | flag-bit |  |  |
| 11 | flag | Unk3 | flag-bit |  |  |
| 12 | flag | Unk4 | flag-bit |  |  |
| 13 | flag | Unk5 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |
| 15 | flag | Unk7 | flag-bit |  |  |

## Map3DFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 8 | ? | ? | world |  |  |
| 9 | flag | Unk1 | flag-bit |  |  |
| 10 | flag | Unk2 | flag-bit |  |  |
| 11 | flag | Unk3 | flag-bit |  |  |
| 12 | flag | Unk4 | flag-bit |  |  |
| 13 | flag | Unk5 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |
| 15 | flag | Unk7 | flag-bit |  |  |

## PhysicalConditions.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | ? | ? | status-flag |  |  |
| 10 | flag | Unk1 | flag-bit |  |  |
| 11 | flag | Unk2 | flag-bit |  |  |
| 12 | flag | Unk3 | flag-bit |  |  |
| 13 | flag | Unk4 | flag-bit |  |  |
| 14 | flag | Unk5 | flag-bit |  |  |
| 15 | flag | Unk6 | flag-bit |  |  |
| 16 | flag | Unk7 | flag-bit |  |  |

## MentalConditions.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | ? | ? | status-flag |  |  |
| 10 | flag | Unk1 | flag-bit |  |  |
| 11 | flag | Unk2 | flag-bit |  |  |
| 12 | flag | Unk3 | flag-bit |  |  |
| 13 | flag | Unk4 | flag-bit |  |  |
| 14 | flag | Unk5 | flag-bit |  |  |
| 15 | flag | Unk6 | flag-bit |  |  |
| 16 | flag | Unk7 | flag-bit |  |  |

## DummyMapEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 47 | byte | Unk1 | unknown |  |  |
| 48 | byte | Unk2 | unknown |  |  |
| 49 | byte | Unk3 | unknown |  |  |
| 50 | byte | Unk4 | unknown |  |  |
| 51 | byte | Unk5 | unknown |  |  |
| 52 | ushort | Unk6 | unknown |  |  |
| 53 | ushort | Unk8 | unknown |  |  |

## LabyrinthObjectFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 8 | ? | ? | world |  |  |
| 9 | flag | Unk1 | flag-bit |  |  |
| 11 | flag | Unk3 | flag-bit |  |  |
| 12 | flag | Unk4 | flag-bit |  |  |
| 13 | flag | Unk5 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |
| 15 | flag | Unk7 | flag-bit |  |  |

## AskSurrenderEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 24 | byte | Unk1 | unknown |  |  |
| 25 | byte | Unk2 | unknown |  |  |
| 26 | byte | Unk3 | unknown |  |  |
| 27 | byte | Unk4 | unknown |  |  |
| 28 | byte | Unk5 | unknown |  |  |
| 29 | ushort | Unk6 | unknown |  |  |
| 30 | ushort | Unk8 | unknown |  |  |

## CreateTransportEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 22 | byte | Unk1 | unknown |  |  |
| 23 | byte | Unk2 | unknown |  |  |
| 24 | byte | Unk3 | unknown |  |  |
| 25 | byte | Unk4 | unknown |  |  |
| 26 | byte | Unk5 | unknown |  |  |
| 27 | ushort | Unk6 | unknown |  |  |
| 28 | ushort | Unk8 | unknown |  |  |

## FloorAndCeiling.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 16 | flag | Unknown3 | flag-bit |  |  |
| 17 | flag | Unknown4 | flag-bit |  |  |
| 24 | byte | Unk1 | unknown |  |  |
| 25 | byte | Unk2 | unknown |  |  |
| 26 | byte | Unk3 | unknown |  |  |
| 29 | byte | Unk5 | unknown |  |  |
| 31 | ushort | Unk8 | unknown |  |  |

## PlaceActionEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk2 | unknown |  |  |
| 37 | byte | Unk3 | unknown |  |  |
| 38 | byte | Unk4 | unknown |  |  |
| 39 | byte | Unk5 | unknown |  | Spell class for learn magic |
| 40 | ushort | Unk6 | unknown |  |  |
| 41 | ushort | Unk8 | unknown |  |  |

## ModifyUnk2Event.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | byte | Unk2 | unknown |  |  |
| 35 | byte | Unk3 | unknown |  |  |
| 36 | byte | Unk4 | unknown |  |  |
| 37 | byte | Unk5 | unknown |  |  |
| 38 | ushort | Unk6 | unknown |  |  |
| 39 | ushort | Unk8 | unknown |  |  |

## NpcFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 10 | flag | Unk2 | flag-bit |  |  |
| 11 | flag | Unk3 | flag-bit |  | Has contact event? |
| 12 | flag | Unk4 | flag-bit |  |  |
| 13 | flag | Unk5 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |
| 15 | flag | Unk7 | flag-bit |  |  |

## LabyrinthData.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 20 | ushort | Unk4 | world |  |  |
| 27 | byte | Unk12 | world |  |  |
| 28 | byte | Unk13 | world |  |  |
| 30 | byte | Unk15 | world |  |  |
| 37 | ushort | Unk20 | world |  |  |
| 39 | ushort | Unk24 | world |  |  |

## DoScriptEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | ? | ? | unknown |  |  |
| 37 | ? | ? | unknown |  |  |
| 38 | ? | ? | unknown |  |  |
| 39 | ? | ? | unknown |  |  |
| 40 | ? | ? | unknown |  |  |
| 41 | ? | ? | unknown |  |  |

## ItemSlotFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 11 | flag | Unk3 | item-flag |  |  |
| 12 | flag | Unk4 | item-flag |  |  |
| 13 | flag | Unk5 | item-flag |  |  |
| 14 | flag | Unk6 | item-flag |  |  |
| 15 | flag | Unk7 | item-flag |  |  |

## TrapEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk1 | unknown |  | Observed values- 1,6,7,11,255 |
| 37 | byte | Unk2 | unknown |  | 2,3 (2 only seen once) |
| 38 | byte | Unk3 | unknown |  | 0,1,2 |
| 39 | byte | Unk5 | unknown |  | [0..12] |
| 40 | ushort | Unk6 | unknown |  | [0..10000], mostly 6, 0 or multiples of 5. Damage? |

## StartAnimEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | int | Unk1 | unknown |  |  |
| 10 | int | Unk2 | unknown |  |  |
| 11 | int | Unk3 | unknown |  |  |
| 12 | int | Unk4 | unknown |  |  |
| 13 | int? | Unk5 | unknown |  |  |

## ActionEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 15 | byte | Unk2 | unknown |  | Always 1, unless ActionType == 14 (in which cas it is 2) |
| 58 | ? | ? | unknown |  |  |
| 59 | ? | ? | unknown |  |  |
| 60 | ? | ? | unknown |  |  |

## ChangeUnkCEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | byte | Unk5 | unknown |  |  |
| 37 | ushort | Unk6 | unknown |  |  |
| 38 | byte | Unk3 | unknown |  |  |

## ChangeUnkBEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | byte | Unk5 | unknown |  |  |
| 37 | ushort | Unk6 | unknown |  |  |
| 38 | byte | Unk3 | unknown |  |  |

## SpellEnvironments.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 15 | flag | Unk4 | flag-bit |  |  |
| 17 | flag | Unk6 | flag-bit |  |  |
| 18 | flag | Unk7 | flag-bit |  |  |

## Wall.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 19 | flag | Unk3 | flag-bit |  |  |
| 20 | flag | Unk4 | flag-bit |  |  |
| 33 | byte | Unk9 | unknown |  | 9 |

## SpellClass.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 11 | ? | ? | unknown |  | Unused |
| 13 | ? | ? | unknown |  | Unused |
| 24 | ? | ? | unknown |  |  |

## ChangeProperty.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 5 | ? | ? | unknown |  |  |
| 11 | flag | UnkB | unknown |  |  |
| 12 | flag | UnkC | unknown |  |  |

## TriggerTypes.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 21 | flag | Unk13 | flag-bit |  | 2000 |
| 22 | flag | Unk14 | flag-bit |  | 4000 |
| 23 | flag | Unk15 | flag-bit |  | 8000 |

## TriggerType.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 18 | ? | ? | unknown |  |  |
| 19 | ? | ? | unknown |  |  |
| 20 | ? | ? | unknown |  |  |

## DisableEventChainEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 39 | byte | Unk2 | unknown |  | Temp / permanent? |
| 40 | ushort | Unk6 | unknown |  | varies |

## TeleportEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 49 | byte | Unk4 | unknown |  | 255 on 2D maps, (1,6) on 3D maps |
| 50 | byte | Unk5 | unknown |  | 2,3,4,5,6,8,9 |

## PlayAnimationEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 40 | byte | Unk4 | unknown |  |  |
| 41 | byte | Unk5 | unknown |  |  |

## ChangeUnk0Event.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | ushort | Unk6 | unknown |  |  |
| 37 | byte | Unk3 | unknown |  |  |

## ExecuteEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 32 | byte | Unk1 | unknown |  |  |
| 33 | ushort | Unk8 | unknown |  |  |

## RemovePartyMemberEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk2 | unknown |  |  |
| 37 | ushort | Unk6 | unknown |  |  |

## NpcActiveEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk5 | unknown |  |  |
| 37 | ushort | Unk6 | unknown |  |  |

## SetPartyLeaderEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 35 | byte | Unk2 | unknown |  |  |
| 36 | byte | Unk3 | unknown |  |  |

## EncounterEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 32 | ushort | Unk6 | unknown |  |  |
| 33 | ushort | Unk8 | unknown |  |  |

## SetMapLightingEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | byte | Unk2 | unknown |  |  |
| 35 | byte | Unk3 | unknown |  |  |

## TileFlags.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 16 | flag | Unk18 | flag-bit |  |  |
| 16 | flag | Unk12 | flag-bit |  |  |

## ChangeIconEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 45 | byte | Unk5 | unknown |  |  |
| 46 | ? | ? | unknown |  |  |

## MapNpc.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 21 | byte | Unk8 | unknown |  |  |
| 22 | byte | Unk9 | unknown |  |  |

## AddPartyMemberEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 38 | byte | Unk2 | unknown |  |  |
| 39 | byte | Unk3 | unknown |  |  |

## SpellTargets.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | flag | Unk1 | flag-bit |  |  |
| 14 | flag | Unk6 | flag-bit |  |  |

## AutomapInfo.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 11 | byte | Unk2 | world |  |  |
| 12 | byte | Unk3 | world |  |  |

## PlayEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | int | Unknown | unknown |  |  |

## MapEventZone.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 14 | byte | Unk1 | unknown |  |  |

## ChangeGoldEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 38 | byte | Unk3 | unknown |  |  |

## TileData.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 18 | byte | Unk7 | unknown |  |  |

## NpcMovementTypes.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 12 | ? | ? | unknown |  |  |

## SpinnerEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 28 | byte | Unk1 | unknown |  |  |

## TickerEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 40 | byte | Unk4 | unknown |  | 0, 1 |

## SwitchEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk3 | unknown |  | 0,1,21 |

## LabyrinthObject.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 15 | byte | Unk7 | world |  | 7 |

## SoundEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 60 | byte | Unk3 | unknown |  | [0..100] (multiples of 5) |

## ItemData.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 16 | byte | Unknown | unknown |  | 0 Always 0 |

## ChangeLanguageEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 39 | byte | Unk3 | unknown |  |  |

## ChangeItemEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 41 | byte | Unk3 | unknown |  |  |

## ChangeHealthEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 38 | byte | Unk3 | unknown |  |  |

## ChangeManaEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 38 | byte | Unk3 | unknown |  |  |

## ChangePartyRationsEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 35 | byte | Unk3 | unknown |  |  |

## ChangePartyGoldEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | byte | Unk3 | unknown |  |  |

## ChangeStatusEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 40 | byte | Unk3 | unknown |  |  |

## MapChange.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 23 | Enum2 | Unk3 | unknown |  | Ranges over [0..3], 3 very popular, 0 moderately, 1 and 2 ~1% each. |

## NumericOperation.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 9 | ? | ? | unknown |  |  |

## OffsetEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 36 | byte | Unk3 | unknown |  |  |

## VisitedEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 13 | byte | Unk0 | unknown |  |  |

## ChangeFoodEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 39 | byte | Unk3 | unknown |  |  |

## ChangeExperienceEvent.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 38 | byte | Unk3 | unknown |  |  |

## MapEventType.cs

| Line | Type | Field | Status | Purpose | Note |
|---|---|---|---|---|---|
| 34 | flag | UnkFf | flag-bit |  | 3D only? |


## automap.c (RE'd 2026-06-12)

Reverse-engineered from MAIN.EXE (radare2 project `albion_aaa`). `__FILE__` string `g:\albion\src\automap.c` at 0x13168c.
The source file spans roughly **0x5acbe–0x5ea68** (the next file, `magic.c`, starts at fcn.0005ea69). Status tags: **CONFIRMED** = read directly from disasm; **INFERRED** = consistent interpretation, not byte-traced.

### Key globals

| Address | Meaning | Status |
|---|---|---|
| `0x153b32` | current map id (word); `0x153b34/36` = party X/Y (1-based) | CONFIRMED |
| `0x14799a` / `0x147994` | map width / height | CONFIRMED |
| `0x14799c` | map-has-walls / 3D flag (gates BlocksSight) | INFERRED |
| `0x14799e` | labyrinth automap-style selector: !=0 → AUTOGFX id 2 + palette 0x2E, ==0 → AUTOGFX id 1 + palette 0x15 | CONFIRMED |
| `0x147130` | "whole map visible" override (TestTile always 1; also unlocks all markers) | CONFIRMED behaviour, source of flag INFERRED (likely map flag/cheat/spell) |
| `0x13e780` | special-item bitfield: bit0 → monster markers, bit1 → person markers, bit2 → trap/secret + type-5 event markers | CONFIRMED bits, item identity INFERRED |
| `0x176fec` / `0x176fe8` | discovery bitfield handle / size in bytes | CONFIRMED |
| `0x177588` | discovery dirty flag (set on SetTile, cleared on flush) | CONFIRMED |
| `0x176fdc` | AUTOGFX resource handle (type 0x1C); `0x176ff0` = compose buffer (2 words/cell); `0x176ff4` = floor mini-tile array | CONFIRMED |
| `0x14a48c` | camera yaw, 16.16 fixed, full circle = 0x4000 | CONFIRMED |
| `0x147134` | automap zoom/detail mode; `>>16 == 2` → generic event markers drawn as glyph 19 | CONFIRMED use, semantics INFERRED |

### Function inventory

| Address | Size | Name (inferred) | Notes |
|---|---|---|---|
| 0x5acff | ~4.5k (undefined in r2; contains 0x5b0d6 assert, line 441) | `AUTOMAP_Show` | UI main: computes buffer dims (map + 8-cell border; stride `[0x17758a]`, height `[0x177592]`, map offset `[0x17758e]/[0x177590]`), allocates compose buffer W*H*4, loads AUTOGFX (type 0x1C, id 1 or 2 by `[0x14799e]`), registers cell callback 0x5c206 (cell 8x8) with tile engine fcn.0006c5e9, calls draw fns below |
| fcn.0005be9a | 194 | text/legend helper | INFERRED (goto-point name display, recursive text breaker) |
| fcn.0005bf5c | 211 | text/legend helper | INFERRED |
| fcn.0005c02f | 219 | `AUTOMAP_ListGotoPoints` | loops AutomapInfo records checking switch type 7 — builds discovered-location list. INFERRED |
| fcn.0005c10a | 252 | `AUTOMAP_CheckGotoPointDiscovery` | called from step handler fcn.000128bd. If party pos == AutomapInfo.X/Y and switch(7, MarkerId) unset: set it + show msg 0xA7. CONFIRMED |
| fcn.0005c206 | 198 | `AUTOMAP_CellCallback` | cell code >= 0x2710 → blit 8x8 from floor mini-tiles at (code−0x2710)*64; else AUTOGFX frame code*64. CONFIRMED |
| fcn.0005c2cc | 202 | `AUTOMAP_Open` | loads resource type **0x1B** (per-map automap bits). Assert line 1407: load failed; line 1415: resource size < `(W*H+7)/8`. Clears dirty flag. CONFIRMED |
| fcn.0005c396 | 46 | `AUTOMAP_Close` | flush + free (caller: fcn.00011cfa = map leave) |
| fcn.0005c3c4 | 74 | `AUTOMAP_Flush` | if dirty: write resource 0x1B back, clear dirty (also called from fcn.00024842, save path) |
| fcn.0005c40e | 3012 (really ends 0x5d183) | `AUTOMAP_Discover(sightDist)` | THE discovery rule, see below. Callers: fcn.0001183b (map enter, after Open) and fcn.0001e52d (party position set / move / teleport), **both pass eax=10** |
| fcn.0005d184 | 263 | `AUTOMAP_TestTile(x,y)` | bounds + bit test; `[0x147130]` short-circuits to 1 |
| fcn.0005d28b | 205 | `AUTOMAP_SetTile(x,y)` | sets bit, dirty=1. Only caller: 0x5d166 (commit loop of Discover) |
| fcn.0005d358 | 2381 | `AUTOMAP_DrawBorder` | fills compose-buffer border with AUTOGFX frames 0..0xD8 + animated edge frames 0x100/0x140/0x180 + table at 0x5ac9e, PRNG `seed = seed*17+87 & 7` seeded from map id |
| fcn.0005dca5 | 803 | `AUTOMAP_PrepareGfx` | allocs `floorCount[0x1511c4] * 64` bytes → 8x8 mini-tile per floor texture (built by fcn.0005dfc8); splices automap palette (type 3, id 0x2E or 0x15) into live palette entries **152..191**. Assert line 2357: palette load failed. CONFIRMED |
| fcn.0005dfc8 | 341 | `AUTOMAP_ShrinkFloorTexture` | 64x64 floor texture → 8x8 (sampling). INFERRED detail |
| fcn.0005e12c | 481 | `AUTOMAP_DrawTiles` | per discovered tile: floor + wall/object glyphs, see glyph mapping |
| fcn.0005e30d | 580 | `AUTOMAP_DrawEventMarkers` | per event zone, see markers |
| fcn.0005e551 | 643 | `AUTOMAP_DrawNpcMarkers` | per NPC slot (96 slots Ã— 0x80 bytes at 0x159c5c area) |
| fcn.0005e7e5 | 188 | `AUTOMAP_DrawGotoPoints` | AutomapInfo records → glyph 18 |
| fcn.0005e8a1 | 255 | `AUTOMAP_DrawWallGlyph(x,y)` | connection-mask wall glyph, see below |
| fcn.0005e9a0 | 201 | `AUTOMAP_PlaceMarker(x,y,type)` | writes marker type (valid 2..19) into overlay word of cell |
| fcn.00012f4f | 454 | `MAP_BlocksSight(x,y)` (dungeon.c side, used heavily here) | see below |

### Discovery mechanics (fcn.0005c40e) — CONFIRMED

Called with sight distance in eax; **both call sites pass 10** (map-enter fcn.0001183b@0x11c75, move/teleport fcn.0001e52d@0x1e639). The code clamps `dist = clamp(dist, 3, 10)`, so 10 is the effective constant.

Not a radius — a **facing-direction field-of-view + flood fill with wall occlusion**:

```
AUTOMAP_Discover(dist):                      # dist = 10 always
    dist = clamp(dist, 3, 10)
    byte grid[21][21] = {0}                  # local cells, party at [10][10]
    ox = partyX; oy = partyY
    dir = (((0x4000 - camYaw) + 0x400) & 0x3FFF) >> 11        # 0..7: 0=N,1=NE,2=E,...,7=NW
    (ox,oy) -= compass8[dir]                 # origin shifted ONE TILE BEHIND the facing dir
                                             # compass8 @ 0x134bf4: (0,-1)(1,-1)(1,0)(1,1)(0,1)(-1,1)(-1,0)(-1,-1)
    switch (dir):
      cardinal (0/2/4/6): for i in 0..dist:           # widening 90-degree wedge
                            for j in 0..2*i:           # row i has width 2i+1, centred
                              cell = origin + i*facing + (j-i)*perpendicular
      diagonal (1/3/5/7): for i in 0..dist, j in 0..dist:   # full square quadrant
                              cell = origin + i*forwardAxis + j*sideAxis
      each in-cone cell:  out of map        -> grid = 2
                          MAP_BlocksSight   -> grid = 3   (wall)
                          else              -> grid = 1   (open, candidate)
    grid[10][10] = 4                        # seed: party tile reached
    # flood fill, repeat passes until no change:
    for each cell with grid==4:
        for k in 0..3:                      # cardinals N,E,S,W (tab @ 0x134be4)
            n = cell + cardinal[k]
            if grid[n]==1: grid[n]=4 (changed)
            if grid[n]==3: grid[n]=5; wallFlag[k]=0xFF      # wall becomes visible
            if grid[n]==5: wallFlag[k]=0xFF
        wallFlag[4] = wallFlag[0]
        for k in 0..3:                      # diagonals NE,SE,SW,NW (tab @ 0x134bf8, stride 8)
            n = cell + diagonal[k]
            if grid[n]==1 and wallFlag[k]==0 and wallFlag[k+1]==0:
                grid[n]=4 (changed)         # corner rule: diagonal blocked if either
                                            # adjacent cardinal neighbour is a wall
    # commit:
    for cy in 0..20, cx in 0..20:
        if grid[cy][cx] in {4,5}: AUTOMAP_SetTile(ox-10+cx, oy-10+cy)
```

So: **sight depth 10 tiles, 90-degree cone in the facing direction (square quadrant when facing diagonally), origin one tile behind the party, walls occlude via 4-connected flood fill with a corner rule, and the blocking walls themselves are discovered (state 5).** Nothing outside the cone is ever discovered; tiles behind walls are not.

`MAP_BlocksSight(x,y)` (fcn.00012f4f): out-of-bounds → blocked. Tile contents byte (3-byte map cells: contents/floor/ceiling):
`0` or `1..100` (objects) → not blocking; `>=101` → wall id = contents−101; blocking iff the wall definition's flags byte0 has **bit 0x04** — which the C# remake calls `WallFlags.WriteOverlay`. CONFIRMED — that bit is actually the "opaque on automap / blocks discovery" bit.

### Save-game / resource bitfield layout — CONFIRMED

Resource type 0x1B per map (the savegame `Automap` blob). `index = (y-1)*Width + (x-1)` (0-based: `y*W+x`), byte = `index >> 3`, bit = `1 << (index & 7)` (LSB-first), **no row padding**, total `ceil(W*H/8)` bytes. The C# `UAlbion.Formats.Assets.Automap` packing matches exactly.

### Drawing / glyph mapping

Compose buffer: one 4-byte cell per map tile (plus border): word[0] = base layer, word[2] = marker overlay. Cell renderer fcn.0005c206 resolves codes to 8x8 source bitmaps:

| Cell code | Meaning |
|---|---|
| 0 | blank/undiscovered |
| 1..~0x1FF (border fn) | AUTOGFX frame index directly (border decorations 0..0xD8, animated edges 0x100+, 0x140+, 0x180+) |
| **0x230 + mask** (560..575) | wall piece; mask bit0=N, bit1=E, bit2=S, bit3=W — set when that cardinal neighbour is discovered AND blocks sight (fcn.0005e8a1). 16 connection variants |
| **0x270F + floorId** (>=10000) | discovered floor: NOT an AUTOGFX glyph — an 8x8 **downscaled copy of the actual floor texture** (mini-tile array built per labyrinth floor by fcn.0005dca5/fcn.0005dfc8). Codes > 0x4E20 reset to 0 |
| overlay word 2..0x13 (2..19) | marker glyph = AUTOGFX frame index (fcn.0005e9a0 rejects type <=1 and >19) |

Tile pass (fcn.0005e12c), per tile with TestTile()==1:
- floor byte != 0 → word[0] = 0x270F + floor
- contents 1..100 (object): `ObjectGroup.AutoGraphicsId` (word 0 of the 0x42-byte group record)
- contents >=101 (wall): `Wall.AutoGfxType` (byte 7 of wall record — already named correctly in C# `Wall.cs`)
- AutoGfx type **1 → connectable wall** (fcn.0005e8a1 mask glyph into word[0]); type 2..19 → marker overlay glyph of that index; 0 → nothing.

### Markers / legend

- **Event markers** (fcn.0005e30d): for each event zone on a discovered tile, fcn.00032f87(eventChain) finds the marker/text event (0xFFFF = none); skipped if global switch type 4 index `(mapId-1)*250 + n` is set (event chain disabled — note the 250-chains-per-map stride). Marker byte read from map data + `[0x147544]`. If zoom mode (`[0x147134]>>16`)==2 → generic glyph **19**. Marker type 5 (trap/secret) only drawn when item bit `[0x13e780]&4` or show-all; type 1 → connectable wall; else direct glyph index. (Lookup chain INFERRED in detail, glyph numbers CONFIRMED.)
- **NPC markers** (fcn.0005e551): 96 NPC slots (0x80 bytes each, base ~0x159c5c): active + on discovered tile. If NPC has event → as above (type 5 gated by `&4`). Else by NPC type byte (+0x159c5c): 0/1 (monster) → glyph **17**, needs item bit `&1`; 2 (person) → glyph **8**, needs item bit `&2`; 3 → ObjectGroup.AutoGraphicsId of word(+0x159c5a). Glyph 1 → connectable wall. CONFIRMED
- **Goto points** (fcn.0005e7e5): AutomapInfo records (0x13 bytes: X@0, Y@1, MarkerId@3 — matches C# `AutomapInfo`): drawn as glyph **18** when switch(7, MarkerId) set (or show-all). Discovery of the point itself happens by **stepping on its tile** (fcn.0005c10a, msg 0xA7). CONFIRMED
- Wall glyphs **560..575**, floors = real floor texture minis, marker glyphs 2..19 = AUTOGFX frames. The party position itself is not specially marked in these fns (the UI cursor handles it).

### Discrepancies vs the C# remake (src/Game/Entities/Map3D/AutomapDialog.cs)

1. `DiscoveryRadius = 2` square around the party (PLACEHOLDER) — original is **depth-10 facing cone/quadrant + wall-occluding flood fill**, origin one tile behind the party. Net effect: original discovers much larger areas in open rooms, nothing behind the party beyond ~1 tile, and nothing through walls.
2. Remake discovers on every party move event with no facing input; original recomputes on map enter and every position change **using camera yaw**.
3. Remake draws discovered floors with a fixed tile glyph; original blits an 8x8 shrunken copy of each tile's actual floor texture, and walls use the 16 connection-mask glyphs (AUTOGFX 560+mask) keyed on *sight-blocking* (Wall flag 0x04), not mere presence.
4. `WallFlags.WriteOverlay (0x04)` is actually "blocks automap sight/discovery" — worth renaming or documenting.
5. Marker gating by the three special-item bits (`[0x13e780]` bits 0/1/2) and the show-all override (`[0x147130]`) is absent from the remake.
6. Bitfield layout in `Formats/Assets/Automap.cs` already matches the original byte-for-byte — no change needed there.


## Sound id space (RE'd 2026-06-12)

**Headline: there is no sound-id → sample mapping above 299, because the "PlaySample" ids 443/444/445 and 762-773 are not sound ids at all.** The function previously identified as PlaySample (fcn.0002f85d) is actually **ShowSystemMessage**: it posts a SYSTEXTS string to the combat/system message UI control. Sample ids genuinely stop at 299 (SAMPLES0-2.XLD); nothing maps 400+ to wavelib entries. CONFIRMED end-to-end.

### The 0x15d824 table is the SYSTEXTS string table — CONFIRMED

`fcn.00043c51` (init, called from fcn.00010bd7 @ 0x10cd1):

```
buf = LoadResource(0x13)            # fcn.000230e3(eax=0x13), handle → [0x15e514]
len = ResourceSize(buf)             # fcn.0008b739
ptr = Lock(buf)                     # fcn.0008ba38
ParseTexts(ptr, len, 0x15d824, 800) # fcn.00043dd4: eax=buf, edx=len, ebx=table, ecx=count
Unlock(buf)                         # fcn.0008b808
```

Resource id 0x13 (19) indexes the global file table (entry = `0x135a62 + id*0x14`, filename = `0x1351c5 + id*13`, description = `0x135442 + id*32`): id 19 → filename **"SYSTEXTS"** (0x1352bc), description **"System texts"** (0x1356a2). Path is built as `XLDLIBS\<language-dir>\SYSTEXTS` (flags byte at entry+0x13: bit0 → language subdir from `[0x134580]`, bit1 → `CURRENT`, else plain `XLDLIBS`). CONFIRMED.

`fcn.00043dd4` (table fill) — parses the raw SYSTEXTS text:

```
ParseTexts(char* buf, int len, char** tab, int count):   # count = 0x320 = 800
    buf[len-1] = 0
    for i in 0..count-1: tab[i] = NULL
    p = buf
    while (p = strchr(p, '[')):                # fcn.000950e3, edx=0x5b
        p++
        digits = strncpy("0000", p, 4)         # template @ 0x43c48, fcn.00092bf2
        id = strtol(digits, base 10)           # fcn.000978b3
        p += 5                                 # skip "NNNN:"
        if id < count:
            if tab[id] != NULL: Error(3)       # duplicate id → fcn.00045d0c
            else: tab[id] = p                  # pointer to text after the colon
        p = strchr(p, ']'); if !p: break
        *p = 0; p++                            # terminate string in place
    for i in 0..count-1:
        if tab[i] == NULL: tab[i] = "NOT DEFINED"   # str @ 0x13dd28
```

So `[0x15d824 + id*4]` = pointer to SYSTEXTS entry `id` (format `[NNNN:text]`, ids 0-799). It is indexed straight by text id; no XLD/wavelib involvement whatsoever.

### fcn.0002f85d = ShowSystemMessage(textId) — CONFIRMED

```
ShowSystemMessage(id):                          # fcn.0002f85d
    if ControlExists([0x1530ea]):               # fcn.00074c4c
        SendMessage([0x1530ea], msg=0x16, param=systexts[id])   # fcn.000747ad
```

`fcn.000747ad` is the generic control message dispatcher (`g:\albion\src\ui\control.c`): looks up the control object (fcn.00074bda), walks its 6-byte `{word msgId, dword handler}` table at `[[obj+0x16]+4]` and calls the matching handler. The message-window control id is stored at 0x1530ea (created @ 0x2d37f via fcn.0007410a, descriptor 0x13d632; handler table @ 0x13d614: msg 0x01→0x2f509, 0x12→0x2f589, 0x13→0x2f695, **0x16→0x2f737**). The msg-0x16 handler (0x2f737) strcpy's the string into the control's text buffer (+0x26) and wakes the display task — pure text output (honours the "Combat text delay" option, SYSTEXTS 0416).

### Concrete answers — CONFIRMED against XLDLIBS\ENGLISH\SYSTEXTS

Combat movement phase (calls @ 0x4e70f/0x4e78d with eax=0x1bc, @ 0x4e788→ eax=0x1bb, @ 0x4e81b→ eax=0x1bc):

| id | SYSTEXTS entry |
|---|---|
| 443 | `{INK 006}Move was blocked!` |
| 444 | `{COMB}{NAME} is moving.` |
| 445 | `{COMB}{NAME} is fleeing!` |

Condition cues: `fcn.000363c2` takes a condition index 0-11 plus a per-condition suppression bitmask, and calls `ShowSystemMessage(762 + conditionIndex)` (add eax, 0x2fa @ 0x3643b):

| id | condition message |
|---|---|
| 762 | `{INK 006}{SUBJ}{NAME} is unconscious!` |
| 763 | `{INK 006}{SUBJ}{NAME} has been poisoned!` |
| 764 | `{INK 006}{SUBJ}{NAME} is ill!` |
| 765 | `{INK 006}{SUBJ}{NAME} is exhausted!` |
| 766 | `{INK 006}{SUBJ}{NAME} is unable to move!` (paralysed) |
| 767 | `{INK 006}{SUBJ}{NAME} has fled!` |
| 768 | `{INK 006}{SUBJ}{NAME} is intoxicated!` |
| 769 | `{INK 006}{SUBJ}{NAME} has been blinded!` |
| 770 | `{INK 006}{SUBJ}{NAME} is panicking!` |
| 771 | `{INK 006}{SUBJ}{NAME} is asleep!` |
| 772 | `{INK 006}{SUBJ}{NAME} has gone insane!` |
| 773 | `{INK 006}{SUBJ}{NAME} is irritated!` |

(Other 0x2fa hits at 0x849f2/0xc87b9 are unrelated data, not `add eax, 0x2fa` call sites — only fcn.000363c2 uses the 762 base. INFERRED that it is the sole condition-cue emitter; CONFIRMED for the formula itself.)

### Wavelib note

For actual audio: SAMPLES0.XLD is file-table id 30, WAVELIB0.XLD id 31 (filenames @ 0x13534b / 0x135358). Sample playback goes through the AIL 3.0 driver API (AIL_allocate_sample_handle etc., strings @ 0x132f69+). Since ids 300+ turned out to be text, the remake's existing 0-299 sample space already covers everything `PlaySample`-like; WAVELIB entries are addressed separately (per-monster combat wave libs), not via a global 800-entry id space.

### Remake implications

- Any UAlbion mapping that treats 443/444/445 or 762-773 as `SampleId`s should instead use `Base.SystemText` ids: 443=MoveBlocked, 444=IsMoving, 445=IsFleeing, 762-773=per-condition status lines (order: unconscious, poisoned, ill, exhausted, paralysed, fled, intoxicated, blinded, panicking, asleep, insane, irritated).
- The 800-entry limit (0x320) is the engine's SYSTEXTS table size, not a sound table size.

## Combat SFX (RE'd 2026-06-12)

### Playback architecture (CONFIRMED)

All in-game sound effects — including every combat sound — are **SAMPLES entries (file-table id 0x1e = 30, SAMPLES0-2.XLD)**. There is no code path that plays WAVELIB (0x1f) entries as SFX: wave libraries feed the XMIDI wave-synth as *music instruments* only (no game-side callers of `AIL_send_channel_voice_message`; the M-HT SR recompile of MAIN.EXE exposes only the AIL *sample* APIs + XMI player, confirming the same).

- **`fcn.00062a41` = PlaySample(eax=sampleId, edx=priority, ebx=volume 0-127, ecx=randomVariation, push rateHz)** — the one-shot SFX entry point (74 call sites).
  - Resource fetch: `fcn.000233b8(eax=0x1e, edx=sampleId)` → cached XLD entry pointer (id/100 selects SAMPLES0/1/2, id%100 the sub-entry).
  - Defaults: volume 0 → 100; rate 0 → **0x2AF8 = 11000 Hz**. Pan is always 0x40 (centre).
  - Variation (ecx≠0): volume += (rand()%(100·var) − 50·var)/100 (≈ ±var/2 units, clamped ≤ 0x7F); rate += rand()%(100·var) − 50·var (±50·var Hz, clamped 1000..0xAC44).
  - Fills a 21-byte request in the 8-slot one-shot pool @ 0x177e7c: `{status@0 (1=pending,2=playing), flagsWord@1 (bit0=loop), prio@3, sampleId@5, vol@7, rate@9, pan@0xB, dataPtr@0xD, len@0x11}`.
- **`fcn.00062f60` = PlayLoopedSample3D(eax=id, edx=prio, ebx=vol, ecx (==100 → loop), stack: rateHz, srcPtrA, srcPtrB)** — 64-slot pool @ 0x1775bc (35-byte slots; extra: id copy @+0x15, vol @+0x17, loopArg @+0x19, two position pointers @+0x1B/+0x1F). Returns slot handle. Users: 3D-map ambient table (`fcn.0004356a`, 13×0x28 table @ 0x13db10, rows of {id,prio,vol,loop,rate}×4) and the map "sound" event (positional case @ 0x3b63f). Same SAMPLES file (0x1e) — *not* wavelib.
- **Mixer pump `fcn.000634fb`** (from sound tick `fcn.000625ac` @ 0x6261d): gathers pending/playing requests from both pools, priority-sorts (`fcn.00070fa6` qsort, comparators 0x637ca/0x63828), keeps the best **12** on the AIL voice table @ 0x177f38 (9-byte slots `{active, requestPtr@1, AIL_handle@5}`), stopping the rest (`fcn.00063e96`).
- **Voice start `fcn.00063bf0`**: finds a free AIL slot then `AIL_set_sample_address` (fcn.0008da0d) → volume (0x8dd3b) → playback rate (0x8dcc5) → pan (0x8ddb1) → loop-count 0 if flag bit0 (0x8de27) → `AIL_start_sample` (fcn.0008db11). `fcn.0008b739/0008ba38` = header-parse data-ptr / length of the raw sample blob.
- **`fcn.00062925` = PlaySong** (file 0x1d = 29, SONGS → AIL XMIDI sequence via fcn.0008f2cb). Map sound-event case 0 routes here. `fcn.00062cb4` re-resolves looping-sample buffers after an XLD cache flush.

### Combat sound usage (CONFIRMED call sites)

| action | function / site | sample | params |
|---|---|---|---|
| Combatant dies (type A, `word[obj]==1`) | `fcn.0004dec9` @ 0x4df78 | **268** (death scream) | prio 100, vol 100, var 50, rate 11000 |
| Combatant dies (type B, else-branch) | `fcn.0004dec9` @ 0x4e03c | **268** | prio 100, vol 60, var 50, rate **15000** (higher pitch, quieter) |
| Melee swing / hit / miss | — | **none** | see below |
| Spell casts | per-spell handlers | 38, 201-268 | prio 100, vol 100, var 0, rate 11000 (table below) |

INFERRED: the two death variants distinguish party members vs monsters (struct word[0] flag; both then emit the SYSTEXTS 13/14 messages via the 0x15f126 block). The *pitch*, not the sample, differs — there is **one** death sound for everything.

**Melee strikes are silent in the DOS engine.** The strike path (`fcn.00051c91` → `fcn.00052b71`, comshow.c:1608) picks a strike type 0-9 from the attacker's weapon/ammo item sheet **byte +0x14**, indexes the 14-byte strike table @ **0x13e370** = `{gfx ×4 (direction/hit variants), w4=150, w5=150, projectileSpeed}` — entry +0xC (values 10/15/7/12) is the projectile *speed* (×100 in `fcn.00052ee8` velocity calc), **not** a sound id. The graphics ids load combat-gfx file 0x28. No PlaySample call is reachable from `fcn.0004e6d1` (MeleeResolve), `fcn.00051b51` (melee anim, file 0x28 sub 0x30), `fcn.00051c91`, or `fcn.00052b71`. Likewise **no per-monster attack sample exists** — nothing reads a MONCHAR sheet sound field anywhere in the audio paths. Monster-specific audio flavour in the original comes solely from the combat *music* (each combat song's WAVELIB contains the monster screech instruments, triggered by the XMI sequence itself).

### Spell SFX dispatch (CONFIRMED mechanism, ids read off handler disasm)

`fcn.0005ecda`: `school = [castEvent+6]>>16`, `spellNo = [castEvent+8]>>16`, handler = `[[0x13e930 + school*4] + (spellNo-1)*4]`; `fcn.0005fdf7` validates/deducts SP first. School pointer arrays: 0=Dji-Kas @ 0x13e83c (21 entries), 1=Dji-Kantos @ 0x13e890 (11), 2=Druid @ 0x13e8bc (10), 3=Oqulo-Kamulos @ 0x13e8e4 (15), 4=null, 5=Zombie @ 0x13e920 (4) — sizes match UAlbion `Base.Spell` exactly.

`fcn.0009b827` = shared "throw magic projectile" helper: plays **201** (ThrowMagicSeed) then flies the projectile caster→target. Per-spell samples (UAlbion `Base.Sample` names where defined):

| spell (Base.Spell) | handler | samples played |
|---|---|---|
| ThornSnare 1 | 0x9b95e | 201 + 203 PoweringUp |
| Hurry 4 | 0x9bd5f | — |
| ViewOfLife 5 | 0x9bddf | 208 TechTension |
| FrostSplinter 6 | 0x9c109 | 201, 209 LaserDoor, 210 MiniPyiew |
| FrostCrystal 7 | 0x9c7ba | 201, 209, 210 |
| FrostAvalanche 8 | 0x9cf43 | 201, 209, 210 |
| LightHealing 9 | 0x640c5 | 38 Healing |
| BlindingSpark 10 | 0x9d6ca | 201, 213 BeamMeDown, 214 Choonk |
| BlindingRay 11 | 0x9dab3 | 201, 213, 214 |
| BlindingStorm 12 | 0x9e292 | 201, 213, 214 |
| SleepSpores 13 | 0x9ea71 | 201, 215 DiddlyDiddly |
| ThornTrap 14 | 0x9f123 | 201, 216 Drrzh, 218 RockCrumbling, 219 Takwow |
| RemoveTrapDK 15 | 0x9fa2d | 221 Bwoowoo, 206 EchoingPing |
| HealParalysis 16 | (null) | — |
| HealIntoxication 17 | 0x641af | 38 |
| HealBlindness 18 | 0x6423d | 38 |
| HealPoisoning 19 | 0x642cb | 38 |
| Fungification 20 | 0x9fd5c | 201, 223 LoHiHiHiHi ×2, 225 MultipleImpacts |
| Light 21 | 0x64359 | 38 |
| Regeneration 31 | 0x643aa | 38 |
| MapView 32 | 0x64571 | 38 |
| Lifebringer 33 | 0x645d1 | 38 |
| Teleporter 34 | 0xa068b | 215, 226 Strings, 210 |
| HealingDC 35 | 0x64621 | 38 |
| QuickWithdrawal 36 | 0xa1021 | — |
| GoddessWrath 39 | 0xa1134 | 266, 267 (unnamed in enum) |
| Irritation 40 | 0xa1799 | 215 |
| Recuperation 41 | 0x6470b | 38 |
| Berserk 61 / BanishDemon 62 / BanishDemons 63 / DemonExodus 64 | 0xa1a69/0xa1aec/0xa1fed/0xa2021 | — |
| SmallFireball 65 | 0xa2055 | 233 FireballChargeUp, 234 FireballLaunch, 235 (impact) |
| MagicShield 66 | 0xa2510 | — |
| HealingD 67 | 0x647bd | 38 |
| Boasting 68 | 0xa2612 | 230 LongGrowlWithLaugh, 231 AngryDissonantBuzz |
| Shock 69 / Panic 70 | 0xa2eaf/0xa2ee3 | (stubs, delegate) |
| Fireball 91 | 0xa2f17 | 233, 234, 235 |
| LightningStrike 92 | 0xa33d2 | 234, 235 |
| FireRain 93 | 0xa43c6 | 260 (unnamed) |
| Thunderbolt 94 | 0xa477a | 239, 235 |
| FireHail 95 | 0xa50ef | 261, 235, 241 |
| Thunderstorm 96 | 0xa5aad | 239, 235 |
| LightningTrap 97 | 0xa6567 | 242, 243, 265 |
| BigLightningTrap 98 | 0xa6fd0 | (stub) |
| LightningMine 99 | 0xa7004 | 242, 206 |
| BigLightningMine 100 | 0xa777f | (stub) |
| StealLife 101 | 0xa77b3 | 206 |
| StealMagic 102 | 0xa80e3 | 206 |
| PersonalProtection 103 | 0xa8a3f | — |
| KamulosGaze 104 | 0xa8b12 | 263, 264 (unnamed) |
| RemoveTrapKK 105 | 0xa92ae | — observed |
| Zombie spells 151-154 | 0xa95a4/0xa96fc/0xa9854/0xa99ac | (stubs — likely reuse Panic/Irritation displays) |

INFERRED in the table: handler→spell-name pairing follows the array index order (mechanism itself CONFIRMED); samples were attributed to handlers by address range, and projectile-launch helper sound 201 applies wherever `fcn.0009b827` is called (0x9ba4a, 0x9c227, 0x9c8a5, 0x9d030, 0x9d761, 0x9db54, 0x9e333, 0x9eb36, 0x9f1e6, 0x9fe01).

### Other PlaySample users (for reference)
- Map "sound" event handler @ 0x3b4e0 (switch on event byte+1): case 0 → PlaySong, case 1 → PlaySample(id, b3, b4, b5, w6) @ 0x3b56d, positional looped case @ 0x3b63f. CONFIRMED.
- Script-opcode play-sample @ 0x6aea9 (args parsed via fcn.0006a7ca) — cutscene scripts.
- UI: 104 Kalunk @ 0x5a90e, 108 WoogityTack @ 0x5ac66 (inventory/shop area, paired with SYSTEXTS 525). INFERRED non-combat.

### Remake implications
- **Combat SFX are `Sample` asset ids (SAMPLES0-2.XLD), never WaveLib entries.** Keep `AudioManager.GetBuffer(songId, instrument)` for music only; combat one-shots should resolve `Base.Sample` ids directly.
- Faithful behaviour: melee swing/hit = **no sound**; combatant death = Sample 268 at 11000 Hz (party side) / 15000 Hz + ~60% volume (other side, INFERRED which is which), with ±25 volume and ±2500 Hz random jitter; spell casts = the table above at default 11000 Hz, no jitter.
- `Base.Sample` is missing names for ids 252-299; ids **260, 261, 263, 264, 265, 266, 267, 268** are definitely present in SAMPLES2.XLD (entries 60-68) and used by the engine — they need enum entries before the spell/death sounds can be wired up.
- Default playback rate when an event passes rate 0 is 11000 Hz; clamps: rate 1000..44100 Hz, volume 0..127.
