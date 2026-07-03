using System;
using System.Collections.Generic;
using UAlbion.Base;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.State;

/// <summary>
/// The curated library of synthetic scenarios — one per key phase of the game, so the whole
/// story is reachable for testing without real saves. Every scenario is crash-safe: a null X/Y
/// (or a stale baked coord) is corrected to a walkable tile at load time (<see cref="SpawnValidator"/>).
///
/// Scope note: the core guarantee is "valid spawn + a sane party + gold at the right location".
/// Quest-flag fidelity for the unverified back half of the game is best-effort and called out in
/// each Description — these let you STAND at a phase to test the engine there, not a byte-perfect
/// save of that story moment. Baked X/Y coords (for the save-backed early maps) are filled in once
/// discovered live; the rest rely on the walkability scan.
/// </summary>
public static class SyntheticScenarioLibrary
{
    // Late-game full party (max 6). Adjust as recruit/leave fidelity is verified.
    static readonly PartyMemberId[] LateParty =
        [PartyMember.Tom, PartyMember.Drirr, PartyMember.Sira, PartyMember.Mellthas, PartyMember.Khunag, PartyMember.Siobhan];

    static SyntheticScenario S(
        string name, string description, MapId map, IEnumerable<PartyMemberId> party,
        ushort gold, double timeHours = 12, ushort? x = null, ushort? y = null)
        => new()
        {
            Name = name,
            Description = description,
            Map = map,
            X = x,
            Y = y,
            Gold = gold,
            TimeHours = timeHours,
            Party = new List<PartyMemberId>(party),
        };

    static readonly IReadOnlyList<SyntheticScenario> Scenarios =
    [
        S("intro", "Ch.0 — Spaceship Toronto intro. Tom alone, no gold.",
            Base.Map.TorontoBegin, [PartyMember.Tom], 0, 8),

        S("nakiridaani", "Ch.1 — Nakiridaani Iskai jungle village (2D), just after the crash.",
            Base.Map.Nakiridaani, [PartyMember.Tom], 50),

        S("jirinaar", "Ch.2 — Jirinaar Iskai city (3D). Tom + Drirr + Sira.",
            Base.Map.Jirinaar, [PartyMember.Tom, PartyMember.Drirr, PartyMember.Sira], 300),

        S("hunterclan", "Ch.2 — Hunter Clan cellar (3D). Tom + Drirr + Sira.",
            Base.Map.HunterClanCellar, [PartyMember.Tom, PartyMember.Drirr, PartyMember.Sira], 400),

        S("drinno", "Ch.3 — Drinno dungeon (3D). Adds Mellthas. Flags best-effort.",
            Base.Map.Drinno, [PartyMember.Tom, PartyMember.Drirr, PartyMember.Sira, PartyMember.Mellthas], 1000),

        S("srimalinar", "Ch.4 — Srimalinar (human-island city). Flags best-effort.",
            Base.Map.Srimalinar, LateParty, 2000),

        S("beloveno", "Ch.4 — Beloveno (human-island city). Flags best-effort.",
            Base.Map.Beloveno, LateParty, 2000),

        S("kounos", "Ch.5 — Kounos area. Flags best-effort.",
            Base.Map.KounosTrader, LateParty, 3000),

        S("maini", "Ch.6 — Maini. Flags best-effort.",
            Base.Map.Maini, LateParty, 3000),

        S("djicantos", "Ch.7 — Dji-Cantos. Seed-plan area. Flags best-effort.",
            Base.Map.DjiCantosCave, LateParty, 4000),

        S("kenget", "Ch.8 — Kenget Kamulos late dungeon (3D). Flags best-effort.",
            Base.Map.Kenget, LateParty, 5000),

        S("finale", "Endgame — Toronto finale location. The surrender win condition is live: " +
            "encounter MonsterGroup.OneAI, down a member, and the AI requests surrender -> endgame FLICs.",
            Base.Map.TorontoBegin, LateParty, 5000),
    ];

    public static IReadOnlyList<SyntheticScenario> BuiltIns() => Scenarios;

    public static bool TryGet(string name, out SyntheticScenario scenario)
    {
        foreach (var s in Scenarios)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                scenario = s;
                return true;
            }
        }
        scenario = null;
        return false;
    }
}
