using UAlbion.Formats.Ids;

namespace UAlbion.Game.State;

/// <summary>
/// The "home NPC index" used by RemovePartyMember (Unk6) / CharacterSheet+0x1C to identify a
/// recruited party member's walking NPC on its home map, so leaving the party can clear the
/// RemovedNpcs bit the recruit chain set and the NPC reappears (RE 5D §6).
///
/// IMPORTANT — two different encodings are in play, one map-stride apart:
///   • The script literal (Unk6 / sheet+0x1C) uses the ORIGINAL engine's <c>(mapId−1)*96 +
///     slot</c> (1-based map number). Edjirr/Sira = (281−1)*96 + 8 = 26888.
///   • The remake's RemovedNpcs <see cref="FlagSet"/> keys by <c>MapId.Id*96 + slot</c>
///     (Edjirr.Id = 281 ⇒ 281*96 + 8 = 26984), and that is also what the recruit chain's
///     <c>modify_npc_off Set 8 Map.Edjirr</c> sets on join.
/// So decoding must add 1 to the map to convert the script literal into the remake's MapId —
/// otherwise leave clears bit 26888 while join hid 26984 and the NPC never reappears (a
/// recruit soft-lock; the earlier code was off by one stride). 0 = "no home NPC".
/// </summary>
public static class HomeNpcIndex
{
    public const int NpcsPerMap = 96; // matches SavedGame.NpcCountPerMap / RemovedNpcs stride

    /// <summary>Encode a (map, slot) into the script-literal form: (mapId−1)*96 + slot.</summary>
    public static ushort Encode(MapId map, int slot) => (ushort)((map.Id - 1) * NpcsPerMap + slot);

    /// <summary>
    /// Decode a non-zero script home-index (Unk6) into the remake MapId + npc slot to pass to
    /// SetNpcDisabled. Adds 1 to the map (script is (mapId−1)-based; the FlagSet is MapId-based).
    /// Returns false for 0.
    /// </summary>
    public static bool TryDecode(ushort index, out MapId map, out byte slot)
    {
        if (index == 0)
        {
            map = MapId.None;
            slot = 0;
            return false;
        }

        map = new MapId(index / NpcsPerMap + 1);
        slot = (byte)(index % NpcsPerMap);
        return true;
    }
}
