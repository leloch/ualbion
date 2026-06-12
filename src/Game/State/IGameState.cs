using System;
using System.Collections.Generic;
using UAlbion.Config;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.State;

public interface IGameState
{
    bool Loaded { get; }
    int TickCount { get; }
    // 48 ticks per day = one tick every 30 game-minutes. This matches the original Albion's
    // NPC waypoint table (48 entries per character, one per half-hour slot — see MapNpc).
    // Phase 4.1 RE'd to confirm: tick rate is 48/day = "M-tick" = 30 game-minutes.
    int MTicksToday => (int)(48.0 * Time.TimeOfDay.TotalHours);
    int HoursSinceResting { get; }
    DateTime Time { get; }
    IParty Party { get; }
    MapId MapId { get; }
    MapId MapIdForNpcs { get; set; } // Set by NpcManagers
    ICharacterSheet GetSheet(SheetId id);
    IPlayer GetPlayerForCombatPosition(int position);
    int? GetCombatPositionForPlayer(PartyMemberId id);
    IInventory GetInventory(InventoryId id);
    short GetTicker(TickerId id);
    bool GetSwitch(SwitchId id);
    MapChangeCollection TemporaryMapChanges { get; }
    MapChangeCollection PermanentMapChanges { get; }
    ActiveItems ActiveItems { get; }
    IList<NpcState> Npcs { get; }
    /// <summary>Per-map automap discovery bitfields (persisted in the save file).</summary>
    IDictionary<AutomapId, byte[]> Automaps { get; }
    /// <summary>Goto-point visited bit (the original's switch type 7, by AutomapInfo.MarkerId).</summary>
    bool IsAutomapMarkerFound(int markerId);
    /// <summary>Mark a goto-point visited (party stepped on its tile).</summary>
    void SetAutomapMarkerFound(int markerId);
    /// <summary>
    /// Active-spell percent for a party member (the original's 0x153b3e table): type 1 =
    /// physical-defense boost, type 2 = magic-resistance boost (MagicShield /
    /// PersonalProtection). 0 when no entry is active. Decays hourly.
    /// </summary>
    int GetActiveSpellPct(PartyMemberId member, int type);
    bool IsChainDisabled(MapId mapId, ushort chain);
    bool IsNpcDisabled(MapId mapId, byte npcNum);
    bool IsEventUsed(AssetId eventSetId, ActionEvent action);
    ICharacterSheet Leader { get; }
    ICharacterSheet Subject { get; }
    ICharacterSheet CurrentInventory { get; }
    ICharacterSheet Combatant { get; }
    ICharacterSheet Victim { get; }
    ItemData Weapon { get; }
}
