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
| `combat-suspect` | CharacterSheet field in 0x1C..0x90 block â€” combat-related per upstream notes. |
| `magic` | Used by spell-cast handlers. |
| `ai` | NPC / monster behaviour. |
| `world` | Map / lighting / fog / 3D-specific. |
| `script-arg` | Argument to a script Map / ScriptEvent â€” semantics depends on engine handler. |
| `flag-bit` | Bit within a known flag enum â€” needs per-bit observation. |
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
The source file spans roughly **0x5acbeâ€“0x5ea68** (the next file, `magic.c`, starts at fcn.0005ea69). Status tags: **CONFIRMED** = read directly from disasm; **INFERRED** = consistent interpretation, not byte-traced.

### Key globals

| Address | Meaning | Status |
|---|---|---|
| `0x153b32` | current map id (word); `0x153b34/36` = party X/Y (1-based) | CONFIRMED |
| `0x14799a` / `0x147994` | map width / height | CONFIRMED |
| `0x14799c` | map-has-walls / 3D flag (gates BlocksSight) | INFERRED |
| `0x14799e` | labyrinth automap-style selector: !=0 â†’ AUTOGFX id 2 + palette 0x2E, ==0 â†’ AUTOGFX id 1 + palette 0x15 | CONFIRMED |
| `0x147130` | "whole map visible" override (TestTile always 1; also unlocks all markers) | CONFIRMED behaviour, source of flag INFERRED (likely map flag/cheat/spell) |
| `0x13e780` | special-item bitfield: bit0 â†’ monster markers, bit1 â†’ person markers, bit2 â†’ trap/secret + type-5 event markers | CONFIRMED bits, item identity INFERRED |
| `0x176fec` / `0x176fe8` | discovery bitfield handle / size in bytes | CONFIRMED |
| `0x177588` | discovery dirty flag (set on SetTile, cleared on flush) | CONFIRMED |
| `0x176fdc` | AUTOGFX resource handle (type 0x1C); `0x176ff0` = compose buffer (2 words/cell); `0x176ff4` = floor mini-tile array | CONFIRMED |
| `0x14a48c` | camera yaw, 16.16 fixed, full circle = 0x4000 | CONFIRMED |
| `0x147134` | automap zoom/detail mode; `>>16 == 2` â†’ generic event markers drawn as glyph 19 | CONFIRMED use, semantics INFERRED |

### Function inventory

| Address | Size | Name (inferred) | Notes |
|---|---|---|---|
| 0x5acff | ~4.5k (undefined in r2; contains 0x5b0d6 assert, line 441) | `AUTOMAP_Show` | UI main: computes buffer dims (map + 8-cell border; stride `[0x17758a]`, height `[0x177592]`, map offset `[0x17758e]/[0x177590]`), allocates compose buffer W*H*4, loads AUTOGFX (type 0x1C, id 1 or 2 by `[0x14799e]`), registers cell callback 0x5c206 (cell 8x8) with tile engine fcn.0006c5e9, calls draw fns below |
| fcn.0005be9a | 194 | text/legend helper | INFERRED (goto-point name display, recursive text breaker) |
| fcn.0005bf5c | 211 | text/legend helper | INFERRED |
| fcn.0005c02f | 219 | `AUTOMAP_ListGotoPoints` | loops AutomapInfo records checking switch type 7 â€” builds discovered-location list. INFERRED |
| fcn.0005c10a | 252 | `AUTOMAP_CheckGotoPointDiscovery` | called from step handler fcn.000128bd. If party pos == AutomapInfo.X/Y and switch(7, MarkerId) unset: set it + show msg 0xA7. CONFIRMED |
| fcn.0005c206 | 198 | `AUTOMAP_CellCallback` | cell code >= 0x2710 â†’ blit 8x8 from floor mini-tiles at (codeâˆ’0x2710)*64; else AUTOGFX frame code*64. CONFIRMED |
| fcn.0005c2cc | 202 | `AUTOMAP_Open` | loads resource type **0x1B** (per-map automap bits). Assert line 1407: load failed; line 1415: resource size < `(W*H+7)/8`. Clears dirty flag. CONFIRMED |
| fcn.0005c396 | 46 | `AUTOMAP_Close` | flush + free (caller: fcn.00011cfa = map leave) |
| fcn.0005c3c4 | 74 | `AUTOMAP_Flush` | if dirty: write resource 0x1B back, clear dirty (also called from fcn.00024842, save path) |
| fcn.0005c40e | 3012 (really ends 0x5d183) | `AUTOMAP_Discover(sightDist)` | THE discovery rule, see below. Callers: fcn.0001183b (map enter, after Open) and fcn.0001e52d (party position set / move / teleport), **both pass eax=10** |
| fcn.0005d184 | 263 | `AUTOMAP_TestTile(x,y)` | bounds + bit test; `[0x147130]` short-circuits to 1 |
| fcn.0005d28b | 205 | `AUTOMAP_SetTile(x,y)` | sets bit, dirty=1. Only caller: 0x5d166 (commit loop of Discover) |
| fcn.0005d358 | 2381 | `AUTOMAP_DrawBorder` | fills compose-buffer border with AUTOGFX frames 0..0xD8 + animated edge frames 0x100/0x140/0x180 + table at 0x5ac9e, PRNG `seed = seed*17+87 & 7` seeded from map id |
| fcn.0005dca5 | 803 | `AUTOMAP_PrepareGfx` | allocs `floorCount[0x1511c4] * 64` bytes â†’ 8x8 mini-tile per floor texture (built by fcn.0005dfc8); splices automap palette (type 3, id 0x2E or 0x15) into live palette entries **152..191**. Assert line 2357: palette load failed. CONFIRMED |
| fcn.0005dfc8 | 341 | `AUTOMAP_ShrinkFloorTexture` | 64x64 floor texture â†’ 8x8 (sampling). INFERRED detail |
| fcn.0005e12c | 481 | `AUTOMAP_DrawTiles` | per discovered tile: floor + wall/object glyphs, see glyph mapping |
| fcn.0005e30d | 580 | `AUTOMAP_DrawEventMarkers` | per event zone, see markers |
| fcn.0005e551 | 643 | `AUTOMAP_DrawNpcMarkers` | per NPC slot (96 slots Ã— 0x80 bytes at 0x159c5c area) |
| fcn.0005e7e5 | 188 | `AUTOMAP_DrawGotoPoints` | AutomapInfo records â†’ glyph 18 |
| fcn.0005e8a1 | 255 | `AUTOMAP_DrawWallGlyph(x,y)` | connection-mask wall glyph, see below |
| fcn.0005e9a0 | 201 | `AUTOMAP_PlaceMarker(x,y,type)` | writes marker type (valid 2..19) into overlay word of cell |
| fcn.00012f4f | 454 | `MAP_BlocksSight(x,y)` (dungeon.c side, used heavily here) | see below |

### Discovery mechanics (fcn.0005c40e) â€” CONFIRMED

Called with sight distance in eax; **both call sites pass 10** (map-enter fcn.0001183b@0x11c75, move/teleport fcn.0001e52d@0x1e639). The code clamps `dist = clamp(dist, 3, 10)`, so 10 is the effective constant.

Not a radius â€” a **facing-direction field-of-view + flood fill with wall occlusion**:

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

`MAP_BlocksSight(x,y)` (fcn.00012f4f): out-of-bounds â†’ blocked. Tile contents byte (3-byte map cells: contents/floor/ceiling):
`0` or `1..100` (objects) â†’ not blocking; `>=101` â†’ wall id = contentsâˆ’101; blocking iff the wall definition's flags byte0 has **bit 0x04** â€” which the C# remake calls `WallFlags.WriteOverlay`. CONFIRMED â€” that bit is actually the "opaque on automap / blocks discovery" bit.

### Save-game / resource bitfield layout â€” CONFIRMED

Resource type 0x1B per map (the savegame `Automap` blob). `index = (y-1)*Width + (x-1)` (0-based: `y*W+x`), byte = `index >> 3`, bit = `1 << (index & 7)` (LSB-first), **no row padding**, total `ceil(W*H/8)` bytes. The C# `UAlbion.Formats.Assets.Automap` packing matches exactly.

### Drawing / glyph mapping

Compose buffer: one 4-byte cell per map tile (plus border): word[0] = base layer, word[2] = marker overlay. Cell renderer fcn.0005c206 resolves codes to 8x8 source bitmaps:

| Cell code | Meaning |
|---|---|
| 0 | blank/undiscovered |
| 1..~0x1FF (border fn) | AUTOGFX frame index directly (border decorations 0..0xD8, animated edges 0x100+, 0x140+, 0x180+) |
| **0x230 + mask** (560..575) | wall piece; mask bit0=N, bit1=E, bit2=S, bit3=W â€” set when that cardinal neighbour is discovered AND blocks sight (fcn.0005e8a1). 16 connection variants |
| **0x270F + floorId** (>=10000) | discovered floor: NOT an AUTOGFX glyph â€” an 8x8 **downscaled copy of the actual floor texture** (mini-tile array built per labyrinth floor by fcn.0005dca5/fcn.0005dfc8). Codes > 0x4E20 reset to 0 |
| overlay word 2..0x13 (2..19) | marker glyph = AUTOGFX frame index (fcn.0005e9a0 rejects type <=1 and >19) |

Tile pass (fcn.0005e12c), per tile with TestTile()==1:
- floor byte != 0 â†’ word[0] = 0x270F + floor
- contents 1..100 (object): `ObjectGroup.AutoGraphicsId` (word 0 of the 0x42-byte group record)
- contents >=101 (wall): `Wall.AutoGfxType` (byte 7 of wall record â€” already named correctly in C# `Wall.cs`)
- AutoGfx type **1 â†’ connectable wall** (fcn.0005e8a1 mask glyph into word[0]); type 2..19 â†’ marker overlay glyph of that index; 0 â†’ nothing.

### Markers / legend

- **Event markers** (fcn.0005e30d): for each event zone on a discovered tile, fcn.00032f87(eventChain) finds the marker/text event (0xFFFF = none); skipped if global switch type 4 index `(mapId-1)*250 + n` is set (event chain disabled â€” note the 250-chains-per-map stride). Marker byte read from map data + `[0x147544]`. If zoom mode (`[0x147134]>>16`)==2 â†’ generic glyph **19**. Marker type 5 (trap/secret) only drawn when item bit `[0x13e780]&4` or show-all; type 1 â†’ connectable wall; else direct glyph index. (Lookup chain INFERRED in detail, glyph numbers CONFIRMED.)
- **NPC markers** (fcn.0005e551): 96 NPC slots (0x80 bytes each, base ~0x159c5c): active + on discovered tile. If NPC has event â†’ as above (type 5 gated by `&4`). Else by NPC type byte (+0x159c5c): 0/1 (monster) â†’ glyph **17**, needs item bit `&1`; 2 (person) â†’ glyph **8**, needs item bit `&2`; 3 â†’ ObjectGroup.AutoGraphicsId of word(+0x159c5a). Glyph 1 â†’ connectable wall. CONFIRMED
- **Goto points** (fcn.0005e7e5): AutomapInfo records (0x13 bytes: X@0, Y@1, MarkerId@3 â€” matches C# `AutomapInfo`): drawn as glyph **18** when switch(7, MarkerId) set (or show-all). Discovery of the point itself happens by **stepping on its tile** (fcn.0005c10a, msg 0xA7). CONFIRMED
- Wall glyphs **560..575**, floors = real floor texture minis, marker glyphs 2..19 = AUTOGFX frames. The party position itself is not specially marked in these fns (the UI cursor handles it).

### Discrepancies vs the C# remake (src/Game/Entities/Map3D/AutomapDialog.cs)

1. `DiscoveryRadius = 2` square around the party (PLACEHOLDER) â€” original is **depth-10 facing cone/quadrant + wall-occluding flood fill**, origin one tile behind the party. Net effect: original discovers much larger areas in open rooms, nothing behind the party beyond ~1 tile, and nothing through walls.
2. Remake discovers on every party move event with no facing input; original recomputes on map enter and every position change **using camera yaw**.
3. Remake draws discovered floors with a fixed tile glyph; original blits an 8x8 shrunken copy of each tile's actual floor texture, and walls use the 16 connection-mask glyphs (AUTOGFX 560+mask) keyed on *sight-blocking* (Wall flag 0x04), not mere presence.
4. `WallFlags.WriteOverlay (0x04)` is actually "blocks automap sight/discovery" â€” worth renaming or documenting.
5. Marker gating by the three special-item bits (`[0x13e780]` bits 0/1/2) and the show-all override (`[0x147130]`) is absent from the remake.
6. Bitfield layout in `Formats/Assets/Automap.cs` already matches the original byte-for-byte â€” no change needed there.

