using System.Collections.Generic;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

public interface IReadOnlyBattle
{
    IReadOnlyList<ICombatParticipant> Mobs { get; }
    ICombatParticipant GetTile(int x, int y);
    ICombatParticipant GetTile(int tileIndex);

    /// <summary>Current LP from the battle's HP shadow (covers monsters, which aren't in GameState.Sheets).</summary>
    int GetLifePoints(ICombatParticipant participant);

    /// <summary>Current SP from the battle's SP shadow (monster casts never touch the frozen sheet).</summary>
    int GetSpellPoints(ICombatParticipant participant);

    /// <summary>Merged conditions: sheet plus the battle's monster-condition shadow.</summary>
    UAlbion.Formats.Assets.Sheets.PlayerConditions GetConditions(ICombatParticipant participant);
}