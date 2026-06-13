using UAlbion.Formats.Ids;

namespace UAlbion.Game.State;

/// <summary>
/// The "home NPC index" encoding (RE 5D §6, CharacterSheet+0x1C / RemovePartyMember Unk6):
/// a flat <c>mapId*96 + npcSlot</c> key into the savegame RemovedNpcs bitfield identifying a
/// recruited party member's walking NPC on its home map. Recruit chains hide that NPC with
/// modify_npc_off; leaving the party clears the same bit so it reappears. 0 = "no home NPC".
/// </summary>
public static class HomeNpcIndex
{
    public const int NpcsPerMap = 96; // matches SavedGame.NpcCountPerMap / RemovedNpcs stride

    public static ushort Encode(MapId map, int slot) => (ushort)(map.Id * NpcsPerMap + slot);

    /// <summary>Decode a non-zero home index into its map + npc slot. Returns false for 0.</summary>
    public static bool TryDecode(ushort index, out MapId map, out byte slot)
    {
        if (index == 0)
        {
            map = MapId.None;
            slot = 0;
            return false;
        }

        map = new MapId(index / NpcsPerMap);
        slot = (byte)(index % NpcsPerMap);
        return true;
    }
}
